// System-wide hotkeys through Carbon's RegisterEventHotKey.
// This API needs no Accessibility permission and keeps working while other apps are in front.

import AppKit
import Carbon.HIToolbox

final class HotKeyCenter {
    static let shared = HotKeyCenter()

    private var refs: [UInt32: EventHotKeyRef] = [:]
    private var actions: [UInt32: () -> Void] = [:]
    private var installed = false
    private let signature: OSType = 0x4C43_4150   // 'LCAP'

    private func install() {
        if installed { return }
        installed = true
        var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        InstallEventHandler(GetApplicationEventTarget(), { _, event, _ -> OSStatus in
            var hk = EventHotKeyID()
            let status = GetEventParameter(event, EventParamName(kEventParamDirectObject), EventParamType(typeEventHotKeyID),
                                           nil, MemoryLayout<EventHotKeyID>.size, nil, &hk)
            if status == noErr {
                let id = hk.id
                DispatchQueue.main.async { HotKeyCenter.shared.fire(id) }
            }
            return noErr
        }, 1, &spec, nil, nil)
    }

    // Returns false when the combination could not be registered.
    @discardableResult
    func register(id: UInt32, hotkey: Hotkey, action: @escaping () -> Void) -> Bool {
        install()
        unregister(id)
        if hotkey.isEmpty { return true }
        var ref: EventHotKeyRef?
        let hkID = EventHotKeyID(signature: signature, id: id)
        let status = RegisterEventHotKey(hotkey.keyCode, hotkey.mods, hkID, GetApplicationEventTarget(), 0, &ref)
        guard status == noErr, let r = ref else { return false }
        refs[id] = r
        actions[id] = action
        return true
    }

    func unregister(_ id: UInt32) {
        if let r = refs[id] { UnregisterEventHotKey(r) }
        refs[id] = nil
        actions[id] = nil
    }

    func unregisterAll() {
        for id in Array(refs.keys) { unregister(id) }
    }

    private func fire(_ id: UInt32) {
        actions[id]?()
    }
}
