// Offline translation with Apple's Translation framework (macOS 15+). Runs on the Mac; the first use
// of a language pair shows the system prompt to download it. Built so the app still compiles with
// older SDKs and runs on macOS 13/14 (the translate button is simply hidden there).

import SwiftUI
#if canImport(Translation)
import Translation
#endif

enum MacTranslate {
    static var supported: Bool {
        #if canImport(Translation)
        if #available(macOS 15.0, *) { return true }
        #endif
        return false
    }

    // "zh-en" when the text is mostly Chinese, otherwise "en-zh".
    static func detectDirection(_ text: String) -> String {
        var cjk = 0, latin = 0
        for u in text.unicodeScalars {
            let v = u.value
            if (0x3400...0x9FFF).contains(v) || (0xF900...0xFAFF).contains(v) { cjk += 1 }
            else if (0x41...0x5A).contains(v) || (0x61...0x7A).contains(v) { latin += 1 }
        }
        // A Chinese character carries about as much as a 3-4 letter word.
        return cjk * 3 >= latin ? "zh-en" : "en-zh"
    }

    // A TranslationSession.Configuration (typed as Any so this compiles for macOS 13).
    static func configuration(direction: String) -> Any? {
        #if canImport(Translation)
        if #available(macOS 15.0, *) {
            let zh = Locale.Language(identifier: "zh-Hans"), en = Locale.Language(identifier: "en")
            return direction == "zh-en"
                ? TranslationSession.Configuration(source: zh, target: en)
                : TranslationSession.Configuration(source: en, target: zh)
        }
        #endif
        return nil
    }

    #if canImport(Translation)
    // Translates paragraph by paragraph so line structure is kept.
    @available(macOS 15.0, *)
    static func run(_ session: TranslationSession, model: OcrModel) async {
        let source = await MainActor.run { model.trSource }
        do {
            var out: [String] = []
            for paragraph in source.components(separatedBy: "\n") {
                let p = paragraph.trimmingCharacters(in: .whitespaces)
                if p.isEmpty { out.append(""); continue }
                let response = try await session.translate(p)
                out.append(response.targetText)
            }
            let text = out.joined(separator: "\n")
            await MainActor.run { model.finishTranslation(text) }
        } catch {
            await MainActor.run { model.failTranslation(error) }
        }
    }
    #endif
}

// Attaches the translation task to the OCR window; it runs whenever the model asks for a translation.
#if canImport(Translation)
struct TranslationHost: ViewModifier {
    @ObservedObject var model: OcrModel

    @ViewBuilder
    func body(content: Content) -> some View {
        if #available(macOS 15.0, *) {
            content.translationTask(model.translationRequest as? TranslationSession.Configuration) { session in
                await MacTranslate.run(session, model: model)
            }
        } else {
            content
        }
    }
}
#else
struct TranslationHost: ViewModifier {
    @ObservedObject var model: OcrModel
    func body(content: Content) -> some View { content }
}
#endif
