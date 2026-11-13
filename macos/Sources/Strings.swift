// Interface strings in Chinese and English.

import Foundation

enum Lang: String {
    case zh, en

    static var systemDefault: Lang {
        let code = Locale.preferredLanguages.first ?? "en"
        return code.hasPrefix("zh") ? .zh : .en
    }
}

enum L {
    static var lang: Lang = .en

    static func t(_ key: String) -> String {
        guard let pair = table[key] else { return key }
        return lang == .en ? pair.1 : pair.0
    }

    // Replaces {0}, {1}, ... with the given values.
    static func f(_ key: String, _ args: Any...) -> String {
        var s = t(key)
        for (i, a) in args.enumerated() {
            s = s.replacingOccurrences(of: "{\(i)}", with: "\(a)")
        }
        return s
    }

    private static let table: [String: (String, String)] = [
        "app": ("轻截 LiteCap", "LiteCap"),
        "app_short": ("轻截", "LiteCap"),
        "version": ("版本 {0}", "Version {0}"),

        // Menu
        "region": ("圈选截图", "Region capture"),
        "full": ("全屏截图", "Full screen"),
        "ocr": ("识别文字", "Recognize text"),
        "delay": ("延时 3 秒圈选", "Region in 3 seconds"),
        "open_folder": ("打开截图文件夹", "Open screenshots folder"),
        "settings_menu": ("设置…", "Settings…"),
        "quit": ("退出轻截", "Quit LiteCap"),
        "header": ("JPG < {0} KB", "JPG < {0} KB"),

        // Permission
        "perm_title": ("需要“屏幕录制”权限", "Screen Recording permission needed"),
        "perm_body": ("请在 系统设置 → 隐私与安全性 → 屏幕录制 中打开“LiteCap”，然后重新打开轻截。",
                      "Turn on LiteCap in System Settings → Privacy & Security → Screen Recording, then reopen LiteCap."),
        "perm_open": ("打开系统设置", "Open System Settings"),
        "perm_later": ("稍后", "Later"),
        "black_title": ("截到的画面是全黑的", "The captured screen is completely black"),
        "black_body": ("系统没有把屏幕画面交给轻截。请在 系统设置 → 隐私与安全性 → 屏幕录制 里，先把 LiteCap 的开关关掉再打开（或用 − 删除后重新添加），然后退出轻截并重新打开。\n\n重新编译过的话，也需要这样做一次。",
                       "macOS did not give LiteCap the screen contents. In System Settings → Privacy & Security → Screen Recording, turn LiteCap off and on again (or remove it with − and add it back), then quit and reopen LiteCap.\n\nThis is also needed after every rebuild."),

        // Overlay
        "hint_region": ("拖动圈选  ·  单击选窗口  ·  Return 全屏  ·  Esc 取消", "Drag to select  ·  Click a window  ·  Return = full screen  ·  Esc to cancel"),
        "hint_ocr": ("圈选要识别文字的区域  ·  单击选窗口  ·  Esc 取消", "Select the area to read  ·  Click a window  ·  Esc to cancel"),
        "tip_rect": ("矩形  R", "Rectangle  R"),
        "tip_ellipse": ("椭圆  E", "Ellipse  E"),
        "tip_arrow": ("箭头  A", "Arrow  A"),
        "tip_pen": ("画笔  P", "Pen  P"),
        "tip_marker": ("荧光笔  H", "Highlighter  H"),
        "tip_text": ("文字  T", "Text  T"),
        "tip_mosaic": ("马赛克  M", "Mosaic  M"),
        "tip_undo": ("撤销  ⌘Z", "Undo  ⌘Z"),
        "tip_ocr": ("识别文字  O", "Recognize text  O"),
        "tip_cancel": ("取消  Esc", "Cancel  Esc"),
        "tip_done": ("完成并复制  Return / 双击", "Done & copy  Return / double-click"),
        "done": ("完成", "Done"),

        // Toasts
        "toast_title": ("已复制 · {0} KB", "Copied · {0} KB"),
        "toast_orig": ("（原 {0}×{1}）", " (from {0}×{1})"),
        "toast_quality": ("质量 {0}", "Quality {0}"),
        "toast_link": ("点击在访达中显示 →", "Click to show in Finder →"),
        "delay_caption": ("秒后截图\n可先打开菜单", "seconds to capture\nopen a menu now"),
        "fail": ("截图失败", "Capture failed"),

        // OCR window
        "ocr_title": ("识别文字", "Text recognition"),
        "ocr_working": ("正在识别…", "Recognizing…"),
        "ocr_done": ("识别完成", "Text recognized"),
        "ocr_stats": ("{0} 行 · {1} 字 · 已自动复制", "{0} lines · {1} characters · copied automatically"),
        "ocr_empty": ("没有识别到文字", "No text found"),
        "ocr_empty_hint": ("可以框得更紧一些，或把页面放大后再试一次。", "Try a tighter selection, or zoom in on the page and capture again."),
        "ocr_failed": ("无法识别", "Recognition failed"),
        "ocr_keep_lines": ("保留换行", "Keep line breaks"),
        "ocr_merge_lines": ("合并成段落", "Merge into paragraphs"),
        "ocr_again": ("重新截取", "Capture again"),
        "ocr_copy": ("复制", "Copy"),
        "ocr_copied_btn": ("已复制 ✓", "Copied ✓"),
        "ocr_close": ("关闭", "Close"),
        "tr_to_en": ("译成英文", "Translate to English"),
        "tr_to_zh": ("译成中文", "Translate to Chinese"),
        "tr_working": ("翻译中…", "Translating…"),
        "tr_show_orig": ("显示原文", "Show original"),
        "tr_show_trans": ("显示译文", "Show translation"),
        "tr_zh": ("中文", "Chinese"),
        "tr_en": ("英文", "English"),
        "tr_stats": ("离线翻译 {0} → {1} · 已自动复制", "Translated offline, {0} → {1} · copied automatically"),
        "tr_failed": ("翻译失败：{0}", "Translation failed: {0}"),
        "tr_need_15": ("离线翻译需要 macOS 15 或更高版本", "Offline translation needs macOS 15 or later"),

        // Settings
        "set_title": ("轻截 设置", "LiteCap Settings"),
        "nav_hotkeys": ("快捷键", "Hotkeys"),
        "nav_output": ("输出", "Output"),
        "nav_general": ("通用", "General"),
        "nav_about": ("关于", "About"),
        "region_desc": ("拖动圈选区域，或单击选中窗口", "Drag an area or click a window"),
        "full_desc": ("截取鼠标所在的屏幕，直接保存", "The screen under the pointer, saved instantly"),
        "ocr_desc": ("圈选后识别文字，完全离线", "Extract text from an area, offline"),
        "hk_note": ("点击方框后按下新组合键；Delete 清除", "Click a box, then press a new combination; Delete clears"),
        "hk_busy": ("这些快捷键被系统或其他 App 占用：{0}", "Already used by the system or another app: {0}"),
        "reset": ("恢复默认", "Reset"),
        "kb_press": ("按下组合键…", "Press keys…"),
        "unset": ("未设置", "Not set"),
        "limit": ("文件大小上限", "Size limit"),
        "limit_desc": ("仅对 JPG 生效，自动调质量", "JPG only · quality auto-tuned to fit"),
        "format": ("文件格式", "Format"),
        "format_desc": ("JPG 体积小；PNG 无损，不限大小", "JPG stays small; PNG is lossless, no size limit"),
        "folder": ("保存位置", "Save to"),
        "open": ("打开", "Open"),
        "change": ("更改…", "Change…"),
        "out_note": ("上限按 1000 字节计算，JPG 严格小于它；截图以文件形式复制", "1 KB = 1000 bytes; JPG files stay strictly below it; captures are copied as files"),
        "lang": ("语言", "Language"),
        "edit_t": ("截图后进入编辑", "Edit after capture"),
        "edit_d": ("画框、箭头、文字、马赛克，可微调选区", "Annotate and fine-tune before saving"),
        "prev_t": ("截图后显示预览", "Show preview"),
        "prev_d": ("右下角小窗显示大小与质量", "A small card shows size and quality"),
        "login_t": ("登录时启动", "Open at login"),
        "login_d": ("建议先把 LiteCap 放进“应用程序”文件夹", "Move LiteCap to Applications first"),
        "about_ver": ("{0}  {1}  ·  轻量截图：JPG 永远小于上限，也可存 PNG", "{0}  {1}  ·  JPG always under the limit, or lossless PNG"),
        "open_logs": ("打开日志文件夹", "Open log folder"),
        "link_github": ("GitHub 主页", "GitHub"),
        "link_feedback": ("反馈问题", "Send feedback"),
        "link_updates": ("检查新版本", "Check for updates"),
        "k_rect": ("矩形", "Rectangle"),
        "k_ellipse": ("椭圆", "Ellipse"),
        "k_arrow": ("箭头", "Arrow"),
        "k_pen": ("画笔", "Pen"),
        "k_marker": ("荧光笔", "Highlighter"),
        "k_text": ("文字", "Text"),
        "k_mosaic": ("马赛克", "Mosaic"),
        "k_size": ("粗细 / 字号", "Size"),
        "k_undo": ("撤销", "Undo"),
        "k_ocr": ("识别文字", "Recognize text"),
        "k_done": ("完成并复制", "Done & copy"),
        "k_cancel": ("取消截图", "Cancel"),
    ]
}
