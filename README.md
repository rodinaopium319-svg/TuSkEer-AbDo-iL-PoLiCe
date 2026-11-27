# TuSkEer AbDo iL PoLiCe

Professional Windows Auto Typing Engine & Script Manager built natively with **C#**, **.NET 10**, and **WPF** for Windows 10 and Windows 11.

## Architecture

- **`src-wpf/TuSkEer.AbDoILPoLiCe/Native/Win32Interop.cs`** — Low-level Win32 P/Invoke bindings for `SendInput` (Unicode `KEYEVENTF_UNICODE` + virtual keys), `SetWindowsHookEx` (`WH_KEYBOARD_LL`), `SetWindowDisplayAffinity` (`WDA_EXCLUDEFROMCAPTURE`), and `DwmSetWindowAttribute` / `SetWindowCompositionAttribute` (Windows 11 Mica/Acrylic & Windows 10 Acrylic fallback).
- **`src-wpf/TuSkEer.AbDoILPoLiCe/Storage/SqliteRepository.cs`** — Persistent SQLite WAL store at `%LocalAppData%\TuSkEerAbDoILPoLiCe\tuskeer.db`, scalable to 300+ scripts, with JSON export/import and online SQLite backup/restore.
- **`src-wpf/TuSkEer.AbDoILPoLiCe/Services/GlobalKeyboardHookService.cs`** — Dedicated background Win32 hook message-pump thread filtering `LLKHF_INJECTED` events, supporting standalone keys (`A–Z`, `0–9`, `F1–F24`, `NumPad 0–9`, navigation/special keys) and all modifier combinations (`Ctrl`, `Alt`, `Shift`), plus interactive shortcut recording and duplicate conflict detection.
- **`src-wpf/TuSkEer.AbDoILPoLiCe/Services/TypingEngine.cs`** — Grapheme-safe (`StringInfo`) character-by-character typing engine supporting Arabic, English, Unicode, and emoji, Repeated Character Fast Send (`ههههه` threshold optimization), live `0–1000 ms` delay updates via `Stopwatch` precision timing, and `APPEND CONTENT` clipboard-preserved direct paste.
- **`src-wpf/TuSkEer.AbDoILPoLiCe/Services/ScriptQueueScheduler.cs`** — Deterministic priority queue scheduler supporting sentence-boundary Pause/Resume, immediate Final Stop, and seamless script interruption/resumption (suspending Script A at its exact sentence index while Script B executes, then resuming Script A automatically).
- **`src-wpf/TuSkEer.AbDoILPoLiCe/Services/SystemTrayService.cs`** — Windows notification area tray icon with **Open**, **Pause / Resume**, **Stop**, and **Exit Completely** menu actions; window `X` button hides to tray while keeping global hooks active.
- **`src-wpf/TuSkEer.AbDoILPoLiCe/Services/CaptureExclusionService.cs`** — Wraps `SetWindowDisplayAffinity` (`WDA_EXCLUDEFROMCAPTURE`).

## Build & Publish (Windows)

### Prerequisites
- Windows 10 (1809+) or Windows 11 (x64)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### Build Commands
```powershell
dotnet restore TuSkEerAbDoILPoLiCe.sln
dotnet build TuSkEerAbDoILPoLiCe.sln -c Release --no-restore
dotnet publish src-wpf/TuSkEer.AbDoILPoLiCe/TuSkEer.AbDoILPoLiCe.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=false -o dist/TuSkEer-AbDo-iL-PoLiCe-win-x64
```

### GitHub Actions Automated Windows x64 Build
Workflow file: `.github/workflows/build-windows.yml`
Runs on `windows-latest`, installs `.NET 10.0.x`, builds Release configuration, publishes self-contained `win-x64` output, and uploads `TuSkEer-AbDo-iL-PoLiCe-win-x64.zip` as a workflow artifact.

## Honest Technical Notes & Limitations
1. **Screen Capture Exclusion (`WDA_EXCLUDEFROMCAPTURE`)**: Supported on Windows 10 Version 2004 (build 19041) and Windows 11 for standard DWM/DXGI screen capture APIs. Older Windows builds, virtualized displays, or kernel-level capture drivers may still capture the window.
2. **Sub-Millisecond & `0 ms` Timing**: At `0 ms`, the engine dispatches `SendInput` without intentional per-character sleep, using `Stopwatch` spin/yield timing for `1–15 ms` to avoid Windows' default 15.6 ms `Task.Delay` timer quantization. Actual character throughput remains bounded by the target foreground application's message-queue processing rate.
3. **Administrator / Elevated Target Apps**: On Windows UIPI (User Interface Privilege Isolation), `SendInput` from a standard-privilege process cannot inject keystrokes into an elevated (Run as Administrator) window unless TuSkEer AbDo iL PoLiCe is also run with matching privileges.
4. **Build Environment Note**: The AI Studio workspace container runs Linux without the Windows Desktop WPF SDK, so compiling the `.exe` and executing Win32 UI/hook integration tests must be performed on Windows or via the included `.github/workflows/build-windows.yml` GitHub Actions runner.
