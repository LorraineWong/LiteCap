# Changelog

All notable changes to LiteCap are documented here. The project follows [Semantic Versioning](https://semver.org/).

## [1.0.0] — 2026-10-30

First public release, for Windows and macOS.

### Capture
- Region capture (drag an area, or click to pick the window under the cursor) and full-screen capture across all monitors
- Default hotkeys — Windows: Ctrl+1 region, Ctrl+2 full screen; macOS: ⇧⌘1 region, ⇧⌘2 full screen. All customizable, with conflict checking
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
- Windows: single executable, no installer, runs on the .NET Framework 4.x built into Windows 10/11
- macOS: menu-bar app built locally with the free Command Line Tools (macOS 13+), ScreenCaptureKit capture on macOS 14+

[1.0.0]: https://github.com/LorraineWong/LiteCap/releases/tag/v1.0.0
