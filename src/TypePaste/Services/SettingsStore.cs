using System.Text;
using TypePaste.Core.Settings;

namespace TypePaste.Services;

/// <summary>
/// Stores settings (and optionally the editor text) in <c>%APPDATA%\TypePaste</c>. Everything stays on this computer.
/// </summary>
internal sealed class SettingsStore
{
    public SettingsStore()
    {
        // TYPEPASTE_DATA_DIR lets automated tests use an isolated profile.
        var overrideDirectory = Environment.GetEnvironmentVariable("TYPEPASTE_DATA_DIR");
        DataDirectory = !string.IsNullOrWhiteSpace(overrideDirectory)
            ? overrideDirectory
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TypePaste");
    }

    public string DataDirectory { get; }

    public string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    public string TextPath => Path.Combine(DataDirectory, "text.txt");

    public AppSettings Load()
    {
        try
        {
            return File.Exists(SettingsPath) ? SettingsSerializer.Deserialize(File.ReadAllText(SettingsPath)) : new AppSettings();
        }
        catch (Exception ex)
        {
            App.Log.Warn($"Could not read settings, using defaults: {ex.Message}");
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings) => WriteAtomically(SettingsPath, SettingsSerializer.Serialize(settings));

    public string? LoadText()
    {
        try
        {
            return File.Exists(TextPath) ? File.ReadAllText(TextPath, Encoding.UTF8) : null;
        }
        catch (Exception ex)
        {
            App.Log.Warn($"Could not read saved text: {ex.Message}");
            return null;
        }
    }

    public void SaveText(string text) => WriteAtomically(TextPath, text);

    public void DeleteText()
    {
        try
        {
            File.Delete(TextPath);
        }
        catch (Exception ex)
        {
            App.Log.Warn($"Could not delete saved text: {ex.Message}");
        }
    }

    private void WriteAtomically(string path, string content)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            var temp = path + ".tmp";
            File.WriteAllText(temp, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            App.Log.Error($"Could not write {Path.GetFileName(path)}", ex);
        }
    }
}
