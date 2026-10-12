# <img src="BrowserSmoothScroll/favicon/Gemini_Generated_Image_raf4s0raf4s0raf4.png" width="48" height="48" align="center"/> Browser Smooth Scroll

Free, open-source smooth scrolling for Chrome and Edge on Windows.

Mouse-wheel scrolling in browsers moves the page in abrupt steps. Browser Smooth Scroll turns each wheel notch into an eased, momentum-based glide, with acceleration when you scroll fast. No account, no trial, no paid tier.

- **Demo video:** https://youtu.be/F5LCaOcKkQQ
- **Download (Windows app):** [Releases](https://github.com/kubi2811/SmoothScroll/releases)
- **Chrome / Edge extension:** [Chrome Web Store](https://chromewebstore.google.com/detail/hehlajelkodjocdmpenpgamgbejcbheo)

## Which version should I use?

| | Windows app | Browser extension |
|---|---|---|
| Install | Download a zip, run the `.exe` | One click from the Web Store |
| Works on | Windows 10/11 | Any OS that runs Chrome or Edge |
| Scope | Every surface of the browser, including the built-in PDF viewer | Regular web pages only |
| Other apps | Yes, every app by default; block the ones you don't want | No |
| Settings | Full set (see below) | Speed, step, easing, Shift-scroll |

The Windows app is the smoother of the two: it works on the wheel input itself, before the browser sees it. The extension runs the same scrolling model inside each page, so it cannot reach places where extensions are not allowed, such as the PDF viewer, `chrome://` pages and the Web Store.

## Windows app

### Requirements

- Windows 10/11 (64-bit)
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) (free, one-time install, about 50 MB)

### Install

1. Turn off the browser's own smooth scrolling at `chrome://flags/#smooth-scrolling` (or `edge://flags/#smooth-scrolling`). Leaving it on smooths the scroll twice.
2. Download the latest `BrowserSmoothScroll_vX.Y.zip` from [Releases](https://github.com/kubi2811/SmoothScroll/releases).
3. Extract the zip and run `BrowserSmoothScroll.exe`.

The app lives in the system tray, next to the clock. Right-click the tray icon to turn it on or off, block an app, or open Settings. It starts with Windows by default.

Out of the box it smooths the wheel in every app, not only browsers. To limit it to Chrome and Edge, untick **Enable for all apps by default** in Settings.

### Settings

| Setting | Default | What it does |
|---|---|---|
| Step size | 120 px | Distance scrolled per wheel notch |
| Animation time | 550 ms | How long each glide lasts |
| Acceleration delta / max | 70 ms / 7x | Fast consecutive notches scroll further, up to the max |
| Tail to head ratio | 3 | How long the glide's slow-down is compared with its start |
| Animation easing | On | Ease in and out instead of moving at constant speed |
| Shift key horizontal scrolling | On | Hold Shift to scroll sideways |
| Horizontal smoothness | On | Smooth sideways scrolling too |
| Reverse wheel direction | Off | Invert the scroll direction |
| Enable for all apps by default | On | Smooth every app; turn off to smooth only the allow list |
| Process allow list | `chrome,msedge` | Process names smoothed when the option above is off |
| Process block list | empty | Process names the app never touches |

Ctrl + wheel (zoom) is passed through to the browser untouched.

### Uninstall

1. Right-click the tray icon, open Settings and untick **Auto start on login**, then exit the app.
2. Delete `BrowserSmoothScroll.exe`.
3. Optionally delete the settings folder `%APPDATA%\BrowserSmoothScroll`.

## Browser extension

Install it from the [Chrome Web Store](https://chromewebstore.google.com/detail/hehlajelkodjocdmpenpgamgbejcbheo), then turn off the browser's own smooth scrolling at `chrome://flags/#smooth-scrolling`. Click the toolbar icon to change the animation speed and scroll step, or to switch it off.

<img src="BrowserSmoothScroll/favicon/Screenshot%202026-02-23%20012802.png" width="300" alt="Extension popup with animation speed, scroll step, easing and Shift-scroll settings"/>

Do not run the Windows app and the extension at the same time; pick one.

## Build from source

Release builds are produced by GitHub Actions from the tagged source ([workflow](.github/workflows/release.yml)). To build the Windows app yourself, install the .NET 8 SDK on Windows and run:

```
dotnet publish BrowserSmoothScroll/BrowserSmoothScroll.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

To load the extension from source, open `chrome://extensions`, turn on Developer mode, choose **Load unpacked** and select the `SmoothScrollExtension` folder.

## License

[MIT](LICENSE)
