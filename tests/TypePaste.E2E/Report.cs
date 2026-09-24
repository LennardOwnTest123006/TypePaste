using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Windows.Forms;

namespace TypePaste.E2E;

internal static class Log
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    public static void Line(string message) => Console.WriteLine($"[{Clock.Elapsed:mm\\:ss\\.f}] {message}");
}

internal sealed record CheckResult(string Area, string Name, bool Passed, string Details, TimeSpan Duration, bool Informational);

/// <summary>Collects check results and screenshots and writes a Markdown report.</summary>
internal sealed class Report
{
    private readonly List<CheckResult> _results = [];
    private readonly string _outputDirectory;
    private int _screenshot;

    public Report(string outputDirectory)
    {
        _outputDirectory = outputDirectory;
        Directory.CreateDirectory(Path.Combine(outputDirectory, "screenshots"));
    }

    public string Area { get; set; } = "General";

    public int Failures => _results.Count(r => !r.Passed && !r.Informational);

    /// <summary>Runs one check; exceptions count as failures and never stop the whole run.</summary>
    public bool Check(string name, Func<string> body, bool informational = false)
    {
        Log.Line($"▶ {Area} / {name}");
        var watch = Stopwatch.StartNew();
        CheckResult result;
        try
        {
            var details = body();
            result = new CheckResult(Area, name, true, details, watch.Elapsed, informational);
        }
        catch (Exception ex)
        {
            var message = ex is CheckFailedException ? ex.Message : $"{ex.GetType().Name}: {ex.Message}";
            result = new CheckResult(Area, name, false, message, watch.Elapsed, informational);
            if (ex is not CheckFailedException)
            {
                Log.Line(ex.ToString());
            }
        }

        _results.Add(result);
        Log.Line($"  {(result.Passed ? "PASS" : informational ? "INFO" : "FAIL")} ({result.Duration.TotalSeconds:0.0} s) {result.Details}");
        return result.Passed;
    }

    public string Screenshot(string name)
    {
        try
        {
            var bounds = SystemInformation.VirtualScreen;
            using var bitmap = new Bitmap(bounds.Width, bounds.Height);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
            }

            var file = Path.Combine(_outputDirectory, "screenshots", $"{++_screenshot:00}-{name}.png");
            bitmap.Save(file, ImageFormat.Png);
            Log.Line($"  screenshot {Path.GetFileName(file)}");
            return file;
        }
        catch (Exception ex)
        {
            Log.Line($"  screenshot failed: {ex.Message}");
            return string.Empty;
        }
    }

    public void Write(string environment)
    {
        var markdown = new StringBuilder();
        markdown.AppendLine("# TypePaste end-to-end test report").AppendLine();
        markdown.AppendLine(environment).AppendLine();
        markdown.AppendLine($"**{_results.Count(r => r.Passed)} passed, {Failures} failed, {_results.Count(r => !r.Passed && r.Informational)} informational**").AppendLine();
        markdown.AppendLine("| Area | Check | Result | Time | Details |");
        markdown.AppendLine("|---|---|---|---|---|");
        foreach (var r in _results)
        {
            var verdict = r.Passed ? "✅ pass" : r.Informational ? "ℹ\uFE0F info" : "❌ FAIL";
            markdown.AppendLine($"| {r.Area} | {r.Name} | {verdict} | {r.Duration.TotalSeconds:0.0} s | {r.Details.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ")} |");
        }

        File.WriteAllText(Path.Combine(_outputDirectory, "report.md"), markdown.ToString(), Encoding.UTF8);
        Console.WriteLine();
        Console.WriteLine(markdown.ToString());

        var summary = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
        if (!string.IsNullOrEmpty(summary))
        {
            File.AppendAllText(summary, markdown.ToString(), Encoding.UTF8);
        }
    }
}

internal sealed class CheckFailedException(string message) : Exception(message);

internal static class Assert
{
    public static void That(bool condition, string message)
    {
        if (!condition)
        {
            throw new CheckFailedException(message);
        }
    }

    /// <summary>Compares typed text with the expectation and explains the first difference.</summary>
    public static void TextEquals(string expected, string actual)
    {
        if (string.Equals(expected, actual, StringComparison.Ordinal))
        {
            return;
        }

        var length = Math.Min(expected.Length, actual.Length);
        var index = 0;
        while (index < length && expected[index] == actual[index])
        {
            index++;
        }

        throw new CheckFailedException(
            $"text differs at index {index} (expected length {expected.Length}, actual {actual.Length}): " +
            $"expected …{Snippet(expected, index)}… got …{Snippet(actual, index)}…");
    }

    public static string Snippet(string text, int index)
    {
        var start = Math.Max(0, index - 12);
        var end = Math.Min(text.Length, index + 12);
        return string.Concat(text[start..end].Select(c => c switch
        {
            '\r' => "\\r",
            '\n' => "\\n",
            '\t' => "\\t",
            _ when char.IsControl(c) || char.IsSurrogate(c) || c > 0x7E => $"\\u{(int)c:X4}",
            _ => c.ToString(),
        }));
    }
}
