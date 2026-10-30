// Text-recognition window: screenshot preview with line boxes on the left, editable text on the right.
// States: recognizing -> result (auto-copied) | no text | error.
// Uses only ObservableObject + @ObservedObject (no @State/@Binding macros, which the
// Command Line Tools cannot expand).

import AppKit
import SwiftUI

final class OcrModel: ObservableObject {
    enum Phase { case working, result, empty, error }

    @Published var phase: Phase = .working
    @Published var title = L.t("ocr_working")
    @Published var stats = ""
    @Published var message = ""
    @Published var merge = false { didSet { if merge != oldValue { DispatchQueue.main.async { self.rebuildText() } } } }
    @Published var copied = false
    @Published var selectedLine = -1
    @Published var hoverLine = -1

    var lines: [String] = []
    var boxes: [CGRect] = []
    let image: NSImage
    let pixelSize: CGSize

    // The text view owns the live text; the model pushes new text and selections into it.
    weak var textView: NSTextView?
    var pendingText = ""

    init(image: CGImage) {
        self.image = NSImage(cgImage: image, size: NSSize(width: image.width, height: image.height))
        self.pixelSize = CGSize(width: image.width, height: image.height)
    }

    var currentText: String { return textView?.string ?? pendingText }

    func rebuildText() {
        let t = merge ? MacOCR.merge(lines) : lines.joined(separator: "\n")
        pendingText = t
        textView?.string = t
        selectedLine = -1
    }

    func select(_ i: Int) {
        guard phase == .result, i >= 0, i < lines.count, let tv = textView else { return }
        selectedLine = i
        let text = tv.string as NSString
        var range = NSRange(location: NSNotFound, length: 0)
        if !merge {
            var loc = 0
            for k in 0..<i { loc += (lines[k] as NSString).length + 1 }
            range = NSRange(location: loc, length: (lines[i] as NSString).length)
        } else {
            range = text.range(of: lines[i])
        }
        guard range.location != NSNotFound, NSMaxRange(range) <= text.length else { return }
        tv.window?.makeFirstResponder(tv)
        tv.setSelectedRange(range)
        tv.scrollRangeToVisible(range)
    }

    func copy() {
        let text: String
        if let tv = textView, tv.selectedRange().length > 0 {
            text = (tv.string as NSString).substring(with: tv.selectedRange())
        } else {
            text = currentText
        }
        guard !text.isEmpty else { return }
        let pb = NSPasteboard.general
        pb.clearContents()
        pb.setString(text, forType: .string)
        copied = true
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.6) { [weak self] in self?.copied = false }
    }
}

// Plain NSTextView so the model can select the line the user clicked in the preview.
struct OcrTextView: NSViewRepresentable {
    let model: OcrModel

    func makeNSView(context: Context) -> NSScrollView {
        let scroll = NSTextView.scrollableTextView()
        scroll.drawsBackground = false
        scroll.borderType = .noBorder
        if let tv = scroll.documentView as? NSTextView {
            tv.font = NSFont.systemFont(ofSize: 14)
            tv.textColor = .labelColor
            tv.drawsBackground = false
            tv.isRichText = false
            tv.allowsUndo = true
            tv.textContainerInset = NSSize(width: 10, height: 10)
            tv.string = model.pendingText
            model.textView = tv
        }
        return scroll
    }

    func updateNSView(_ nsView: NSScrollView, context: Context) {}
}

struct OcrPreview: View {
    @ObservedObject var model: OcrModel

    var body: some View {
        GeometryReader { geo in
            let pad: CGFloat = 12
            let aw = geo.size.width - pad * 2, ah = geo.size.height - pad * 2
            let s = min(aw / max(model.pixelSize.width, 1), ah / max(model.pixelSize.height, 1), 2)
            let w = model.pixelSize.width * s, h = model.pixelSize.height * s
            let ox = (geo.size.width - w) / 2, oy = (geo.size.height - h) / 2
            ZStack(alignment: .topLeading) {
                Image(nsImage: model.image)
                    .resizable()
                    .frame(width: w, height: h)
                    .overlay(Rectangle().stroke(Color(NSColor.separatorColor), lineWidth: 1))
                    .offset(x: ox, y: oy)
                if model.phase == .result {
                    ForEach(0..<model.boxes.count, id: \.self) { i in
                        let b = model.boxes[i]
                        let strong = i == model.hoverLine || i == model.selectedLine
                        Rectangle()
                            .fill(Color.accentColor.opacity(strong ? 0.28 : 0.11))
                            .overlay(Rectangle().stroke(Color.accentColor.opacity(strong ? 0.95 : 0.5), lineWidth: strong ? 1.5 : 1))
                            .frame(width: b.width * w + 4, height: b.height * h + 4)
                            .offset(x: ox + b.minX * w - 2, y: oy + b.minY * h - 2)
                            .onHover { inside in model.hoverLine = inside ? i : (model.hoverLine == i ? -1 : model.hoverLine) }
                            .onTapGesture { model.select(i) }
                    }
                }
            }
        }
        .background(Color(NSColor.textBackgroundColor))
        .clipShape(RoundedRectangle(cornerRadius: 10))
        .overlay(RoundedRectangle(cornerRadius: 10).stroke(Color(NSColor.separatorColor)))
    }
}

struct OcrView: View {
    @ObservedObject var model: OcrModel
    let close: () -> Void
    let again: () -> Void

    private var iconName: String {
        switch model.phase {
        case .result: return "checkmark"
        case .error: return "exclamationmark.triangle"
        default: return "text.viewfinder"
        }
    }

    private var iconTint: Color {
        switch model.phase {
        case .error: return .red
        case .empty: return .secondary
        default: return .accentColor
        }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack(spacing: 14) {
                Image(systemName: iconName)
                    .font(.system(size: 17, weight: .semibold))
                    .foregroundColor(iconTint)
                    .frame(width: 40, height: 40)
                    .background(iconTint.opacity(0.12))
                    .clipShape(RoundedRectangle(cornerRadius: 10))
                VStack(alignment: .leading, spacing: 3) {
                    Text(model.title).font(.system(size: 18, weight: .semibold))
                    if model.phase == .result {
                        Text(model.stats).font(.system(size: 12)).foregroundColor(.secondary)
                    }
                }
                Spacer()
            }

            HStack(alignment: .top, spacing: 16) {
                OcrPreview(model: model)
                    .frame(minWidth: 260, maxWidth: .infinity)
                VStack(alignment: .leading, spacing: 10) {
                    if model.phase == .result {
                        Picker("", selection: $model.merge) {
                            Text(L.t("ocr_keep_lines")).tag(false)
                            Text(L.t("ocr_merge_lines")).tag(true)
                        }
                        .pickerStyle(.segmented)
                        .labelsHidden()
                        .fixedSize()
                    }
                    ZStack {
                        RoundedRectangle(cornerRadius: 10).fill(Color(NSColor.textBackgroundColor))
                        if model.phase == .result {
                            OcrTextView(model: model)
                        } else {
                            Text(model.phase == .working ? L.t("ocr_working") : model.message)
                                .font(.system(size: 14))
                                .foregroundColor(model.phase == .error ? .primary : .secondary)
                                .multilineTextAlignment(.center)
                                .padding(24)
                        }
                    }
                    .overlay(RoundedRectangle(cornerRadius: 10).stroke(Color(NSColor.separatorColor)))
                }
                .frame(minWidth: 300, maxWidth: .infinity)
            }

            HStack {
                Button(L.t("ocr_again")) { again() }
                Spacer()
                Button(L.t("ocr_close")) { close() }
                    .keyboardShortcut(.cancelAction)
                Button(model.copied ? L.t("ocr_copied_btn") : L.t("ocr_copy")) { model.copy() }
                    .keyboardShortcut(.defaultAction)
                    .disabled(model.phase != .result)
                    .frame(minWidth: 110)
            }
            .controlSize(.large)
        }
        .padding(20)
        .frame(minWidth: 720, minHeight: 440)
    }
}

final class OcrWindowController {
    private static var current: OcrWindowController?
    private let window: NSWindow
    let model: OcrModel

    static func open(image: CGImage, again: @escaping () -> Void) -> OcrWindowController {
        current?.window.close()
        let c = OcrWindowController(image: image, again: again)
        current = c
        return c
    }

    private init(image: CGImage, again: @escaping () -> Void) {
        model = OcrModel(image: image)
        window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 900, height: 540),
                          styleMask: [.titled, .closable, .resizable, .miniaturizable], backing: .buffered, defer: false)
        window.title = L.t("ocr_title")
        window.isReleasedWhenClosed = false
        window.level = .floating
        window.minSize = NSSize(width: 720, height: 440)
        let w = window
        let view = OcrView(model: model, close: { w.close() }, again: { w.close(); again() })
        window.contentViewController = NSHostingController(rootView: view)
        window.setContentSize(NSSize(width: 900, height: 540))
        window.center()
        NSApp.activate(ignoringOtherApps: true)
        window.makeKeyAndOrderFront(nil)
    }

    func show(_ out: OcrOutput) {
        if out.lines.isEmpty {
            model.phase = .empty
            model.title = L.t("ocr_empty")
            model.message = L.t("ocr_empty_hint")
            return
        }
        model.lines = out.lines
        model.boxes = out.boxes
        let chars = out.lines.reduce(0) { $0 + $1.count }
        model.stats = L.f("ocr_stats", out.lines.count, chars)
        model.title = L.t("ocr_done")
        model.pendingText = out.text
        model.phase = .result
        // The text view is created by this phase change; copy once it exists.
        DispatchQueue.main.async { [weak self] in
            self?.model.rebuildText()
            self?.model.copy()
        }
    }

    func fail(_ error: Error) {
        model.phase = .error
        model.title = L.t("ocr_failed")
        model.message = String(describing: error)
    }
}
