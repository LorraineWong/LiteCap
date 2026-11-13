# Contributing to LiteCap

Thanks for taking the time to help. LiteCap is a small, free side project, so every bug report,
idea and pull request genuinely matters. 中文或英文都可以。

## Ways to help

| You want to… | Do this |
|---|---|
| Report a bug | [Open a bug report](https://github.com/LorraineWong/LiteCap/issues/new?template=bug_report.yml) — include your OS version and steps to reproduce |
| Suggest a feature | [Open a feature request](https://github.com/LorraineWong/LiteCap/issues/new?template=feature_request.yml) — describe the problem first, then your idea |
| Ask a question or share a tip | [Start a discussion](https://github.com/LorraineWong/LiteCap/discussions) |
| Fix something or add a feature | Read on, then open a pull request |

For anything bigger than a small fix, please open an issue first so we can agree on the approach before you spend time on it.

## Design principles

New features are weighed against what LiteCap is for:

1. **Small output** — JPG captures always stay under the size limit.
2. **Lightweight** — one small download, no installer, no runtime to install.
3. **Offline and private** — nothing leaves the computer unless the user explicitly agrees.
4. **Fast** — a capture should be one hotkey and one paste.

A feature that breaks one of these (for example, bundling a 100 MB dependency) will probably be declined,
even if it is useful. That is a scope decision, not a judgement of the idea.

## Project layout

```
windows/        C# WinForms tray app (C# 5, .NET Framework 4.x, no NuGet packages)
  src/          Core, Overlay (editor), Ocr, OcrForm, Translate, SettingsForm, Tray, I18n, ...
  build.bat
macos/          Swift AppKit + SwiftUI menu-bar app (macOS 13+)
  Sources/      Strings.swift holds all interface text
  Resources/    App and menu-bar icons
  build.sh, Install.command
docs/images/    Screenshots used in the README
.github/        CI workflows and issue templates
```

## Building

### Windows

Double-click `windows/build.bat`. It uses the C# compiler that ships with Windows
(`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`) and writes `windows/LiteCap.exe`.
Keep the code within **C# 5** — that is what this compiler supports, and it is why LiteCap needs nothing installed.

### macOS

```bash
xcode-select --install     # once
cd macos
./build.sh                 # produces LiteCap.app (ad-hoc signed)
```

Or run `macos/Install.command`, which builds, installs into Applications and opens the app.
After rebuilding, toggle LiteCap off and on in **System Settings → Privacy & Security → Screen Recording**.

## Making a change

1. Fork the repository and create a branch from `main` (`fix/…` or `feature/…`).
2. Keep the Windows and macOS versions in step where it makes sense. If you can only build one platform,
   say so in the pull request — that is fine.
3. **Interface text:** every string needs both English and Chinese.
   Windows: add it in `windows/src/I18n.cs`; macOS: in `macos/Sources/Strings.swift`.
   Code, comments, file names and commit messages are in English.
4. Test the paths you touched: region capture, full screen, annotation, OCR, and the size limit if output changed.
5. Add a line under an `## [Unreleased]` heading in `CHANGELOG.md`.
6. Open a pull request and fill in the template. Screenshots or a short recording help a lot for UI changes.

GitHub Actions builds both apps on every push, so you will see whether your change compiles on both platforms.

## Releasing (maintainer)

1. Set the version in `windows/src/Core.cs` (`AssemblyVersion`, `AssemblyFileVersion`) and `macos/Info.plist` (`CFBundleShortVersionString`).
2. Turn `## [Unreleased]` in `CHANGELOG.md` into `## [x.y.z] — YYYY-MM-DD`.
3. Commit and push, then `git tag vx.y.z && git push origin vx.y.z`
   (or **Releases → Draft a new release** on GitHub with tag `vx.y.z` on `main`).

The release workflow checks that the tag, both version numbers and the changelog agree, then attaches
`LiteCap.exe`, `LiteCap.dmg` and the changelog notes to the release (about 3 minutes).
The Windows translation pack is built separately by `.github/workflows/translate-pack.yml` (run it manually with **publish**).

## Code of conduct

By taking part you agree to follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## License

By contributing, you agree that your contributions are licensed under the [MIT License](LICENSE).
