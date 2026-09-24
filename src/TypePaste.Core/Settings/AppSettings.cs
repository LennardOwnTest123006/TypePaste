using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using TypePaste.Core.Hotkeys;
using TypePaste.Core.Typing;

namespace TypePaste.Core.Settings;

public enum ThemePreference
{
    System,
    Light,
    Dark,
}

public enum EditorFontStyle
{
    Sans,
    Monospace,
}

/// <summary>User settings, stored as JSON in the user's roaming AppData folder.</summary>
public sealed class AppSettings
{
    public const int MaxCountdownSeconds = 10;

    public HotkeyGesture StartHotkey { get; set; } = HotkeyGesture.DefaultStart;

    public HotkeyGesture StopHotkey { get; set; } = HotkeyGesture.DefaultStop;

    public SpeedMode Speed { get; set; } = SpeedMode.Instant;

    public double CustomDelayMs { get; set; } = 5;

    public int LineBreakDelayMs { get; set; }

    public LineBreakStyle LineBreak { get; set; } = LineBreakStyle.Enter;

    public TabStyle Tab { get; set; } = TabStyle.Character;

    public InputMethod InputMethod { get; set; } = InputMethod.Unicode;

    public bool StopOnFocusChange { get; set; } = true;

    /// <summary>Seconds to wait after clicking Start before typing begins.</summary>
    public int StartCountdownSeconds { get; set; } = 3;

    /// <summary>Minimize the TypePaste window when Start is clicked so the previous app regains focus.</summary>
    public bool MinimizeOnStart { get; set; } = true;

    /// <summary>Show the small on-screen progress overlay while typing.</summary>
    public bool ShowOverlay { get; set; } = true;

    /// <summary>Start hidden in the notification area (system tray).</summary>
    public bool StartMinimizedToTray { get; set; }

    /// <summary>Closing the window keeps TypePaste running in the notification area.</summary>
    public bool CloseToTray { get; set; } = true;

    /// <summary>Start TypePaste automatically when the user signs in to Windows.</summary>
    public bool LaunchAtStartup { get; set; }

    /// <summary>Keep the editor text between sessions (stored locally in AppData).</summary>
    public bool RememberText { get; set; }

    public ThemePreference Theme { get; set; } = ThemePreference.System;

    public EditorFontStyle EditorFont { get; set; } = EditorFontStyle.Sans;

    public bool HotkeysPaused { get; set; }

    public TypingOptions ToTypingOptions() => new()
    {
        Speed = Speed,
        CustomDelayMs = CustomDelayMs,
        LineBreakDelayMs = LineBreakDelayMs,
        LineBreak = LineBreak,
        Tab = Tab,
        Method = InputMethod,
        StopOnFocusChange = StopOnFocusChange,
    };

    /// <summary>Clamps out-of-range values and repairs invalid hotkeys (for hand-edited or corrupted files).</summary>
    public AppSettings Normalize()
    {
        CustomDelayMs = double.IsFinite(CustomDelayMs) ? Math.Clamp(CustomDelayMs, 0, TypingOptions.MaxCustomDelayMs) : 5;
        LineBreakDelayMs = Math.Clamp(LineBreakDelayMs, 0, TypingOptions.MaxLineBreakDelayMs);
        StartCountdownSeconds = Math.Clamp(StartCountdownSeconds, 0, MaxCountdownSeconds);

        if (!Enum.IsDefined(Speed))
        {
            Speed = SpeedMode.Instant;
        }

        if (!Enum.IsDefined(LineBreak))
        {
            LineBreak = LineBreakStyle.Enter;
        }

        if (!Enum.IsDefined(Tab))
        {
            Tab = TabStyle.Character;
        }

        if (!Enum.IsDefined(InputMethod))
        {
            InputMethod = InputMethod.Unicode;
        }

        if (!Enum.IsDefined(Theme))
        {
            Theme = ThemePreference.System;
        }

        if (!Enum.IsDefined(EditorFont))
        {
            EditorFont = EditorFontStyle.Sans;
        }

        if (!StartHotkey.Validate(allowPlainEscape: false, out _))
        {
            StartHotkey = HotkeyGesture.DefaultStart;
        }

        if (!StopHotkey.Validate(allowPlainEscape: true, out _) || StopHotkey == StartHotkey)
        {
            StopHotkey = HotkeyGesture.DefaultStop;
        }

        if (StopHotkey == StartHotkey)
        {
            StartHotkey = HotkeyGesture.DefaultStart;
            StopHotkey = HotkeyGesture.DefaultStop;
        }

        return this;
    }

    public AppSettings Clone() => SettingsSerializer.Deserialize(SettingsSerializer.Serialize(this));
}

/// <summary>Reads and writes <see cref="AppSettings"/> as indented JSON.</summary>
public static class SettingsSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(), new HotkeyGestureJsonConverter() },
    };

    public static string Serialize(AppSettings settings) => JsonSerializer.Serialize(settings, Options);

    /// <summary>Parses settings; invalid JSON or values fall back to defaults instead of throwing.</summary>
    public static AppSettings Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new AppSettings();
        }

        try
        {
            return (JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings()).Normalize();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    private sealed class HotkeyGestureJsonConverter : JsonConverter<HotkeyGesture>
    {
        public override HotkeyGesture Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
            if (reader.TokenType != JsonTokenType.String)
            {
                reader.Skip();
            }

            return HotkeyGesture.TryParse(text, out var gesture) ? gesture : default;
        }

        public override void Write(Utf8JsonWriter writer, HotkeyGesture value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }
}
