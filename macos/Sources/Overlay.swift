// Full-screen capture overlay: one window per display showing the frozen screen.
// Drag to select or click a window, adjust, annotate, then finish or cancel.

import AppKit

struct SessionResult {
    let image: CGImage      // cropped pixels (annotations included unless OCR)
    let ocr: Bool
}

final class CaptureSession {
    private var windows: [OverlayWindow] = []
    private var views: [OverlayView] = []
    private let completion: (SessionResult?) -> Void
    private var finished = false

    init(shots: [ScreenShot], ocrMode: Bool, editable: Bool, completion: @escaping (SessionResult?) -> Void) {
        self.completion = completion
        for shot in shots {
            let win = OverlayWindow(screen: shot.screen)
            let view = OverlayView(shot: shot, session: self, ocrMode: ocrMode,
                                   editable: editable && !ocrMode,
                                   windows: Capture.windowRects(on: shot.screen))
            win.contentView = view
            windows.append(win)
            views.append(view)
        }
    }

    func begin() {
        NSApp.activate(ignoringOtherApps: true)
        let mouse = NSEvent.mouseLocation
        for (i, w) in windows.enumerated() {
            w.orderFrontRegardless()
            if w.screen?.frame.contains(mouse) == true || i == 0 {
                w.makeKey()
                w.makeFirstResponder(views[i])
            }
        }
        NSCursor.crosshair.set()
    }

    // Another display started a selection: clear hover/selection on the others.
    func activate(_ view: OverlayView) {
        for v in views where v !== view { v.resetToIdle() }
    }

    func finish(view: OverlayView, rect: CGRect, shapes: [Shape], ocr: Bool) {
        guard !finished else { return }
        let image = render(view: view, rect: rect, shapes: ocr ? [] : shapes)
        close()
        if let img = image { completion(SessionResult(image: img, ocr: ocr)) } else { completion(nil) }
    }

    func cancel() {
        guard !finished else { return }
        close()
        completion(nil)
    }

    private func close() {
        finished = true
        for w in windows { w.orderOut(nil) }
        windows.removeAll()
        views.removeAll()
    }

    private func render(view: OverlayView, rect: CGRect, shapes: [Shape]) -> CGImage? {
        let s = view.shot.scale
        let px = CGRect(x: rect.minX * s, y: rect.minY * s, width: rect.width * s, height: rect.height * s).integral
        guard let crop = view.shot.image.cropping(to: px) else { return nil }
        if shapes.isEmpty { return crop }
        let w = crop.width, h = crop.height
        guard let ctx = CGContext(data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: 0,
                                  space: CGColorSpace(name: CGColorSpace.sRGB)!,
                                  bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return crop }
        ctx.draw(crop, in: CGRect(x: 0, y: 0, width: w, height: h))
        // Switch to the view's coordinate space: top-left origin, points.
        ctx.translateBy(x: 0, y: CGFloat(h))
        ctx.scaleBy(x: 1, y: -1)
        ctx.scaleBy(x: s, y: s)
        ctx.translateBy(x: -px.minX / s, y: -px.minY / s)
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = NSGraphicsContext(cgContext: ctx, flipped: true)
        for shape in shapes { shape.draw(source: view.shot.image, scale: s) }
        NSGraphicsContext.restoreGraphicsState()
        return ctx.makeImage() ?? crop
    }
}

final class OverlayWindow: NSWindow {
    init(screen: NSScreen) {
        super.init(contentRect: screen.frame, styleMask: [.borderless], backing: .buffered, defer: false)
        setFrame(screen.frame, display: false)
        level = .screenSaver
        isOpaque = true
        backgroundColor = .black
        hasShadow = false
        acceptsMouseMovedEvents = true
        isReleasedWhenClosed = false
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary]
    }

    override var canBecomeKey: Bool { return true }
    override var canBecomeMain: Bool { return true }
}

final class OverlayView: NSView, NSTextFieldDelegate {
    enum State { case idle, creating, adjusting, moving, resizing }
    enum Hit { case none, move, n, s, e, w, ne, nw, se, sw }
    enum Tool: String { case off, rect, ellipse, arrow, pen, marker, text, mosaic }

    struct Btn {
        let id: String
        let rect: CGRect
        let tip: String?
    }

    static let palette: [NSColor] = [
        NSColor(srgbRed: 0.94, green: 0.27, blue: 0.27, alpha: 1), NSColor(srgbRed: 0.98, green: 0.45, blue: 0.09, alpha: 1),
        NSColor(srgbRed: 0.98, green: 0.80, blue: 0.08, alpha: 1), NSColor(srgbRed: 0.13, green: 0.77, blue: 0.37, alpha: 1),
        NSColor(srgbRed: 0.05, green: 0.65, blue: 0.91, alpha: 1), NSColor(srgbRed: 0.66, green: 0.33, blue: 0.97, alpha: 1),
        NSColor.white, NSColor(srgbRed: 0.07, green: 0.09, blue: 0.15, alpha: 1)]
    static let strokeWidths: [CGFloat] = [2, 4, 7]
    static let textSizes: [CGFloat] = [16, 22, 32]
    static let mosaicCells: [CGFloat] = [6, 10, 16]
    static let tools: [(Tool, String, String)] = [
        (.rect, "square", "tip_rect"), (.ellipse, "circle", "tip_ellipse"), (.arrow, "arrow.up.right", "tip_arrow"),
        (.pen, "pencil", "tip_pen"), (.marker, "highlighter", "tip_marker"), (.text, "textformat", "tip_text"),
        (.mosaic, "checkerboard.rectangle", "tip_mosaic")]
    static var colorIdx = 0
    static var sizeIdx = 1

    // Dark toolbar palette (matches the Windows build).
    static let barBg = NSColor(srgbRed: 28 / 255, green: 31 / 255, blue: 38 / 255, alpha: 0.97)
    static let barSoft = NSColor(srgbRed: 48 / 255, green: 52 / 255, blue: 62 / 255, alpha: 1)
    static let barLine = NSColor(srgbRed: 66 / 255, green: 71 / 255, blue: 84 / 255, alpha: 1)
    static let accent = NSColor(srgbRed: 37 / 255, green: 99 / 255, blue: 235 / 255, alpha: 1)
    static let accentHover = NSColor(srgbRed: 59 / 255, green: 130 / 255, blue: 246 / 255, alpha: 1)
    static let textMain = NSColor(srgbRed: 236 / 255, green: 239 / 255, blue: 244 / 255, alpha: 1)

    let shot: ScreenShot
    private weak var session: CaptureSession?
    private let ocrMode: Bool
    private let editable: Bool
    private let windowRects: [CGRect]
    private let frozen: NSImage

    private var state: State = .idle
    private var pending = false
    private var downPt = CGPoint.zero
    private var cursorPt = CGPoint.zero
    private var sel = CGRect.zero
    private var selAtDown = CGRect.zero
    private var hover = CGRect.zero
    private var grab: Hit = .none
    private var tool: Tool = .off
    private var shapes: [Shape] = []
    private var drawing: Shape?
    private var editor: NSTextField?
    private var buttons: [Btn] = []
    private var mainBar = CGRect.zero
    private var subBar = CGRect.zero
    private var hoverBtn: String?
    private var dormant = false        // another display owns the selection

    override var isFlipped: Bool { return true }
    override var acceptsFirstResponder: Bool { return true }

    init(shot: ScreenShot, session: CaptureSession, ocrMode: Bool, editable: Bool, windows: [CGRect]) {
        self.shot = shot
        self.session = session
        self.ocrMode = ocrMode
        self.editable = editable
        self.windowRects = windows
        self.frozen = NSImage(cgImage: shot.image, size: shot.screen.frame.size)
        super.init(frame: CGRect(origin: .zero, size: shot.screen.frame.size))
        let area = NSTrackingArea(rect: .zero, options: [.mouseMoved, .activeAlways, .inVisibleRect, .mouseEnteredAndExited],
                                  owner: self, userInfo: nil)
        addTrackingArea(area)
    }

    required init?(coder: NSCoder) { fatalError("not used") }

    private var full: CGRect { return bounds }
    private var activeRect: CGRect { return state == .idle ? (dormant ? .zero : hover) : sel }
    private var showBars: Bool { return state == .adjusting && editable }

    func resetToIdle() {
        commitText()
        state = .idle
        sel = .zero
        shapes.removeAll()
        tool = .off
        dormant = true
        needsDisplay = true
    }

    private func windowAt(_ p: CGPoint) -> CGRect {
        for r in windowRects where r.contains(p) { return r }
        return full
    }

    // MARK: geometry

    private func fromPoints(_ a: CGPoint, _ b: CGPoint) -> CGRect {
        return CGRect(x: min(a.x, b.x), y: min(a.y, b.y), width: abs(a.x - b.x), height: abs(a.y - b.y))
    }

    private func moved(_ r: CGRect, _ dx: CGFloat, _ dy: CGFloat) -> CGRect {
        let x = max(0, min(full.width - r.width, r.minX + dx))
        let y = max(0, min(full.height - r.height, r.minY + dy))
        return CGRect(x: x, y: y, width: r.width, height: r.height)
    }

    private func resized(_ r: CGRect, _ h: Hit, _ dx: CGFloat, _ dy: CGFloat) -> CGRect {
        var l = r.minX, t = r.minY, rt = r.maxX, b = r.maxY
        if h == .w || h == .nw || h == .sw { l += dx }
        if h == .e || h == .ne || h == .se { rt += dx }
        if h == .n || h == .ne || h == .nw { t += dy }
        if h == .s || h == .se || h == .sw { b += dy }
        let n = CGRect(x: min(l, rt), y: min(t, b), width: max(1, abs(rt - l)), height: max(1, abs(b - t)))
        return n.intersection(full)
    }

    private func handleAt(_ p: CGPoint) -> Hit {
        if state != .adjusting || tool != .off || sel.isEmpty { return .none }
        let tol: CGFloat = 6
        let nl = abs(p.x - sel.minX) <= tol, nr = abs(p.x - sel.maxX) <= tol
        let nt = abs(p.y - sel.minY) <= tol, nb = abs(p.y - sel.maxY) <= tol
        let inX = p.x >= sel.minX - tol && p.x <= sel.maxX + tol
        let inY = p.y >= sel.minY - tol && p.y <= sel.maxY + tol
        if nt && nl { return .nw }
        if nt && nr { return .ne }
        if nb && nl { return .sw }
        if nb && nr { return .se }
        if nt && inX { return .n }
        if nb && inX { return .s }
        if nl && inY { return .w }
        if nr && inY { return .e }
        if sel.contains(p) { return .move }
        return .none
    }

    private func cursor(for h: Hit) -> NSCursor {
        switch h {
        case .n, .s: return .resizeUpDown
        case .e, .w: return .resizeLeftRight
        case .move: return .openHand
        default: return .crosshair
        }
    }

    // MARK: toolbar layout

    private func layoutBars() {
        buttons = []
        mainBar = .zero
        subBar = .zero
        if !showBars { return }

        let bs: CGFloat = 32, gap: CGFloat = 2, pad: CGFloat = 6, sep: CGFloat = 13
        let doneW = max(84, (L.t("done") as NSString).size(withAttributes: [.font: NSFont.systemFont(ofSize: 13, weight: .semibold)]).width + 46)
        let H = bs + pad * 2
        let count = CGFloat(OverlayView.tools.count)
        let W = pad + count * bs + (count - 1) * gap + sep + bs + gap + bs + sep + bs + gap * 2 + doneW + pad
        let showSub = tool != .off
        let dot: CGFloat = 28, sw: CGFloat = 22, swGap: CGFloat = 5, subH: CGFloat = 38
        let palette = CGFloat(OverlayView.palette.count)
        let subW = pad + 3 * dot + (tool == .mosaic ? 0 : sep + palette * sw + (palette - 1) * swGap) + pad + 4
        let need = H + (showSub ? subH + 6 : 0)
        let margin: CGFloat = 10

        let x = max(0, min(sel.maxX - W, full.width - W))
        var y: CGFloat
        var above = false
        if sel.maxY + margin + need <= full.height { y = sel.maxY + margin }
        else if sel.minY - margin - need >= 0 { y = sel.minY - margin - H; above = true }
        else { y = sel.maxY - margin - H; above = true }
        mainBar = CGRect(x: x, y: y, width: W, height: H)
        if showSub {
            let sx = max(0, min(x, full.width - subW))
            subBar = above ? CGRect(x: sx, y: y - 6 - subH, width: subW, height: subH)
                           : CGRect(x: sx, y: y + H + 6, width: subW, height: subH)
        }

        var bx = x + pad
        let by = y + pad
        for (t, _, tip) in OverlayView.tools {
            buttons.append(Btn(id: "tool:" + t.rawValue, rect: CGRect(x: bx, y: by, width: bs, height: bs), tip: L.t(tip)))
            bx += bs + gap
        }
        bx += sep - gap
        buttons.append(Btn(id: "undo", rect: CGRect(x: bx, y: by, width: bs, height: bs), tip: L.t("tip_undo")))
        bx += bs + gap
        buttons.append(Btn(id: "ocr", rect: CGRect(x: bx, y: by, width: bs, height: bs), tip: L.t("tip_ocr")))
        bx += bs + sep
        buttons.append(Btn(id: "cancel", rect: CGRect(x: bx, y: by, width: bs, height: bs), tip: L.t("tip_cancel")))
        bx += bs + gap * 2
        buttons.append(Btn(id: "done", rect: CGRect(x: bx, y: by, width: doneW, height: bs), tip: L.t("tip_done")))

        if showSub {
            var sx2 = subBar.minX + pad
            for i in 0..<3 {
                buttons.append(Btn(id: "size:\(i)", rect: CGRect(x: sx2, y: subBar.minY + (subH - dot) / 2, width: dot, height: dot), tip: nil))
                sx2 += dot
            }
            if tool != .mosaic {
                sx2 += sep
                for i in 0..<OverlayView.palette.count {
                    buttons.append(Btn(id: "color:\(i)", rect: CGRect(x: sx2, y: subBar.minY + (subH - sw) / 2, width: sw, height: sw), tip: nil))
                    sx2 += sw + swGap
                }
            }
        }
    }

    private func button(at p: CGPoint) -> Btn? {
        return buttons.first(where: { $0.rect.contains(p) })
    }

    // MARK: drawing

    private static var symbolCache: [String: NSImage] = [:]

    private static func symbol(_ name: String, _ color: NSColor, _ size: CGFloat = 15) -> NSImage? {
        let key = "\(name)|\(size)|\(color.description)"
        if let img = symbolCache[key] { return img }
        guard let base = NSImage(systemSymbolName: name, accessibilityDescription: nil)?
            .withSymbolConfiguration(NSImage.SymbolConfiguration(pointSize: size, weight: .medium)) else { return nil }
        let img = NSImage(size: base.size)
        img.lockFocus()
        base.draw(at: .zero, from: .zero, operation: .sourceOver, fraction: 1)
        color.set()
        CGRect(origin: .zero, size: base.size).fill(using: .sourceAtop)
        img.unlockFocus()
        symbolCache[key] = img
        return img
    }

    private func drawSymbol(_ name: String, in r: CGRect, color: NSColor) {
        guard let img = OverlayView.symbol(name, color) else { return }
        let s = img.size
        let dst = CGRect(x: r.midX - s.width / 2, y: r.midY - s.height / 2, width: s.width, height: s.height)
        img.draw(in: dst, from: .zero, operation: .sourceOver, fraction: 1, respectFlipped: true, hints: nil)
    }

    private func drawTag(_ text: String, at r: CGRect, font: NSFont, color: NSColor = .white) {
        OverlayView.barBg.setFill()
        NSBezierPath(roundedRect: r, xRadius: 6, yRadius: 6).fill()
        let a = NSAttributedString(string: text, attributes: [.font: font, .foregroundColor: color])
        let s = a.size()
        a.draw(at: CGPoint(x: r.midX - s.width / 2, y: r.midY - s.height / 2))
    }

    private func tagRect(_ text: String, font: NSFont, near p: CGPoint, above: Bool) -> CGRect {
        let s = (text as NSString).size(withAttributes: [.font: font])
        let w = s.width + 16, h = s.height + 8
        let x = max(0, min(p.x, full.width - w))
        var y = above ? p.y - h - 6 : p.y + 6
        if y < 0 { y = p.y + 6 }
        return CGRect(x: x, y: y, width: w, height: h)
    }

    override func draw(_ dirtyRect: NSRect) {
        frozen.draw(in: bounds, from: .zero, operation: .copy, fraction: 1, respectFlipped: true, hints: nil)

        let a = activeRect
        let dim = NSBezierPath(rect: bounds)
        if !a.isEmpty {
            dim.append(NSBezierPath(rect: a))
            dim.windingRule = .evenOdd
        }
        NSColor(white: 0, alpha: 0.45).setFill()
        dim.fill()

        if !a.isEmpty && (!shapes.isEmpty || drawing != nil) {
            NSGraphicsContext.saveGraphicsState()
            NSBezierPath(rect: a).addClip()
            for s in shapes { s.draw(source: shot.image, scale: shot.scale) }
            drawing?.draw(source: shot.image, scale: shot.scale)
            NSGraphicsContext.restoreGraphicsState()
        }

        if !a.isEmpty {
            OverlayView.accentHover.setStroke()
            let border = NSBezierPath(rect: a.insetBy(dx: 1, dy: 1))
            border.lineWidth = 2
            border.stroke()
            if state != .idle && tool == .off {
                for h in handleRects(a) {
                    NSColor.white.setFill()
                    let o = NSBezierPath(ovalIn: h)
                    o.fill()
                    OverlayView.accentHover.setStroke()
                    o.lineWidth = 1.5
                    o.stroke()
                }
            }
            let label = "\(Int(a.width * shot.scale)) × \(Int(a.height * shot.scale))"
            let f = NSFont.monospacedDigitSystemFont(ofSize: 12, weight: .semibold)
            drawTag(label, at: tagRect(label, font: f, near: CGPoint(x: a.minX, y: a.minY), above: true), font: f)
        }

        if (state == .idle || state == .creating) && !dormant && window?.isKeyWindow == true {
            let hint = L.t(ocrMode ? "hint_ocr" : "hint_region")
            let f = NSFont.systemFont(ofSize: 14)
            let s = (hint as NSString).size(withAttributes: [.font: f])
            let r = CGRect(x: (full.width - s.width - 32) / 2, y: 36, width: s.width + 32, height: s.height + 16)
            if !r.intersects(a) || a == full { drawTag(hint, at: r, font: f, color: OverlayView.textMain) }
        }

        if showMagnifier { drawMagnifier() }
        if showBars { drawBars() }
    }

    // MARK: magnifier (pixel loupe with coordinates and colour, as on Windows)

    private static let magCells = 15      // pixels shown per side
    private static let magZoom: CGFloat = 8
    private static let magInfoH: CGFloat = 40

    private var showMagnifier: Bool {
        return (state == .idle || state == .creating || state == .resizing) && !dormant && !pending
            && window?.isKeyWindow == true && full.contains(cursorPt)
    }

    private var magRect: CGRect {
        let side = CGFloat(OverlayView.magCells) * OverlayView.magZoom
        let w = side, h = side + OverlayView.magInfoH
        var x = cursorPt.x + 20, y = cursorPt.y + 20
        if x + w > full.width { x = cursorPt.x - 20 - w }
        if y + h > full.height { y = cursorPt.y - 20 - h }
        return CGRect(x: x, y: y, width: w, height: h)
    }

    /// Cursor position in image pixels, clamped to the image.
    private var cursorPixel: (x: Int, y: Int) {
        let s = shot.scale
        let px = Int((cursorPt.x * s).rounded(.down)), py = Int((cursorPt.y * s).rounded(.down))
        return (max(0, min(shot.image.width - 1, px)), max(0, min(shot.image.height - 1, py)))
    }

    private func pixelColor(x: Int, y: Int) -> (r: Int, g: Int, b: Int)? {
        guard let one = shot.image.cropping(to: CGRect(x: x, y: y, width: 1, height: 1)) else { return nil }
        var data = [UInt8](repeating: 0, count: 4)
        let ok: Bool = data.withUnsafeMutableBytes { buf in
            guard let ctx = CGContext(data: buf.baseAddress, width: 1, height: 1, bitsPerComponent: 8, bytesPerRow: 4,
                                      space: CGColorSpace(name: CGColorSpace.sRGB) ?? CGColorSpaceCreateDeviceRGB(),
                                      bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return false }
            ctx.draw(one, in: CGRect(x: 0, y: 0, width: 1, height: 1))
            return true
        }
        return ok ? (Int(data[0]), Int(data[1]), Int(data[2])) : nil
    }

    private func drawMagnifier() {
        let m = magRect
        let cells = OverlayView.magCells, half = cells / 2, zoom = OverlayView.magZoom
        let side = CGFloat(cells) * zoom
        let img = CGRect(x: m.minX, y: m.minY, width: side, height: side)
        NSColor.black.setFill()
        img.fill()

        let c = cursorPixel
        // Source square may run past the image edge: crop what exists and place it at the right offset.
        let src = CGRect(x: c.x - half, y: c.y - half, width: cells, height: cells)
        let bounded = src.intersection(CGRect(x: 0, y: 0, width: shot.image.width, height: shot.image.height))
        if !bounded.isNull, let crop = shot.image.cropping(to: bounded), let ctx = NSGraphicsContext.current?.cgContext {
            let dst = CGRect(x: img.minX + (bounded.minX - src.minX) * zoom, y: img.minY + (bounded.minY - src.minY) * zoom,
                             width: bounded.width * zoom, height: bounded.height * zoom)
            ctx.saveGState()
            ctx.interpolationQuality = .none
            // The view is flipped; flip the image back so it is drawn upright.
            ctx.translateBy(x: 0, y: dst.maxY + dst.minY)
            ctx.scaleBy(x: 1, y: -1)
            ctx.draw(crop, in: dst)
            ctx.restoreGState()
        }

        OverlayView.accent.withAlphaComponent(0.4).setFill()
        CGRect(x: img.minX, y: img.minY + CGFloat(half) * zoom, width: side, height: zoom).fill()
        CGRect(x: img.minX + CGFloat(half) * zoom, y: img.minY, width: zoom, height: side).fill()
        NSColor.white.setStroke()
        let center = NSBezierPath(rect: CGRect(x: img.minX + CGFloat(half) * zoom + 0.5, y: img.minY + CGFloat(half) * zoom + 0.5,
                                               width: zoom - 1, height: zoom - 1))
        center.lineWidth = 1
        center.stroke()

        let info = CGRect(x: m.minX, y: img.maxY, width: side, height: OverlayView.magInfoH)
        OverlayView.barBg.setFill()
        info.fill()
        var text = "X \(c.x)  Y \(c.y)"
        if let rgb = pixelColor(x: c.x, y: c.y) { text += "\nRGB \(rgb.r),\(rgb.g),\(rgb.b)" }
        let para = NSMutableParagraphStyle()
        para.alignment = .center
        let a = NSAttributedString(string: text, attributes: [
            .font: NSFont.monospacedSystemFont(ofSize: 11, weight: .regular),
            .foregroundColor: OverlayView.textMain, .paragraphStyle: para])
        let h = a.boundingRect(with: CGSize(width: side, height: info.height), options: [.usesLineFragmentOrigin]).height
        a.draw(with: CGRect(x: info.minX, y: info.midY - h / 2, width: side, height: h), options: [.usesLineFragmentOrigin])

        NSColor(white: 1, alpha: 0.35).setStroke()
        let frame = NSBezierPath(rect: m.insetBy(dx: 0.5, dy: 0.5))
        frame.lineWidth = 1
        frame.stroke()
    }

    private func handleRects(_ r: CGRect) -> [CGRect] {
        let s: CGFloat = 8
        let pts = [CGPoint(x: r.minX, y: r.minY), CGPoint(x: r.midX, y: r.minY), CGPoint(x: r.maxX, y: r.minY),
                   CGPoint(x: r.minX, y: r.midY), CGPoint(x: r.maxX, y: r.midY),
                   CGPoint(x: r.minX, y: r.maxY), CGPoint(x: r.midX, y: r.maxY), CGPoint(x: r.maxX, y: r.maxY)]
        return pts.map { CGRect(x: $0.x - s / 2, y: $0.y - s / 2, width: s, height: s) }
    }

    private func drawBar(_ r: CGRect) {
        let shadow = NSShadow()
        shadow.shadowColor = NSColor(white: 0, alpha: 0.35)
        shadow.shadowBlurRadius = 12
        shadow.shadowOffset = NSSize(width: 0, height: -3)
        NSGraphicsContext.saveGraphicsState()
        shadow.set()
        OverlayView.barBg.setFill()
        NSBezierPath(roundedRect: r, xRadius: 10, yRadius: 10).fill()
        NSGraphicsContext.restoreGraphicsState()
        NSColor(white: 1, alpha: 0.12).setStroke()
        let p = NSBezierPath(roundedRect: r.insetBy(dx: 0.5, dy: 0.5), xRadius: 10, yRadius: 10)
        p.lineWidth = 1
        p.stroke()
    }

    private func drawBars() {
        drawBar(mainBar)
        if !subBar.isEmpty { drawBar(subBar) }
        let sepTop = mainBar.minY + 12, sepBottom = mainBar.maxY - 12

        for b in buttons {
            let isHover = b.id == hoverBtn
            if b.id.hasPrefix("tool:") {
                let name = String(b.id.dropFirst(5))
                let active = tool.rawValue == name
                if active { OverlayView.accent.setFill(); NSBezierPath(roundedRect: b.rect, xRadius: 7, yRadius: 7).fill() }
                else if isHover { OverlayView.barSoft.setFill(); NSBezierPath(roundedRect: b.rect, xRadius: 7, yRadius: 7).fill() }
                let sym = OverlayView.tools.first(where: { $0.0.rawValue == name })?.1 ?? "square"
                drawSymbol(sym, in: b.rect, color: active ? .white : OverlayView.textMain)
            } else if b.id == "undo" || b.id == "ocr" || b.id == "cancel" {
                if isHover {
                    (b.id == "cancel" ? NSColor(srgbRed: 0.94, green: 0.27, blue: 0.27, alpha: 0.3) : OverlayView.barSoft).setFill()
                    NSBezierPath(roundedRect: b.rect, xRadius: 7, yRadius: 7).fill()
                }
                let name = b.id == "undo" ? "arrow.uturn.backward" : (b.id == "ocr" ? "text.viewfinder" : "xmark")
                var c = OverlayView.textMain
                if b.id == "undo" && shapes.isEmpty && editor == nil { c = OverlayView.textMain.withAlphaComponent(0.35) }
                drawSymbol(name, in: b.rect, color: c)
                if b.id != "ocr" {
                    OverlayView.barLine.setStroke()
                    let l = NSBezierPath()
                    l.move(to: CGPoint(x: b.rect.minX - 7, y: sepTop))
                    l.line(to: CGPoint(x: b.rect.minX - 7, y: sepBottom))
                    l.lineWidth = 1
                    l.stroke()
                }
            } else if b.id == "done" {
                (isHover ? OverlayView.accentHover : OverlayView.accent).setFill()
                NSBezierPath(roundedRect: b.rect, xRadius: 7, yRadius: 7).fill()
                let f = NSFont.systemFont(ofSize: 13, weight: .semibold)
                let t = NSAttributedString(string: L.t("done"), attributes: [.font: f, .foregroundColor: NSColor.white])
                let ts = t.size()
                let icon = OverlayView.symbol("checkmark", .white, 13)
                let iw = (icon?.size.width ?? 0) + 6
                let start = b.rect.midX - (iw + ts.width) / 2
                if let ic = icon {
                    ic.draw(in: CGRect(x: start, y: b.rect.midY - ic.size.height / 2, width: ic.size.width, height: ic.size.height),
                            from: .zero, operation: .sourceOver, fraction: 1, respectFlipped: true, hints: nil)
                }
                t.draw(at: CGPoint(x: start + iw, y: b.rect.midY - ts.height / 2))
            } else if b.id.hasPrefix("size:") {
                let i = Int(b.id.dropFirst(5)) ?? 0
                if i == OverlayView.sizeIdx { OverlayView.barSoft.setFill(); NSBezierPath(roundedRect: b.rect, xRadius: 7, yRadius: 7).fill() }
                let d: CGFloat = 5 + CGFloat(i) * 4
                var c = tool == .mosaic ? OverlayView.textMain : OverlayView.palette[OverlayView.colorIdx]
                if c.brightnessComponentSafe < 0.2 { c = OverlayView.textMain }
                (i == OverlayView.sizeIdx ? c : c.withAlphaComponent(0.55)).setFill()
                NSBezierPath(ovalIn: CGRect(x: b.rect.midX - d / 2, y: b.rect.midY - d / 2, width: d, height: d)).fill()
                if i == 2 && tool != .mosaic {
                    OverlayView.barLine.setStroke()
                    let l = NSBezierPath()
                    l.move(to: CGPoint(x: b.rect.maxX + 6, y: subBar.minY + 10))
                    l.line(to: CGPoint(x: b.rect.maxX + 6, y: subBar.maxY - 10))
                    l.stroke()
                }
            } else if b.id.hasPrefix("color:") {
                let i = Int(b.id.dropFirst(6)) ?? 0
                var r = b.rect
                if i == OverlayView.colorIdx {
                    NSColor.white.setStroke()
                    let ring = NSBezierPath(ovalIn: r.insetBy(dx: -2, dy: -2))
                    ring.lineWidth = 2
                    ring.stroke()
                } else if !isHover {
                    r = r.insetBy(dx: 2, dy: 2)
                }
                OverlayView.palette[i].setFill()
                NSBezierPath(ovalIn: r).fill()
                NSColor(white: 1, alpha: 0.25).setStroke()
                NSBezierPath(ovalIn: r).stroke()
            }
        }

        if let id = hoverBtn, let b = buttons.first(where: { $0.id == id }), let tip = b.tip {
            let f = NSFont.systemFont(ofSize: 12)
            let barTop = mainBar.contains(b.rect) ? mainBar.minY : subBar.minY
            let r = tagRect(tip, font: f, near: CGPoint(x: b.rect.midX - 40, y: barTop), above: true)
            drawTag(tip, at: r, font: f, color: OverlayView.textMain)
        }
    }

    // MARK: annotation

    private func setTool(_ t: Tool) {
        commitText()
        tool = t
        refresh()
    }

    private func newShape(_ p: CGPoint) -> Shape? {
        let c = OverlayView.palette[OverlayView.colorIdx]
        let w = OverlayView.strokeWidths[OverlayView.sizeIdx]
        switch tool {
        case .rect: return BoxShape(p, color: c, width: w, ellipse: false)
        case .ellipse: return BoxShape(p, color: c, width: w, ellipse: true)
        case .arrow: return ArrowShape(p, color: c, width: w)
        case .pen: return StrokeShape(p, color: c, width: w)
        case .marker: return StrokeShape(p, color: c.withAlphaComponent(0.42), width: w * 3 + 8)
        case .mosaic: return MosaicShape(p, cell: OverlayView.mosaicCells[OverlayView.sizeIdx])
        default: return nil
        }
    }

    private var textFont: NSFont {
        return NSFont.systemFont(ofSize: OverlayView.textSizes[OverlayView.sizeIdx], weight: .bold)
    }

    private func styleEditor(_ f: NSTextField) {
        let c = OverlayView.palette[OverlayView.colorIdx]
        f.textColor = c
        f.font = textFont
        f.backgroundColor = c.brightnessComponentSafe > 0.7 ? NSColor(white: 0.18, alpha: 1) : .white
        fitEditor(f)
    }

    private func fitEditor(_ f: NSTextField) {
        let s = ((f.stringValue + "国") as NSString).size(withAttributes: [.font: textFont])
        f.frame = CGRect(x: f.frame.minX, y: f.frame.minY, width: max(60, s.width + 12), height: s.height + 6)
    }

    private func startText(_ p: CGPoint) {
        let f = NSTextField(frame: CGRect(x: p.x, y: p.y, width: 60, height: 30))
        f.isBordered = false
        f.drawsBackground = true
        f.focusRingType = .none
        f.isEditable = true
        f.isSelectable = true
        f.delegate = self
        f.cell?.wraps = false
        f.cell?.isScrollable = true
        styleEditor(f)
        addSubview(f)
        editor = f
        window?.makeFirstResponder(f)
        refresh()
    }

    private func commitText() {
        guard let f = editor else { return }
        editor = nil
        let t = f.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        if !t.isEmpty {
            shapes.append(TextShape(CGPoint(x: f.frame.minX + 2, y: f.frame.minY + 3), text: t,
                                    font: f.font ?? textFont, color: f.textColor ?? .red))
        }
        f.removeFromSuperview()
        window?.makeFirstResponder(self)
        refresh()
    }

    private func cancelText() {
        guard let f = editor else { return }
        editor = nil
        f.removeFromSuperview()
        window?.makeFirstResponder(self)
        refresh()
    }

    func controlTextDidChange(_ obj: Notification) {
        if let f = editor { fitEditor(f) }
    }

    func control(_ control: NSControl, textView: NSTextView, doCommandBy commandSelector: Selector) -> Bool {
        if commandSelector == #selector(NSResponder.insertNewline(_:)) { commitText(); return true }
        if commandSelector == #selector(NSResponder.cancelOperation(_:)) { cancelText(); return true }
        return false
    }

    private func undo() {
        if editor != nil { cancelText(); return }
        if !shapes.isEmpty { shapes.removeLast() }
        refresh()
    }

    private func onButton(_ id: String) {
        if id.hasPrefix("tool:") {
            let t = Tool(rawValue: String(id.dropFirst(5))) ?? Tool.off
            setTool(tool == t ? Tool.off : t)
        } else if id == "undo" { undo() }
        else if id == "ocr" { finish(ocr: true) }
        else if id == "cancel" { session?.cancel() }
        else if id == "done" { finish(ocr: false) }
        else if id.hasPrefix("size:") {
            OverlayView.sizeIdx = Int(id.dropFirst(5)) ?? 1
            if let f = editor { styleEditor(f) }
            refresh()
        } else if id.hasPrefix("color:") {
            OverlayView.colorIdx = Int(id.dropFirst(6)) ?? 0
            if let f = editor { styleEditor(f) }
            refresh()
        }
    }

    private func finish(ocr: Bool) {
        commitText()
        let r = sel.intersection(full)
        if r.width < 2 || r.height < 2 { return }
        session?.finish(view: self, rect: r, shapes: shapes, ocr: ocr || ocrMode)
    }

    private func enterAdjusting(_ r: CGRect) {
        sel = r
        if editable { state = .adjusting } else { finish(ocr: ocrMode) }
    }

    private func refresh() {
        layoutBars()
        needsDisplay = true
    }

    private func updateCursor(_ p: CGPoint) {
        if state != .adjusting { NSCursor.crosshair.set(); return }
        if button(at: p) != nil { NSCursor.pointingHand.set(); return }
        if tool != .off {
            if sel.contains(p) { (tool == .text ? NSCursor.iBeam : NSCursor.crosshair).set() } else { NSCursor.arrow.set() }
            return
        }
        cursor(for: handleAt(p)).set()
    }

    // MARK: mouse

    private func point(_ e: NSEvent) -> CGPoint { return convert(e.locationInWindow, from: nil) }

    override func mouseEntered(with event: NSEvent) {
        if window?.isKeyWindow == false { window?.makeKey(); window?.makeFirstResponder(self) }
    }

    override func mouseMoved(with event: NSEvent) {
        let p = point(event)
        cursorPt = p
        if window?.isKeyWindow == false { window?.makeKey(); window?.makeFirstResponder(self) }
        if state == .idle && !pending {
            if dormant { dormant = false }
            hover = windowAt(p)
        } else if state == .adjusting {
            hoverBtn = button(at: p)?.id
        }
        updateCursor(p)
        needsDisplay = true
    }

    override func mouseDown(with event: NSEvent) {
        let p = point(event)
        cursorPt = p
        if editor != nil {
            let insideText = tool == .text && sel.contains(p) && button(at: p) == nil
            commitText()
            if insideText { return }
        }
        if event.clickCount == 2 && state == .adjusting && tool == .off && sel.contains(p) && button(at: p) == nil {
            finish(ocr: false)
            return
        }
        downPt = p
        selAtDown = sel
        if state == .adjusting {
            layoutBars()
            if let b = button(at: p) { onButton(b.id); return }
            if tool != .off {
                if !sel.contains(p) { return }
                if tool == .text { startText(p); return }
                drawing = newShape(p)
                needsDisplay = true
                return
            }
            let h = handleAt(p)
            if h == .move { state = .moving; NSCursor.closedHand.set() }
            else if h != .none { state = .resizing; grab = h }
            else { pending = true }
            refresh()
            return
        }
        if state == .idle {
            dormant = false
            session?.activate(self)
            pending = true
        }
    }

    override func mouseDragged(with event: NSEvent) {
        let p = point(event)
        cursorPt = p
        if let d = drawing {
            let c = CGPoint(x: max(sel.minX, min(sel.maxX - 1, p.x)), y: max(sel.minY, min(sel.maxY - 1, p.y)))
            d.update(c)
            needsDisplay = true
            return
        }
        if pending && (abs(p.x - downPt.x) > 4 || abs(p.y - downPt.y) > 4) {
            pending = false
            if state == .adjusting && !shapes.isEmpty { return }   // keep annotations
            state = .creating
        }
        let dx = p.x - downPt.x, dy = p.y - downPt.y
        switch state {
        case .creating: sel = fromPoints(downPt, p).intersection(full)
        case .moving: sel = moved(selAtDown, dx, dy)
        case .resizing: sel = resized(selAtDown, grab, dx, dy)
        default: break
        }
        refresh()
    }

    override func mouseUp(with event: NSEvent) {
        let p = point(event)
        if let d = drawing {
            drawing = nil
            if d.isValid { shapes.append(d) }
            refresh()
            return
        }
        if pending {
            pending = false
            if state == .idle { enterAdjusting(windowAt(p)) }
        } else if state == .creating {
            if sel.width < 3 || sel.height < 3 { state = .idle; sel = .zero }
            else { enterAdjusting(sel) }
        } else if state == .moving || state == .resizing {
            state = .adjusting
        }
        updateCursor(p)
        refresh()
    }

    override func rightMouseDown(with event: NSEvent) {
        if editor != nil { commitText() }
        if tool != .off { setTool(.off); return }
        if state == .adjusting {
            if !shapes.isEmpty { return }      // keep annotations; Esc abandons
            state = .idle
            sel = .zero
            hover = windowAt(point(event))
            refresh()
        } else {
            session?.cancel()
        }
    }

    // MARK: keyboard

    override func keyDown(with event: NSEvent) {
        let code = event.keyCode
        let cmd = event.modifierFlags.contains(.command)
        let shift = event.modifierFlags.contains(.shift)
        let ch = (event.charactersIgnoringModifiers ?? "").lowercased()

        if code == 53 {   // Esc
            if tool != .off { setTool(.off) } else { session?.cancel() }
            return
        }
        if code == 36 || code == 76 || code == 49 || (cmd && ch == "c") {   // Return, Enter, Space, ⌘C
            if state == .adjusting { finish(ocr: false) }
            else if code == 36 || code == 76 { sel = full; finish(ocr: ocrMode) }
            return
        }
        if state != .adjusting { return }
        if cmd && ch == "z" { undo(); return }
        if editable && !cmd {
            let map: [String: Tool] = ["r": .rect, "e": .ellipse, "a": .arrow, "p": .pen, "h": .marker, "t": .text, "m": .mosaic]
            if let t = map[ch] { setTool(tool == t ? Tool.off : t); return }
            if ch == "o" { finish(ocr: true); return }
            if tool != .off, let n = Int(ch), n >= 1 && n <= 3 { onButton("size:\(n - 1)"); return }
        }
        if tool == .off {
            let step: CGFloat = cmd ? 10 : 1
            var dx: CGFloat = 0, dy: CGFloat = 0
            switch code {
            case 123: dx = -step
            case 124: dx = step
            case 125: dy = step
            case 126: dy = -step
            default: return
            }
            sel = shift ? resized(sel, .se, dx, dy) : moved(sel, dx, dy)
            refresh()
        }
    }
}

extension NSColor {
    // brightnessComponent raises for colours outside an RGB space; convert first.
    var brightnessComponentSafe: CGFloat {
        return usingColorSpace(.sRGB)?.brightnessComponent ?? 0.5
    }
}
