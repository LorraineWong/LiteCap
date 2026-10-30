// Offline text recognition with Apple's Vision framework (built into macOS, nothing leaves the Mac).

import AppKit
import Vision

struct OcrOutput {
    let lines: [String]
    let boxes: [CGRect]      // per line, normalized 0...1 with a top-left origin
    var text: String { return lines.joined(separator: "\n") }
}

enum MacOCR {
    static func recognize(_ image: CGImage, completion: @escaping (Result<OcrOutput, Error>) -> Void) {
        DispatchQueue.global(qos: .userInitiated).async {
            let request = VNRecognizeTextRequest()
            request.recognitionLevel = .accurate
            request.usesLanguageCorrection = true
            // Chinese models also read Latin text, so list them first for mixed content.
            let wanted = ["zh-Hans", "zh-Hant", "en-US"]
            if let supported = try? request.supportedRecognitionLanguages() {
                let langs = wanted.filter { supported.contains($0) }
                if !langs.isEmpty { request.recognitionLanguages = langs }
            }
            let handler = VNImageRequestHandler(cgImage: image, options: [:])
            do {
                try handler.perform([request])
                let observations = request.results ?? []
                // Reading order: top to bottom (Vision's y axis points up), then left to right.
                let sorted = observations.sorted { a, b in
                    let ay = a.boundingBox.midY, by = b.boundingBox.midY
                    if abs(ay - by) > min(a.boundingBox.height, b.boundingBox.height) * 0.5 { return ay > by }
                    return a.boundingBox.minX < b.boundingBox.minX
                }
                var lines: [String] = []
                var boxes: [CGRect] = []
                for o in sorted {
                    guard let s = o.topCandidates(1).first?.string.trimmingCharacters(in: .whitespaces), !s.isEmpty else { continue }
                    let b = o.boundingBox
                    lines.append(s)
                    boxes.append(CGRect(x: b.minX, y: 1 - b.maxY, width: b.width, height: b.height))
                }
                let out = OcrOutput(lines: lines, boxes: boxes)
                DispatchQueue.main.async { completion(.success(out)) }
            } catch {
                DispatchQueue.main.async { completion(.failure(error)) }
            }
        }
    }

    // Joins wrapped lines into paragraphs: no space between CJK characters, one space otherwise;
    // a line ending in sentence punctuation starts a new paragraph.
    static func merge(_ lines: [String]) -> String {
        var out = ""
        for (i, cur) in lines.enumerated() {
            if i == 0 { out = cur; continue }
            let last = lines[i - 1].last ?? " "
            let first = cur.first ?? " "
            if "。！？.!?:：;；".contains(last) {
                out += "\n"
            } else if last == "-" {
                out.removeLast()
            } else if !(isCJK(last) && isCJK(first)) {
                out += " "
            }
            out += cur
        }
        return out
    }

    static func isCJK(_ c: Character) -> Bool {
        guard let v = c.unicodeScalars.first?.value else { return false }
        return (0x3000...0x303F).contains(v) || (0x3400...0x9FFF).contains(v) || (0xF900...0xFAFF).contains(v) || (0xFF00...0xFFEF).contains(v)
    }
}
