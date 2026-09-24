using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace TypePaste.E2E;

/// <summary>Installs and uninstalls TypePaste and verifies files, shortcuts, registry entries and icons.</summary>
internal static class InstallerChecks
{
    public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\TypePaste";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static string InstallDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "TypePaste");

    public static string StartMenuShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "TypePaste.lnk");

    public static string DesktopShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "TypePaste.lnk");

    /// <summary>The user's Downloads folder, as Windows reports it (it can be moved).</summary>
    public static string DownloadsShortcut => Path.Combine(SHGetKnownFolderPath(FolderIdDownloads, 0, 0), "TypePaste.lnk");

    /// <summary>
    /// Installs through the wizard like a user would (accepting the defaults) and checks that TypePaste opens by
    /// itself once the installation has finished.
    /// </summary>
    public static string InteractiveInstall(string setup, Report report)
    {
        var watch = Stopwatch.StartNew();
        using var process = Process.Start(new ProcessStartInfo(setup) { UseShellExecute = true })!;
        try
        {
            var window = Wait.Until(
                () => Win32.TopLevelWindows(h => Win32.ProcessIdOf(h) == process.Id && Win32.IsWindowVisible(h) && Win32.GetWindowText(h).Contains("TypePaste", StringComparison.Ordinal)).FirstOrDefault(),
                TimeSpan.FromSeconds(60), "installer window");
            Thread.Sleep(1500);
            var title = Win32.GetWindowText(window);
            Assert.That(title.StartsWith("TypePaste", StringComparison.Ordinal), $"unexpected installer title '{title}'");
            report.Screenshot("installer-welcome");
            NextPage(window);
            report.Screenshot("installer-components");
            NextPage(window);
            report.Screenshot("installer-directory");
            NextPage(window); // Install

            var app = Wait.Until(() => AppWindows().FirstOrDefault(), TimeSpan.FromSeconds(180), "TypePaste to open after the installation");
            var opened = watch.Elapsed;
            Thread.Sleep(2000);
            report.Screenshot("installer-finished-typepaste-open");
            var inFront = Win32.GetForegroundWindow() == app;
            Assert.That(!process.HasExited, "the installer closed before showing its finish page");

            var pid = Win32.ProcessIdOf(app);
            using var running = Process.GetProcessById(pid);
            var path = running.MainModule?.FileName ?? string.Empty;
            Assert.That(path.StartsWith(InstallDirectory, StringComparison.OrdinalIgnoreCase), $"TypePaste started from {path}");

            NextPage(window); // Finish
            Assert.That(process.WaitForExit(30_000), "the installer did not close after clicking Finish");
            Assert.That(process.ExitCode == 0, $"installer exit code {process.ExitCode}");
            CloseApp(running);
            return $"installed through the wizard; TypePaste opened by itself {opened.TotalSeconds:0.0} s after starting the setup " +
                   $"({(inFront ? "in front" : "behind another window")}); Finish closed the wizard";
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(10_000);
            }
        }
    }

    /// <summary>Clicks the wizard's default button (Next, Install or Finish).</summary>
    private static void NextPage(nint installer)
    {
        Win32.PostMessageW(installer, Win32.WM_COMMAND, 1 /* IDOK */, 0);
        Thread.Sleep(1500);
    }

    /// <summary>Visible TypePaste main windows.</summary>
    public static List<nint> AppWindows() =>
        Win32.TopLevelWindows(h => Win32.IsWindowVisible(h) && Win32.GetWindowText(h) == "TypePaste" && Win32.GetClassName(h).StartsWith("HwndWrapper", StringComparison.Ordinal));

    /// <summary>Asks a running TypePaste to exit the way the installer does and waits for it.</summary>
    public static void CloseApp(Process running)
    {
        var message = Win32.TopLevelWindows(h => Win32.GetClassName(h) == AppDriver.MessageWindowClass && Win32.ProcessIdOf(h) == running.Id).FirstOrDefault();
        Win32.PostMessageW(message, Win32.WM_CLOSE, 0, 0);
        Assert.That(running.WaitForExit(15_000), "TypePaste did not exit when asked");
    }

    public static string SilentInstall(string setup)
    {
        var watch = Stopwatch.StartNew();
        using var process = Process.Start(new ProcessStartInfo(setup, "/S") { UseShellExecute = true })!;
        Assert.That(process.WaitForExit(180_000), "installer did not finish within 3 minutes");
        Assert.That(process.ExitCode == 0, $"installer exit code {process.ExitCode}");
        var elapsed = watch.Elapsed;

        // Unattended installs never start the app.
        Thread.Sleep(3000);
        var started = Process.GetProcessesByName("TypePaste");
        Assert.That(started.Length == 0, "a silent install started TypePaste");
        return $"installed in {elapsed.TotalSeconds:0.0} s; TypePaste was not started";
    }

    public static string VerifyInstallation(string publishListFile)
    {
        var exe = Path.Combine(InstallDirectory, "TypePaste.exe");
        Assert.That(File.Exists(exe), $"{exe} missing");
        Assert.That(File.Exists(Path.Combine(InstallDirectory, "Uninstall.exe")), "Uninstall.exe missing");

        var expected = File.ReadAllLines(publishListFile).Where(l => l.Length > 0).ToList();
        var missing = expected.Where(f => !File.Exists(Path.Combine(InstallDirectory, f))).ToList();
        Assert.That(missing.Count == 0, $"{missing.Count} runtime files missing, e.g. {string.Join(", ", missing.Take(5))}");

        var installed = Directory.GetFiles(InstallDirectory, "*", SearchOption.AllDirectories).Length;
        Assert.That(File.Exists(StartMenuShortcut), $"Start menu shortcut missing: {StartMenuShortcut}");
        Assert.That(File.Exists(DesktopShortcut), $"desktop shortcut missing: {DesktopShortcut}");
        Assert.That(File.Exists(DownloadsShortcut), $"Downloads shortcut missing: {DownloadsShortcut}");
        foreach (var shortcut in new[] { StartMenuShortcut, DesktopShortcut, DownloadsShortcut })
        {
            Assert.That(PointsTo(shortcut, exe), $"{shortcut} does not point to {exe}");
        }

        using var key = Registry.LocalMachine.OpenSubKey(UninstallKey);
        Assert.That(key is not null, "uninstall registry key missing");
        Assert.That((string?)key!.GetValue("DisplayName") == "TypePaste", "DisplayName wrong");
        Assert.That(((string?)key.GetValue("UninstallString"))?.Contains("Uninstall.exe", StringComparison.Ordinal) == true, "UninstallString wrong");
        Assert.That(((string?)key.GetValue("DisplayIcon"))?.Contains("TypePaste.exe", StringComparison.Ordinal) == true, "DisplayIcon wrong");

        var version = FileVersionInfo.GetVersionInfo(exe);
        return $"{installed} files ({expected.Count} runtime files + uninstaller), Start menu, desktop and Downloads shortcuts and uninstall entry present; " +
               $"TypePaste.exe {version.FileVersion}, product '{version.ProductName}', {(key.GetValue("EstimatedSize") is int kb ? kb / 1024 : 0)} MB";
    }

    /// <summary>Checks that the icon resources of an executable are exactly the images of the official logo icon.</summary>
    public static string VerifyIcon(string executable, string referenceIco)
    {
        var reference = ReadIcoImages(File.ReadAllBytes(referenceIco));
        var embedded = ReadEmbeddedIcon(executable);
        Assert.That(embedded.Count > 0, $"{Path.GetFileName(executable)} has no icon");
        foreach (var (size, bytes) in reference)
        {
            Assert.That(embedded.TryGetValue(size, out var actual), $"{Path.GetFileName(executable)} lacks the {size}px logo image");
            Assert.That(actual!.AsSpan().SequenceEqual(bytes), $"{Path.GetFileName(executable)}: {size}px icon differs from the TypePaste logo");
        }

        return $"{Path.GetFileName(executable)}: all {reference.Count} logo sizes ({string.Join(", ", reference.Keys.Order())} px) identical";
    }

    public static string SilentUninstall(AppDriver app)
    {
        var uninstaller = Path.Combine(InstallDirectory, "Uninstall.exe");
        Assert.That(File.Exists(uninstaller), "Uninstall.exe missing");
        var appWasRunning = !app.HasExited;
        var watch = Stopwatch.StartNew();
        using (var process = Process.Start(new ProcessStartInfo(uninstaller, "/S") { UseShellExecute = true })!)
        {
            process.WaitForExit(60_000);
        }

        // The uninstaller copies itself to %TEMP% and continues there; wait for the folder to disappear.
        Wait.Until(() => !Directory.Exists(InstallDirectory), TimeSpan.FromSeconds(120), "installation folder removal");
        Assert.That(app.HasExited, "the running TypePaste was not closed by the uninstaller");
        Assert.That(!File.Exists(StartMenuShortcut), "Start menu shortcut still present");
        Assert.That(!File.Exists(DesktopShortcut), "desktop shortcut still present");
        Assert.That(!File.Exists(DownloadsShortcut), "Downloads shortcut still present");
        foreach (var folder in new[] { Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData })
        {
            var data = Path.Combine(Environment.GetFolderPath(folder), "TypePaste");
            Assert.That(!Directory.Exists(data), $"{data} still present");
        }
        using (var key = Registry.LocalMachine.OpenSubKey(UninstallKey))
        {
            Assert.That(key is null, "uninstall registry key still present");
        }

        using (var run = Registry.CurrentUser.OpenSubKey(RunKey))
        {
            Assert.That(run?.GetValue("TypePaste") is null, "startup entry still present");
        }

        return $"removed in {watch.Elapsed.TotalSeconds:0.0} s: folder, all shortcuts, settings and registry entries gone" +
               (appWasRunning ? "; running app was closed first" : string.Empty);
    }

    /// <summary>Whether a .lnk file targets <paramref name="target"/> (its path is stored as ANSI and/or UTF-16).</summary>
    private static bool PointsTo(string shortcut, string target)
    {
        var bytes = File.ReadAllBytes(shortcut);
        return Encoding.Latin1.GetString(bytes).Contains(target, StringComparison.OrdinalIgnoreCase) ||
               Encoding.Unicode.GetString(bytes).Contains(target, StringComparison.OrdinalIgnoreCase) ||
               Encoding.Unicode.GetString(bytes, 1, bytes.Length - 1).Contains(target, StringComparison.OrdinalIgnoreCase);
    }

    private static readonly Guid FolderIdDownloads = new("374DE290-123F-4565-9164-39C4925E467B");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, PreserveSig = false)]
    private static extern string SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, nint token);

    private static Dictionary<int, byte[]> ReadIcoImages(byte[] ico)
    {
        var result = new Dictionary<int, byte[]>();
        var count = BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4));
        for (var i = 0; i < count; i++)
        {
            var entry = ico.AsSpan(6 + (16 * i), 16);
            var size = entry[0] == 0 ? 256 : entry[0];
            var length = BinaryPrimitives.ReadInt32LittleEndian(entry[8..]);
            var offset = BinaryPrimitives.ReadInt32LittleEndian(entry[12..]);
            result[size] = ico.AsSpan(offset, length).ToArray();
        }

        return result;
    }

    private const uint LOAD_LIBRARY_AS_DATAFILE = 0x2;
    private const uint LOAD_LIBRARY_AS_IMAGE_RESOURCE = 0x20;
    private static readonly nint RT_ICON = 3;
    private static readonly nint RT_GROUP_ICON = 14;

    private delegate bool EnumResNameProc(nint module, nint type, nint name, nint param);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadLibraryExW(string path, nint file, uint flags);

    [DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(nint module);

    [DllImport("kernel32.dll")]
    private static extern bool EnumResourceNamesW(nint module, nint type, EnumResNameProc callback, nint param);

    [DllImport("kernel32.dll")]
    private static extern nint FindResourceW(nint module, nint name, nint type);

    [DllImport("kernel32.dll")]
    private static extern nint LoadResource(nint module, nint resource);

    [DllImport("kernel32.dll")]
    private static extern nint LockResource(nint data);

    [DllImport("kernel32.dll")]
    private static extern int SizeofResource(nint module, nint resource);

    private static byte[] ResourceBytes(nint module, nint name, nint type)
    {
        var resource = FindResourceW(module, name, type);
        if (resource == 0)
        {
            return [];
        }

        var size = SizeofResource(module, resource);
        var pointer = LockResource(LoadResource(module, resource));
        var bytes = new byte[size];
        Marshal.Copy(pointer, bytes, 0, size);
        return bytes;
    }

    private static Dictionary<int, byte[]> ReadEmbeddedIcon(string executable)
    {
        var result = new Dictionary<int, byte[]>();
        var module = LoadLibraryExW(executable, 0, LOAD_LIBRARY_AS_DATAFILE | LOAD_LIBRARY_AS_IMAGE_RESOURCE);
        Assert.That(module != 0, $"could not open {executable}");
        try
        {
            nint group = 0;
            EnumResourceNamesW(module, RT_GROUP_ICON, (_, _, name, _) =>
            {
                group = name;
                return false; // first (main) icon group
            }, 0);
            Assert.That(group != 0, "no icon group resource");

            var directory = ResourceBytes(module, group, RT_GROUP_ICON);
            var count = BinaryPrimitives.ReadUInt16LittleEndian(directory.AsSpan(4));
            for (var i = 0; i < count; i++)
            {
                var entry = directory.AsSpan(6 + (14 * i), 14);
                var size = entry[0] == 0 ? 256 : entry[0];
                var id = BinaryPrimitives.ReadUInt16LittleEndian(entry[12..]);
                result[size] = ResourceBytes(module, id, RT_ICON);
            }
        }
        finally
        {
            FreeLibrary(module);
        }

        return result;
    }
}
