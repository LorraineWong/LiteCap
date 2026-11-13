<p align="center">
  <img src="docs/images/logo.png" width="96" alt="LiteCap logo">
</p>

<h1 align="center">LiteCap</h1>

<p align="center">
  Screenshots that are always JPG and always under 100&nbsp;KB (or lossless PNG), copied to the clipboard as a file.<br>
  Annotation, offline text recognition and offline translation included. For Windows and macOS.
</p>

<p align="center">
  <a href="https://github.com/LorraineWong/LiteCap/releases/latest"><img alt="Latest release" src="https://img.shields.io/github/v/release/LorraineWong/LiteCap?color=2563eb"></a>
  <a href="https://github.com/LorraineWong/LiteCap/releases"><img alt="Downloads" src="https://img.shields.io/github/downloads/LorraineWong/LiteCap/total?color=0891b2"></a>
  <img alt="Platforms" src="https://img.shields.io/badge/platform-Windows%2010%2F11%20%7C%20macOS%2013%2B-0f766e">
  <a href="LICENSE"><img alt="License: MIT" src="https://img.shields.io/badge/license-MIT-1e3a8a"></a>
</p>

<p align="center">
  <a href="https://github.com/LorraineWong/LiteCap/releases/latest/download/LiteCap.exe">Download for Windows</a> ·
  <a href="https://github.com/LorraineWong/LiteCap/releases/latest/download/LiteCap.dmg">Download for macOS</a> ·
  <a href="CHANGELOG.md">Changelog</a> ·
  <a href="README.zh-CN.md">中文说明</a>
</p>

---

## Why

Many upload forms, ticketing systems and chat tools reject images over 100 KB. LiteCap removes the
"screenshot → open an editor → export → check size → try again" loop: every capture is saved as a
JPG that is already below the limit, at the highest quality that fits.

## Features

- **Region or full screen** — drag an area or click a window; full screen saves instantly
- **Always under the limit** — default 100 KB (adjustable); quality is tuned automatically, resolution is reduced only as a last resort
- **JPG or PNG** — JPG keeps the size limit; PNG is lossless with no limit (Settings → Output)
- **Copied as a file** — paste straight into email, chat or upload fields
- **Annotate** — rectangle, ellipse, arrow, pen, highlighter, text, mosaic; 8 colours, 3 sizes, undo
- **Offline text recognition (OCR)** — built into the OS; nothing leaves your computer
- **Offline translation** — translate recognized text between Chinese and English, on your computer
- **Delayed capture** — 3-second countdown for right-click menus and hover tips
- **English / Chinese interface** — English by default, switch in Settings

<p align="center">
  <img src="docs/images/editor-en.png" width="720" alt="Editor with annotation toolbar">
</p>

<p align="center">
  <img src="docs/images/ocr-en.png" width="720" alt="Text recognition window">
</p>

## Install

### Windows 10 / 11

1. Download **[LiteCap.exe](https://github.com/LorraineWong/LiteCap/releases/latest/download/LiteCap.exe)** and double-click it. The icon appears in the system tray.
2. If Windows shows "Windows protected your PC", click **More info → Run anyway** (the app is not code-signed).

One file, no installer, nothing else to install — it uses the .NET Framework 4.x that ships with Windows.
Tip: put it in a fixed folder (not Downloads) before turning on "Start with Windows".

### macOS 13 Ventura or later (Apple silicon and Intel)

1. Download **[LiteCap.dmg](https://github.com/LorraineWong/LiteCap/releases/latest/download/LiteCap.dmg)**, open it and drag **LiteCap** into **Applications**.
2. Open LiteCap. The first time, macOS blocks it because it is not notarized:
   **System Settings → Privacy & Security → Open Anyway**, then confirm.
3. In **System Settings → Privacy & Security → Screen Recording**, turn **LiteCap** on, then quit and reopen LiteCap.

> After updating to a new version, switch Screen Recording off and on again for LiteCap, then reopen it.
> If captures come out black, this is the fix.

### Updating

Download the new version from [Releases](https://github.com/LorraineWong/LiteCap/releases) and replace the old file
(Windows: quit LiteCap from the tray first). Settings are kept.

## Usage

| Action | Windows | macOS |
|---|---|---|
| Region capture (drag, or click a window) | `Ctrl+1` | `⌘1` |
| Full screen (screen under the pointer) | `Ctrl+2` | `⌘2` |
| Recognize text | `Ctrl+3` / tray menu / editor `O` | `⌘3` / menu bar / editor `O` |
| Region in 3 seconds | tray menu | menu bar |

**Editor:** `R` rectangle · `E` ellipse · `A` arrow · `P` pen · `H` highlighter · `T` text · `M` mosaic · `1/2/3` size · `Ctrl/⌘+Z` undo · `O` recognize text
**Finish:** `Enter` / `Space` / `Ctrl/⌘+C` / double-click the selection · **Cancel:** `Esc`

Screenshots are saved to `Pictures/LiteCap`. Hotkeys, size limit, folder, language and start-up behaviour
are in **Settings** (right-click the tray icon on Windows, click the menu-bar icon on macOS).

### Text recognition

- **Windows** uses `Windows.Media.Ocr`. If it reports that no recognition language is installed:
  Settings → Time & language → Language & region → English or Chinese (Simplified) → ⋯ → Language options → install **Optical character recognition**.
- **macOS** uses Apple's Vision framework; nothing to install.

### Offline translation (Chinese ↔ English)

In the text-recognition window, click **Translate to English / Translate to Chinese** (the direction is detected
automatically). Wrapped lines are joined into sentences first; the translation is copied automatically, and
**Show original** switches back.

- **Windows** runs OPUS-MT models with ONNX Runtime on your PC. The first time, LiteCap asks to download the
  translation pack once (about 170 MB, from this repository's
  [translate-pack-1](https://github.com/LorraineWong/LiteCap/releases/tag/translate-pack-1) release) into
  `%LOCALAPPDATA%\LiteCap\translate`. If the download is blocked (for example by a company proxy), download
  `LiteCap-Translate-1.zip` in your browser and choose it when LiteCap asks. After that, nothing goes online.
- **macOS 15 or later** uses Apple's built-in Translation; macOS asks once to download the languages.
  On macOS 13–14 the translate button is hidden.

Quality is good enough to understand a text, not to publish it: technical terms can come out stiff.

## Build from source

- **Windows:** double-click `windows/build.bat` (uses the C# compiler that ships with Windows; no Visual Studio needed).
- **macOS:** `xcode-select --install` once, then `cd macos && ./build.sh`.

Details, project layout and the release process are in [CONTRIBUTING.md](CONTRIBUTING.md).

## Privacy

Screenshots, recognized text, translations and settings stay on your computer. The only network access is the
one-time download of the Windows translation pack, and only after you agree to it.
Logs are written only when an error occurs
(Windows: `%APPDATA%\LiteCap\logs`, macOS: `~/Library/Logs/LiteCap`).

## Feedback and contributing

LiteCap is a free side project, built for daily use and shared in case it helps someone else. There is no paid
version and no tracking. Feedback of any size is welcome:

- **Something broken?** [Report a bug](https://github.com/LorraineWong/LiteCap/issues/new?template=bug_report.yml)
- **Missing a feature?** [Suggest an idea](https://github.com/LorraineWong/LiteCap/issues/new?template=feature_request.yml)
- **Questions, tips, or just saying hi** — [Discussions](https://github.com/LorraineWong/LiteCap/discussions)
- **Want to help build it?** Read [CONTRIBUTING.md](CONTRIBUTING.md). Translations, testing on other Windows/macOS
  versions, and small fixes are all great first contributions.

Issues and discussions can be written in English or Chinese. 中文反馈同样欢迎。

If LiteCap saves you time, a ⭐ helps other people find it.

## Acknowledgements

- Offline translation on Windows uses [OPUS-MT](https://github.com/Helsinki-NLP/Opus-MT) models by Helsinki-NLP
  (CC BY 4.0), the int8 ONNX conversion by [Xenova](https://huggingface.co/Xenova), and
  [ONNX Runtime](https://github.com/microsoft/onnxruntime) (MIT). They are downloaded separately, not bundled.
- Text recognition uses the OCR engines built into Windows and macOS.

## License

[MIT](LICENSE) © 2026 Lorraine Wong
