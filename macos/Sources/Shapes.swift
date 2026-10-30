// Annotation shapes, stored in the overlay view's point coordinates (top-left origin, flipped).
// The same objects draw on screen and onto the final pixel image.

import AppKit

class Shape {
    var color: NSColor
    var width: CGFloat

    init(color: NSColor, width: CGFloat) {
        self.color = color
        self.width = width
    }

    func update(_ p: CGPoint) {}
    var bounds: CGRect { return .zero }
    var isValid: Bool { return true }

    // `source` is the frozen screen image; `scale` converts points to its pixels.
    func draw(source: CGImage, scale: CGFloat) {}

    static func rect(_ a: CGPoint, _ b: CGPoint) -> CGRect {
        return CGRect(x: min(a.x, b.x), y: min(a.y, b.y), width: abs(a.x - b.x), height: abs(a.y - b.y))
    }

    func padded(_ r: CGRect, _ extra: CGFloat = 0) -> CGRect {
        return r.insetBy(dx: -(width / 2 + extra + 3), dy: -(width / 2 + extra + 3))
    }

    func stroke(_ path: NSBezierPath) {
        path.lineWidth = width
        path.lineCapStyle = .round
        path.lineJoinStyle = .round
        color.setStroke()
        path.stroke()
    }
}

final class BoxShape: Shape {
    private var a: CGPoint
    private var b: CGPoint
    private let ellipse: Bool

    init(_ p: CGPoint, color: NSColor, width: CGFloat, ellipse: Bool) {
        a = p; b = p
        self.ellipse = ellipse
        super.init(color: color, width: width)
    }

    override func update(_ p: CGPoint) { b = p }
    override var bounds: CGRect { return padded(Shape.rect(a, b)) }
    override var isValid: Bool { let r = Shape.rect(a, b); return r.width > 3 && r.height > 3 }

    override func draw(source: CGImage, scale: CGFloat) {
        let r = Shape.rect(a, b)
        stroke(ellipse ? NSBezierPath(ovalIn: r) : NSBezierPath(roundedRect: r, xRadius: 1.5, yRadius: 1.5))
    }
}

final class ArrowShape: Shape {
    private var a: CGPoint
    private var b: CGPoint

    init(_ p: CGPoint, color: NSColor, width: CGFloat) {
        a = p; b = p
        super.init(color: color, width: width)
    }

    private var head: CGFloat { return max(width * 3.4, 12) }
    override func update(_ p: CGPoint) { b = p }
    override var bounds: CGRect { return padded(Shape.rect(a, b), head) }
    override var isValid: Bool { return hypot(b.x - a.x, b.y - a.y) > 8 }

    override func draw(source: CGImage, scale: CGFloat) {
        let dx = b.x - a.x, dy = b.y - a.y
        let len = hypot(dx, dy)
        if len < 1 { return }
        let ux = dx / len, uy = dy / len
        let h = min(head, len * 0.6)
        let base = CGPoint(x: b.x - ux * h * 0.85, y: b.y - uy * h * 0.85)
        let line = NSBezierPath()
        line.move(to: a)
        line.line(to: base)
        stroke(line)
        let half = h * 0.55
        let tri = NSBezierPath()
        tri.move(to: b)
        tri.line(to: CGPoint(x: b.x - ux * h - uy * half, y: b.y - uy * h + ux * half))
        tri.line(to: CGPoint(x: b.x - ux * h + uy * half, y: b.y - uy * h - ux * half))
        tri.close()
        color.setFill()
        tri.fill()
    }
}

final class StrokeShape: Shape {
    private var points: [CGPoint]

    init(_ p: CGPoint, color: NSColor, width: CGFloat) {
        points = [p]
        super.init(color: color, width: width)
    }

    override func update(_ p: CGPoint) {
        if let last = points.last, abs(p.x - last.x) + abs(p.y - last.y) < 1.5 { return }
        points.append(p)
    }

    override var bounds: CGRect {
        var r = CGRect(origin: points[0], size: .zero)
        for p in points { r = r.union(CGRect(origin: p, size: .zero)) }
        return padded(r)
    }

    override func draw(source: CGImage, scale: CGFloat) {
        if points.count == 1 {
            let p = points[0]
            color.setFill()
            NSBezierPath(ovalIn: CGRect(x: p.x - width / 2, y: p.y - width / 2, width: width, height: width)).fill()
            return
        }
        let path = NSBezierPath()
        path.move(to: points[0])
        for p in points.dropFirst() { path.line(to: p) }
        stroke(path)
    }
}

final class MosaicShape: Shape {
    private var a: CGPoint
    private var b: CGPoint
    private let cell: CGFloat
    private var cache: CGImage?
    private var cacheRect: CGRect = .null

    init(_ p: CGPoint, cell: CGFloat) {
        a = p; b = p
        self.cell = max(4, cell)
        super.init(color: .clear, width: 0)
    }

    override func update(_ p: CGPoint) { b = p }
    override var bounds: CGRect { return padded(Shape.rect(a, b)) }
    override var isValid: Bool { let r = Shape.rect(a, b); return r.width > 4 && r.height > 4 }

    override func draw(source: CGImage, scale: CGFloat) {
        let r = Shape.rect(a, b)
        if r.width < 1 || r.height < 1 { return }
        if cache == nil || cacheRect != r {
            let px = CGRect(x: r.minX * scale, y: r.minY * scale, width: r.width * scale, height: r.height * scale).integral
            guard let crop = source.cropping(to: px) else { return }
            let w = max(1, Int(r.width / cell)), h = max(1, Int(r.height / cell))
            cache = JpegFitter.resize(crop, w, h)
            cacheRect = r
        }
        guard let small = cache, let ctx = NSGraphicsContext.current else { return }
        let old = ctx.imageInterpolation
        ctx.imageInterpolation = .none
        NSImage(cgImage: small, size: r.size).draw(in: r, from: .zero, operation: .sourceOver, fraction: 1,
                                                   respectFlipped: true, hints: [.interpolation: NSImageInterpolation.none.rawValue])
        ctx.imageInterpolation = old
    }
}

final class TextShape: Shape {
    private let origin: CGPoint
    private let text: String
    private let font: NSFont

    init(_ p: CGPoint, text: String, font: NSFont, color: NSColor) {
        origin = p
        self.text = text
        self.font = font
        super.init(color: color, width: 0)
    }

    private var attributed: NSAttributedString {
        return NSAttributedString(string: text, attributes: [.font: font, .foregroundColor: color])
    }

    override var bounds: CGRect {
        let s = attributed.size()
        return padded(CGRect(origin: origin, size: s), 2)
    }

    override func draw(source: CGImage, scale: CGFloat) {
        attributed.draw(at: origin)
    }
}
