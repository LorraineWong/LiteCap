// Persistent settings (UserDefaults) and the hotkey model.

import AppKit
import Carbon.HIToolbox

struct Hotkey: Equatable, Codable {
    var keyCode: UInt32     // Carbon virtual key code
    var mods: UInt32        // Carbon modifier mask (cmdKey, shiftKey, optionKey, controlKey)

    static let empty = Hotkey(keyCode: UInt32.max, mods: 0)
    var isEmpty: Bool { return keyCode == UInt32.max }

    static let regionDefault = Hotkey(keyCode: UInt32(kVK_ANSI_1), mods: UInt32(cmdKey))
    static let fullDefault = Hotkey(keyCode: UInt32(kVK_ANSI_2), mods: UInt32(cmdKey))
    static let ocrDefault = Hotkey(keyCode: UInt32(kVK_ANSI_3), mods: UInt32(cmdKey))

    // Defaults before 1.0.0 (⇧⌘1 / ⇧⌘2 / no OCR key), used to migrate untouched installs.
    static let legacyRegion = Hotkey(keyCode: UInt32(kVK_ANSI_1), mods: UInt32(cmdKey | shiftKey))
    static let legacyFull = Hotkey(keyCode: UInt32(kVK_ANSI_2), mods: UInt32(cmdKey | shiftKey))

    var modifierSymbols: [String] {
        var out: [String] = []
        if mods & UInt32(controlKey) != 0 { out.append("⌃") }
        if mods & UInt32(optionKey) != 0 { out.append("⌥") }
        if mods & UInt32(shiftKey) != 0 { out.append("⇧") }
        if mods & UInt32(cmdKey) != 0 { out.append("⌘") }
        return out
    }

    var keyName: String { return Hotkey.name(for: keyCode) }

    var display: String {
        if isEmpty { return "" }
        return modifierSymbols.joined() + keyName
    }

    // Converts AppKit modifier flags to the Carbon mask used by RegisterEventHotKey.
    static func carbonMods(_ flags: NSEvent.ModifierFlags) -> UInt32 {
        var m: UInt32 = 0
        if flags.contains(.command) { m |= UInt32(cmdKey) }
        if flags.contains(.shift) { m |= UInt32(shiftKey) }
        if flags.contains(.option) { m |= UInt32(optionKey) }
        if flags.contains(.control) { m |= UInt32(controlKey) }
        return m
    }

    static func isFunctionKey(_ code: UInt32) -> Bool {
        let fkeys: [Int] = [kVK_F1, kVK_F2, kVK_F3, kVK_F4, kVK_F5, kVK_F6, kVK_F7, kVK_F8, kVK_F9, kVK_F10,
                            kVK_F11, kVK_F12, kVK_F13, kVK_F14, kVK_F15, kVK_F16, kVK_F17, kVK_F18, kVK_F19, kVK_F20]
        return fkeys.contains(Int(code))
    }

    static func name(for code: UInt32) -> String {
        let names: [Int: String] = [
            kVK_ANSI_A: "A", kVK_ANSI_B: "B", kVK_ANSI_C: "C", kVK_ANSI_D: "D", kVK_ANSI_E: "E", kVK_ANSI_F: "F",
            kVK_ANSI_G: "G", kVK_ANSI_H: "H", kVK_ANSI_I: "I", kVK_ANSI_J: "J", kVK_ANSI_K: "K", kVK_ANSI_L: "L",
            kVK_ANSI_M: "M", kVK_ANSI_N: "N", kVK_ANSI_O: "O", kVK_ANSI_P: "P", kVK_ANSI_Q: "Q", kVK_ANSI_R: "R",
            kVK_ANSI_S: "S", kVK_ANSI_T: "T", kVK_ANSI_U: "U", kVK_ANSI_V: "V", kVK_ANSI_W: "W", kVK_ANSI_X: "X",
            kVK_ANSI_Y: "Y", kVK_ANSI_Z: "Z",
            kVK_ANSI_0: "0", kVK_ANSI_1: "1", kVK_ANSI_2: "2", kVK_ANSI_3: "3", kVK_ANSI_4: "4",
            kVK_ANSI_5: "5", kVK_ANSI_6: "6", kVK_ANSI_7: "7", kVK_ANSI_8: "8", kVK_ANSI_9: "9",
            kVK_ANSI_Minus: "-", kVK_ANSI_Equal: "=", kVK_ANSI_LeftBracket: "[", kVK_ANSI_RightBracket: "]",
            kVK_ANSI_Semicolon: ";", kVK_ANSI_Quote: "'", kVK_ANSI_Comma: ",", kVK_ANSI_Period: ".",
            kVK_ANSI_Slash: "/", kVK_ANSI_Backslash: "\\", kVK_ANSI_Grave: "`",
            kVK_Space: "Space", kVK_Return: "↩", kVK_Tab: "⇥", kVK_Escape: "⎋",
            kVK_F1: "F1", kVK_F2: "F2", kVK_F3: "F3", kVK_F4: "F4", kVK_F5: "F5", kVK_F6: "F6",
            kVK_F7: "F7", kVK_F8: "F8", kVK_F9: "F9", kVK_F10: "F10", kVK_F11: "F11", kVK_F12: "F12",
            kVK_F13: "F13", kVK_F14: "F14", kVK_F15: "F15", kVK_F16: "F16", kVK_F17: "F17", kVK_F18: "F18",
            kVK_F19: "F19", kVK_F20: "F20",
            kVK_LeftArrow: "←", kVK_RightArrow: "→", kVK_UpArrow: "↑", kVK_DownArrow: "↓",
        ]
        return names[Int(code)] ?? "#\(code)"
    }

    // Key equivalent string for showing the shortcut in a menu (letters and digits only).
    var menuKeyEquivalent: String? {
        if isEmpty { return nil }
        let n = keyName
        if n.count == 1 { return n.lowercased() }
        return nil
    }

    var menuModifierMask: NSEvent.ModifierFlags {
        var f: NSEvent.ModifierFlags = []
        if mods & UInt32(cmdKey) != 0 { f.insert(.command) }
        if mods & UInt32(shiftKey) != 0 { f.insert(.shift) }
        if mods & UInt32(optionKey) != 0 { f.insert(.option) }
        if mods & UInt32(controlKey) != 0 { f.insert(.control) }
        return f
    }
}

final class AppSettings: ObservableObject {
    static let shared = AppSettings()
    private let d = UserDefaults.standard

    @Published var maxKB: Int { didSet { d.set(maxKB, forKey: "maxKB") } }
    @Published var png: Bool { didSet { d.set(png ? "png" : "jpg", forKey: "format") } }   // PNG: lossless, no size limit
    @Published var editAfterCapture: Bool { didSet { d.set(editAfterCapture, forKey: "editAfterCapture") } }
    @Published var showPreview: Bool { didSet { d.set(showPreview, forKey: "showPreview") } }
    @Published var folder: String { didSet { d.set(folder, forKey: "folder") } }
    @Published var lang: Lang { didSet { d.set(lang.rawValue, forKey: "lang"); L.lang = lang } }
    @Published var hkRegion: Hotkey { didSet { store(hkRegion, "hkRegion") } }
    @Published var hkFull: Hotkey { didSet { store(hkFull, "hkFull") } }
    @Published var hkOcr: Hotkey { didSet { store(hkOcr, "hkOcr") } }

    // Transient messages shown in the settings window (not saved).
    @Published var hotkeyWarning: String = ""
    @Published var loginError: String = ""

    static var defaultFolder: String {
        let pics = FileManager.default.urls(for: .picturesDirectory, in: .userDomainMask).first
            ?? URL(fileURLWithPath: NSHomeDirectory()).appendingPathComponent("Pictures")
        return pics.appendingPathComponent("LiteCap").path
    }

    private init() {
        // Read through a local reference: `self` is not usable until every property is set.
        let ud = UserDefaults.standard
        let kb = ud.integer(forKey: "maxKB")
        maxKB = kb >= 10 ? kb : 100
        png = ud.string(forKey: "format") == "png"
        editAfterCapture = ud.object(forKey: "editAfterCapture") as? Bool ?? true
        showPreview = ud.object(forKey: "showPreview") as? Bool ?? true
        folder = ud.string(forKey: "folder") ?? AppSettings.defaultFolder
        // English is the default; langVer 1 resets older installs to English once.
        if ud.integer(forKey: "langVer") < 1 {
            ud.set(Lang.en.rawValue, forKey: "lang")
            ud.set(1, forKey: "langVer")
        }
        lang = Lang(rawValue: ud.string(forKey: "lang") ?? "") ?? .en
        // hkVer 1: defaults moved to ⌘1 / ⌘2 / ⌘3. Only hotkeys still at the old defaults are reset;
        // anything the user customised is kept.
        if ud.integer(forKey: "hkVer") < 1 {
            if AppSettings.load(ud, "hkRegion") == Hotkey.legacyRegion { ud.removeObject(forKey: "hkRegion") }
            if AppSettings.load(ud, "hkFull") == Hotkey.legacyFull { ud.removeObject(forKey: "hkFull") }
            if AppSettings.load(ud, "hkOcr") == Hotkey.empty { ud.removeObject(forKey: "hkOcr") }
            ud.set(1, forKey: "hkVer")
        }
        hkRegion = AppSettings.load(ud, "hkRegion") ?? Hotkey.regionDefault
        hkFull = AppSettings.load(ud, "hkFull") ?? Hotkey.fullDefault
        hkOcr = AppSettings.load(ud, "hkOcr") ?? Hotkey.ocrDefault
        L.lang = lang
    }

    private func store(_ h: Hotkey, _ key: String) {
        if let data = try? JSONEncoder().encode(h) { d.set(data, forKey: key) }
    }

    private static func load(_ d: UserDefaults, _ key: String) -> Hotkey? {
        guard let data = d.data(forKey: key) else { return nil }
        return try? JSONDecoder().decode(Hotkey.self, from: data)
    }
}
