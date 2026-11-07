// Screen grabbing, window geometry, JPEG size fitting and saving.

import AppKit
import ImageIO
import ScreenCaptureKit

struct ScreenShot {
    let screen: NSScreen
    let image: CGImage          // full-resolution pixels of this screen
    var scale: CGFloat { return CGFloat(image.width) / screen.frame.width }
}

enum CaptureError: Error, CustomStringConvertible {
    case failed(String)
    var description: String {
        switch self { case .failed(let s): return s }
    }
}

enum Capture {
    // Freezes every display. Uses ScreenCaptureKit (in-process, macOS 14+) and falls back to the
    // `screencapture` tool. Calls back on the main thread.
    static func grabAll(completion: @escaping (Result<[ScreenShot], Error>) -> Void) {
        if #available(macOS 14.0, *) {
            Task {
                do {
                    let shots = try await grabWithScreenCaptureKit()
                    DispatchQueue.main.async { completion(.success(shots)) }
                } catch {
                    Log.write("ScreenCaptureKit failed, falling back to screencapture: \(error)", context: "capture")
                    DispatchQueue.main.async { completion(Result { try grabWithTool() }) }
                }
            }
        } else {
            completion(Result { try grabWithTool() })
        }
    }

    static func displayID(of screen: NSScreen) -> CGDirectDisplayID? {
        let key = NSDeviceDescriptionKey("NSScreenNumber")
        return (screen.deviceDescription[key] as? NSNumber)?.uint32Value
    }

    // Runs on the main actor: NSScreen must only be touched there.
    @available(macOS 14.0, *)
    @MainActor
    static func grabWithScreenCaptureKit() async throws -> [ScreenShot] {
        let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: true)
        let screens = NSScreen.screens
        var shots: [ScreenShot] = []
        for screen in screens {
            guard let id = displayID(of: screen),
                  let display = content.displays.first(where: { $0.displayID == id }) else { continue }
            let filter = SCContentFilter(display: display, excludingWindows: [])
            let config = SCStreamConfiguration()
            let scale = screen.backingScaleFactor
            config.width = Int(screen.frame.width * scale)
            config.height = Int(screen.frame.height * scale)
            config.showsCursor = false
            config.captureResolution = .best
            let image = try await SCScreenshotManager.captureImage(contentFilter: filter, configuration: config)
            shots.append(ScreenShot(screen: screen, image: image))
        }
        if shots.isEmpty { throw CaptureError.failed("ScreenCaptureKit returned no displays") }
        return shots
    }

    // True when the picture is (almost) completely black — what macOS hands out without permission.
    static func looksBlack(_ img: CGImage) -> Bool {
        guard let small = JpegFitter.resize(img, 24, 24), let data = small.dataProvider?.data,
              let ptr = CFDataGetBytePtr(data) else { return false }
        let bpr = small.bytesPerRow
        var brightest = 0
        for y in 0..<small.height {
            for x in 0..<small.width {
                let o = y * bpr + x * 4
                brightest = max(brightest, Int(ptr[o]), Int(ptr[o + 1]), Int(ptr[o + 2]))
            }
        }
        return brightest < 12
    }

    static func grabWithTool() throws -> [ScreenShot] {
        let screens = NSScreen.screens
        let dir = FileManager.default.temporaryDirectory
        let token = UUID().uuidString
        let files = screens.indices.map { dir.appendingPathComponent("litecap-\(token)-\($0).png") }

        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/usr/sbin/screencapture")
        p.arguments = ["-x"] + files.map { $0.path }
        try p.run()
        p.waitUntilExit()

        var images: [CGImage] = []
        for f in files {
            defer { try? FileManager.default.removeItem(at: f) }
            guard let src = CGImageSourceCreateWithURL(f as CFURL, nil),
                  let img = CGImageSourceCreateImageAtIndex(src, 0, nil) else { continue }
            images.append(img)
        }
        if images.isEmpty { throw CaptureError.failed("screencapture produced no image") }

        // Pair images with screens; fall back to aspect-ratio matching if the order differs.
        var result: [ScreenShot] = []
        var unused = images
        for s in screens {
            let aspect = s.frame.width / s.frame.height
            if let i = unused.firstIndex(where: { abs(CGFloat($0.width) / CGFloat($0.height) - aspect) < 0.02 }) {
                result.append(ScreenShot(screen: s, image: unused.remove(at: i)))
            } else if !unused.isEmpty {
                result.append(ScreenShot(screen: s, image: unused.removeFirst()))
            }
        }
        return result
    }

    // Height of the primary screen, used to flip between Cocoa (bottom-left) and CG (top-left) coordinates.
    static var primaryHeight: CGFloat {
        return NSScreen.screens.first?.frame.height ?? 0
    }

    // Visible app windows (front to back) in this screen's top-left view coordinates.
    static func windowRects(on screen: NSScreen) -> [CGRect] {
        let opts: CGWindowListOption = [.optionOnScreenOnly, .excludeDesktopElements]
        guard let list = CGWindowListCopyWindowInfo(opts, kCGNullWindowID) as? [[String: Any]] else { return [] }
        let me = ProcessInfo.processInfo.processIdentifier
        let sf = screen.frame
        let screenTop = primaryHeight - sf.maxY      // screen origin in CG global coordinates
        var out: [CGRect] = []
        for w in list {
            guard let layer = w[kCGWindowLayer as String] as? Int, layer == 0 else { continue }
            if let pid = w[kCGWindowOwnerPID as String] as? Int32, pid == me { continue }
            if let alpha = w[kCGWindowAlpha as String] as? Double, alpha < 0.05 { continue }
            guard let bd = w[kCGWindowBounds as String] as? NSDictionary,
                  let r = CGRect(dictionaryRepresentation: bd as CFDictionary) else { continue }
            if r.width < 40 || r.height < 40 { continue }
            let local = CGRect(x: r.minX - sf.minX, y: r.minY - screenTop, width: r.width, height: r.height)
            let clipped = local.intersection(CGRect(origin: .zero, size: sf.size))
            if !clipped.isNull && clipped.width > 1 && clipped.height > 1 { out.append(clipped) }
        }
        return out
    }
}

enum JpegFitter {
    static let qualityMax = 0.92
    static let qualityFloor = 0.35   // below this, text gets unreadable; shrink pixels instead

    static func encode(_ img: CGImage, _ quality: Double) -> Data {
        let data = NSMutableData()
        guard let dest = CGImageDestinationCreateWithData(data, "public.jpeg" as CFString, 1, nil) else { return Data() }
        let props: [CFString: Any] = [kCGImageDestinationLossyCompressionQuality: quality]
        CGImageDestinationAddImage(dest, img, props as CFDictionary)
        CGImageDestinationFinalize(dest)
        return data as Data
    }

    static func resize(_ img: CGImage, _ w: Int, _ h: Int) -> CGImage? {
        guard let ctx = CGContext(data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: 0,
                                  space: CGColorSpace(name: CGColorSpace.sRGB)!,
                                  bitmapInfo: CGImageAlphaInfo.noneSkipLast.rawValue) else { return nil }
        ctx.interpolationQuality = .high
        ctx.draw(img, in: CGRect(x: 0, y: 0, width: w, height: h))
        return ctx.makeImage()
    }

    // Highest quality whose size is strictly below maxBytes; dimensions shrink only when needed.
    static func fit(_ src: CGImage, maxBytes: Int) -> (data: Data, quality: Int, width: Int, height: Int) {
        var work = src
        var scale = 1.0
        while true {
            let top = encode(work, qualityMax)
            if top.count < maxBytes { return (top, Int(qualityMax * 100), work.width, work.height) }

            let floor = encode(work, qualityFloor)
            if floor.count < maxBytes {
                var best = floor
                var bestQ = Int(qualityFloor * 100)
                var lo = bestQ + 1, hi = Int(qualityMax * 100) - 1
                while lo <= hi {
                    let mid = (lo + hi) / 2
                    let d = encode(work, Double(mid) / 100)
                    if d.count < maxBytes { best = d; bestQ = mid; lo = mid + 1 } else { hi = mid - 1 }
                }
                return (best, bestQ, work.width, work.height)
            }

            let step = sqrt(Double(maxBytes) / Double(floor.count)) * 0.95
            scale *= max(0.4, min(0.92, step))
            let w = Int(Double(src.width) * scale), h = Int(Double(src.height) * scale)
            if w < 32 || h < 32 { return (encode(work, 0.1), 10, work.width, work.height) }
            guard let next = resize(src, w, h) else { return (floor, Int(qualityFloor * 100), work.width, work.height) }
            work = next
        }
    }
}

struct SavedShot {
    let url: URL
    let bytes: Int
    let quality: Int
    let size: CGSize
    let original: CGSize
}

enum Output {
    static func encodePNG(_ img: CGImage) -> Data {
        let data = NSMutableData()
        guard let dest = CGImageDestinationCreateWithData(data, "public.png" as CFString, 1, nil) else { return Data() }
        CGImageDestinationAddImage(dest, img, nil)
        CGImageDestinationFinalize(dest)
        return data as Data
    }

    // Safe to call off the main thread: takes plain values, not the shared settings object.
    // JPG is fitted under maxKB; PNG is lossless and has no size limit (quality is reported as 0).
    static func save(_ img: CGImage, maxKB: Int, folder: String, png: Bool) throws -> SavedShot {
        let fit: (data: Data, quality: Int, width: Int, height: Int) = png
            ? (encodePNG(img), 0, img.width, img.height)
            : JpegFitter.fit(img, maxBytes: maxKB * 1000)
        let ext = png ? "png" : "jpg"
        let dir = URL(fileURLWithPath: folder, isDirectory: true)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let fmt = DateFormatter()
        fmt.dateFormat = "yyyyMMdd_HHmmss"
        let base = "LiteCap_" + fmt.string(from: Date())
        var url = dir.appendingPathComponent(base + "." + ext)
        var n = 2
        while FileManager.default.fileExists(atPath: url.path) {
            url = dir.appendingPathComponent("\(base)_\(n).\(ext)")
            n += 1
        }
        try fit.data.write(to: url)
        return SavedShot(url: url, bytes: fit.data.count, quality: fit.quality,
                         size: CGSize(width: fit.width, height: fit.height),
                         original: CGSize(width: img.width, height: img.height))
    }

    // Copies the saved jpg/png as a file, so pasting gives exactly this file.
    static func copyFile(_ url: URL) {
        let pb = NSPasteboard.general
        pb.clearContents()
        pb.writeObjects([url as NSURL])
    }
}
