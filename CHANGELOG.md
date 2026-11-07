# Changelog

All notable changes to LiteCap are documented here. The project follows [Semantic Versioning](https://semver.org/).

## [1.1.0] — 2026-11-07

### Added
- **PNG output**: Settings → Output → Format: JPG or PNG. PNG is lossless and has no size limit; the size limit applies to JPG only
- **Offline translation (Chinese ↔ English)** in the text-recognition window: direction detected automatically, wrapped lines joined into sentences first, result copied automatically, "Show original" switches back, edits in either view are kept
  - Windows: OPUS-MT models on ONNX Runtime, run on the PC. A one-time translation pack (about 230 MB) is downloaded on first use after confirmation; if the download is blocked, the zip can be downloaded in a browser and chosen manually. `LiteCap.exe` stays a single small file
  - macOS 15+: Apple's built-in Translation framework (the system asks once to download the languages). Hidden on macOS 13–14

### Changed
- Tray / menu-bar header and the preview card show "PNG" when PNG output is selected

## [1.0.0] — 2026-10-30

First public release, for Windows and macOS.

### Capture
- Region capture (drag an area, or click to pick the window under the cursor) and full-screen capture across all monitors
- Default hotkeys — Windows: Ctrl+1 region, Ctrl+2 full screen; macOS: ⌘1 region, ⌘2 full screen, ⌘3 recognize text. All customizable, with conflict checking
- Region editing: move/resize handles, arrow-key nudging, magnifier with coordinates and pixel colour (Windows)
- "Region in 3 seconds": a countdown that never takes focus, so right-click menus and hover tips can be captured

### Annotation
- Rectangle, ellipse, arrow, pen, highlighter, text (IME supported), mosaic
- 8 colours, 3 sizes, undo, a keyboard shortcut for every tool

### Output
- Always JPG, always strictly below the size limit (default 100 KB; 1 KB = 1000 bytes)
- Highest quality that fits; resolution is reduced only when quality would drop below 35
- Saved to Pictures/LiteCap and copied to the clipboard as a file
- Preview card after each capture (size, quality, click to locate the file)

### Text recognition (OCR)
- Fully offline: Windows.Media.Ocr on Windows 10/11, Apple Vision on macOS — screenshots never leave the computer
- "Recognize text" in the editor toolbar (key O), the tray/menu-bar menu, and an optional hotkey
- Result window: the capture with every recognized line outlined (click a line to select its text), editable text, "Keep line breaks / Merge into paragraphs", automatic copy, "Capture again"
- Chinese and English; spaces Windows inserts between Chinese characters are removed
- Step-by-step guidance and a shortcut to language settings when Windows has no OCR language installed

### App
- English interface by default; Chinese available in Settings → General → Language (switches live)
- Settings: Hotkeys, Output, General, About
- Start with Windows / Open at login
- Crash protection: errors are logged (Windows: %APPDATA%\LiteCap\logs, macOS: ~/Library/Logs/LiteCap) and the app keeps running
- Windows: single `LiteCap.exe`, no installer, runs on the .NET Framework 4.x built into Windows 10/11
- macOS: `LiteCap.dmg` with a universal app (Apple silicon + Intel, macOS 13+); ScreenCaptureKit capture on macOS 14+
- Both builds are produced by GitHub Actions and attached to each release

[1.1.0]: https://github.com/LorraineWong/LiteCap/releases/tag/v1.1.0
[1.0.0]: https://github.com/LorraineWong/LiteCap/releases/tag/v1.0.0
