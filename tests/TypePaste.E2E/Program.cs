using System.Diagnostics;
using Microsoft.Win32;

namespace TypePaste.E2E;

/// <summary>
/// TypePaste end-to-end tests for Windows 10/11.
///
///   TypePaste.E2E.exe --setup "TypePaste Setup.exe" --ico TypePaste.ico --files publish-files.txt --out results
///
/// Installs TypePaste silently, verifies the installation and icons, runs the typing scenarios against the installed
/// app, uninstalls it and writes results/report.md plus screenshots. The exit code is the number of failed checks.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        var options = ParseArguments(args);
        var output = Path.GetFullPath(options.GetValueOrDefault("out", "e2e-results"));
        var work = Path.Combine(Path.GetTempPath(), "TypePaste.E2E." + Environment.ProcessId);
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(work);

        var report = new Report(output);
        var environment = DescribeEnvironment();
        Log.Line(environment);

        var setup = Path.GetFullPath(options["setup"]);
        var ico = Path.GetFullPath(options["ico"]);
        var files = Path.GetFullPath(options["files"]);
        var exe = Path.Combine(InstallerChecks.InstallDirectory, "TypePaste.exe");

        report.Area = "Installer";
        report.Check("installer icon is the TypePaste logo", () => InstallerChecks.VerifyIcon(setup, ico));
        report.Check("installer wizard opens", () => InstallerChecks.InstallerUi(setup, report));
        var installed = report.Check("silent install", () => InstallerChecks.SilentInstall(setup));
        report.Check("all files, shortcuts and uninstall entry present", () => InstallerChecks.VerifyInstallation(files));
        report.Check("TypePaste.exe icon is the TypePaste logo", () => InstallerChecks.VerifyIcon(exe, ico));
        report.Check("Uninstall.exe icon is the TypePaste logo", () => InstallerChecks.VerifyIcon(Path.Combine(InstallerChecks.InstallDirectory, "Uninstall.exe"), ico));
        report.Check("Start menu shortcut launches TypePaste", () => LaunchFromShortcut(report));

        if (installed)
        {
            using var targets = new TargetHost();
            using var app = new AppDriver(exe, work);
            try
            {
                new Scenarios(report, app, targets, work).Run();
            }
            catch (Exception ex)
            {
                Log.Line($"Scenario run aborted: {ex}");
                report.Area = "Harness";
                report.Check("scenario run completed", () => throw ex);
            }

            report.Area = "Uninstaller";
            report.Check("silent uninstall removes everything (closing the running app)", () => InstallerChecks.SilentUninstall(app));
        }

        report.Write(environment);
        Log.Line($"{report.Failures} failed check(s). Report: {Path.Combine(output, "report.md")}");
        return report.Failures;
    }

    private static string LaunchFromShortcut(Report report)
    {
        using var process = Process.Start(new ProcessStartInfo(InstallerChecks.StartMenuShortcut) { UseShellExecute = true });
        var window = Wait.Until(
            () => Win32.TopLevelWindows(h => Win32.IsWindowVisible(h) && Win32.GetWindowText(h) == "TypePaste" && Win32.GetClassName(h).StartsWith("HwndWrapper", StringComparison.Ordinal)).FirstOrDefault(),
            TimeSpan.FromSeconds(60), "TypePaste window from the Start menu shortcut");
        Thread.Sleep(1500);
        report.Screenshot("first-launch-from-start-menu");
        var pid = Win32.ProcessIdOf(window);
        using var running = Process.GetProcessById(pid);
        var path = running.MainModule?.FileName ?? string.Empty;
        var message = Win32.TopLevelWindows(h => Win32.GetClassName(h) == AppDriver.MessageWindowClass && Win32.ProcessIdOf(h) == pid).FirstOrDefault();
        Win32.PostMessageW(message, Win32.WM_CLOSE, 0, 0);
        Assert.That(running.WaitForExit(15_000), "TypePaste did not exit when asked");
        Assert.That(path.StartsWith(InstallerChecks.InstallDirectory, StringComparison.OrdinalIgnoreCase), $"started from {path}");
        return $"started {path} and exited cleanly";
    }

    private static string DescribeEnvironment()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var product = key?.GetValue("ProductName") as string ?? "Windows";
        var display = key?.GetValue("DisplayVersion") as string ?? string.Empty;
        var build = key?.GetValue("CurrentBuildNumber") as string ?? Environment.OSVersion.Version.Build.ToString();
        var screen = System.Windows.Forms.SystemInformation.VirtualScreen;
        return $"Environment: {product} {display} (build {build}), screen {screen.Width}×{screen.Height}, " +
               $"user {Environment.UserName}, {Environment.ProcessorCount} CPUs, .NET {Environment.Version}";
    }

    private static Dictionary<string, string> ParseArguments(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i + 1 < args.Length; i += 2)
        {
            result[args[i].TrimStart('-')] = args[i + 1];
        }

        foreach (var required in new[] { "setup", "ico", "files" })
        {
            if (!result.ContainsKey(required))
            {
                Console.Error.WriteLine("usage: TypePaste.E2E --setup <TypePaste Setup.exe> --ico <TypePaste.ico> --files <publish-files.txt> [--out <dir>]");
                Environment.Exit(64);
            }
        }

        return result;
    }
}
