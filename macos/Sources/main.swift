// Entry point. Only one copy of LiteCap runs at a time.

import AppKit

let bundleID = Bundle.main.bundleIdentifier ?? "app.litecap.mac"
let others = NSRunningApplication.runningApplications(withBundleIdentifier: bundleID)
    .filter { $0.processIdentifier != ProcessInfo.processInfo.processIdentifier }
if !others.isEmpty {
    exit(0)
}

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.run()
