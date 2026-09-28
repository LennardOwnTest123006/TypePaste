using System.Runtime.InteropServices;
using static TypePaste.Native.NativeMethods;

namespace TypePaste.Native;

/// <summary>Scheduler state of one thread.</summary>
internal readonly record struct ThreadSnapshot(int State, int WaitReason, uint ContextSwitches)
{
    private const int StateWaiting = 5;
    private const int WaitReasonWrUserRequest = 13;

    /// <summary>
    /// True when the thread is blocked in a window-message wait (GetMessage/MsgWaitForMultipleObjects), which only
    /// happens once its input queue is empty.
    /// </summary>
    public bool IsWaitingForMessages => State == StateWaiting && WaitReason == WaitReasonWrUserRequest;

    /// <summary>True when the thread is blocked in any kind of wait (not running or ready to run).</summary>
    public bool IsWaiting => State == StateWaiting;
}

/// <summary>
/// Reads thread scheduler states through <c>NtQuerySystemInformation(SystemProcessInformation)</c>. The typing engine
/// uses it to see when the target's GUI thread has finished processing the keystrokes sent so far.
/// </summary>
internal sealed unsafe class ThreadStateProbe : IDisposable
{
    // x64 layouts of SYSTEM_PROCESS_INFORMATION and SYSTEM_THREAD_INFORMATION.
    private const int ProcessInfoSize = 0x100;
    private const int ProcessIdOffset = 0x50;
    private const int ThreadInfoSize = 0x50;
    private const int ThreadIdOffset = 0x30;
    private const int ContextSwitchesOffset = 0x40;
    private const int ThreadStateOffset = 0x44;
    private const int WaitReasonOffset = 0x48;

    private byte* _buffer;
    private int _size = 512 * 1024;

    private ThreadStateProbe()
    {
        _buffer = (byte*)NativeMemory.Alloc((nuint)_size);
    }

    /// <summary>Creates a probe after verifying the structure layout against the calling thread; null if unsupported.</summary>
    public static ThreadStateProbe? TryCreate()
    {
        if (!Environment.Is64BitProcess)
        {
            return null;
        }

        var probe = new ThreadStateProbe();
        try
        {
            // The calling thread is running right now, so a correct parse must report it as Running (2).
            if (probe.TryQuery(Environment.ProcessId, (int)GetCurrentThreadId(), out var self) && self.State == 2)
            {
                return probe;
            }

            App.Log.Warn($"Thread state probe self-test failed (state {self.State}); using fixed pacing.");
        }
        catch (Exception ex)
        {
            App.Log.Warn($"Thread state probe unavailable: {ex.Message}");
        }

        probe.Dispose();
        return null;
    }

    public bool TryQuery(int processId, int threadId, out ThreadSnapshot snapshot)
    {
        snapshot = default;
        if (_buffer is null)
        {
            return false;
        }

        uint status;
        var attempts = 0;
        while ((status = NtQuerySystemInformation(SystemProcessInformation, _buffer, _size, out var needed)) == STATUS_INFO_LENGTH_MISMATCH)
        {
            if (++attempts > 4)
            {
                return false;
            }

            NativeMemory.Free(_buffer);
            _size = Math.Max(_size * 2, needed + (128 * 1024));
            _buffer = (byte*)NativeMemory.Alloc((nuint)_size);
        }

        if (status != 0)
        {
            return false;
        }

        var entry = _buffer;
        while (true)
        {
            var nextOffset = *(uint*)entry;
            var threadCount = *(uint*)(entry + 4);
            var pid = (long)*(nint*)(entry + ProcessIdOffset);
            if (pid == processId)
            {
                var thread = entry + ProcessInfoSize;
                for (var i = 0; i < threadCount; i++, thread += ThreadInfoSize)
                {
                    if ((long)*(nint*)(thread + ThreadIdOffset) == threadId)
                    {
                        snapshot = new ThreadSnapshot(
                            *(int*)(thread + ThreadStateOffset),
                            *(int*)(thread + WaitReasonOffset),
                            *(uint*)(thread + ContextSwitchesOffset));
                        return true;
                    }
                }

                return false;
            }

            if (nextOffset == 0)
            {
                return false;
            }

            entry += nextOffset;
        }
    }

    public void Dispose()
    {
        if (_buffer is not null)
        {
            NativeMemory.Free(_buffer);
            _buffer = null;
        }
    }
}
