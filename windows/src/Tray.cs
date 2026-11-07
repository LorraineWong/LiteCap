// Tray application: hotkeys, dark tray menu, capture pipeline, preview popup.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LiteCap
{
    // ------------------------------------------------------------------
    // Global hotkeys
    // ------------------------------------------------------------------
    class HotkeyWindow : NativeWindow, IDisposable
    {
        const int TestId = 900;
        public event Action<int> HotkeyPressed;
        readonly List<int> ids = new List<int>();

        public HotkeyWindow()
        {
            CreateParams cp = new CreateParams();
            cp.Caption = "LiteCap.Hotkeys";
            CreateHandle(cp);
        }

        public bool Register(int id, Hotkey hk)
        {
            if (hk.IsEmpty) return true;
            bool ok = Native.RegisterHotKey(Handle, id, hk.Mods | Native.MOD_NOREPEAT, (uint)hk.Key);
            if (ok) ids.Add(id);
            return ok;
        }

        public bool CanRegister(Hotkey hk)
        {
            if (hk.IsEmpty) return true;
            if (!Native.RegisterHotKey(Handle, TestId, hk.Mods | Native.MOD_NOREPEAT, (uint)hk.Key)) return false;
            Native.UnregisterHotKey(Handle, TestId);
            return true;
        }

        public void UnregisterAll()
        {
            foreach (int id in ids) Native.UnregisterHotKey(Handle, id);
            ids.Clear();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY && HotkeyPressed != null) HotkeyPressed(m.WParam.ToInt32());
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            UnregisterAll();
            DestroyHandle();
        }
    }

    // ------------------------------------------------------------------
    // Dark tray menu
    // ------------------------------------------------------------------
    class MenuColors : ProfessionalColorTable
    {
        public override Color MenuBorder { get { return Ui.Border; } }
        public override Color MenuItemBorder { get { return Ui.BlueSoft; } }
        public override Color MenuItemSelected { get { return Ui.BlueSoft; } }
        public override Color MenuItemSelectedGradientBegin { get { return Ui.BlueSoft; } }
        public override Color MenuItemSelectedGradientEnd { get { return Ui.BlueSoft; } }
        public override Color MenuItemPressedGradientBegin { get { return Color.FromArgb(219, 231, 254); } }
        public override Color MenuItemPressedGradientEnd { get { return Color.FromArgb(219, 231, 254); } }
        public override Color ToolStripDropDownBackground { get { return Ui.Surface; } }
        public override Color ImageMarginGradientBegin { get { return Ui.Surface; } }
        public override Color ImageMarginGradientMiddle { get { return Ui.Surface; } }
        public override Color ImageMarginGradientEnd { get { return Ui.Surface; } }
        public override Color SeparatorDark { get { return Ui.Divider; } }
        public override Color SeparatorLight { get { return Ui.Surface; } }
    }

    class MenuRenderer : ToolStripProfessionalRenderer
    {
        public MenuRenderer() : base(new MenuColors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            ToolStripMenuItem mi = e.Item as ToolStripMenuItem;
            bool shortcut = mi != null && !string.IsNullOrEmpty(mi.ShortcutKeyDisplayString) && e.Text == mi.ShortcutKeyDisplayString;
            e.TextColor = !e.Item.Enabled || shortcut || e.Item is ToolStripLabel ? Ui.InkSoft : Ui.Ink;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (Pen p = new Pen(Ui.Divider))
                e.Graphics.DrawLine(p, Ui.Px(10), y, e.Item.Width - Ui.Px(10), y);
        }
    }

    // ------------------------------------------------------------------
    // Preview popup shown bottom-right after a capture
    // ------------------------------------------------------------------
    class PreviewToast : Form
    {
        static PreviewToast current;

        readonly Bitmap thumb;
        readonly string title, info, path;
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly Font fTitle, fInfo, fLink;
        int elapsed;
        bool fading;

        public static void Pop(Image img, string title, string info, string path)
        {
            if (current != null && !current.IsDisposed) current.Close();
            current = new PreviewToast(img, title, info, path);
            current.Show();
        }

        PreviewToast(Image img, string title, string info, string path)
        {
            this.title = title;
            this.info = info;
            this.path = path;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            BackColor = Ui.Surface;
            Cursor = Cursors.Hand;

            int w = Ui.Px(392), h = Ui.Px(104);
            Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Bounds = new Rectangle(wa.Right - w - Ui.Px(16), wa.Bottom - h - Ui.Px(16), w, h);

            fTitle = Ui.F(15, FontStyle.Bold);
            fInfo = Ui.F(12.5f, FontStyle.Regular);
            fLink = Ui.F(12, FontStyle.Regular);

            int tw = Ui.Px(116), th = Ui.Px(76);
            double s = Math.Min((double)tw / img.Width, (double)th / img.Height);
            thumb = new Bitmap(Math.Max(1, (int)(img.Width * s)), Math.Max(1, (int)(img.Height * s)));
            using (Graphics g = Graphics.FromImage(thumb))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(img, 0, 0, thumb.Width, thumb.Height);
            }

            timer.Interval = 40;
            timer.Tick += OnTick;
            timer.Start();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 | 0x80 | 0x8;   // NOACTIVATE | TOOLWINDOW | TOPMOST
                cp.ClassStyle |= 0x20000;                // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.RoundCorners(Handle);
        }

        void OnTick(object sender, EventArgs e)
        {
            bool inside = ClientRectangle.Contains(PointToClient(Cursor.Position));
            if (inside) { elapsed = 0; fading = false; Opacity = 1; return; }
            elapsed += timer.Interval;
            if (!fading && elapsed >= 3200) fading = true;
            if (fading)
            {
                Opacity -= 0.12;
                if (Opacity <= 0.05) Close();
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (File.Exists(path)) Process.Start("explorer.exe", "/select,\"" + path + "\"");
            Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int pad = Ui.Px(14);
            using (Pen p = new Pen(Ui.Border)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);

            int boxW = Ui.Px(116), boxH = Ui.Px(76);
            Rectangle box = new Rectangle(pad, (Height - boxH) / 2, boxW, boxH);
            Ui.FillRound(g, box, Ui.Px(8), Ui.Page);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            Rectangle img = new Rectangle(box.X + (boxW - thumb.Width) / 2, box.Y + (boxH - thumb.Height) / 2, thumb.Width, thumb.Height);
            using (TextureBrush tb = new TextureBrush(thumb, WrapMode.Clamp))
            {
                tb.TranslateTransform(img.X, img.Y);
                using (GraphicsPath gp = Ui.Round(img, Ui.Px(6))) g.FillPath(tb, gp);
            }
            Ui.StrokeRound(g, img, Ui.Px(6), Ui.Border);

            int tx = box.Right + pad;
            int y = Ui.Px(16);
            Rectangle ok = new Rectangle(tx, y + Ui.Px(2), Ui.Px(18), Ui.Px(18));
            Ui.FillRound(g, ok, Ui.Px(9), Color.FromArgb(22, 163, 74));
            Icons.Draw(g, "Done", new Rectangle(ok.X - Ui.Px(2), ok.Y - Ui.Px(2), ok.Width + Ui.Px(4), ok.Height + Ui.Px(4)), Color.White);
            TextRenderer.DrawText(g, title, fTitle, new Point(ok.Right + Ui.Px(8), y), Ui.Ink);
            y += fTitle.Height + Ui.Px(8);
            TextRenderer.DrawText(g, info, fInfo, new Point(tx, y), Ui.InkSoft);
            y += fInfo.Height + Ui.Px(6);
            TextRenderer.DrawText(g, L.T("toast_link"), fLink, new Point(tx, y), Ui.Blue);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                thumb.Dispose();
                fTitle.Dispose(); fInfo.Dispose(); fLink.Dispose();
                if (current == this) current = null;
            }
            base.Dispose(disposing);
        }
    }

    // ------------------------------------------------------------------
    // Small countdown badge for delayed capture; it hides itself before the screen is grabbed
    // and never takes focus, so open menus stay open.
    // ------------------------------------------------------------------
    class CountdownToast : Form
    {
        int remaining;
        readonly Action done;
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly Font fNum, fCap;

        public static void Run(int seconds, Action done)
        {
            new CountdownToast(seconds, done).Show();
        }

        CountdownToast(int seconds, Action done)
        {
            remaining = seconds;
            this.done = done;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            BackColor = Ui.Bg;
            int w = Ui.Px(178), h = Ui.Px(64);
            Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Bounds = new Rectangle(wa.Right - w - Ui.Px(16), wa.Bottom - h - Ui.Px(16), w, h);
            fNum = Ui.F(30, FontStyle.Bold);
            fCap = Ui.F(12, FontStyle.Regular);
            timer.Interval = 1000;
            timer.Tick += OnTick;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 | 0x80 | 0x8 | 0x20;   // NOACTIVATE | TOOLWINDOW | TOPMOST | TRANSPARENT
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.RoundCorners(Handle);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            timer.Start();
        }

        void OnTick(object sender, EventArgs e)
        {
            remaining--;
            if (remaining > 0) { Invalidate(); return; }
            timer.Stop();
            Hide();
            // Give the compositor a moment to remove the badge before the screen is grabbed.
            System.Windows.Forms.Timer settle = new System.Windows.Forms.Timer();
            settle.Interval = 120;
            settle.Tick += delegate
            {
                settle.Stop();
                settle.Dispose();
                Close();
                done();
            };
            settle.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle num = new Rectangle(Ui.Px(8), 0, Ui.Px(50), Height);
            TextRenderer.DrawText(g, remaining.ToString(), fNum, num, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            Rectangle cap = new Rectangle(num.Right, 0, Width - num.Right - Ui.Px(10), Height);
            TextRenderer.DrawText(g, L.T("delay_caption"), fCap, cap, Ui.TextDim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { timer.Dispose(); fNum.Dispose(); fCap.Dispose(); }
            base.Dispose(disposing);
        }
    }

    // ------------------------------------------------------------------
    // Tray application
    // ------------------------------------------------------------------
    enum CaptureMode { Region, FullScreen, Ocr }

    class Shot
    {
        public byte[] Data;
        public bool Png;
        public int Quality;
        public Size Size, Original;
        public string Path;
    }

    class TrayApp : ApplicationContext
    {
        const int HK_REGION = 1, HK_FULL = 3, HK_OCR = 4;
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        readonly NotifyIcon tray;
        readonly HotkeyWindow hotkeys;
        readonly Icon appIcon;
        Settings settings;
        ToolStripMenuItem miRegion, miFull, miOcr;
        ToolStripLabel header;
        bool busy;
        SettingsForm settingsForm;

        public TrayApp()
        {
            settings = Settings.Load();
            appIcon = Ui.AppIcon(SystemInformation.SmallIconSize.Width);

            tray = new NotifyIcon();
            tray.Icon = appIcon;
            tray.ContextMenuStrip = BuildMenu();
            UpdateTrayText();
            tray.Visible = true;
            tray.MouseDoubleClick += delegate (object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) StartCapture(CaptureMode.Region, true);
            };

            hotkeys = new HotkeyWindow();
            hotkeys.HotkeyPressed += OnHotkey;
            List<string> failed = ApplyHotkeys();

            MigrateLegacyAutostart();
            if (IsAutostart()) SetAutostart(true);   // keep the path current if the exe moved

            if (failed.Count > 0)
            {
                tray.ShowBalloonTip(5000, L.T("hk_busy_title"),
                    L.F("hk_busy_body", string.Join(L.T("list_sep"), failed.ToArray())), ToolTipIcon.Warning);
            }
            else if (!settings.Welcomed)
            {
                tray.ShowBalloonTip(5000, L.F("started_title", L.T("app")), L.F("started_body", HotkeySummary()), ToolTipIcon.Info);
                settings.Welcomed = true;
                settings.Save();
            }
        }

        void UpdateTrayText()
        {
            tray.Text = settings.Png ? L.F("tray_tip_png", L.T("app")) : L.F("tray_tip", L.T("app"), settings.MaxKB);
            if (header != null) header.Text = settings.Png ? L.F("header_png", L.T("app")) : L.F("header", L.T("app"), settings.MaxKB);
        }

        string HotkeySummary()
        {
            return L.T("sum_region") + " " + Show(settings.HkRegion) + "  ·  " + L.T("sum_full") + " " + Show(settings.HkFull);
        }

        static string Show(Hotkey hk) { return hk.IsEmpty ? L.T("unset") : hk.Display.Replace(" ", ""); }

        List<string> ApplyHotkeys()
        {
            hotkeys.UnregisterAll();
            List<string> failed = new List<string>();
            if (!hotkeys.Register(HK_REGION, settings.HkRegion)) failed.Add(L.T("sum_region") + " " + settings.HkRegion.Display);
            if (!hotkeys.Register(HK_FULL, settings.HkFull)) failed.Add(L.T("sum_full") + " " + settings.HkFull.Display);
            if (!hotkeys.Register(HK_OCR, settings.HkOcr)) failed.Add(L.T("ocr") + " " + settings.HkOcr.Display);
            miRegion.ShortcutKeyDisplayString = settings.HkRegion.Display.Replace(" ", "");
            miFull.ShortcutKeyDisplayString = settings.HkFull.Display.Replace(" ", "");
            miOcr.ShortcutKeyDisplayString = settings.HkOcr.Display.Replace(" ", "");
            return failed;
        }

        ContextMenuStrip BuildMenu()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            m.Renderer = new MenuRenderer();
            m.Font = Ui.F(13.5f, FontStyle.Regular);
            m.ImageScalingSize = new Size(Ui.Px(18), Ui.Px(18));
            m.BackColor = Ui.Surface;
            m.ShowImageMargin = true;

            header = new ToolStripLabel();
            header.Image = Ui.Logo(Ui.Px(18));
            header.Font = Ui.F(12.5f, FontStyle.Bold);
            header.Padding = new Padding(0, Ui.Px(4), 0, Ui.Px(4));
            m.Items.Add(header);
            m.Items.Add(new ToolStripSeparator());

            miRegion = Item(L.T("region"), "Region", delegate { StartCapture(CaptureMode.Region, true); });
            miRegion.Font = Ui.F(13.5f, FontStyle.Bold);
            miFull = Item(L.T("full"), "Full", delegate { StartCapture(CaptureMode.FullScreen, true); });
            m.Items.Add(miRegion);
            m.Items.Add(miFull);
            miOcr = Item(L.T("ocr"), "ScanText", delegate { StartCapture(CaptureMode.Ocr, true); });
            m.Items.Add(miOcr);
            m.Items.Add(Item(L.T("delay"), "Timer", delegate { StartDelayed(); }));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Item(L.T("open_folder"), "Folder", delegate { OpenFolder(); }));
            m.Items.Add(Item(L.T("settings_menu"), "Gear", delegate { OpenSettings(); }));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Item(L.T("exit"), "Exit", delegate { ExitThread(); }));
            return m;
        }

        static ToolStripMenuItem Item(string text, string icon, EventHandler onClick)
        {
            ToolStripMenuItem i = new ToolStripMenuItem(text);
            i.Image = Icons.Make(icon, Ui.Px(18), Ui.IconInk);
            i.Padding = new Padding(0, Ui.Px(3), 0, Ui.Px(3));
            i.Click += onClick;
            return i;
        }

        void OnHotkey(int id)
        {
            if (id == HK_REGION) StartCapture(CaptureMode.Region, false);
            else if (id == HK_FULL) StartCapture(CaptureMode.FullScreen, false);
            else if (id == HK_OCR) StartCapture(CaptureMode.Ocr, false);
        }

        // ---------------- settings ----------------

        void OpenSettings()
        {
            if (settingsForm != null)
            {
                settingsForm.Activate();
                return;
            }
            try
            {
                using (Icon big = Ui.AppIcon(32))
                using (SettingsForm f = new SettingsForm(settings, IsAutostart(), big, TestHotkeys, SuspendHotkeys))
                {
                    settingsForm = f;
                    if (f.ShowDialog() == DialogResult.OK)
                    {
                        settings = f.Result;
                        settings.Save();
                        L.Lang = settings.Lang;
                        ContextMenuStrip old = tray.ContextMenuStrip;
                        tray.ContextMenuStrip = BuildMenu();
                        old.Dispose();
                        SetAutostart(f.Autostart);
                        UpdateTrayText();
                    }
                }
            }
            finally
            {
                settingsForm = null;
                List<string> failed = ApplyHotkeys();
                if (failed.Count > 0)
                    tray.ShowBalloonTip(4000, L.T("hk_busy_title"), string.Join(L.T("list_sep"), failed.ToArray()), ToolTipIcon.Warning);
            }
        }

        // Hotkeys stay live while settings are open; they pause only while a hotkey box is
        // recording, so the box can receive the combination instead of triggering a capture.
        void SuspendHotkeys(bool suspend)
        {
            if (suspend) hotkeys.UnregisterAll();
            else ApplyHotkeys();
        }

        List<int> TestHotkeys(Hotkey[] list)
        {
            hotkeys.UnregisterAll();   // our own registrations must not count as conflicts
            List<int> bad = new List<int>();
            for (int i = 0; i < list.Length; i++)
                if (!hotkeys.CanRegister(list[i])) bad.Add(i);
            ApplyHotkeys();
            return bad;
        }

        // ---------------- capture pipeline ----------------

        void StartCapture(CaptureMode mode, bool fromMenu)
        {
            if (busy) return;
            busy = true;
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = fromMenu ? 250 : 20;           // let the tray menu fade out first
            t.Tick += delegate
            {
                t.Stop();
                t.Dispose();
                Deliver(mode);
            };
            t.Start();
        }

        // Region capture after a 3-second countdown, so a right-click menu can be opened first.
        void StartDelayed()
        {
            if (busy) return;
            busy = true;
            CountdownToast.Run(3, delegate
            {
                Deliver(CaptureMode.Region);
            });
        }

        // Grabs, then either saves an image or runs text recognition.
        void Deliver(CaptureMode mode)
        {
            Bitmap part = null;
            bool ocr = false;
            try { part = Grab(mode, out ocr); }
            catch (Exception ex) { ShowError(ex); }
            if (part == null) { busy = false; return; }
            if (ocr) RunOcr(part);
            else EncodeAsync(part);
        }

        void RunOcr(Bitmap part)
        {
            OcrForm form = OcrForm.Open(part, delegate { StartCapture(CaptureMode.Ocr, true); });
            SynchronizationContext ui = SynchronizationContext.Current;
            WaitCallback work = delegate
            {
                OcrOutput result = null;
                Exception err = null;
                try { result = WinOcr.Recognize(part); }
                catch (Exception ex) { err = ex; }
                finally { part.Dispose(); }
                SendOrPostCallback done = delegate
                {
                    try
                    {
                        if (err == null) form.ShowResult(result);
                        else
                        {
                            Crash.Log(err, "ocr");
                            bool missing = err is OcrUnavailableException;
                            form.ShowError(missing ? err.Message : L.T("ocr_unavailable") + "\r\n\r\n" + err.Message, missing);
                        }
                    }
                    finally { busy = false; }
                };
                if (ui != null) ui.Post(done, null); else done(null);
            };
            if (ui != null) ThreadPool.QueueUserWorkItem(work);
            else work(null);
        }

        Bitmap Grab(CaptureMode mode, out bool ocr)
        {
            ocr = false;
            Rectangle vs = SystemInformation.VirtualScreen;
            List<Rectangle> windows = mode == CaptureMode.FullScreen ? null : Native.GetVisibleWindowRects();

            using (Bitmap screen = new Bitmap(vs.Width, vs.Height, PixelFormat.Format24bppRgb))
            {
                using (Graphics g = Graphics.FromImage(screen))
                    g.CopyFromScreen(vs.Left, vs.Top, 0, 0, vs.Size, CopyPixelOperation.SourceCopy);

                Rectangle crop = new Rectangle(0, 0, vs.Width, vs.Height);
                List<Shape> shapes = null;
                if (mode != CaptureMode.FullScreen)
                {
                    using (SelectOverlay ov = new SelectOverlay(screen, vs, windows, mode == CaptureMode.Ocr, settings.AdjustSelection))
                    {
                        if (ov.ShowDialog() != DialogResult.OK) return null;
                        crop = ov.Result;
                        shapes = ov.Shapes;
                        ocr = ov.OcrRequested;
                    }
                }
                if (crop.Width < 2 || crop.Height < 2) return null;

                Bitmap part = screen.Clone(crop, PixelFormat.Format24bppRgb);
                // Text recognition reads the clean, uncompressed pixels without annotations.
                if (!ocr && shapes != null && shapes.Count > 0)
                {
                    using (Graphics g = Graphics.FromImage(part))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        g.TranslateTransform(-crop.X, -crop.Y);
                        foreach (Shape s in shapes) s.Draw(g, screen);
                    }
                }
                return part;
            }
        }

        void EncodeAsync(Bitmap part)
        {
            Settings snap = settings;
            SynchronizationContext ui = SynchronizationContext.Current;
            WaitCallback work = delegate
            {
                Shot shot = null;
                Exception err = null;
                try { shot = EncodeAndSave(part, snap); }
                catch (Exception ex) { err = ex; }
                finally { part.Dispose(); }
                SendOrPostCallback done = delegate
                {
                    try
                    {
                        if (err != null) ShowError(err);
                        else Deliver(shot, snap);
                    }
                    catch (Exception ex) { ShowError(ex); }
                    finally { busy = false; }
                };
                if (ui != null) ui.Post(done, null); else done(null);
            };
            if (ui != null) ThreadPool.QueueUserWorkItem(work);
            else work(null);
        }

        static Shot EncodeAndSave(Bitmap img, Settings s)
        {
            Shot shot = new Shot();
            shot.Original = img.Size;
            shot.Png = s.Png;
            if (s.Png)
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    img.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    shot.Data = ms.ToArray();
                }
                shot.Size = img.Size;
            }
            else shot.Data = JpegFitter.Fit(img, s.MaxKB * 1000, out shot.Quality, out shot.Size);

            Directory.CreateDirectory(s.Folder);
            string ext = s.Png ? ".png" : ".jpg";
            string name = "LiteCap_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string path = System.IO.Path.Combine(s.Folder, name + ext);
            int n = 2;
            while (File.Exists(path)) path = System.IO.Path.Combine(s.Folder, name + "_" + (n++) + ext);
            using (FileStream fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read | FileShare.Delete))
            {
                fs.Write(shot.Data, 0, shot.Data.Length);
                fs.Flush(true);
            }
            shot.Path = path;
            return shot;
        }

        void Deliver(Shot shot, Settings s)
        {
            // Always copy as a file so the pasted/uploaded item is exactly the saved jpg/png.
            DataObject data = new DataObject();
            data.SetData(DataFormats.FileDrop, new string[] { shot.Path });
            try { Clipboard.SetDataObject(data, true, 8, 100); }
            catch (ExternalException) { }

            if (!s.ShowPreview) return;
            using (MemoryStream ms = new MemoryStream(shot.Data))
            using (Image decoded = Image.FromStream(ms))
            {
                string title = L.F("toast_title", (shot.Data.Length / 1000.0).ToString("0.0"));
                string info = shot.Size.Width + "×" + shot.Size.Height
                    + (shot.Size != shot.Original ? L.F("toast_orig", shot.Original.Width, shot.Original.Height) : "")
                    + "  ·  " + (shot.Png ? "PNG" : L.F("toast_quality", shot.Quality));
                PreviewToast.Pop(decoded, title, info, shot.Path);
            }
        }

        void ShowError(Exception ex)
        {
            Crash.Log(ex, "capture");
            tray.ShowBalloonTip(4000, L.T("fail"), ex.Message, ToolTipIcon.Error);
        }

        // ---------------- misc ----------------

        void OpenFolder()
        {
            try
            {
                Directory.CreateDirectory(settings.Folder);
                Process.Start("explorer.exe", "\"" + settings.Folder + "\"");
            }
            catch (Exception ex) { ShowError(ex); }
        }

        static void MigrateLegacyAutostart()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null || k.GetValue("Shot100K") == null) return;
                    k.DeleteValue("Shot100K", false);
                    k.SetValue("LiteCap", "\"" + Application.ExecutablePath + "\"");
                }
            }
            catch { }
        }

        static bool IsAutostart()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                return k != null && k.GetValue("LiteCap") != null;
        }

        static void SetAutostart(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue("LiteCap", "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue("LiteCap", false);
                }
            }
            catch { }
        }

        protected override void ExitThreadCore()
        {
            hotkeys.Dispose();
            tray.Visible = false;
            tray.Dispose();
            base.ExitThreadCore();
        }
    }
}
