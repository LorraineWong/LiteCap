// Interface strings in Chinese and English.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace LiteCap
{
    static class L
    {
        static readonly Dictionary<string, string[]> map = new Dictionary<string, string[]>();
        static bool en;

        public static string Lang
        {
            get { return en ? "en" : "zh"; }
            set { en = value == "en"; }
        }

        public static bool IsEnglish { get { return en; } }

        // "zh" for Chinese UI cultures, otherwise "en".
        public static string SystemDefault()
        {
            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? "zh" : "en";
        }

        public static string T(string key)
        {
            string[] v;
            if (!map.TryGetValue(key, out v)) return key;
            return en ? v[1] : v[0];
        }

        public static string F(string key, params object[] args) { return string.Format(T(key), args); }

        static void A(string key, string zh, string english) { map[key] = new string[] { zh, english }; }

        static L()
        {
            // App
            A("app", "轻截 LiteCap", "LiteCap");
            A("app_short", "轻截", "LiteCap");
            A("version", "LiteCap  v{0}", "Version {0}");
            A("running", "轻截 LiteCap 已在运行（见右下角托盘）。", "LiteCap is already running (see the system tray).");
            A("list_sep", "、", ", ");

            // Tray
            A("region", "圈选截图", "Region capture");
            A("full", "全屏截图", "Full screen");
            A("delay", "延时 3 秒圈选", "Region in 3 seconds");
            A("delay_caption", "秒后截图\n可先打开菜单", "seconds to capture\nopen a menu now");
            A("ocr", "识别文字", "Recognize text");
            A("ocr_desc", "圈选后识别文字，完全离线", "Extract text from an area, offline");
            A("hint_ocr", "圈选要识别文字的区域  ·  单击选窗口  ·  Esc 取消", "Select the area to read  ·  Click a window  ·  Esc to cancel");
            A("tip_ocr", "识别文字  O", "Recognize text  O");
            A("k_ocr", "识别文字", "Recognize text");
            A("ocr_title", "识别文字", "Recognized text");
            A("ocr_working", "正在识别…", "Recognizing…");
            A("ocr_done", "识别完成", "Text recognized");
            A("ocr_status", "共 {0} 行  ·  识别语言：{1}  ·  可直接修改后再复制", "{0} lines  ·  Language: {1}  ·  Edit freely, then copy");
            A("ocr_empty", "没有识别到文字", "No text found");
            A("ocr_empty_hint", "这块区域里没有找到可以识别的文字。\r\n可以框选得更紧一些，或把页面放大后点“重新截取”。", "No readable text was found in this area.\r\nTry a tighter selection, or zoom in and choose Capture again.");
            A("ocr_failed", "无法识别", "Recognition unavailable");
            A("ocr_unavailable", "Windows 还没有安装文字识别语言。\r\n\r\n1. 点下方“打开语言设置”\r\n2. 在“中文（简体）”或“English”右侧点 ··· → 语言选项\r\n3. 安装“光学字符识别”，然后点“重新截取”",
                "Windows has no text-recognition language installed yet.\r\n\r\n1. Click Open language settings below\r\n2. Next to English or Chinese (Simplified), click ··· → Language options\r\n3. Install Optical character recognition, then choose Capture again");
            A("ocr_copy", "复制", "Copy");
            A("ocr_copied_btn", "已复制 ✓", "Copied ✓");
            A("ocr_keep_lines", "保留换行", "Keep line breaks");
            A("ocr_merge_lines", "合并成段落", "Merge into paragraphs");
            A("ocr_again", "重新截取", "Capture again");
            A("ocr_fix", "打开语言设置", "Open language settings");
            A("ocr_stats", "{0} 行  ·  {1} 字  ·  {2}  ·  已自动复制", "{0} lines  ·  {1} characters  ·  {2}  ·  copied automatically");
            A("ocr_copied", "已复制到剪贴板", "Copied to clipboard");
            A("ocr_close", "关闭", "Close");
            A("tr_to_en", "译成英文", "Translate to English");
            A("tr_to_zh", "译成中文", "Translate to Chinese");
            A("tr_working", "翻译中…", "Translating…");
            A("tr_progress", "翻译中 {0}/{1}", "Translating {0}/{1}");
            A("tr_show_orig", "显示原文", "Show original");
            A("tr_show_trans", "显示译文", "Show translation");
            A("tr_zh", "中文", "Chinese");
            A("tr_en", "英文", "English");
            A("tr_stats", "离线翻译 {0} → {1}  ·  {2} 秒  ·  已自动复制", "Translated offline, {0} → {1}  ·  {2} s  ·  copied automatically");
            A("tr_dl_title", "离线翻译", "Offline translation");
            A("tr_dl_ask", "离线翻译需要先下载一次翻译包（约 {0} MB）。\n\n之后翻译完全在这台电脑上进行，文字不会上传。\n\n现在下载吗？",
                "Offline translation needs a one-time download of the translation pack (about {0} MB).\n\nAfter that, translation runs entirely on this PC — your text is never uploaded.\n\nDownload now?");
            A("tr_downloading", "下载中 {0}%", "Downloading {0}%");
            A("tr_installing", "安装中…", "Installing…");
            A("tr_dl_failed", "翻译包下载失败：\n{0}\n\n可以用浏览器下载 LiteCap-Translate-1.zip，再在这里选择该文件。\n现在打开下载页面吗？",
                "Couldn't download the translation pack:\n{0}\n\nYou can download LiteCap-Translate-1.zip in your browser and choose it here.\nOpen the download page now?");
            A("tr_pick_zip", "选择下载好的 LiteCap-Translate-1.zip", "Choose the downloaded LiteCap-Translate-1.zip");
            A("tr_failed", "翻译失败：\n{0}", "Translation failed:\n{0}");
            A("open_folder", "打开截图文件夹", "Open screenshots folder");
            A("settings_menu", "设置…", "Settings…");
            A("exit", "退出", "Exit");
            A("tray_tip", "{0} · JPG < {1}KB", "{0} · JPG < {1}KB");
            A("header", "{0}   ·   JPG < {1} KB", "{0}   ·   JPG < {1} KB");
            A("header_png", "{0}   ·   PNG", "{0}   ·   PNG");
            A("tray_tip_png", "{0} · PNG", "{0} · PNG");
            A("hk_busy_title", "部分快捷键被占用", "Some hotkeys are taken");
            A("hk_busy_body", "{0}\n右键托盘图标 → 设置，可改成别的组合", "{0}\nRight-click the tray icon → Settings to choose another");
            A("started_title", "{0} 已启动", "{0} is running");
            A("started_body", "{0}\n右键托盘图标 → 设置，可自定义快捷键", "{0}\nRight-click the tray icon → Settings to customize hotkeys");
            A("sum_region", "圈选", "Region");
            A("sum_full", "全屏", "Full");
            A("unset", "未设置", "Not set");
            A("fail", "截图失败", "Capture failed");
            A("toast_title", "已复制 · {0} KB", "Copied · {0} KB");
            A("toast_orig", "（原 {0}×{1}）", " (from {0}×{1})");
            A("toast_quality", "质量 {0}", "Quality {0}");
            A("toast_link", "点击在文件夹中显示 →", "Click to show in folder →");

            // Overlay
            A("hint_region", "拖动圈选  ·  单击选窗口  ·  Enter 全屏  ·  Esc 取消", "Drag to select  ·  Click a window  ·  Enter = full screen  ·  Esc to cancel");
            A("tip_Rect", "矩形  R", "Rectangle  R");
            A("tip_Ellipse", "椭圆  E", "Ellipse  E");
            A("tip_Arrow", "箭头  A", "Arrow  A");
            A("tip_Pen", "画笔  P", "Pen  P");
            A("tip_Marker", "荧光笔  H", "Highlighter  H");
            A("tip_Text", "文字  T", "Text  T");
            A("tip_Mosaic", "马赛克  M", "Mosaic  M");
            A("tip_undo", "撤销  Ctrl+Z", "Undo  Ctrl+Z");
            A("tip_cancel", "取消  Esc", "Cancel  Esc");
            A("tip_done", "完成并复制  Enter / 双击", "Done & copy  Enter / double-click");
            A("done", "完成", "Done");
            A("size_0", "细  1", "Thin  1");
            A("size_1", "中  2", "Medium  2");
            A("size_2", "粗  3", "Thick  3");
            A("tsize_0", "小字  1", "Small  1");
            A("tsize_1", "中字  2", "Medium  2");
            A("tsize_2", "大字  3", "Large  3");
            A("msize_0", "细格  1", "Fine  1");
            A("msize_1", "中格  2", "Medium  2");
            A("msize_2", "粗格  3", "Coarse  3");

            // Hotkey box
            A("kb_press", "按下新的组合键…", "Press a new combination…");
            A("kb_needmod", "需配合 Ctrl / Alt / Shift / Win", "Add Ctrl / Alt / Shift / Win");

            // Settings
            A("set_title", "轻截 LiteCap 设置", "LiteCap Settings");
            A("nav_hotkeys", "快捷键", "Hotkeys");
            A("nav_output", "输出", "Output");
            A("nav_general", "通用", "General");
            A("nav_about", "关于", "About");
            A("hk_sub", "在任何程序里按下即可截图。点击右侧方框，再按新的组合键。", "Works in any app. Click a box, then press a new combination.");
            A("region_desc", "拖动圈选区域，或单击选中窗口", "Drag an area or click a window");
            A("full_desc", "截取所有屏幕，直接保存", "All screens, saved instantly");
            A("hk_note", "Backspace 清除  ·  要截右键菜单，请用不含 Alt 的组合", "Backspace clears  ·  To capture menus, use a combo without Alt");
            A("reset", "恢复默认", "Reset");
            A("out_sub", "截图保存为 JPG 或 PNG，并以文件形式复制到剪贴板。", "Captures are saved as JPG or PNG and copied to the clipboard as a file.");
            A("format", "文件格式", "Format");
            A("format_desc", "JPG 体积小；PNG 无损，不限大小", "JPG stays small; PNG is lossless, no size limit");
            A("limit", "文件大小上限", "Size limit");
            A("limit_desc", "仅对 JPG 生效，自动调质量", "JPG only, quality auto-tuned");
            A("folder", "保存位置", "Save to");
            A("open", "打开", "Open");
            A("change", "更改…", "Change…");
            A("pick_folder", "选择截图保存位置", "Choose where to save screenshots");
            A("out_note", "上限按 1000 字节计算，JPG 文件严格小于它", "1 KB = 1000 bytes; JPG files always stay strictly below the limit");
            A("gen_sub", "语言、截图后的流程，以及开机行为。", "Language, what happens after a capture, and startup.");
            A("lang", "语言", "Language");
            A("lang_desc", "界面显示语言", "Interface language");
            A("edit_t", "截图后进入编辑", "Edit after capture");
            A("edit_d", "画框、箭头、文字、马赛克，可微调选区；关闭则松手即截图", "Annotate and fine-tune; off = capture on release");
            A("prev_t", "截图后显示预览", "Show preview");
            A("prev_d", "右下角小窗显示大小与质量，点击可定位文件", "A small card shows size and quality; click to locate");
            A("auto_t", "开机自动启动", "Start with Windows");
            A("auto_d", "登录 Windows 后自动在托盘待命", "Waits in the tray after you sign in");
            A("about_sub", "编辑模式下的快捷键速查。", "Editor shortcuts at a glance.");
            A("k_rect", "矩形", "Rectangle");
            A("k_ellipse", "椭圆", "Ellipse");
            A("k_arrow", "箭头", "Arrow");
            A("k_pen", "画笔", "Pen");
            A("k_marker", "荧光笔", "Highlighter");
            A("k_text", "文字", "Text");
            A("k_mosaic", "马赛克", "Mosaic");
            A("k_size", "粗细 / 字号", "Size");
            A("k_undo", "撤销", "Undo");
            A("k_done", "完成并复制", "Done & copy");
            A("k_right", "退出工具 / 重选", "Exit tool / reselect");
            A("k_cancel", "取消截图", "Cancel");
            A("key_right", "右键", "R-click");
            A("about_ver", "{0}  v{1}  ·  轻量截图：JPG 永远小于上限，也可存 PNG", "{0}  v{1}  ·  JPG always under the limit, or lossless PNG");
            A("save", "保存", "Save");
            A("cancel", "取消", "Cancel");
            A("err_dup", "{0} 和 {1} 的快捷键重复", "{0} and {1} use the same hotkey");
            A("err_busy", "已被其他程序占用：{0}", "Already used by another app: {0}");
            A("err_kb", "大小上限请填 10 – 10000 之间的数字", "Size limit must be a number from 10 to 10000");
            A("crash_msg", "遇到一个问题，已自动记录到日志，程序会继续运行。\n\n要打开日志文件夹吗？（反馈问题时把日志发给开发者即可）",
                "Something went wrong. It has been logged and LiteCap will keep running.\n\nOpen the log folder? (Send the log file when reporting the issue.)");
            A("crash_fatal", "遇到严重错误，程序需要退出。错误已记录到日志，请重新打开轻截。",
                "A serious error occurred and LiteCap has to close. It has been logged; please start LiteCap again.");
            A("open_logs", "打开日志文件夹", "Open log folder");
            A("link_github", "GitHub 主页", "GitHub");
            A("link_feedback", "反馈问题", "Send feedback");
            A("link_updates", "检查新版本", "Check for updates");
            A("err_folder", "保存位置无效或无法写入", "The save location is invalid or not writable");
        }
    }
}
