using System.Text;

namespace TypePaste.Services;

/// <summary>
/// Small local diagnostics log in <c>%LOCALAPPDATA%\TypePaste\TypePaste.log</c> (rotated at 1 MB). It never contains
/// the text being typed.
/// </summary>
internal sealed class Log
{
    private const long MaxSize = 1024 * 1024;
    private readonly object _gate = new();

    public Log()
    {
        var overrideDirectory = Environment.GetEnvironmentVariable("TYPEPASTE_DATA_DIR");
        Directory = !string.IsNullOrWhiteSpace(overrideDirectory)
            ? overrideDirectory
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TypePaste");
        FilePath = Path.Combine(Directory, "TypePaste.log");
    }

    public string Directory { get; }

    public string FilePath { get; }

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message}: {exception}");

    private void Write(string level, string message)
    {
        try
        {
            lock (_gate)
            {
                System.IO.Directory.CreateDirectory(Directory);
                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length > MaxSize)
                {
                    File.Move(FilePath, FilePath + ".old", overwrite: true);
                }

                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch (Exception)
        {
            // Logging must never break the app.
        }
    }
}
