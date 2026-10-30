// Floating, non-activating panels: the preview card after a capture and the delay countdown.

import AppKit

private func cornerOrigin(size: NSSize) -> NSPoint {
    let screen = NSScreen.screens.first(where: { $0.frame.contains(NSEvent.mouseLocation) }) ?? NSScreen.main ?? NSScreen.screens[0]
    let v = screen.visibleFrame
    return NSPoint(x: v.maxX - size.width - 16, y: v.minY + 16)
}

private func makePanel(size: NSSize) -> NSPanel {
    let p = NSPanel(contentRect: NSRect(origin: cornerOrigin(size: size), size: size),
                    styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
    p.level = .statusBar
    p.isOpaque = false
    p.backgroundColor = .clear
    p.hasShadow = true
    p.hidesOnDeactivate = false
    p.isReleasedWhenClosed = false
    p.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
    return p
}

// MARK: preview card

final class PreviewToast {
    private static var current: PreviewToast?
    private let panel: NSPanel
    private var timer: Timer?

    static func show(image: CGImage, title: String, info: String, file: URL) {
        current?.close()
        current = PreviewToast(image: image, title: title, info: info, file: file)
    }

    private init(image: CGImage, title: String, info: String, file: URL) {
        let size = NSSize(width: 380, height: 96)
        panel = makePanel(size: size)
        let view = PreviewView(frame: NSRect(origin: .zero, size: size), image: image, title: title, info: info)
        view.onClick = { [weak self] in
            NSWorkspace.shared.activateFileViewerSelecting([file])
            self?.close()
        }
        panel.contentView = view
        panel.orderFrontRegardless()
        timer = Timer.scheduledTimer(withTimeInterval: 3.2, repeats: false) { [weak self] _ in self?.fadeOut() }
    }

    private func fadeOut() {
        if let v = panel.contentView as? PreviewView, v.isHovered {
            timer = Timer.scheduledTimer(withTimeInterval: 1.0, repeats: false) { [weak self] _ in self?.fadeOut() }
            return
        }
        NSAnimationContext.runAnimationGroup({ ctx in
            ctx.duration = 0.3
            self.panel.animator().alphaValue = 0
        }, completionHandler: { [weak self] in self?.close() })
    }

    private func close() {
        timer?.invalidate()
        panel.orderOut(nil)
        if PreviewToast.current === self { PreviewToast.current = nil }
    }
}

private final class PreviewView: NSView {
    var onClick: (() -> Void)?
    private(set) var isHovered = false
    private let thumb: NSImage
    private let title: String
    private let info: String

    init(frame: NSRect, image: CGImage, title: String, info: String) {
        let maxW: CGFloat = 112, maxH: CGFloat = 72
        let s = min(maxW / CGFloat(image.width), maxH / CGFloat(image.height))
        thumb = NSImage(cgImage: image, size: NSSize(width: CGFloat(image.width) * s, height: CGFloat(image.height) * s))
        self.title = title
        self.info = info
        super.init(frame: frame)
        addTrackingArea(NSTrackingArea(rect: .zero, options: [.mouseEnteredAndExited, .activeAlways, .inVisibleRect], owner: self, userInfo: nil))
    }

    required init?(coder: NSCoder) { fatalError("not used") }

    override var isFlipped: Bool { return true }
    override func mouseEntered(with event: NSEvent) { isHovered = true }
    override func mouseExited(with event: NSEvent) { isHovered = false }
    override func mouseDown(with event: NSEvent) { onClick?() }

    override func draw(_ dirtyRect: NSRect) {
        let card = NSBezierPath(roundedRect: bounds.insetBy(dx: 0.5, dy: 0.5), xRadius: 14, yRadius: 14)
        NSColor.windowBackgroundColor.setFill()
        card.fill()
        NSColor.separatorColor.setStroke()
        card.stroke()

        let box = NSRect(x: 14, y: (bounds.height - 76) / 2, width: 116, height: 76)
        NSColor.quaternaryLabelColor.setFill()
        NSBezierPath(roundedRect: box, xRadius: 8, yRadius: 8).fill()
        let ts = thumb.size
        thumb.draw(in: NSRect(x: box.midX - ts.width / 2, y: box.midY - ts.height / 2, width: ts.width, height: ts.height),
                   from: .zero, operation: .sourceOver, fraction: 1, respectFlipped: true, hints: nil)

        let x = box.maxX + 14
        let ok = NSRect(x: x, y: 18, width: 18, height: 18)
        NSColor.systemGreen.setFill()
        NSBezierPath(ovalIn: ok).fill()
        if let check = NSImage(systemSymbolName: "checkmark", accessibilityDescription: nil)?
            .withSymbolConfiguration(NSImage.SymbolConfiguration(pointSize: 9, weight: .bold)) {
            let tinted = NSImage(size: check.size)
            tinted.lockFocus()
            check.draw(at: .zero, from: .zero, operation: .sourceOver, fraction: 1)
            NSColor.white.set()
            NSRect(origin: .zero, size: check.size).fill(using: .sourceAtop)
            tinted.unlockFocus()
            tinted.draw(in: NSRect(x: ok.midX - check.size.width / 2, y: ok.midY - check.size.height / 2,
                                   width: check.size.width, height: check.size.height),
                        from: .zero, operation: .sourceOver, fraction: 1, respectFlipped: true, hints: nil)
        }
        NSAttributedString(string: title, attributes: [.font: NSFont.systemFont(ofSize: 15, weight: .semibold),
                                                         .foregroundColor: NSColor.labelColor])
            .draw(at: NSPoint(x: ok.maxX + 8, y: 16))
        NSAttributedString(string: info, attributes: [.font: NSFont.systemFont(ofSize: 12),
                                                        .foregroundColor: NSColor.secondaryLabelColor])
            .draw(at: NSPoint(x: x, y: 44))
        NSAttributedString(string: L.t("toast_link"), attributes: [.font: NSFont.systemFont(ofSize: 12),
                                                                   .foregroundColor: NSColor.linkColor])
            .draw(at: NSPoint(x: x, y: 64))
    }
}

// MARK: countdown badge (ignores the mouse, never takes focus, so open menus stay open)

final class Countdown {
    private static var current: Countdown?
    private let panel: NSPanel
    private let view: CountdownView
    private var remaining: Int
    private var timer: Timer?
    private let done: () -> Void

    static func run(seconds: Int, done: @escaping () -> Void) {
        current = Countdown(seconds: seconds, done: done)
    }

    private init(seconds: Int, done: @escaping () -> Void) {
        remaining = seconds
        self.done = done
        let size = NSSize(width: 190, height: 64)
        panel = makePanel(size: size)
        panel.ignoresMouseEvents = true
        view = CountdownView(frame: NSRect(origin: .zero, size: size))
        view.value = seconds
        panel.contentView = view
        panel.orderFrontRegardless()
        timer = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { [weak self] _ in self?.tick() }
    }

    private func tick() {
        remaining -= 1
        if remaining > 0 {
            view.value = remaining
            view.needsDisplay = true
            return
        }
        timer?.invalidate()
        panel.orderOut(nil)
        // Let the window server remove the badge before the screen is grabbed.
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.15) {
            self.done()
            Countdown.current = nil
        }
    }
}

private final class CountdownView: NSView {
    var value = 3
    override var isFlipped: Bool { return true }

    override func draw(_ dirtyRect: NSRect) {
        NSColor(srgbRed: 28 / 255, green: 31 / 255, blue: 38 / 255, alpha: 0.96).setFill()
        NSBezierPath(roundedRect: bounds, xRadius: 14, yRadius: 14).fill()
        let num = NSAttributedString(string: "\(value)", attributes: [.font: NSFont.systemFont(ofSize: 30, weight: .bold),
                                                                      .foregroundColor: NSColor.white])
        let ns = num.size()
        num.draw(at: NSPoint(x: 34 - ns.width / 2, y: (bounds.height - ns.height) / 2))
        let cap = NSAttributedString(string: L.t("delay_caption"), attributes: [.font: NSFont.systemFont(ofSize: 12),
                                                                                .foregroundColor: NSColor(white: 0.75, alpha: 1)])
        let cs = cap.boundingRect(with: NSSize(width: 120, height: 60), options: [.usesLineFragmentOrigin])
        cap.draw(with: NSRect(x: 62, y: (bounds.height - cs.height) / 2, width: 120, height: cs.height), options: [.usesLineFragmentOrigin])
    }
}
