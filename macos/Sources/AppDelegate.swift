// Menu bar app: status item, hotkeys, and the capture → save / recognize pipeline.

import AppKit
import Combine

final class AppDelegate: NSObject, NSApplicationDelegate {
    enum Mode { case region, full, ocr }

    private var statusItem: NSStatusItem!
    private let settings = AppSettings.shared
    private var session: CaptureSession?
    private var ocrWindow: OcrWindowController?
    private var busy = false
    private var bag = Set<AnyCancellable>()

    func applicationDidFinishLaunching(_ notification: Notification) {
        Log.installExceptionHandler()
        NSApp.setActivationPolicy(.accessory)

        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        if let button = statusItem.button {
            button.image = AppDelegate.menuBarIcon()
            button.toolTip = L.t("app")
        }
        rebuildMenu()
        applyHotkeys()

        NotificationCenter.default.addObserver(forName: .liteCapResumeHotkeys, object: nil, queue: .main) { [weak self] _ in
            self?.applyHotkeys()
        }
        // @Published fires before the value changes, so defer the reaction to the next run-loop turn.
        settings.$hkRegion.dropFirst().sink { [weak self] _ in DispatchQueue.main.async { self?.applyHotkeys() } }.store(in: &bag)
        settings.$hkFull.dropFirst().sink { [weak self] _ in DispatchQueue.main.async { self?.applyHotkeys() } }.store(in: &bag)
        settings.$hkOcr.dropFirst().sink { [weak self] _ in DispatchQueue.main.async { self?.applyHotkeys() } }.store(in: &bag)
        settings.$lang.dropFirst().sink { [weak self] _ in
            DispatchQueue.main.async {
                self?.rebuildMenu()
                self?.statusItem.button?.toolTip = L.t("app")
                SettingsWindowController.updateTitle()
            }
        }.store(in: &bag)
        settings.$maxKB.dropFirst().sink { [weak self] _ in DispatchQueue.main.async { self?.rebuildMenu() } }.store(in: &bag)
        settings.$png.dropFirst().sink { [weak self] _ in DispatchQueue.main.async { self?.rebuildMenu() } }.store(in: &bag)

        _ = ensurePermission(prompt: true)
    }

    static func menuBarIcon() -> NSImage? {
        if let img = NSImage(named: "MenuIcon") {
            img.isTemplate = true
            img.size = NSSize(width: 18, height: 18)
            return img
        }
        let fallback = NSImage(systemSymbolName: "viewfinder", accessibilityDescription: "LiteCap")
        fallback?.isTemplate = true
        return fallback
    }

    // MARK: hotkeys and menu

    private func applyHotkeys() {
        let c = HotKeyCenter.shared
        var failed: [String] = []
        if !c.register(id: 1, hotkey: settings.hkRegion, action: { [weak self] in self?.capture(.region) }) { failed.append(L.t("region")) }
        if !c.register(id: 2, hotkey: settings.hkFull, action: { [weak self] in self?.capture(.full) }) { failed.append(L.t("full")) }
        if !c.register(id: 3, hotkey: settings.hkOcr, action: { [weak self] in self?.capture(.ocr) }) { failed.append(L.t("ocr")) }
        settings.hotkeyWarning = failed.isEmpty ? "" : L.f("hk_busy", failed.joined(separator: ", "))
        rebuildMenu()
    }

    private func rebuildMenu() {
        let m = NSMenu()
        let format = settings.png ? "PNG" : L.f("header", settings.maxKB)
        let header = NSMenuItem(title: L.t("app") + "   ·   " + format, action: nil, keyEquivalent: "")
        header.isEnabled = false
        m.addItem(header)
        m.addItem(.separator())
        addItem(m, L.t("region"), "viewfinder", #selector(menuRegion), settings.hkRegion)
        addItem(m, L.t("full"), "display", #selector(menuFull), settings.hkFull)
        addItem(m, L.t("ocr"), "text.viewfinder", #selector(menuOcr), settings.hkOcr)
        addItem(m, L.t("delay"), "timer", #selector(menuDelay), nil)
        m.addItem(.separator())
        addItem(m, L.t("open_folder"), "folder", #selector(menuOpenFolder), nil)
        addItem(m, L.t("settings_menu"), "gearshape", #selector(menuSettings), nil)
        m.addItem(.separator())
        addItem(m, L.t("quit"), "power", #selector(menuQuit), nil)
        statusItem.menu = m
    }

    private func addItem(_ m: NSMenu, _ title: String, _ symbol: String, _ action: Selector, _ hk: Hotkey?) {
        let item = NSMenuItem(title: title, action: action, keyEquivalent: hk?.menuKeyEquivalent ?? "")
        if let h = hk, h.menuKeyEquivalent != nil { item.keyEquivalentModifierMask = h.menuModifierMask }
        item.target = self
        item.image = NSImage(systemSymbolName: symbol, accessibilityDescription: nil)
        m.addItem(item)
    }

    // A short delay lets the menu close before the screen is frozen.
    @objc private func menuRegion() { capture(.region, delay: 0.25) }
    @objc private func menuFull() { capture(.full, delay: 0.25) }
    @objc private func menuOcr() { capture(.ocr, delay: 0.25) }
    @objc private func menuOpenFolder() {
        try? FileManager.default.createDirectory(atPath: settings.folder, withIntermediateDirectories: true)
        NSWorkspace.shared.open(URL(fileURLWithPath: settings.folder))
    }
    @objc private func menuSettings() { SettingsWindowController.show() }
    @objc private func menuQuit() { NSApp.terminate(nil) }

    @objc private func menuDelay() {
        guard !busy, ensurePermission(prompt: true) else { return }
        busy = true
        Countdown.run(seconds: 3) { [weak self] in self?.grab(.region) }
    }

    // MARK: capture pipeline

    private func capture(_ mode: Mode, delay: Double = 0) {
        guard !busy, ensurePermission(prompt: true) else { return }
        busy = true
        if delay > 0 {
            DispatchQueue.main.asyncAfter(deadline: .now() + delay) { [weak self] in self?.grab(mode) }
        } else {
            grab(mode)
        }
    }

    private func grab(_ mode: Mode) {
        Capture.grabAll { [weak self] result in
            guard let self = self else { return }
            switch result {
            case .failure(let error): self.fail(error)
            case .success(let shots): self.present(shots, mode: mode)
            }
        }
    }

    private func present(_ shots: [ScreenShot], mode: Mode) {
        guard !shots.isEmpty else { busy = false; return }
        if shots.allSatisfy({ Capture.looksBlack($0.image) }) {
            let sizes = shots.map { "\($0.image.width)x\($0.image.height)" }.joined(separator: ", ")
            Log.write("All captured screens are black (\(sizes)); preflight=\(CGPreflightScreenCaptureAccess())", context: "capture")
            busy = false
            showBlackCaptureHelp()
            return
        }

        if mode == .full {
            let mouse = NSEvent.mouseLocation
            let shot = shots.first(where: { $0.screen.frame.contains(mouse) }) ?? shots[0]
            deliver(shot.image, ocr: false)
            return
        }
        let s = CaptureSession(shots: shots, ocrMode: mode == .ocr, editable: settings.editAfterCapture) { [weak self] result in
            guard let self = self else { return }
            self.session = nil
            guard let r = result else { self.busy = false; return }
            self.deliver(r.image, ocr: r.ocr)
        }
        session = s
        s.begin()
    }

    private func deliver(_ image: CGImage, ocr: Bool) {
        if ocr {
            let w = OcrWindowController.open(image: image, again: { [weak self] in self?.capture(.ocr, delay: 0.25) })
            ocrWindow = w
            MacOCR.recognize(image) { [weak self] result in
                switch result {
                case .success(let out): w.show(out)
                case .failure(let error):
                    Log.write(error, context: "ocr")
                    w.fail(error)
                }
                self?.busy = false
            }
            return
        }

        let maxKB = settings.maxKB, folder = settings.folder, preview = settings.showPreview, png = settings.png
        DispatchQueue.global(qos: .userInitiated).async { [weak self] in
            do {
                let saved = try Output.save(image, maxKB: maxKB, folder: folder, png: png)
                DispatchQueue.main.async {
                    Output.copyFile(saved.url)
                    if preview {
                        let kb = String(format: "%.1f", Double(saved.bytes) / 1000)
                        var info = "\(Int(saved.size.width))×\(Int(saved.size.height))"
                        if saved.size != saved.original {
                            info += L.f("toast_orig", Int(saved.original.width), Int(saved.original.height))
                        }
                        info += "  ·  " + (png ? "PNG" : L.f("toast_quality", saved.quality))
                        PreviewToast.show(image: image, title: L.f("toast_title", kb), info: info, file: saved.url)
                    }
                    self?.busy = false
                }
            } catch {
                DispatchQueue.main.async { self?.fail(error) }
            }
        }
    }

    private func fail(_ error: Error) {
        Log.write(error, context: "capture")
        busy = false
        let a = NSAlert()
        a.messageText = L.t("fail")
        a.informativeText = String(describing: error)
        NSApp.activate(ignoringOtherApps: true)
        a.runModal()
    }

    private func showBlackCaptureHelp() {
        let a = NSAlert()
        a.messageText = L.t("black_title")
        a.informativeText = L.t("black_body")
        a.addButton(withTitle: L.t("perm_open"))
        a.addButton(withTitle: L.t("perm_later"))
        NSApp.activate(ignoringOtherApps: true)
        if a.runModal() == .alertFirstButtonReturn,
           let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture") {
            NSWorkspace.shared.open(url)
        }
    }

    // MARK: permission

    private var permissionAsked = false

    private func ensurePermission(prompt: Bool) -> Bool {
        if CGPreflightScreenCaptureAccess() { return true }
        guard prompt else { return false }
        if !permissionAsked {
            permissionAsked = true
            CGRequestScreenCaptureAccess()   // registers LiteCap in the list and shows the system prompt once
        }
        let a = NSAlert()
        a.messageText = L.t("perm_title")
        a.informativeText = L.t("perm_body")
        a.addButton(withTitle: L.t("perm_open"))
        a.addButton(withTitle: L.t("perm_later"))
        NSApp.activate(ignoringOtherApps: true)
        if a.runModal() == .alertFirstButtonReturn,
           let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture") {
            NSWorkspace.shared.open(url)
        }
        return false
    }
}
