// Text-recognition window: screenshot preview with line boxes on the left, editable text on the right.
// States: recognizing -> result (auto-copied) | no text | error (with a fix button when OCR is missing).
// The result can be translated offline (Chinese <-> English); the first use downloads the translation pack.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using ThreadPool = System.Threading.ThreadPool;
using System.Windows.Forms;

namespace LiteCap
{
    // Screenshot preview that outlines each recognized line; clicking a line raises LineClicked.
    class OcrPreview : Control
    {
        public Bitmap Image;
        public List<RectangleF> Boxes = new List<RectangleF>();
        public int Selected = -1;
        public event Action<int> LineClicked;
        int hover = -1;

        public OcrPreview()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Ui.Page;
        }

        RectangleF ImageRect()
        {
            if (Image == null) return RectangleF.Empty;
            float pad = Ui.Px(12);
            float aw = Width - pad * 2, ah = Height - pad * 2;
            float s = Math.Min(aw / Image.Width, ah / Image.Height);
            s = Math.Min(s, 2f);
            float w = Image.Width * s, h = Image.Height * s;
            return new RectangleF((Width - w) / 2, (Height - h) / 2, w, h);
        }

        RectangleF Map(RectangleF b, RectangleF ir)
        {
            float s = ir.Width / Image.Width;
            RectangleF r = new RectangleF(ir.X + b.X * s, ir.Y + b.Y * s, b.Width * s, b.Height * s);
            r.Inflate(Ui.Px(2), Ui.Px(2));
            return r;
        }

        int IndexAt(Point p)
        {
            if (Image == null) return -1;
            RectangleF ir = ImageRect();
            for (int i = 0; i < Boxes.Count; i++)
                if (!Boxes[i].IsEmpty && Map(Boxes[i], ir).Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = IndexAt(e.Location);
            Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
            if (i != hover) { hover = i; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = -1;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            int i = IndexAt(e.Location);
            if (i >= 0 && LineClicked != null) LineClicked(i);
            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle card = new Rectangle(0, 0, Width, Height);
            Ui.FillRound(g, card, Ui.Px(10), Ui.Surface);
            Ui.StrokeRound(g, card, Ui.Px(10), Ui.Border);
            if (Image == null) return;
            RectangleF ir = ImageRect();
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(Image, ir);
            using (Pen frame = new Pen(Ui.Border)) g.DrawRectangle(frame, ir.X, ir.Y, ir.Width, ir.Height);

            for (int i = 0; i < Boxes.Count; i++)
            {
                if (Boxes[i].IsEmpty) continue;
                RectangleF r = Map(Boxes[i], ir);
                bool strong = i == hover || i == Selected;
                using (SolidBrush b = new SolidBrush(Color.FromArgb(strong ? 70 : 28, Ui.Blue)))
                    g.FillRectangle(b, r);
                using (Pen p = new Pen(Color.FromArgb(strong ? 230 : 120, Ui.Blue), strong ? 1.6f : 1f))
                    g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
            }
        }
    }

    class OcrForm : Form
    {
        static OcrForm current;

        enum Phase { Working, Result, Empty, Error }

        readonly Bitmap image;
        readonly OcrPreview preview = new OcrPreview();
        readonly Panel editorFrame = new Panel();
        readonly TextBox box = new TextBox();
        readonly FlatButton btnCopy, btnClose, btnRecapture, btnFix, segKeep, segMerge, btnTranslate;
        readonly Font fTitle, fStats, fBody, fSmall, fBtn, fState;
        readonly Timer copiedTimer = new Timer();

        Phase phase = Phase.Working;
        string title, stats = "", stateMessage = "";
        List<string> lines = new List<string>();
        bool merge;

        // Translation state
        string direction = "zh-en", ocrStats = "", trStats = "", originalText, translatedText;
        bool showingTranslation, translating;
        volatile bool closing;

        public static OcrForm Open(Image source, Action recapture)
        {
            if (current != null && !current.IsDisposed) current.Close();
            current = new OcrForm(source, recapture);
            current.Show();
            current.Activate();
            return current;
        }

        OcrForm(Image source, Action recapture)
        {
            image = new Bitmap(source);

            Text = L.T("ocr_title");
            Icon = Ui.AppIcon(32);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Ui.Page;
            DoubleBuffered = true;
            TopMost = true;
            KeyPreview = true;

            fTitle = Ui.F(18, FontStyle.Bold);
            fStats = Ui.F(12.5f, FontStyle.Regular);
            fBody = Ui.F(14.5f, FontStyle.Regular);
            fSmall = Ui.F(12.5f, FontStyle.Regular);
            fBtn = Ui.F(13.5f, FontStyle.Bold);
            fState = Ui.F(14, FontStyle.Regular);
            title = L.T("ocr_working");

            ClientSize = new Size(Ui.Px(900), Ui.Px(540));
            MinimumSize = new Size(Ui.Px(680), Ui.Px(440));
            Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(wa.Left + Math.Max(0, (wa.Width - Width) / 2), wa.Top + Math.Max(0, (wa.Height - Height) / 2));

            preview.Image = image;
            preview.LineClicked += SelectLine;
            Controls.Add(preview);

            editorFrame.BackColor = Ui.Page;
            editorFrame.Paint += PaintEditorFrame;
            box.Multiline = true;
            box.ScrollBars = ScrollBars.Vertical;
            box.BorderStyle = BorderStyle.None;
            box.BackColor = Ui.Surface;
            box.ForeColor = Ui.Ink;
            box.Font = fBody;
            box.WordWrap = true;
            box.Visible = false;
            box.GotFocus += delegate { editorFrame.Invalidate(); };
            box.LostFocus += delegate { editorFrame.Invalidate(); };
            editorFrame.Controls.Add(box);
            Controls.Add(editorFrame);

            segKeep = Seg(L.T("ocr_keep_lines"));
            segMerge = Seg(L.T("ocr_merge_lines"));
            segKeep.Selected = true;
            segKeep.Click += delegate { SetMerge(false); };
            segMerge.Click += delegate { SetMerge(true); };


            btnRecapture = MakeButton(L.T("ocr_again"), false);
            btnRecapture.Click += delegate { Close(); if (recapture != null) recapture(); };
            btnFix = MakeButton(L.T("ocr_fix"), false);
            btnFix.Visible = false;
            btnFix.Click += delegate { try { Process.Start("ms-settings:regionlanguage"); } catch { } };
            btnTranslate = MakeButton(L.T("tr_to_en"), false);
            btnTranslate.Visible = false;
            btnTranslate.Click += delegate { TranslateClicked(); };
            btnClose = MakeButton(L.T("ocr_close"), false);
            btnClose.Click += delegate { Close(); };
            btnCopy = MakeButton(L.T("ocr_copy"), true);
            btnCopy.Enabled = false;
            btnCopy.Click += delegate { CopyText(); };

            copiedTimer.Interval = 1600;
            copiedTimer.Tick += delegate { copiedTimer.Stop(); btnCopy.Text = L.T("ocr_copy"); };

            DoLayout();
        }

        FlatButton Seg(string text)
        {
            FlatButton b = new FlatButton(text, false);
            b.Font = fSmall;
            b.BackColor = Ui.Page;
            b.Visible = false;
            Controls.Add(b);
            return b;
        }

        FlatButton MakeButton(string text, bool primary)
        {
            FlatButton b = new FlatButton(text, primary);
            b.Font = fBtn;
            b.BackColor = Ui.Page;
            Controls.Add(b);
            return b;
        }

        // ---------- layout ----------

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (btnCopy != null) DoLayout();   // also fires while the constructor sets the size
        }

        void DoLayout()
        {
            int W = ClientSize.Width, H = ClientSize.Height, pad = Ui.Px(20);
            int top = Ui.Px(78), bottom = H - Ui.Px(70);
            int leftW = (int)((W - pad * 3) * 0.44);
            preview.Bounds = new Rectangle(pad, top, leftW, bottom - top);

            int rx = pad * 2 + leftW, rw = W - rx - pad;
            int segH = Ui.Px(32);
            int sw1 = TextRenderer.MeasureText(segKeep.Text, fSmall).Width + Ui.Px(26);
            int sw2 = TextRenderer.MeasureText(segMerge.Text, fSmall).Width + Ui.Px(26);
            segKeep.Bounds = new Rectangle(rx, top, sw1, segH);
            segMerge.Bounds = new Rectangle(rx + sw1 + Ui.Px(6), top, sw2, segH);

            int edTop = top + segH + Ui.Px(10);
            editorFrame.Bounds = new Rectangle(rx, edTop, rw, bottom - edTop);
            box.Bounds = new Rectangle(Ui.Px(14), Ui.Px(12), editorFrame.Width - Ui.Px(22), editorFrame.Height - Ui.Px(24));

            int by = H - Ui.Px(54), bh = Ui.Px(38);
            btnRecapture.Bounds = new Rectangle(pad, by, TextRenderer.MeasureText(btnRecapture.Text, fBtn).Width + Ui.Px(36), bh);
            btnFix.Bounds = new Rectangle(btnRecapture.Right + Ui.Px(10), by, TextRenderer.MeasureText(btnFix.Text, fBtn).Width + Ui.Px(36), bh);
            // Translate shares the Fix slot (Fix only shows on errors, Translate only with a result).
            int tw = 0;
            foreach (string k in new string[] { "tr_to_en", "tr_to_zh", "tr_show_orig", "tr_show_trans", "tr_working", "tr_installing" })
                tw = Math.Max(tw, TextRenderer.MeasureText(L.T(k), fBtn).Width);
            tw = Math.Max(tw, TextRenderer.MeasureText(L.F("tr_downloading", 100), fBtn).Width);
            tw = Math.Max(tw, TextRenderer.MeasureText(L.F("tr_progress", 88, 88), fBtn).Width);
            btnTranslate.Bounds = new Rectangle(btnRecapture.Right + Ui.Px(10), by, tw + Ui.Px(36), bh);
            int cw = Math.Max(Ui.Px(120), TextRenderer.MeasureText(L.T("ocr_copied_btn"), fBtn).Width + Ui.Px(36));
            btnCopy.Bounds = new Rectangle(W - pad - cw, by, cw, bh);
            int clw = Math.Max(Ui.Px(96), TextRenderer.MeasureText(btnClose.Text, fBtn).Width + Ui.Px(36));
            btnClose.Bounds = new Rectangle(btnCopy.Left - Ui.Px(10) - clw, by, clw, bh);
            Invalidate();
            editorFrame.Invalidate();
        }

        // ---------- states ----------

        public void ShowResult(OcrOutput o)
        {
            if (IsDisposed) return;
            if (o.Lines == 0)
            {
                phase = Phase.Empty;
                title = L.T("ocr_empty");
                stateMessage = L.T("ocr_empty_hint");
                Refresh();
                return;
            }
            phase = Phase.Result;
            lines = o.LineList.Count > 0 ? o.LineList : new List<string>(o.Text.Split(new string[] { "\r\n" }, StringSplitOptions.None));
            preview.Boxes = o.Boxes;
            preview.Invalidate();
            title = L.T("ocr_done");
            int chars = 0;
            foreach (string l in lines) chars += l.Length;
            stats = L.F("ocr_stats", lines.Count, chars, string.IsNullOrEmpty(o.Language) ? "—" : o.Language);
            ocrStats = stats;
            segKeep.Visible = segMerge.Visible = true;
            direction = OfflineTranslator.DetectDirection(string.Join("\n", lines.ToArray()));
            btnTranslate.Text = L.T(direction == "zh-en" ? "tr_to_en" : "tr_to_zh");
            btnTranslate.Visible = true;
            box.Visible = true;
            ApplyText();
            btnCopy.Enabled = true;
            CopyText();
            box.Focus();
            box.Select(0, 0);
            Refresh();
        }

        public void ShowError(string message, bool languageMissing)
        {
            if (IsDisposed) return;
            phase = Phase.Error;
            title = L.T("ocr_failed");
            stateMessage = message;
            btnFix.Visible = languageMissing;
            btnCopy.Enabled = false;
            Refresh();
        }

        void SetMerge(bool on)
        {
            merge = on;
            segKeep.Selected = !on;
            segMerge.Selected = on;
            ApplyText();
            preview.Selected = -1;
            preview.Invalidate();
        }

        void ApplyText()
        {
            box.Text = merge ? WinOcr.MergeLines(lines) : string.Join("\r\n", lines.ToArray());
        }

        void SelectLine(int i)
        {
            if (phase != Phase.Result || i < 0 || i >= lines.Count) return;
            if (showingTranslation) ShowTranslation(false);
            preview.Selected = i;
            preview.Invalidate();
            int start;
            if (!merge)
            {
                start = 0;
                for (int k = 0; k < i; k++) start += lines[k].Length + 2;
            }
            else start = box.Text.IndexOf(lines[i], StringComparison.Ordinal);
            if (start < 0) return;
            box.Focus();
            box.Select(start, lines[i].Length);
            box.ScrollToCaret();
        }

        void CopyText()
        {
            string t = box.SelectionLength > 0 ? box.SelectedText : box.Text;
            if (t.Length == 0) return;
            try { Clipboard.SetText(t); } catch { return; }
            btnCopy.Text = L.T("ocr_copied_btn");
            copiedTimer.Stop();
            copiedTimer.Start();
        }

        // ---------- translation ----------

        void TranslateClicked()
        {
            if (translating || phase != Phase.Result) return;
            if (translatedText != null) { ShowTranslation(!showingTranslation); return; }
            if (OfflineTranslator.Installed) { StartTranslate(); return; }
            DialogResult ask = MessageBox.Show(this, L.F("tr_dl_ask", OfflineTranslator.PackMB), L.T("tr_dl_title"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (ask == DialogResult.Yes) StartDownload();
        }

        void SetBusy(bool on, string label)
        {
            translating = on;
            btnTranslate.Enabled = !on;
            btnTranslate.Text = label;
        }

        string IdleLabel()
        {
            if (translatedText != null) return L.T(showingTranslation ? "tr_show_orig" : "tr_show_trans");
            return L.T(direction == "zh-en" ? "tr_to_en" : "tr_to_zh");
        }

        // Runs work on a pool thread and posts the outcome back to this window (if it is still open).
        void Background(Action work, Action done, Action<Exception> failed)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                Exception err = null;
                try { work(); }
                catch (Exception ex) { err = ex; }
                if (closing || IsDisposed) return;
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (IsDisposed) return;
                        if (err == null) done();
                        else if (!(err is OperationCanceledException)) failed(err);
                        else SetBusy(false, IdleLabel());
                    });
                }
                catch (InvalidOperationException) { }
            });
        }

        void Post(Action a)
        {
            if (closing || IsDisposed) return;
            try { BeginInvoke((MethodInvoker)delegate { if (!IsDisposed) a(); }); } catch (InvalidOperationException) { }
        }

        void StartDownload()
        {
            SetBusy(true, L.F("tr_downloading", 0));
            int lastPct = -1;
            Background(
                delegate
                {
                    OfflineTranslator.Download(delegate (long got, long total)
                    {
                        int pct = total > 0 ? (int)(got * 100 / total) : 0;
                        if (pct == lastPct) return;
                        lastPct = pct;
                        Post(delegate { btnTranslate.Text = pct >= 100 ? L.T("tr_installing") : L.F("tr_downloading", pct); });
                    }, delegate { return closing; });
                },
                StartTranslate,
                delegate (Exception ex)
                {
                    Crash.Log(ex, "translate-download");
                    SetBusy(false, IdleLabel());
                    if (MessageBox.Show(this, L.F("tr_dl_failed", ex.Message), L.T("tr_dl_title"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                    try { Process.Start(OfflineTranslator.PackPage); } catch { }
                    InstallFromFile();
                });
        }

        void InstallFromFile()
        {
            string file;
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = L.T("tr_pick_zip");
                d.Filter = "LiteCap-Translate (*.zip)|*.zip";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                file = d.FileName;
            }
            SetBusy(true, L.T("tr_installing"));
            Background(
                delegate { OfflineTranslator.Install(file, false); },
                StartTranslate,
                delegate (Exception ex)
                {
                    Crash.Log(ex, "translate-install");
                    SetBusy(false, IdleLabel());
                    MessageBox.Show(this, L.F("tr_failed", ex.Message), L.T("tr_dl_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                });
        }

        void StartTranslate()
        {
            // Wrapped lines are joined first: the model translates sentences, not fragments.
            originalText = box.Text;
            List<string> current = new List<string>(box.Text.Replace("\r\n", "\n").Split('\n'));
            string source = merge ? box.Text : WinOcr.MergeLines(current);
            direction = OfflineTranslator.DetectDirection(source);
            string dir = direction;
            string result = null;
            Stopwatch sw = Stopwatch.StartNew();
            SetBusy(true, L.T("tr_working"));
            Background(
                delegate
                {
                    result = OfflineTranslator.Translate(source, dir, delegate { return closing; }, delegate (int done, int total)
                    {
                        if (total > 1) Post(delegate { btnTranslate.Text = L.F("tr_progress", Math.Min(done + 1, total), total); });
                    });
                },
                delegate
                {
                    translatedText = result;
                    bool toEn = dir == "zh-en";
                    trStats = L.F("tr_stats", L.T(toEn ? "tr_zh" : "tr_en"), L.T(toEn ? "tr_en" : "tr_zh"), (sw.ElapsedMilliseconds / 1000.0).ToString("0.0"));
                    translating = false;
                    btnTranslate.Enabled = true;
                    ShowTranslation(true);
                    CopyText();
                },
                delegate (Exception ex)
                {
                    Crash.Log(ex, "translate");
                    SetBusy(false, IdleLabel());
                    MessageBox.Show(this, L.F("tr_failed", ex.Message), L.T("ocr_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                });
        }

        void ShowTranslation(bool on)
        {
            if (on != showingTranslation)
            {
                // Keep any edits the user made in either view.
                if (showingTranslation) translatedText = box.Text; else originalText = box.Text;
                showingTranslation = on;
                box.Text = on ? translatedText : originalText;
            }
            segKeep.Visible = segMerge.Visible = !on;
            stats = on ? trStats : ocrStats;
            preview.Selected = -1;
            preview.Invalidate();
            btnTranslate.Text = IdleLabel();
            Invalidate();
        }

        // ---------- painting ----------

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.StyleCaption(Handle, false, Ui.Page, Ui.Border);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int pad = Ui.Px(20);
            Rectangle tile = new Rectangle(pad, Ui.Px(18), Ui.Px(40), Ui.Px(40));
            Color tint = phase == Phase.Error ? Color.FromArgb(254, 226, 226) : phase == Phase.Empty ? Ui.Divider : Ui.BlueSoft;
            Color ink = phase == Phase.Error ? Ui.Danger : phase == Phase.Empty ? Ui.InkSoft : Ui.Blue;
            Ui.FillRound(g, tile, Ui.Px(10), tint);
            Icons.Draw(g, phase == Phase.Result ? "Done" : phase == Phase.Error ? "Info" : "ScanText", tile, ink);
            int tx = tile.Right + Ui.Px(14);
            TextRenderer.DrawText(g, title, fTitle, new Point(tx, Ui.Px(16)), Ui.Ink);
            if (phase == Phase.Result)
                TextRenderer.DrawText(g, stats, fStats, new Point(tx, Ui.Px(44)), Ui.InkSoft);
        }

        void PaintEditorFrame(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, editorFrame.Width, editorFrame.Height);
            Ui.FillRound(g, r, Ui.Px(10), Ui.Surface);
            Ui.StrokeRound(g, r, Ui.Px(10), box.Focused ? Ui.Blue : Ui.Border);
            if (phase == Phase.Result) return;

            string msg = phase == Phase.Working ? L.T("ocr_working") : stateMessage;
            Color c = phase == Phase.Error ? Ui.Ink : Ui.InkSoft;
            Rectangle inner = new Rectangle(Ui.Px(28), Ui.Px(20), r.Width - Ui.Px(56), r.Height - Ui.Px(40));
            TextRenderer.DrawText(g, msg, fState, inner, c,
                TextFormatFlags.WordBreak | TextFormatFlags.VerticalCenter |
                (phase == Phase.Error ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter));
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            if (keyData == (Keys.Control | Keys.Enter)) { CopyText(); return true; }
            if (keyData == (Keys.Control | Keys.C) && box.SelectionLength == 0) { CopyText(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                closing = true;
                ThreadPool.QueueUserWorkItem(delegate { try { OfflineTranslator.Unload(); } catch { } });
                copiedTimer.Dispose();
                image.Dispose();
                fTitle.Dispose(); fStats.Dispose(); fBody.Dispose(); fSmall.Dispose(); fBtn.Dispose(); fState.Dispose();
                if (current == this) current = null;
            }
            base.Dispose(disposing);
        }
    }
}
