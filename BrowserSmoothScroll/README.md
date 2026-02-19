# BrowserSmoothScroll

Windows tray app that smooths mouse wheel scrolling for selected apps (default: `chrome`, `msedge`).

## Features
- Runs in system tray.
- Works only for process allow-list by default (`chrome,msedge`).
- Optional "all apps" mode.
- Optional debug telemetry logging for wheel input/acceleration/output.
- Configurable:
  - step size
  - animation time
  - acceleration window/max
  - tail-to-head easing ratio
  - shift-to-horizontal scrolling
  - reverse wheel direction
- Auto-start on login (HKCU Run key).

## How it works
1. Installs a global low-level mouse hook (`WH_MOUSE_LL`).
2. Intercepts wheel events when foreground process matches allow-list.
3. Suppresses original wheel message.
4. Replays wheel input in small animated chunks via `SendInput`.

## Build and run
```powershell
dotnet build
dotnet run
```

## Publish
```powershell
dotnet publish -c Release -r win-x64 --self-contained false
```

## Recommended browser setting
Disable native browser smooth scrolling to avoid double smoothing:
- `chrome://flags/#smooth-scrolling`
- `edge://flags/#smooth-scrolling`

## Debug logs
- Turn on `Debug mode` in Settings.
- Logs are written to `%AppData%\BrowserSmoothScroll\logs`.
- Use tray menu `Open Logs Folder` to open the folder quickly.

### Compare other smooth-scroll app vs BrowserSmoothScroll
1. `Debug mode = ON`, `Enabled = OFF`:
   - BrowserSmoothScroll works as a probe only (no interception), logs raw and injected wheel events from other apps.
2. Run your other app and reproduce the same scroll test.
3. Save that log file.
4. `Debug mode = ON`, `Enabled = ON`:
   - Reproduce the same scroll test with BrowserSmoothScroll.
5. Save the second log file and compare.

In logs, `HOOK source=` helps separate event origin:
- `raw`
- `external-injected` (likely from other app)
- `self-injected` (from BrowserSmoothScroll)
