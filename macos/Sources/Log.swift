// Error log in ~/Library/Logs/LiteCap — one file per month, written only when something goes wrong.

import AppKit

enum Log {
    static var folder: URL {
        return FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/Logs/LiteCap", isDirectory: true)
    }

    static func write(_ message: String, context: String) {
        let fm = FileManager.default
        try? fm.createDirectory(at: folder, withIntermediateDirectories: true)
        let month = DateFormatter()
        month.dateFormat = "yyyy-MM"
        let stamp = DateFormatter()
        stamp.dateFormat = "yyyy-MM-dd HH:mm:ss"
        let file = folder.appendingPathComponent("litecap-\(month.string(from: Date())).log")
        let os = ProcessInfo.processInfo.operatingSystemVersionString
        let version = Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "?"
        let entry = "==== \(stamp.string(from: Date()))  [\(context)]\nLiteCap \(version)  |  macOS \(os)  |  lang \(L.lang.rawValue)\n\(message)\n\n"
        guard let data = entry.data(using: .utf8) else { return }
        if let h = try? FileHandle(forWritingTo: file) {
            h.seekToEndOfFile()
            h.write(data)
            h.closeFile()
        } else {
            try? data.write(to: file)
        }
    }

    static func write(_ error: Error, context: String) {
        write(String(describing: error), context: context)
    }

    static func installExceptionHandler() {
        NSSetUncaughtExceptionHandler { e in
            Log.write("\(e.name.rawValue): \(e.reason ?? "")\n\(e.callStackSymbols.joined(separator: "\n"))", context: "exception")
        }
    }

    static func openFolder() {
        try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        NSWorkspace.shared.open(folder)
    }
}
