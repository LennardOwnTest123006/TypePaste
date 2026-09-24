using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
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

    /// <summary>Opens the installer wizard, screenshots the first pages, then cancels.</summary>
    public static string InstallerUi(string setup, Report report)
    {
        using var process = Process.Start(new ProcessStartInfo(setup) { UseShellExecute = true })!;
        try
        {
            var window = Wait.Until(
                () => Win32.TopLevelWindows(h => Win32.ProcessIdOf(h) == process.Id && Win32.IsWindowVisible(h) && Win32.GetWindowText(h).Contains("TypePaste", StringComparison.Ordinal)).FirstOrDefault(),
                TimeSpan.FromSeconds(60), "installer window");
            Thread.Sleep(1500);
            var title = Win32.GetWindowText(window);
            report.Screenshot("installer-welcome");
            Win32.SetForegroundWindow(window);
            Win32.Press(Win32.VK_RETURN);
            Thread.Sleep(1200);
            report.Screenshot("installer-components");
            Win32.Press(Win32.VK_RETURN);
            Thread.Sleep(1200);
            report.Screenshot("installer-directory");
            Assert.That(title.StartsWith("TypePaste", StringComparison.Ordinal), $"unexpected installer title '{title}'");
            return $"wizard opened: '{title}'";
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

    public static string SilentInstall(string setup)
    {
        var watch = Stopwatch.StartNew();
        using var process = Process.Start(new ProcessStartInfo(setup, "/S") { UseShellExecute = true })!;
        Assert.That(process.WaitForExit(180_000), "installer did not finish within 3 minutes");
        Assert.That(process.ExitCode == 0, $"installer exit code {process.ExitCode}");
        return $"installed in {watch.Elapsed.TotalSeconds:0.0} s";
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

        using var key = Registry.LocalMachine.OpenSubKey(UninstallKey);
        Assert.That(key is not null, "uninstall registry key missing");
        Assert.That((string?)key!.GetValue("DisplayName") == "TypePaste", "DisplayName wrong");
        Assert.That(((string?)key.GetValue("UninstallString"))?.Contains("Uninstall.exe", StringComparison.Ordinal) == true, "UninstallString wrong");
        Assert.That(((string?)key.GetValue("DisplayIcon"))?.Contains("TypePaste.exe", StringComparison.Ordinal) == true, "DisplayIcon wrong");

        var version = FileVersionInfo.GetVersionInfo(exe);
        return $"{installed} files ({expected.Count} runtime files + uninstaller), shortcuts and uninstall entry present; " +
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
        using (var key = Registry.LocalMachine.OpenSubKey(UninstallKey))
        {
            Assert.That(key is null, "uninstall registry key still present");
        }

        using (var run = Registry.CurrentUser.OpenSubKey(RunKey))
        {
            Assert.That(run?.GetValue("TypePaste") is null, "startup entry still present");
        }

        return $"removed in {watch.Elapsed.TotalSeconds:0.0} s: folder, shortcuts and registry entries gone" +
               (appWasRunning ? "; running app was closed first" : string.Empty);
    }

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
