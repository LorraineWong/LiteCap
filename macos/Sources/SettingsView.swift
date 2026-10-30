// Settings window (SwiftUI). Changes apply immediately, the usual behaviour for Mac settings.

import AppKit
import SwiftUI
import ServiceManagement
import Carbon.HIToolbox

extension Notification.Name {
    static let liteCapResumeHotkeys = Notification.Name("LiteCapResumeHotkeys")
}

// MARK: hotkey recorder

final class RecorderView: NSView {
    var hotkey: Hotkey = .empty { didSet { needsDisplay = true } }
    var onChange: ((Hotkey) -> Void)?
    private var recording = false { didSet { needsDisplay = true } }
    private var liveMods: UInt32 = 0
    private var message: String?

    override var acceptsFirstResponder: Bool { return true }
    override var isFlipped: Bool { return true }

    override func mouseDown(with event: NSEvent) {
        window?.makeFirstResponder(self)
    }

    override func becomeFirstResponder() -> Bool {
        recording = true
        message = nil
        HotKeyCenter.shared.unregisterAll()   // so our own combinations can be recorded
        return true
    }

    override func resignFirstResponder() -> Bool {
        recording = false
        liveMods = 0
        message = nil
        NotificationCenter.default.post(name: .liteCapResumeHotkeys, object: nil)
        return true
    }

    override func performKeyEquivalent(with event: NSEvent) -> Bool {
        if recording { keyDown(with: event); return true }
        return super.performKeyEquivalent(with: event)
    }

    override func flagsChanged(with event: NSEvent) {
        liveMods = Hotkey.carbonMods(event.modifierFlags)
        needsDisplay = true
    }

    override func keyDown(with event: NSEvent) {
        guard recording else { super.keyDown(with: event); return }
        let code = UInt32(event.keyCode)
        let mods = Hotkey.carbonMods(event.modifierFlags)
        if mods == 0 && code == 53 { window?.makeFirstResponder(nil); return }                // Esc: stop
        if mods == 0 && (code == 51 || code == 117) { set(.empty); return }                    // Delete: clear
        let hasRealModifier = mods & UInt32(cmdKey | controlKey | optionKey) != 0
        if !hasRealModifier && !Hotkey.isFunctionKey(code) {
            message = L.lang == .en ? "Add ⌘ / ⌃ / ⌥" : "需配合 ⌘ / ⌃ / ⌥"
            needsDisplay = true
            return
        }
        set(Hotkey(keyCode: code, mods: mods))
    }

    private func set(_ h: Hotkey) {
        hotkey = h
        onChange?(h)
        window?.makeFirstResponder(nil)
    }

    override func draw(_ dirtyRect: NSRect) {
        let r = bounds.insetBy(dx: 1, dy: 1)
        let field = NSBezierPath(roundedRect: r, xRadius: 7, yRadius: 7)
        (recording ? NSColor.controlAccentColor.withAlphaComponent(0.08) : NSColor.controlBackgroundColor).setFill()
        field.fill()
        (recording ? NSColor.controlAccentColor : NSColor.separatorColor).setStroke()
        field.lineWidth = recording ? 1.5 : 1
        field.stroke()

        func centered(_ s: String, _ color: NSColor) {
            let a = NSAttributedString(string: s, attributes: [.font: NSFont.systemFont(ofSize: 12), .foregroundColor: color])
            let sz = a.size()
            a.draw(at: NSPoint(x: bounds.midX - sz.width / 2, y: bounds.midY - sz.height / 2))
        }

        if recording {
            if let m = message { centered(m, .systemOrange); return }
            if liveMods != 0 { drawCaps(Hotkey(keyCode: UInt32.max - 1, mods: liveMods).modifierSymbols + ["…"]); return }
            centered(L.t("kb_press"), .controlAccentColor)
            return
        }
        if hotkey.isEmpty { centered(L.t("unset"), .secondaryLabelColor); return }
        drawCaps(hotkey.modifierSymbols + [hotkey.keyName])
    }

    private func drawCaps(_ parts: [String]) {
        let font = NSFont.systemFont(ofSize: 12, weight: .semibold)
        let h: CGFloat = bounds.height - 10
        let widths = parts.map { max(h, ($0 as NSString).size(withAttributes: [.font: font]).width + 12) }
        let total = widths.reduce(0, +) + CGFloat(parts.count - 1) * 4
        var x = bounds.midX - total / 2
        let y = (bounds.height - h) / 2 - 1
        for (i, p) in parts.enumerated() {
            let cap = NSRect(x: x, y: y, width: widths[i], height: h)
            NSColor.separatorColor.setFill()
            NSBezierPath(roundedRect: cap.offsetBy(dx: 0, dy: 1.5), xRadius: 4, yRadius: 4).fill()
            NSColor.textBackgroundColor.setFill()
            NSBezierPath(roundedRect: cap, xRadius: 4, yRadius: 4).fill()
            let a = NSAttributedString(string: p, attributes: [.font: font, .foregroundColor: NSColor.labelColor])
            let s = a.size()
            a.draw(at: NSPoint(x: cap.midX - s.width / 2, y: cap.midY - s.height / 2))
            x += widths[i] + 4
        }
    }
}

// Plain Binding (not @Binding): SwiftUI property wrappers are macros in recent SDKs,
// and the free Command Line Tools ship without the macro plugins.
struct HotkeyRecorder: NSViewRepresentable {
    let hotkey: Binding<Hotkey>

    func makeNSView(context: Context) -> RecorderView {
        let v = RecorderView()
        v.hotkey = hotkey.wrappedValue
        let b = hotkey
        v.onChange = { h in b.wrappedValue = h }
        return v
    }

    func updateNSView(_ v: RecorderView, context: Context) {
        v.hotkey = hotkey.wrappedValue
        let b = hotkey
        v.onChange = { h in b.wrappedValue = h }
    }
}

// MARK: tabs

private struct RowLabel: View {
    let icon: String
    let title: String
    let desc: String

    var body: some View {
        HStack(spacing: 12) {
            Image(systemName: icon)
                .font(.system(size: 15, weight: .medium))
                .foregroundColor(.accentColor)
                .frame(width: 30, height: 30)
                .background(Color.accentColor.opacity(0.12))
                .clipShape(RoundedRectangle(cornerRadius: 7))
            VStack(alignment: .leading, spacing: 2) {
                Text(title)
                Text(desc).font(.system(size: 11)).foregroundColor(.secondary)
            }
        }
    }
}

struct HotkeysTab: View {
    @ObservedObject var s = AppSettings.shared

    var body: some View {
        Form {
            Section {
                row("viewfinder", "region", "region_desc", $s.hkRegion)
                row("display", "full", "full_desc", $s.hkFull)
                row("text.viewfinder", "ocr", "ocr_desc", $s.hkOcr)
            } footer: {
                VStack(alignment: .leading, spacing: 6) {
                    Text(L.t("hk_note")).font(.system(size: 11)).foregroundColor(.secondary)
                    if !s.hotkeyWarning.isEmpty {
                        Text(s.hotkeyWarning).font(.system(size: 11)).foregroundColor(.red)
                    }
                }
            }
            HStack {
                Spacer()
                Button(L.t("reset")) {
                    s.hkRegion = Hotkey.regionDefault
                    s.hkFull = Hotkey.fullDefault
                    s.hkOcr = Hotkey.empty
                }
            }
        }
        .formStyle(.grouped)
    }

    private func row(_ icon: String, _ title: String, _ desc: String, _ binding: Binding<Hotkey>) -> some View {
        HStack {
            RowLabel(icon: icon, title: L.t(title), desc: L.t(desc))
            Spacer()
            HotkeyRecorder(hotkey: binding).frame(width: 160, height: 30)
        }
    }
}

struct OutputTab: View {
    @ObservedObject var s = AppSettings.shared

    var body: some View {
        Form {
            Section {
                HStack {
                    RowLabel(icon: "doc.zipper", title: L.t("limit"), desc: L.t("limit_desc"))
                    Spacer()
                    Picker("", selection: $s.maxKB) {
                        Text("50").tag(50)
                        Text("100").tag(100)
                        Text("200").tag(200)
                        if ![50, 100, 200].contains(s.maxKB) { Text("\(s.maxKB)").tag(s.maxKB) }
                    }
                    .pickerStyle(.segmented)
                    .labelsHidden()
                    .frame(width: 180)
                    TextField("", value: $s.maxKB, format: .number)
                        .frame(width: 60)
                        .multilineTextAlignment(.center)
                        .onSubmit { s.maxKB = min(10000, max(10, s.maxKB)) }
                    Text("KB").foregroundColor(.secondary)
                }
                VStack(alignment: .leading, spacing: 8) {
                    HStack {
                        RowLabel(icon: "folder", title: L.t("folder"), desc: "")
                        Spacer()
                        Button(L.t("change")) { chooseFolder() }
                        Button(L.t("open")) {
                            try? FileManager.default.createDirectory(atPath: s.folder, withIntermediateDirectories: true)
                            NSWorkspace.shared.open(URL(fileURLWithPath: s.folder))
                        }
                    }
                    Text(s.folder)
                        .font(.system(size: 12, design: .monospaced))
                        .foregroundColor(.secondary)
                        .lineLimit(1)
                        .truncationMode(.middle)
                        .textSelection(.enabled)
                }
            } footer: {
                Text(L.t("out_note")).font(.system(size: 11)).foregroundColor(.secondary)
            }
        }
        .formStyle(.grouped)
    }

    private func chooseFolder() {
        let p = NSOpenPanel()
        p.canChooseDirectories = true
        p.canChooseFiles = false
        p.canCreateDirectories = true
        p.directoryURL = URL(fileURLWithPath: s.folder)
        if p.runModal() == .OK, let url = p.url { s.folder = url.path }
    }
}

struct GeneralTab: View {
    @ObservedObject var s = AppSettings.shared

    private var loginBinding: Binding<Bool> {
        let settings = s
        return Binding(get: { SMAppService.mainApp.status == .enabled }, set: { on in
            do {
                if on { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() }
                settings.loginError = ""
            } catch {
                settings.loginError = error.localizedDescription
            }
            settings.objectWillChange.send()
        })
    }

    var body: some View {
        Form {
            Section {
                HStack {
                    RowLabel(icon: "globe", title: L.t("lang"), desc: "中文 / English")
                    Spacer()
                    Picker("", selection: $s.lang) {
                        Text("中文").tag(Lang.zh)
                        Text("English").tag(Lang.en)
                    }
                    .pickerStyle(.segmented)
                    .labelsHidden()
                    .frame(width: 160)
                }
                Toggle(isOn: $s.editAfterCapture) { RowLabel(icon: "pencil.and.outline", title: L.t("edit_t"), desc: L.t("edit_d")) }
                Toggle(isOn: $s.showPreview) { RowLabel(icon: "rectangle.on.rectangle", title: L.t("prev_t"), desc: L.t("prev_d")) }
                Toggle(isOn: loginBinding) { RowLabel(icon: "power", title: L.t("login_t"), desc: L.t("login_d")) }
                if !s.loginError.isEmpty {
                    Text(s.loginError).font(.system(size: 11)).foregroundColor(.red)
                }
            }
        }
        .formStyle(.grouped)
        .toggleStyle(.switch)
    }
}

struct AboutTab: View {
    @ObservedObject var s = AppSettings.shared

    private let keys: [(String, String)] = [
        ("R", "k_rect"), ("E", "k_ellipse"), ("A", "k_arrow"), ("P", "k_pen"), ("H", "k_marker"), ("T", "k_text"),
        ("M", "k_mosaic"), ("1 2 3", "k_size"), ("⌘Z", "k_undo"), ("O", "k_ocr"), ("↩", "k_done"), ("⎋", "k_cancel")]

    var body: some View {
        VStack(spacing: 16) {
            HStack(spacing: 14) {
                Image(nsImage: NSApp.applicationIconImage).resizable().frame(width: 56, height: 56)
                VStack(alignment: .leading, spacing: 3) {
                    Text(L.t("app")).font(.system(size: 18, weight: .semibold))
                    Text(L.f("about_ver", L.t("app"), version)).font(.system(size: 12)).foregroundColor(.secondary)
                }
                Spacer()
            }
            Grid(alignment: .leading, horizontalSpacing: 14, verticalSpacing: 8) {
                ForEach(0..<6, id: \.self) { r in
                    GridRow {
                        keycap(keys[r].0)
                        Text(L.t(keys[r].1)).frame(width: 140, alignment: .leading)
                        keycap(keys[r + 6].0)
                        Text(L.t(keys[r + 6].1))
                    }
                }
            }
            .padding(16)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(Color(NSColor.controlBackgroundColor))
            .clipShape(RoundedRectangle(cornerRadius: 10))
            HStack {
                Spacer()
                Button(L.t("open_logs")) { Log.openFolder() }
            }
            Spacer()
        }
        .padding(20)
    }

    private var version: String {
        return Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "1.0.0"
    }

    private func keycap(_ k: String) -> some View {
        Text(k)
            .font(.system(size: 12, weight: .semibold))
            .padding(.horizontal, 8)
            .frame(minWidth: 30, minHeight: 24)
            .background(Color(NSColor.textBackgroundColor))
            .clipShape(RoundedRectangle(cornerRadius: 5))
            .overlay(RoundedRectangle(cornerRadius: 5).stroke(Color(NSColor.separatorColor)))
    }
}

struct SettingsView: View {
    @ObservedObject var s = AppSettings.shared

    var body: some View {
        TabView {
            HotkeysTab().tabItem { Label(L.t("nav_hotkeys"), systemImage: "keyboard") }
            OutputTab().tabItem { Label(L.t("nav_output"), systemImage: "folder") }
            GeneralTab().tabItem { Label(L.t("nav_general"), systemImage: "slider.horizontal.3") }
            AboutTab().tabItem { Label(L.t("nav_about"), systemImage: "info.circle") }
        }
        .frame(width: 600, height: 440)
        .id(s.lang)    // rebuild every tab when the language changes
    }
}

final class SettingsWindowController {
    private static var window: NSWindow?

    static func show() {
        if let w = window {
            w.title = L.t("set_title")
            NSApp.activate(ignoringOtherApps: true)
            w.makeKeyAndOrderFront(nil)
            return
        }
        let w = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 600, height: 440),
                         styleMask: [.titled, .closable], backing: .buffered, defer: false)
        w.title = L.t("set_title")
        w.isReleasedWhenClosed = false
        w.contentViewController = NSHostingController(rootView: SettingsView())
        w.center()
        window = w
        NSApp.activate(ignoringOtherApps: true)
        w.makeKeyAndOrderFront(nil)
    }

    static func updateTitle() {
        window?.title = L.t("set_title")
    }
}
