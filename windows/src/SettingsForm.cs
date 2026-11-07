// Light settings window: sidebar navigation + one compact page per section.
// The whole UI is rebuilt from a draft when the language changes, so edits survive the switch.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace LiteCap
{
    class SettingsForm : Form
    {
        // Unsaved values, kept across rebuilds.
        class Draft
        {
            public Hotkey Region, Full, Ocr;
            public string Kb, Folder, Lang;
            public bool Edit, Preview, Auto, Png;
            public int Page;
        }

        KeyBox kbRegion, kbFull, kbOcr;
        FieldBox fbKB, fbFolder;
        Toggle tgEdit, tgPreview, tgAuto;
        Label lblError;
        List<FlatButton> chips, langButtons, formatButtons;
        List<NavItem> nav;
        List<Panel> pages;
        bool built;

        readonly Draft draft = new Draft();
        readonly Func<Hotkey[], List<int>> tester;
        readonly Action<bool> suspendHotkeys;
        readonly Settings original;
        readonly Bitmap logo;
        readonly Font fName, fVer, fPageTitle, fPageSub, fBody, fSmall, fKey, fNav, fBtn;
        readonly int W, H, sideW, contentX, contentW, cardTop;

        public Settings Result;
        public bool Autostart;

        public SettingsForm(Settings s, bool autostart, Icon icon, Func<Hotkey[], List<int>> tester, Action<bool> suspendHotkeys)
        {
            this.tester = tester;
            this.suspendHotkeys = suspendHotkeys;
            this.original = s;

            Icon = icon;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Ui.Page;
            DoubleBuffered = true;
            TopMost = true;

            fName = Ui.F(17, FontStyle.Bold);
            fVer = Ui.F(12, FontStyle.Regular);
            fPageTitle = Ui.F(21, FontStyle.Bold);
            fPageSub = Ui.F(13, FontStyle.Regular);
            fBody = Ui.F(14, FontStyle.Regular);
            fSmall = Ui.F(12.5f, FontStyle.Regular);
            fKey = Ui.F(12.5f, FontStyle.Bold);
            fNav = Ui.F(14, FontStyle.Regular);
            fBtn = Ui.F(14, FontStyle.Bold);
            Font = fBody;
            try { logo = Ui.Logo(Ui.Px(40)); } catch { logo = null; }

            W = Ui.Px(780);
            H = Ui.Px(500);
            sideW = Ui.Px(208);
            contentX = sideW + Ui.Px(32);
            contentW = W - contentX - Ui.Px(32);
            cardTop = Ui.Px(100);
            ClientSize = new Size(W, H);

            draft.Region = s.HkRegion;
            draft.Full = s.HkFull;
            draft.Ocr = s.HkOcr;
            draft.Kb = s.MaxKB.ToString();
            draft.Png = s.Png;
            draft.Folder = s.Folder;
            draft.Lang = s.Lang;
            draft.Edit = s.AdjustSelection;
            draft.Preview = s.ShowPreview;
            draft.Auto = autostart;
            Build();
        }

        // ---------- build / rebuild ----------

        void CaptureDraft()
        {
            if (!built) return;
            draft.Region = kbRegion.Value;
            draft.Full = kbFull.Value;
            draft.Ocr = kbOcr.Value;
            draft.Kb = fbKB.Box.Text;
            draft.Folder = fbFolder.Box.Text;
            draft.Edit = tgEdit.Checked;
            draft.Preview = tgPreview.Checked;
            draft.Auto = tgAuto.Checked;
            for (int i = 0; i < pages.Count; i++) if (pages[i].Visible) draft.Page = i;
        }

        void Build()
        {
            SuspendLayout();
            List<Control> old = new List<Control>();
            foreach (Control c in Controls) old.Add(c);
            Controls.Clear();
            foreach (Control c in old) c.Dispose();

            Text = L.T("set_title");
            kbRegion = new KeyBox(); kbFull = new KeyBox(); kbOcr = new KeyBox();
            foreach (KeyBox kb in new KeyBox[] { kbRegion, kbFull, kbOcr })
            {
                kb.GotFocus += delegate { suspendHotkeys(true); };
                kb.LostFocus += delegate { suspendHotkeys(false); };
            }
            fbKB = new FieldBox(fBody);
            fbFolder = new FieldBox(fSmall);
            tgEdit = new Toggle(); tgPreview = new Toggle(); tgAuto = new Toggle();
            chips = new List<FlatButton>();
            langButtons = new List<FlatButton>();
            formatButtons = new List<FlatButton>();
            nav = new List<NavItem>();
            pages = new List<Panel>();

            // Sidebar
            string[] navText = { L.T("nav_hotkeys"), L.T("nav_output"), L.T("nav_general"), L.T("nav_about") };
            string[] navIcon = { "Keyboard", "Folder", "Sliders", "Info" };
            int ny = Ui.Px(96);
            for (int i = 0; i < navText.Length; i++)
            {
                NavItem n = new NavItem(navText[i], navIcon[i]);
                n.Font = fNav;
                n.BackColor = Ui.Sidebar;
                n.Bounds = new Rectangle(Ui.Px(14), ny, sideW - Ui.Px(28), Ui.Px(40));
                int idx = i;
                n.Click += delegate { ShowPage(idx); };
                Controls.Add(n);
                nav.Add(n);
                ny += Ui.Px(44);
            }

            pages.Add(BuildHotkeyPage());
            pages.Add(BuildOutputPage());
            pages.Add(BuildGeneralPage());
            pages.Add(BuildAboutPage());
            foreach (Panel p in pages) Controls.Add(p);

            // Footer
            int fy = H - Ui.Px(62);
            lblError = new Label();
            lblError.Font = fSmall;
            lblError.ForeColor = Ui.Danger;
            lblError.BackColor = Ui.Page;
            lblError.AutoSize = false;
            lblError.TextAlign = ContentAlignment.MiddleLeft;
            lblError.Bounds = new Rectangle(contentX, fy, contentW - Ui.Px(230), Ui.Px(40));
            Controls.Add(lblError);
            FlatButton save = new FlatButton(L.T("save"), true), cancel = new FlatButton(L.T("cancel"), false);
            foreach (FlatButton b in new FlatButton[] { save, cancel })
            {
                b.Font = fBtn;
                b.BackColor = Ui.Page;
                b.Size = new Size(Ui.Px(104), Ui.Px(38));
            }
            save.Location = new Point(contentX + contentW - save.Width, fy + Ui.Px(1));
            cancel.Location = new Point(save.Left - Ui.Px(10) - cancel.Width, save.Top);
            save.Click += delegate { TrySave(); };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(save);
            Controls.Add(cancel);

            built = true;
            ShowPage(draft.Page);
            ResumeLayout();
            ActiveControl = save;
            Invalidate();
        }

        Panel NewPage(string title, string sub)
        {
            Panel p = new Panel();
            p.BackColor = Ui.Page;
            p.Bounds = new Rectangle(contentX, 0, contentW, H - Ui.Px(70));
            Label t = MakeLabel(title, fPageTitle, Ui.Ink, Ui.Page);
            t.Location = new Point(-Ui.Px(2), Ui.Px(28));
            Label d = MakeLabel(sub, fPageSub, Ui.InkSoft, Ui.Page);
            d.Location = new Point(0, Ui.Px(64));
            p.Controls.Add(t);
            p.Controls.Add(d);
            return p;
        }

        Card NewCard(Panel page, int y, int h)
        {
            Card c = new Card();
            c.Bounds = new Rectangle(0, y, contentW, h);
            page.Controls.Add(c);
            return c;
        }

        Label MakeLabel(string text, Font f, Color c, Color back)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = f;
            l.ForeColor = c;
            l.BackColor = back;
            l.AutoSize = true;
            l.UseMnemonic = false;
            return l;
        }

        // Title (+ optional description) on the left of a card row, vertically centred.
        void RowText(Card card, string title, string desc, int y, int rowH, int x)
        {
            Label lt = MakeLabel(title, fBody, Ui.Ink, Ui.Surface);
            card.Controls.Add(lt);
            if (desc == null)
            {
                lt.Location = new Point(x, y + (rowH - lt.PreferredHeight) / 2);
                return;
            }
            Label ld = MakeLabel(desc, fSmall, Ui.InkSoft, Ui.Surface);
            card.Controls.Add(ld);
            int block = lt.PreferredHeight + Ui.Px(3) + ld.PreferredHeight;
            lt.Location = new Point(x, y + (rowH - block) / 2);
            ld.Location = new Point(x, lt.Bottom + Ui.Px(3));
        }

        FlatButton SmallButton(string text, int w)
        {
            FlatButton b = new FlatButton(text, false);
            b.Font = fSmall;
            b.BackColor = Ui.Surface;
            b.Size = new Size(w, Ui.Px(32));
            return b;
        }

        Panel BuildHotkeyPage()
        {
            Panel page = NewPage(L.T("nav_hotkeys"), L.T("hk_sub"));
            int rowH = Ui.Px(68), pad = Ui.Px(20);
            Card card = NewCard(page, cardTop, rowH * 3);
            string[] names = { L.T("region"), L.T("full"), L.T("ocr") };
            string[] descs = { L.T("region_desc"), L.T("full_desc"), L.T("ocr_desc") };
            string[] icons = { "Region", "Full", "ScanText" };
            KeyBox[] boxes = { kbRegion, kbFull, kbOcr };
            Hotkey[] values = { draft.Region, draft.Full, draft.Ocr };
            for (int i = 0; i < boxes.Length; i++)
            {
                int y = i * rowH;
                PictureBox pb = new PictureBox();
                int sz = Ui.Px(38);
                pb.Image = IconTile(icons[i], sz);
                pb.BackColor = Ui.Surface;
                pb.Bounds = new Rectangle(pad, y + (rowH - sz) / 2, sz, sz);
                card.Controls.Add(pb);
                RowText(card, names[i], descs[i], y, rowH, pad + sz + Ui.Px(14));
                boxes[i].Font = fKey;
                boxes[i].BackColor = Ui.Surface;
                boxes[i].Value = values[i];
                boxes[i].Bounds = new Rectangle(contentW - pad - Ui.Px(210), y + (rowH - Ui.Px(38)) / 2, Ui.Px(210), Ui.Px(38));
                card.Controls.Add(boxes[i]);
                if (i > 0) card.Dividers.Add(y);
            }

            int ny = cardTop + card.Height + Ui.Px(14);
            Label note = MakeLabel(L.T("hk_note"), fSmall, Ui.InkSoft, Ui.Page);
            note.Location = new Point(Ui.Px(2), ny + Ui.Px(6));
            page.Controls.Add(note);
            FlatButton reset = SmallButton(L.T("reset"), Ui.Px(92));
            reset.BackColor = Ui.Page;
            reset.Location = new Point(contentW - reset.Width, ny);
            reset.Click += delegate
            {
                kbRegion.Value = Settings.DefaultRegion;
                kbFull.Value = Settings.DefaultFull;
                kbOcr.Value = Settings.DefaultOcr;
            };
            page.Controls.Add(reset);
            return page;
        }

        Panel BuildOutputPage()
        {
            Panel page = NewPage(L.T("nav_output"), L.T("out_sub"));
            int rowH = Ui.Px(68), pad = Ui.Px(20);
            Card card = NewCard(page, cardTop, rowH * 2 + Ui.Px(118));

            // Format: segmented JPG / PNG.
            RowText(card, L.T("format"), L.T("format_desc"), 0, rowH, pad);
            int fx = contentW - pad;
            string[] fmts = { "PNG", "JPG" };
            foreach (string f in fmts)
            {
                FlatButton b = SmallButton(f, Ui.Px(70));
                fx -= b.Width;
                b.Location = new Point(fx, (rowH - b.Height) / 2);
                fx -= Ui.Px(6);
                bool png = f == "PNG";
                b.Selected = draft.Png == png;
                b.Click += delegate
                {
                    draft.Png = png;
                    foreach (FlatButton o in formatButtons) o.Selected = (o.Text == "PNG") == png;
                };
                card.Controls.Add(b);
                formatButtons.Add(b);
            }
            card.Dividers.Add(rowH);

            int ly = rowH;
            RowText(card, L.T("limit"), L.T("limit_desc"), ly, rowH, pad);
            Label unit = MakeLabel("KB", fBody, Ui.InkSoft, Ui.Surface);
            card.Controls.Add(unit);
            unit.Location = new Point(contentW - pad - unit.PreferredWidth, ly + (rowH - unit.PreferredHeight) / 2);
            fbKB.Bounds = new Rectangle(unit.Left - Ui.Px(8) - Ui.Px(76), ly + (rowH - Ui.Px(36)) / 2, Ui.Px(76), Ui.Px(36));
            fbKB.Box.Text = draft.Kb;
            fbKB.Box.TextAlign = HorizontalAlignment.Center;
            fbKB.Box.MaxLength = 5;
            fbKB.Box.KeyPress += delegate (object o, KeyPressEventArgs e) { if (!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar)) e.Handled = true; };
            fbKB.Box.TextChanged += delegate { SyncChips(); };
            card.Controls.Add(fbKB);
            int cx = fbKB.Left - Ui.Px(12);
            foreach (string v in new string[] { "200", "100", "50" })
            {
                FlatButton chip = SmallButton(v, Ui.Px(50));
                cx -= chip.Width;
                chip.Location = new Point(cx, ly + (rowH - chip.Height) / 2);
                cx -= Ui.Px(6);
                string val = v;
                chip.Click += delegate { fbKB.Box.Text = val; };
                card.Controls.Add(chip);
                chips.Add(chip);
            }
            SyncChips();
            card.Dividers.Add(rowH * 2);

            int y2 = rowH * 2;
            RowText(card, L.T("folder"), null, y2 + Ui.Px(6), Ui.Px(44), pad);
            FlatButton open = SmallButton(L.T("open"), Ui.Px(72)), change = SmallButton(L.T("change"), Ui.Px(84));
            open.Location = new Point(contentW - pad - open.Width, y2 + Ui.Px(6) + (Ui.Px(44) - open.Height) / 2);
            change.Location = new Point(open.Left - Ui.Px(8) - change.Width, open.Top);
            card.Controls.Add(open);
            card.Controls.Add(change);
            fbFolder.Bounds = new Rectangle(pad, y2 + Ui.Px(56), contentW - pad * 2, Ui.Px(38));
            fbFolder.Box.Text = draft.Folder;
            fbFolder.Box.ReadOnly = true;
            card.Controls.Add(fbFolder);
            open.Click += delegate
            {
                try
                {
                    Directory.CreateDirectory(fbFolder.Box.Text);
                    Process.Start("explorer.exe", "\"" + fbFolder.Box.Text + "\"");
                }
                catch { }
            };
            change.Click += delegate
            {
                using (FolderBrowserDialog d = new FolderBrowserDialog())
                {
                    d.SelectedPath = fbFolder.Box.Text;
                    d.Description = L.T("pick_folder");
                    if (d.ShowDialog(this) == DialogResult.OK) fbFolder.Box.Text = d.SelectedPath;
                }
            };

            Label note = MakeLabel(L.T("out_note"), fSmall, Ui.InkSoft, Ui.Page);
            note.Location = new Point(Ui.Px(2), cardTop + card.Height + Ui.Px(16));
            page.Controls.Add(note);
            return page;
        }

        Panel BuildGeneralPage()
        {
            Panel page = NewPage(L.T("nav_general"), L.T("gen_sub"));
            int rowH = Ui.Px(66), pad = Ui.Px(20);
            Card card = NewCard(page, cardTop, rowH * 4);

            // Language: segmented choice, applied live.
            RowText(card, L.T("lang"), L.T("lang_desc"), 0, rowH, pad);
            string[] codes = { "en", "zh" };
            string[] labels = { "English", "中文" };
            int lx = contentW - pad;
            for (int i = 0; i < 2; i++)
            {
                FlatButton b = SmallButton(labels[i], Ui.Px(84));
                lx -= b.Width;
                b.Location = new Point(lx, (rowH - b.Height) / 2);
                lx -= Ui.Px(6);
                b.Selected = draft.Lang == codes[i];
                string code = codes[i];
                b.Click += delegate { SwitchLanguage(code); };
                card.Controls.Add(b);
                langButtons.Add(b);
            }

            Toggle[] ts = { tgEdit, tgPreview, tgAuto };
            string[] titles = { L.T("edit_t"), L.T("prev_t"), L.T("auto_t") };
            string[] descs = { L.T("edit_d"), L.T("prev_d"), L.T("auto_d") };
            bool[] vals = { draft.Edit, draft.Preview, draft.Auto };
            for (int i = 0; i < 3; i++)
            {
                int y = (i + 1) * rowH;
                card.Dividers.Add(y);
                RowText(card, titles[i], descs[i], y, rowH, pad);
                ts[i].BackColor = Ui.Surface;
                ts[i].Size = new Size(Ui.Px(44), Ui.Px(24));
                ts[i].Location = new Point(contentW - pad - ts[i].Width, y + (rowH - ts[i].Height) / 2);
                ts[i].Checked = vals[i];
                card.Controls.Add(ts[i]);
            }
            return page;
        }

        Panel BuildAboutPage()
        {
            Panel page = NewPage(L.T("nav_about"), L.T("about_sub"));
            string[][] keys = {
                new string[] { "R", "k_rect" }, new string[] { "E", "k_ellipse" }, new string[] { "A", "k_arrow" },
                new string[] { "P", "k_pen" }, new string[] { "H", "k_marker" }, new string[] { "T", "k_text" },
                new string[] { "M", "k_mosaic" }, new string[] { "1 2 3", "k_size" }, new string[] { "Ctrl+Z", "k_undo" },
                new string[] { "Enter", "k_done" }, new string[] { L.T("key_right"), "k_right" }, new string[] { "Esc", "k_cancel" }, new string[] { "O", "k_ocr" } };
            int rowH = Ui.Px(36), pad = Ui.Px(20), cols = 2;
            int rows = (keys.Length + cols - 1) / cols;
            Card card = NewCard(page, cardTop, rows * rowH + Ui.Px(16));
            int colW = (contentW - pad * 2) / cols;
            for (int i = 0; i < keys.Length; i++)
            {
                int col = i / rows, row = i % rows;
                int x = pad + col * colW, y = Ui.Px(8) + row * rowH;
                Panel keyCap = KeyCap(keys[i][0]);
                keyCap.Location = new Point(x, y + (rowH - keyCap.Height) / 2);
                Label l = MakeLabel(L.T(keys[i][1]), fBody, Ui.Ink, Ui.Surface);
                l.Location = new Point(x + Ui.Px(88), y + (rowH - l.PreferredHeight) / 2);
                card.Controls.Add(keyCap);
                card.Controls.Add(l);
            }
            int ny = cardTop + card.Height + Ui.Px(14);
            Label ver = MakeLabel(L.F("about_ver", L.T("app"), Ui.Version), fSmall, Ui.InkSoft, Ui.Page);
            ver.Location = new Point(Ui.Px(2), ny + Ui.Px(6));
            page.Controls.Add(ver);
            FlatButton logs = SmallButton(L.T("open_logs"), TextRenderer.MeasureText(L.T("open_logs"), fSmall).Width + Ui.Px(28));
            logs.BackColor = Ui.Page;
            logs.Location = new Point(contentW - logs.Width, ny);
            logs.Click += delegate { Crash.OpenLogFolder(); };
            page.Controls.Add(logs);
            return page;
        }

        // Static keycap label for the cheat sheet.
        Panel KeyCap(string text)
        {
            Panel p = new Panel();
            int w = Math.Max(Ui.Px(30), TextRenderer.MeasureText(text, fKey).Width + Ui.Px(14));
            p.Size = new Size(Math.Min(w, Ui.Px(80)), Ui.Px(28));
            p.BackColor = Ui.Surface;
            p.Paint += delegate (object o, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle cap = new Rectangle(0, 0, p.Width, p.Height - Ui.Px(2));
                Rectangle under = cap;
                under.Offset(0, Ui.Px(2));
                Ui.FillRound(g, under, Ui.Px(5), Color.FromArgb(214, 219, 228));
                Ui.FillRound(g, cap, Ui.Px(5), Ui.FieldBg);
                Ui.StrokeRound(g, cap, Ui.Px(5), Color.FromArgb(214, 219, 228));
                TextRenderer.DrawText(g, text, fKey, cap, Ui.Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };
            return p;
        }

        // Soft blue rounded tile with an icon, used at the start of hotkey rows.
        static Bitmap IconTile(string id, int size)
        {
            Bitmap b = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Ui.Surface);
                Ui.FillRound(g, new Rectangle(0, 0, size, size), size / 4, Ui.BlueSoft);
                Icons.Draw(g, id, new Rectangle(0, 0, size, size), Ui.Blue);
            }
            return b;
        }

        void SyncChips()
        {
            foreach (FlatButton c in chips) c.Selected = c.Text == fbKB.Box.Text.Trim();
        }

        void ShowPage(int i)
        {
            for (int k = 0; k < pages.Count; k++)
            {
                pages[k].Visible = k == i;
                nav[k].Selected = k == i;
            }
            if (lblError != null) lblError.Text = "";
        }

        void SwitchLanguage(string code)
        {
            if (draft.Lang == code) return;
            CaptureDraft();
            draft.Lang = code;
            L.Lang = code;
            Build();
        }

        // ---------- chrome ----------

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.StyleCaption(Handle, false, Ui.Sidebar, Ui.Border);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // Never open taller or wider than the screen it lands on.
            Rectangle wa = Screen.FromControl(this).WorkingArea;
            int x = Math.Max(wa.Left, Math.Min(Left, wa.Right - Width));
            int y = Math.Max(wa.Top, Math.Min(Top, wa.Bottom - Height));
            Location = new Point(x, y);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // A cancelled dialog must not leave a previewed language behind.
            if (DialogResult != DialogResult.OK) L.Lang = original.Lang;
            base.OnFormClosed(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            using (SolidBrush sb = new SolidBrush(Ui.Sidebar)) g.FillRectangle(sb, 0, 0, sideW, Height);
            using (Pen p = new Pen(Ui.Border)) g.DrawLine(p, sideW, 0, sideW, Height);

            int ls = Ui.Px(40);
            if (logo != null) g.DrawImage(logo, Ui.Px(22), Ui.Px(28), ls, ls);
            int tx = Ui.Px(22) + ls + Ui.Px(12);
            TextRenderer.DrawText(g, L.T("app_short"), fName, new Point(tx, Ui.Px(26)), Ui.Ink);
            TextRenderer.DrawText(g, L.F("version", Ui.Version), fVer, new Point(tx, Ui.Px(51)), Ui.InkSoft);

        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!(ActiveControl is KeyBox))
            {
                if (keyData == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); return true; }
                if (keyData == Keys.Enter) { TrySave(); return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---------- save ----------

        void Fail(int page, string message)
        {
            ShowPage(page);
            lblError.Text = message;
        }

        void TrySave()
        {
            CaptureDraft();
            Hotkey[] hks = { draft.Region, draft.Full, draft.Ocr };
            string[] names = { L.T("region"), L.T("full"), L.T("ocr") };
            for (int i = 0; i < hks.Length; i++)
                for (int j = i + 1; j < hks.Length; j++)
                    if (!hks[i].IsEmpty && hks[i].SameAs(hks[j]))
                    {
                        Fail(0, L.F("err_dup", names[i], names[j]));
                        return;
                    }

            List<int> bad = tester(hks);
            if (bad.Count > 0)
            {
                List<string> msg = new List<string>();
                foreach (int i in bad) msg.Add(names[i] + " (" + hks[i].Display + ")");
                Fail(0, L.F("err_busy", string.Join(L.T("list_sep"), msg.ToArray())));
                return;
            }

            int kb;
            if (!int.TryParse(draft.Kb.Trim(), out kb) || kb < 10 || kb > 10000)
            {
                Fail(1, L.T("err_kb"));
                return;
            }

            string folder = draft.Folder.Trim();
            try { Directory.CreateDirectory(folder); }
            catch { Fail(1, L.T("err_folder")); return; }

            Result = original.Clone();
            Result.HkRegion = hks[0];
            Result.HkFull = hks[1];
            Result.HkOcr = hks[2];
            Result.MaxKB = kb;
            Result.Png = draft.Png;
            Result.AdjustSelection = draft.Edit;
            Result.ShowPreview = draft.Preview;
            Result.Folder = folder;
            Result.Lang = draft.Lang;
            Autostart = draft.Auto;
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (logo != null) logo.Dispose();
                fName.Dispose(); fVer.Dispose(); fPageTitle.Dispose(); fPageSub.Dispose();
                fSmall.Dispose(); fKey.Dispose(); fNav.Dispose(); fBtn.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
