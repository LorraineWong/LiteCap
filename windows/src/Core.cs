// LiteCap - tray screenshot tool: JPG files always under a size limit (default 100 KB), or lossless PNG.
// Core: entry point, Win32 interop, shared UI helpers, hotkey model, settings, JPEG fitting.
// Kept C# 5 compatible so it builds with the csc.exe that ships with Windows (.NET Framework 4.x).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Reflection;
using Microsoft.Win32;

[assembly: AssemblyTitle("LiteCap")]
[assembly: AssemblyDescription("Lightweight screenshot tool: JPG under a size limit, or PNG")]
[assembly: AssemblyProduct("LiteCap")]
[assembly: AssemblyCopyright("LiteCap")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace LiteCap
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length >= 3 && args[0] == "--translate-selftest") return TranslateSelfTest.Run(args[1], args[2]);
            bool created;
            Settings boot = Settings.Load();
            L.Lang = boot.Lang;
            Crash.Install();
            using (Mutex mutex = new Mutex(true, "LiteCap_SingleInstance_Mutex", out created))
            {
                if (!created)
                {
                    MessageBox.Show(L.T("running"), L.T("app"));
                    return 0;
                }
                try { Environment.CurrentDirectory = Path.GetDirectoryName(Application.ExecutablePath); } catch { }
                Native.EnableDpiAwareness();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp());
            }
            return 0;
        }
    }

    // ------------------------------------------------------------------
    // Win32 interop
    // ------------------------------------------------------------------
    static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr hIcon);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool GetLayeredWindowAttributes(IntPtr hWnd, out uint key, out byte alpha, out uint flags);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out RECT value, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out int value, int size);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hWnd, int attr, ref int value, int size);

        // Windows 11: caption mode/colours. Silently ignored on older Windows.
        public static void StyleCaption(IntPtr hWnd, bool dark, Color caption, Color border)
        {
            try
            {
                int on = dark ? 1 : 0;
                if (DwmSetWindowAttribute(hWnd, 20, ref on, 4) != 0) DwmSetWindowAttribute(hWnd, 19, ref on, 4);
                int cap = caption.R | (caption.G << 8) | (caption.B << 16);
                int bor = border.R | (border.G << 8) | (border.B << 16);
                DwmSetWindowAttribute(hWnd, 35, ref cap, 4);
                DwmSetWindowAttribute(hWnd, 34, ref bor, 4);
            }
            catch { }
        }

        // Windows 11: rounded corners for borderless popups.
        public static void RoundCorners(IntPtr hWnd)
        {
            try { int v = 2; DwmSetWindowAttribute(hWnd, 33, ref v, 4); } catch { }
        }

        public const int WM_HOTKEY = 0x0312;
        public const int WM_DPICHANGED = 0x02E0;
        public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;
        const int GWL_EXSTYLE = -20;
        const int WS_EX_TOOLWINDOW = 0x80;
        const int WS_EX_TRANSPARENT = 0x20;
        const int WS_EX_LAYERED = 0x80000;
        const int WS_EX_NOACTIVATE = 0x08000000;
        const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        const int DWMWA_CLOAKED = 14;

        public static void EnableDpiAwareness()
        {
            // Per-monitor v2 so captured pixels and coordinates are physical, not scaled.
            try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } catch { }
            try { SetProcessDPIAware(); } catch { }
        }

        // Visible frame bounds (excludes the invisible resize border on Win10/11).
        public static Rectangle GetWindowBounds(IntPtr hWnd)
        {
            RECT r;
            if (DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf(typeof(RECT))) != 0)
                GetWindowRect(hWnd, out r);
            return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        }

        static bool IsCloaked(IntPtr hWnd)
        {
            int cloaked;
            return DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out cloaked, 4) == 0 && cloaked != 0;
        }

        // Top-level windows in z-order (topmost first), screen coordinates.
        public static List<Rectangle> GetVisibleWindowRects()
        {
            List<Rectangle> list = new List<Rectangle>();
            uint ownPid = (uint)Process.GetCurrentProcess().Id;
            IntPtr shell = GetShellWindow();
            EnumWindows(delegate (IntPtr h, IntPtr p)
            {
                if (h == shell || !IsWindowVisible(h) || IsIconic(h) || IsCloaked(h)) return true;
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                if (pid == ownPid) return true;
                int ex = GetWindowLong(h, GWL_EXSTYLE);
                // Click-through and tool windows: screen watermarks, overlays, floating toolbars.
                if ((ex & (WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT)) != 0) return true;
                bool untitled = GetWindowTextLength(h) == 0;
                if (untitled && (ex & WS_EX_NOACTIVATE) != 0) return true;
                if ((ex & WS_EX_LAYERED) != 0)
                {
                    uint key, flags;
                    byte alpha;
                    if (GetLayeredWindowAttributes(h, out key, out alpha, out flags) && (flags & 0x2) != 0 && alpha < 40)
                        return true;
                }
                Rectangle r = GetWindowBounds(h);
                if (r.Width < 20 || r.Height < 20) return true;
                // An untitled window covering a whole monitor is almost always an invisible overlay.
                if (untitled)
                    foreach (Screen sc in Screen.AllScreens)
                        if (r.Contains(sc.Bounds)) return true;
                list.Add(r);
                return true;
            }, IntPtr.Zero);
            return list;
        }

        public static bool IsDesktopOrTaskbar(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero || hWnd == GetShellWindow()) return true;
            StringBuilder sb = new StringBuilder(64);
            GetClassName(hWnd, sb, sb.Capacity);
            string c = sb.ToString();
            return c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd";
        }

        public static bool WinKeyDown()
        {
            return (GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0;
        }
    }

    // ------------------------------------------------------------------
    // Shared UI helpers (DPI scale, palette)
    // ------------------------------------------------------------------
    static class Ui
    {
        static float scale;

        public static float S
        {
            get
            {
                if (scale == 0)
                    using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) scale = Math.Max(1f, g.DpiX / 96f);
                return scale;
            }
        }

        public static int Px(float v) { return (int)Math.Round(v * S); }

        // Dark palette: capture overlay (toolbars, tags, magnifier).
        public static readonly Color Bg = Color.FromArgb(28, 31, 38);
        public static readonly Color BgSoft = Color.FromArgb(48, 52, 62);
        public static readonly Color Line = Color.FromArgb(66, 71, 84);
        public static readonly Color Accent = Color.FromArgb(147, 197, 253);
        public static readonly Color AccentDeep = Color.FromArgb(37, 99, 235);
        public static readonly Color AccentHover = Color.FromArgb(59, 130, 246);
        public static readonly Color Select = Color.FromArgb(59, 130, 246);
        public static readonly Color TextDim = Color.FromArgb(156, 163, 175);
        public static readonly Color TextMain = Color.FromArgb(236, 239, 244);

        // Light palette: settings window, tray menu, preview popup.
        public static readonly Color Page = Color.FromArgb(244, 246, 250);
        public static readonly Color Sidebar = Color.FromArgb(236, 240, 246);
        public static readonly Color Surface = Color.White;
        public static readonly Color FieldBg = Color.FromArgb(248, 250, 252);
        public static readonly Color Border = Color.FromArgb(222, 227, 235);
        public static readonly Color Divider = Color.FromArgb(238, 241, 246);
        public static readonly Color Ink = Color.FromArgb(17, 24, 39);
        public static readonly Color InkSoft = Color.FromArgb(107, 114, 128);
        public static readonly Color Blue = Color.FromArgb(37, 99, 235);
        public static readonly Color BlueHover = Color.FromArgb(29, 78, 216);
        public static readonly Color BlueSoft = Color.FromArgb(235, 242, 255);
        public static readonly Color IconInk = Color.FromArgb(71, 85, 105);
        public static readonly Color Danger = Color.FromArgb(220, 38, 38);
        public const string Version = "1.1.0";
        public const string RepoUrl = "https://github.com/LorraineWong/LiteCap";

        public static GraphicsPath Round(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 1) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Rectangle r, int radius, Color fill)
        {
            using (GraphicsPath p = Round(r, radius))
            using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
        }

        public static void StrokeRound(Graphics g, Rectangle r, int radius, Color line)
        {
            using (GraphicsPath p = Round(new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1), radius))
            using (Pen pen = new Pen(line)) g.DrawPath(pen, p);
        }

        // Soft drop shadow under a rounded panel.
        public static void Shadow(Graphics g, Rectangle r, int radius)
        {
            for (int i = 1; i <= 4; i++)
            {
                Rectangle s = r;
                s.Inflate(i * Px(2), i * Px(2));
                s.Offset(0, Px(2));
                FillRound(g, s, radius + i * Px(2), Color.FromArgb(22 - i * 4, 0, 0, 0));
            }
        }

        public static Font F(float px, FontStyle style)
        {
            return new Font("Microsoft YaHei UI", Px(px), style, GraphicsUnit.Pixel);
        }

        // The multi-size app.ico embedded at build time.
        public static Icon AppIcon(int size)
        {
            using (Stream st = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
            {
                if (st != null) return new Icon(st, size, size);
            }
            return Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }

        static Bitmap logoSource;

        // Crisp logo bitmap at any size, from the embedded 128px PNG.
        public static Bitmap Logo(int size)
        {
            if (logoSource == null)
                using (Stream st = Assembly.GetExecutingAssembly().GetManifestResourceStream("logo.png"))
                    logoSource = new Bitmap(st);
            Bitmap b = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(logoSource, 0, 0, size, size);
            }
            return b;
        }
    }

    // ------------------------------------------------------------------
    // Hotkey value: modifiers + key, serialisable as "Ctrl+Alt+S"
    // ------------------------------------------------------------------
    struct Hotkey
    {
        public uint Mods;
        public Keys Key;

        public Hotkey(uint mods, Keys key) { Mods = mods; Key = key; }

        public static readonly Hotkey Empty = new Hotkey(0, Keys.None);
        public bool IsEmpty { get { return Key == Keys.None; } }
        public bool SameAs(Hotkey o) { return Mods == o.Mods && Key == o.Key; }

        List<string> ModNames()
        {
            List<string> p = new List<string>();
            if ((Mods & Native.MOD_CONTROL) != 0) p.Add("Ctrl");
            if ((Mods & Native.MOD_ALT) != 0) p.Add("Alt");
            if ((Mods & Native.MOD_SHIFT) != 0) p.Add("Shift");
            if ((Mods & Native.MOD_WIN) != 0) p.Add("Win");
            return p;
        }

        public string Serialize()
        {
            if (IsEmpty) return "";
            List<string> p = ModNames();
            p.Add(Key.ToString());
            return string.Join("+", p.ToArray());
        }

        public string Display
        {
            get
            {
                List<string> p = ModNames();
                if (!IsEmpty) p.Add(KeyName(Key));
                return string.Join(" + ", p.ToArray());
            }
        }

        public static string KeyName(Keys k)
        {
            if (k >= Keys.D0 && k <= Keys.D9) return ((int)(k - Keys.D0)).ToString();
            if (k >= Keys.NumPad0 && k <= Keys.NumPad9) return "Num " + (int)(k - Keys.NumPad0);
            switch (k)
            {
                case Keys.PrintScreen: return "PrtSc";
                case Keys.Return: return "Enter";
                case Keys.Next: return "PageDown";
                case Keys.Prior: return "PageUp";
                case Keys.Oemtilde: return "`";
                case Keys.OemMinus: return "-";
                case Keys.Oemplus: return "=";
                case Keys.OemOpenBrackets: return "[";
                case Keys.Oem6: return "]";
                case Keys.Oem5: return "\\";
                case Keys.Oem1: return ";";
                case Keys.Oem7: return "'";
                case Keys.Oemcomma: return ",";
                case Keys.OemPeriod: return ".";
                case Keys.OemQuestion: return "/";
                default: return k.ToString();
            }
        }

        public static bool CanStandAlone(Keys k)
        {
            return (k >= Keys.F1 && k <= Keys.F24) || k == Keys.PrintScreen || k == Keys.Pause || k == Keys.Scroll;
        }

        public static bool IsModifierKey(Keys k)
        {
            return k == Keys.ControlKey || k == Keys.ShiftKey || k == Keys.Menu || k == Keys.LWin || k == Keys.RWin
                || k == Keys.LControlKey || k == Keys.RControlKey || k == Keys.LShiftKey || k == Keys.RShiftKey
                || k == Keys.LMenu || k == Keys.RMenu;
        }

        public static Hotkey Parse(string s, Hotkey fallback)
        {
            if (s == null) return fallback;
            s = s.Trim();
            if (s.Length == 0) return Empty;
            uint mods = 0;
            Keys key = Keys.None;
            foreach (string raw in s.Split('+'))
            {
                string t = raw.Trim();
                if (t.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) mods |= Native.MOD_CONTROL;
                else if (t.Equals("Alt", StringComparison.OrdinalIgnoreCase)) mods |= Native.MOD_ALT;
                else if (t.Equals("Shift", StringComparison.OrdinalIgnoreCase)) mods |= Native.MOD_SHIFT;
                else if (t.Equals("Win", StringComparison.OrdinalIgnoreCase)) mods |= Native.MOD_WIN;
                else
                {
                    try { key = (Keys)Enum.Parse(typeof(Keys), t, true); }
                    catch { return fallback; }
                }
            }
            if (key == Keys.None) return fallback;
            return new Hotkey(mods, key);
        }
    }

    // ------------------------------------------------------------------
    // Settings stored in %APPDATA%\LiteCap\settings.ini
    // ------------------------------------------------------------------
    class Settings
    {
        public static readonly Hotkey DefaultRegion = new Hotkey(Native.MOD_CONTROL, Keys.D1);
        public static readonly Hotkey DefaultFull = new Hotkey(Native.MOD_CONTROL, Keys.D2);
        public static readonly Hotkey DefaultOcr = new Hotkey(Native.MOD_CONTROL, Keys.D3);   // matches macOS Cmd+3

        // Defaults before v2.3; saved values equal to these are upgraded to the new defaults once.
        static readonly Hotkey OldRegion = new Hotkey(Native.MOD_CONTROL | Native.MOD_ALT, Keys.S);
        static readonly Hotkey OldFull = new Hotkey(Native.MOD_CONTROL | Native.MOD_ALT, Keys.F);
        const int HotkeyVersion = 5;

        public int MaxKB = 100;              // hard limit for JPG; output is always strictly smaller
        public bool Png = false;             // PNG is lossless and has no size limit
        public bool AdjustSelection = true;  // after dragging, allow move/resize before confirming
        public bool ShowPreview = true;      // small preview popup after each capture
        public bool Welcomed = false;
        public string Lang = "en";   // English first; Chinese is optional
        public Hotkey HkRegion = DefaultRegion;
        public Hotkey HkFull = DefaultFull;
        public Hotkey HkOcr = DefaultOcr;
        public string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "LiteCap");

        public Settings Clone() { return (Settings)MemberwiseClone(); }

        static Hotkey AltToCtrl(Hotkey hk)
        {
            bool digit = hk.Key == Keys.D1 || hk.Key == Keys.D2 || hk.Key == Keys.D3;
            return digit && hk.Mods == Native.MOD_ALT ? new Hotkey(Native.MOD_CONTROL, hk.Key) : hk;
        }

        static string IniPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LiteCap", "settings.ini"); }
        }

        public static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                string legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Shot100K", "settings.ini");
                if (!File.Exists(IniPath) && File.Exists(legacy))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(IniPath));
                    File.Copy(legacy, IniPath);
                }
                if (!File.Exists(IniPath)) return s;
                int hkVer = 0, langVer = 0;
                foreach (string line in File.ReadAllLines(IniPath, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;
                    string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                    int n;
                    if (k == "maxKB" && int.TryParse(v, out n) && n >= 10) s.MaxKB = n;
                    else if (k == "format") s.Png = v == "png";
                    else if (k == "adjust") s.AdjustSelection = v == "1";
                    else if (k == "preview") s.ShowPreview = v == "1";
                    else if (k == "welcomed") s.Welcomed = v == "1";
                    else if (k == "hkRegion") s.HkRegion = Hotkey.Parse(v, DefaultRegion);
                    else if (k == "hkFull") s.HkFull = Hotkey.Parse(v, DefaultFull);
                    else if (k == "hkOcr") s.HkOcr = Hotkey.Parse(v, DefaultOcr);
                    else if (k == "folder" && v.Length > 0) s.Folder = v;
                    else if (k == "hkVer" && int.TryParse(v, out n)) hkVer = n;
                    else if (k == "lang" && (v == "zh" || v == "en")) s.Lang = v;
                    else if (k == "langVer" && int.TryParse(v, out n)) langVer = n;
                }
                if (langVer < 1) s.Lang = "en";   // v1.2: interface defaults to English once
                if (hkVer < 2)
                {
                    if (s.HkRegion.SameAs(OldRegion)) s.HkRegion = DefaultRegion;
                    if (s.HkFull.SameAs(OldFull)) s.HkFull = DefaultFull;
                }
                if (hkVer < 3)
                {
                    // Alt closes open menus, so Alt+1/2/3 from earlier builds move to Ctrl, keeping each digit.
                    s.HkRegion = AltToCtrl(s.HkRegion);
                    s.HkFull = AltToCtrl(s.HkFull);
                }
                if (hkVer < 4 && s.HkFull.Mods == Native.MOD_CONTROL && (s.HkFull.Key == Keys.D2 || s.HkFull.Key == Keys.D3))
                    s.HkFull = DefaultFull;   // window capture was removed; full screen takes Ctrl+2
                if (hkVer < 5 && s.HkOcr.IsEmpty && !s.HkRegion.SameAs(DefaultOcr) && !s.HkFull.SameAs(DefaultOcr))
                    s.HkOcr = DefaultOcr;   // v1.1: OCR gets Ctrl+3 by default, like Cmd+3 on macOS
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(IniPath));
                File.WriteAllLines(IniPath, new string[] {
                    "maxKB=" + MaxKB,
                    "format=" + (Png ? "png" : "jpg"),
                    "adjust=" + (AdjustSelection ? "1" : "0"),
                    "preview=" + (ShowPreview ? "1" : "0"),
                    "welcomed=" + (Welcomed ? "1" : "0"),
                    "hkRegion=" + HkRegion.Serialize(),
                    "hkFull=" + HkFull.Serialize(),
                    "hkOcr=" + HkOcr.Serialize(),
                    "hkVer=" + HotkeyVersion,
                    "lang=" + Lang,
                    "langVer=1",
                    "folder=" + Folder
                }, Encoding.UTF8);
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------
    // JPEG size fitting
    // ------------------------------------------------------------------
    static class JpegFitter
    {
        const int QualityMax = 92;
        const int QualityFloor = 35;   // below this, text gets unreadable; shrink pixels instead

        static ImageCodecInfo jpegCodec;

        static ImageCodecInfo Codec
        {
            get
            {
                if (jpegCodec == null)
                    foreach (ImageCodecInfo c in ImageCodecInfo.GetImageEncoders())
                        if (c.MimeType == "image/jpeg") jpegCodec = c;
                return jpegCodec;
            }
        }

        public static byte[] Encode(Bitmap bmp, int quality)
        {
            using (MemoryStream ms = new MemoryStream())
            using (EncoderParameters ps = new EncoderParameters(1))
            {
                ps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)quality);
                bmp.Save(ms, Codec, ps);
                return ms.ToArray();
            }
        }

        static Bitmap ResizeTo(Bitmap src, int w, int h)
        {
            Bitmap dst = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(dst))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.DrawImage(src, new Rectangle(0, 0, w, h));
            }
            return dst;
        }

        // Highest JPEG quality whose size is strictly below maxBytes; shrinks dimensions only if needed.
        public static byte[] Fit(Bitmap src, int maxBytes, out int quality, out Size finalSize)
        {
            Bitmap work = src;
            double scale = 1.0;
            try
            {
                while (true)
                {
                    byte[] top = Encode(work, QualityMax);
                    if (top.Length < maxBytes) { quality = QualityMax; finalSize = work.Size; return top; }

                    byte[] floor = Encode(work, QualityFloor);
                    if (floor.Length < maxBytes)
                    {
                        byte[] best = floor;
                        int bestQ = QualityFloor, lo = QualityFloor + 1, hi = QualityMax - 1;
                        while (lo <= hi)
                        {
                            int mid = (lo + hi) / 2;
                            byte[] d = Encode(work, mid);
                            if (d.Length < maxBytes) { best = d; bestQ = mid; lo = mid + 1; }
                            else hi = mid - 1;
                        }
                        quality = bestQ; finalSize = work.Size;
                        return best;
                    }

                    // Too big even at floor quality: shrink by the overshoot ratio (size ~ pixel count).
                    double step = Math.Sqrt((double)maxBytes / floor.Length) * 0.95;
                    scale *= Math.Max(0.4, Math.Min(0.92, step));
                    int w = (int)(src.Width * scale), h = (int)(src.Height * scale);
                    if (w < 32 || h < 32)
                    {
                        quality = 10; finalSize = work.Size;
                        return Encode(work, 10);
                    }
                    Bitmap next = ResizeTo(src, w, h);
                    if (work != src) work.Dispose();
                    work = next;
                }
            }
            finally
            {
                if (work != src) work.Dispose();
            }
        }
    }
}
