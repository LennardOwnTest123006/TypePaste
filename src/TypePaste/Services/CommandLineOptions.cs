namespace TypePaste.Services;

/// <summary>
/// Command line: <c>TypePaste.exe [--tray] [--load &lt;file&gt;] [file]</c>. <c>--tray</c> starts hidden in the
/// notification area; <c>--load</c> (or a plain file path) opens a text file in the editor.
/// </summary>
internal sealed record CommandLineOptions(bool StartInTray, string? LoadPath)
{
    public static CommandLineOptions Parse(IReadOnlyList<string> args)
    {
        var tray = false;
        string? load = null;
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            switch (arg.ToLowerInvariant())
            {
                case "--tray" or "/tray" or "--minimized" or "/minimized" or "-m":
                    tray = true;
                    break;
                case "--load" or "/load" or "-l" when i + 1 < args.Count:
                    load = args[++i];
                    break;
                case "--activate":
                    break;
                default:
                    if (!arg.StartsWith('-') && File.Exists(arg))
                    {
                        load = arg;
                    }

                    break;
            }
        }

        return new CommandLineOptions(tray, load);
    }
}
