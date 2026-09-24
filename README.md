<p align="center">
  <img src="assets/TypePaste-logo.png" width="180" alt="TypePaste logo">
</p>

<h1 align="center">TypePaste</h1>

<p align="center"><b>Prepare text once. Press F6 in any app. TypePaste types it for you — character by character.</b></p>

TypePaste is a Windows 10/11 desktop app for places where copy & paste doesn't work: remote sessions, locked-down
forms, consoles, some games and web apps. Put your text into TypePaste, click into any text field in another app
and press **F6**. TypePaste types the text with real keyboard input, very fast and exactly as written. Press
**Esc** to stop at any moment.

- **Exact text** — spaces, line breaks, tabs, punctuation, symbols, upper/lower case and Unicode (accents, CJK,
  Arabic/Hebrew, emoji) are typed exactly. Nothing is autocorrected, reformatted or dropped.
- **Real keyboard input, not the clipboard** — uses the Windows `SendInput` API (Unicode keyboard packets), so it
  works wherever keyboard input is accepted.
- **Fast and safe** — *Instant* mode types as fast as the target app can accept keystrokes and never runs ahead of
  it, so Esc stops immediately and slow apps don't lose characters. Typing stops automatically when the target
  window closes or another window becomes active, and a second F6 press never starts a second run.
- **Local and private** — no account, no internet connection, no cloud service, no background service.

## Install

Run **`TypePaste Setup.exe`** and follow the wizard. It installs TypePaste to `C:\Program Files\TypePaste`, adds a
Start menu shortcut and (optionally) a desktop shortcut and sign-in start. Everything TypePaste needs is included —
no .NET or other runtime has to be installed. Uninstall from *Settings → Apps* or the Start menu like any other app.

Silent install/uninstall for administrators:

```
"TypePaste Setup.exe" /S [/D=C:\Custom\Path]
"C:\Program Files\TypePaste\Uninstall.exe" /S [/KEEPDATA]
```

## Use

1. Type or paste your text into the large editor (or drop a text file onto it, or click **Open**).
2. Click into the text field where the text should go — in any app.
3. Press **F6**. A small card at the top of the screen shows the progress. Press **Esc** to stop.

Other ways to start:

- **Start** counts down (3 s by default) and minimizes TypePaste, so you can click into the target field first.
- **Test typing** types into a built-in test pad and verifies, character by character, that everything arrived.

TypePaste keeps running in the notification area when you close its window, so F6 keeps working. Right-click the
tray icon to pause the hotkey, open settings or exit.

### Settings

| Setting | What it does |
|---|---|
| **Speed** | *Instant* (maximum speed, adaptive), *Fast* (~500 characters/s), *Steady* (~65 characters/s, for picky apps and games) or *Custom* (your own delay between characters, from 0 to 1000 ms). |
| **Pause after each line break** | Extra time for editors and chat apps to process new lines. |
| **Line breaks** | *Enter*, or *Shift+Enter* for chat apps (Teams, Slack, Discord, WhatsApp) that would otherwise send every line as its own message. |
| **Tabs** | Type a literal tab character (exact text) or press the Tab key. |
| **Input method** | *Unicode* (recommended; works with every character) or *Keyboard layout*: presses the physical keys of your layout, for apps that ignore Unicode input (some games and remote-desktop clients). Characters that are not on your layout still use Unicode. |
| **Stop when another window becomes active** | Keeps text from landing in the wrong app. |
| **Hotkeys** | Change the start (F6) and stop (Esc) hotkeys. Keys needed for typing require a modifier. |
| **Start button countdown** | 0–10 seconds. |
| **Theme / editor font** | System, light or dark; sans-serif or monospace editor. |
| **Remember text between sessions** | Off by default. Saved only on this PC. |
| **Start when I sign in / launch minimized to the tray / keep running when closed** | Windows integration. |

Settings are stored in `%APPDATA%\TypePaste\settings.json`; a small diagnostics log (never containing your text)
is in `%LOCALAPPDATA%\TypePaste\TypePaste.log`.

### Good to know

- **Apps running as administrator**: Windows does not allow normal apps to send keystrokes to elevated apps.
  TypePaste detects this and tells you; start TypePaste as administrator if you need to type into such an app.
- **Protected input is respected**: TypePaste does not try to get around the secure desktop (UAC prompts, sign-in
  screen), password protections, application security restrictions or anti-cheat systems. Apps that reject
  simulated input simply won't receive it.
- **Editors with auto-indent or auto-complete** (for example code editors) react to typed input the same way they
  react to your own typing; turn those features off in the target app if you need a verbatim copy.
- **Invisible control characters** (such as NUL or BEL) cannot be typed on any keyboard; TypePaste shows how many
  there are and skips only those.

## Build from source

TypePaste is a .NET 10 WPF application. The installer is built with NSIS.

**Windows** (requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) and [NSIS 3](https://nsis.sourceforge.io)):

```powershell
powershell -ExecutionPolicy Bypass -File build\build.ps1
```

**Linux / WSL / macOS** (cross-compiles; requires the .NET 10 SDK, NSIS (`apt install nsis`) and Python 3):

```bash
./build/build.sh
```

Both produce `artifacts/TypePaste Setup.exe` (≈ 44 MB, self-contained) and the app in `artifacts/publish/`.

For development, open `TypePaste.sln` in Visual Studio 2022/2026 or run `dotnet run --project src/TypePaste`.

## Project structure

```
src/TypePaste.Core/       Platform-independent logic (unit-tested on any OS)
  Typing/TypingEngine.cs    Batching, pacing, stop conditions
  Input/KeystrokePlanner.cs Text → keyboard events (line breaks, surrogate pairs, tabs, layout keys)
  Text/, Settings/, Hotkeys/
src/TypePaste/            The Windows app (WPF)
  Native/                   SendInput platform, target-window inspection, thread-idle probe, precise timer
  Services/                 Hotkeys, tray icon, single instance, settings store, theme, typing sessions
  ViewModels/, Views/       Main window, settings panel, test pad, progress overlay
  Themes/                   Light/dark palettes and control styles
installer/                NSIS script and branding bitmaps
tests/TypePaste.Core.Tests/  Unit tests (xUnit)
tests/TypePaste.E2E/      End-to-end tests that drive the installed app on Windows
assets/                   Official TypePaste logo
```

### How typing works

For every character TypePaste sends a key-down and key-up *Unicode packet* through `SendInput`; Windows delivers it
to the focused control as a normal character message, independent of the keyboard layout. Line breaks (CRLF, CR or
LF) become one Enter (or Shift+Enter) key press; surrogate pairs (emoji) are always sent together.

In *Instant* mode keystrokes go out in small batches. After each batch TypePaste checks the scheduler state of the
target's input thread (`NtQuerySystemInformation`) and continues as soon as the target has processed its input
queue. This gives maximum speed for each individual app, keeps Esc and focus-change stops immediate, and avoids
flooding slow apps. The paced modes send one keystroke at a time on a precise high-resolution timer.

## Tests

- `dotnet test tests/TypePaste.Core.Tests` — 88 unit tests for the engine, keystroke planner, statistics, hotkeys
  and settings (runs on any OS).
- `tests/TypePaste.E2E` — Windows end-to-end suite used by CI on Windows Server 2022 (Windows 10 based) and
  Windows Server 2025 (Windows 11 based). It installs `TypePaste Setup.exe` silently, verifies files, shortcuts,
  registry entries and that every icon is the TypePaste logo, then presses the real F6/Esc keys to type into a
  WinForms TextBox, a RichEdit control, Notepad and a Microsoft Edge text area (multi-line text, all ASCII symbols,
  Unicode and emoji, 100,000-character text, tabs), tests stopping with Esc, focus changes, closed targets, repeated
  F6 presses, every speed and input mode, custom hotkeys, the Start countdown, the test pad, tray mode and themes,
  takes screenshots, and finally uninstalls and verifies that nothing is left behind.

## License

[CC0 1.0 Universal](LICENSE)
