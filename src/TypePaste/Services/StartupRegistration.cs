using Microsoft.Win32;

namespace TypePaste.Services;

/// <summary>Adds or removes TypePaste from the user's "run at sign-in" list (HKCU\...\Run).</summary>
internal static class StartupRegistration
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "TypePaste";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (enabled)
            {
                var exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "TypePaste.exe");
                key.SetValue(ValueName, $"\"{exe}\" --tray");
            }
            else if (key.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception ex)
        {
            App.Log.Error("Could not update the startup registration", ex);
            return false;
        }
    }
}
