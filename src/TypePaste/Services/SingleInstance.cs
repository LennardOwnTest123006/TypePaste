using System.Runtime.InteropServices;
using static TypePaste.Native.NativeMethods;

namespace TypePaste.Services;

/// <summary>
/// Ensures only one TypePaste runs per Windows session. A second launch forwards its command line (for example
/// <c>--load file.txt</c>) to the running instance and exits.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    public const nint CopyDataSignature = 0x54505043; // "TPPC"
    private const string MutexName = @"Local\TypePaste.SingleInstance.5B2E7C1D-8F4A-4C63-9E0B-6D1F2A3B4C5D";

    private readonly Mutex _mutex;

    private SingleInstance(Mutex mutex, bool isFirst)
    {
        _mutex = mutex;
        IsFirstInstance = isFirst;
    }

    public bool IsFirstInstance { get; }

    public static SingleInstance Acquire()
    {
        var mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            try
            {
                // The previous owner exited without releasing it: we own it now.
                createdNew = mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                createdNew = true;
            }
        }

        return new SingleInstance(mutex, createdNew);
    }

    /// <summary>Sends the command line to the running instance. Returns false if it could not be reached.</summary>
    public static bool Forward(string[] args)
    {
        // The first instance may still be starting up; give it a few seconds to create its window.
        nint window = 0;
        for (var attempt = 0; attempt < 50 && window == 0; attempt++)
        {
            window = FindWindowW(MessageWindow.ClassName, null);
            if (window == 0)
            {
                Thread.Sleep(100);
            }
        }

        if (window == 0)
        {
            return false;
        }

        GetWindowThreadProcessId(window, out var processId);
        AllowSetForegroundWindow(processId);

        var payload = string.Join('\0', args.Length == 0 ? ["--activate"] : args) + '\0';
        var buffer = Marshal.StringToHGlobalUni(payload);
        try
        {
            var data = new COPYDATASTRUCT { dwData = CopyDataSignature, cbData = payload.Length * 2, lpData = buffer };
            return SendMessageTimeoutW(window, WM_COPYDATA, 0, ref data, SMTO_ABORTIFHUNG, 5000, out _) != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose()
    {
        if (IsFirstInstance)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Not owned by this thread anymore.
            }
        }

        _mutex.Dispose();
    }
}
