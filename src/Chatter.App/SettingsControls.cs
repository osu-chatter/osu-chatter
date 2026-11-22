using System;
using System.Drawing;
using System.Windows.Forms;

namespace Chatter.App {
    // A clipped content surface with an app-drawn scrollbar, rather than a native white gutter.
    sealed class SettingsScroller : Panel {
        public readonly FlowLayoutPanel Content = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = false, BackColor = Palette.Bg };
        int offset, grab;
        bool dragging, sizing;
        int Px(int v) => (int)Math.Round(v * DeviceDpi / 96.0);
        public SettingsScroller() {
            DoubleBuffered = true; BackColor = Palette.Bg; Controls.Add(Content);
            Content.SizeChanged += delegate { PlaceContent(); };
            Content.Layout += delegate { UpdateExtent(); };
        }
        protected override void OnLayout(LayoutEventArgs e) {
            base.OnLayout(e);
            if (Content == null) return;
            int width = Math.Max(1, ClientSize.Width - Px(18));
            Content.MinimumSize = new Size(width, 0); Content.MaximumSize = new Size(width, 0); Content.Width = width;
            Content.SuspendLayout();
            foreach (Control row in Content.Controls) { if (row is Label label && label.AutoSize) label.MaximumSize = new Size(Math.Max(1, width - row.Margin.Horizontal), 0); row.Width = Math.Max(1, width - row.Margin.Horizontal); }
            Content.ResumeLayout(true); UpdateExtent();
        }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible && Content != null) { Content.PerformLayout(); UpdateExtent(); } }
        void UpdateExtent() {
            if (sizing) return; sizing = true;
            try { int height = 0; foreach (Control row in Content.Controls) if (row.Visible) height = Math.Max(height, row.Bottom + row.Margin.Bottom); Content.Height = height; PlaceContent(); }
            finally { sizing = false; }
        }
        void PlaceContent() {
            offset = Math.Max(0, Math.Min(offset, Math.Max(0, Content.Height - Height)));
            Content.Location = new Point(0, -offset); Invalidate();
        }
        internal void ScrollBy(int delta) { offset -= delta; PlaceContent(); }
        internal void EnsureVisible(Control row) {
            if (row.Top < offset) offset = Math.Max(0, row.Top - Px(8));
            else if (row.Bottom > offset + Height) offset = row.Bottom - Height + Px(8);
            PlaceContent();
        }
        Rectangle Thumb() {
            if (Content.Height <= Height || Height <= 0) return Rectangle.Empty;
            int size = Math.Max(Px(36), Height * Height / Content.Height);
            return new Rectangle(Width - Px(7), (int)((long)offset * (Height - size) / Math.Max(1, Content.Height - Height)), Px(5), size);
        }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e); var thumb = Thumb();
            if (!thumb.IsEmpty) PaintKit.FillRound(e.Graphics, thumb, Px(3), dragging ? Palette.Muted : Color.FromArgb(62, 69, 83));
        }
        protected override void OnMouseWheel(MouseEventArgs e) { ScrollBy(e.Delta / 3); if (e is HandledMouseEventArgs h) h.Handled = true; }
        protected override void OnMouseDown(MouseEventArgs e) {
            if (e.Button != MouseButtons.Left || e.X < Width - Px(15) || Thumb().IsEmpty) return;
            var thumb = Thumb(); grab = thumb.Contains(e.Location) ? e.Y - thumb.Y : thumb.Height / 2; dragging = true; Capture = true; DragTo(e.Y);
        }
        void DragTo(int y) { offset = (int)((long)(y - grab) * Math.Max(0, Content.Height - Height) / Math.Max(1, Height - Thumb().Height)); PlaceContent(); }
        protected override void OnMouseMove(MouseEventArgs e) { if (dragging) DragTo(e.Y); }
        protected override void OnMouseUp(MouseEventArgs e) { dragging = false; Capture = false; Invalidate(); }
    }

    sealed class SettingsRow : Panel {
        readonly Label caption = new Label { ForeColor = Palette.Ink, Font = UiFont.BodyStrong, BackColor = Palette.Elevated };
        readonly Label detail = new Label { ForeColor = Palette.Muted, Font = UiFont.Small, BackColor = Palette.Elevated };
        public Control Editor { get; }
        public string Caption => caption.Text;
        readonly bool wide;
        readonly bool hasDescription;
        int measuredWidth = -1, measuredTitle, measuredDetail;
        int Px(int v) => (int)Math.Round(v * DeviceDpi / 96.0);
        public SettingsRow(string title, string description, Control editor, bool fullWidth = false) {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Palette.Elevated; Margin = new Padding(0, 0, 0, 8); wide = fullWidth;
            caption.Text = title; detail.Text = description; hasDescription = description.Length > 0; detail.Visible = hasDescription; Editor = editor;
            caption.FontChanged += delegate { measuredWidth = -1; }; detail.FontChanged += delegate { measuredWidth = -1; };
            editor.AccessibleName = title; editor.BackColor = Palette.Elevated;
            Controls.Add(caption); Controls.Add(detail); Controls.Add(editor);
            editor.SizeChanged += delegate { if (!layingOut) PerformLayout(); };
            editor.Enter += delegate { EnsureVisible(); };
            foreach (Control child in editor.Controls) child.Enter += delegate { EnsureVisible(); };
            Height = 78;
        }
        void EnsureVisible() { if (Parent?.Parent is SettingsScroller viewport) viewport.EnsureVisible(this); }
        bool layingOut;
        protected override void OnLayout(LayoutEventArgs e) {
            base.OnLayout(e);
            if (Editor == null || layingOut) return;
            layingOut = true;
            try {
                int pad = Px(18), gap = Px(20), available = Math.Max(1, Width - pad * 2);
                bool compact = Editor is SwitchBox || Editor is SettingsNumber;
                bool stacked = wide || (!compact && Width < Px(560));
                int editorW = compact ? (Editor is SwitchBox ? Px(44) : Px(126)) : stacked ? available : Math.Min(Px(290), available * 45 / 100);
                int textW = stacked ? available : Math.Max(1, available - editorW - gap);
                if (measuredWidth != textW) { measuredWidth = textW; measuredTitle = PaintKit.Measure(caption.Text, caption.Font, textW).Height; measuredDetail = hasDescription ? PaintKit.Measure(detail.Text, detail.Font, textW).Height : 0; }
                int titleH = measuredTitle;
                int descriptionH = measuredDetail;
                int textH = titleH + (descriptionH > 0 ? Px(5) + descriptionH : 0);
                int editorH = Editor is SettingsText text && text.Input.Multiline ? Px(84) : Editor is SwitchBox ? Px(24) : Px(36);
                int height = stacked ? pad * 2 + textH + Px(12) + editorH : Math.Max(Px(76), pad * 2 + Math.Max(textH, editorH));
                if (Height != height) Height = height;
                int y = stacked ? pad : Math.Max(pad, (Height - textH) / 2);
                caption.SetBounds(pad, y, textW, titleH + 1);
                detail.SetBounds(pad, y + titleH + Px(5), textW, descriptionH + 1);
                Editor.SetBounds(stacked ? pad : Width - pad - editorW, stacked ? pad + textH + Px(12) : (Height - editorH) / 2, editorW, editorH);
            } finally { layingOut = false; }
        }
        protected override void OnPaint(PaintEventArgs e) { e.Graphics.Clear(Palette.Bg); PaintKit.FillRound(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1), Px(10), Palette.Elevated); }
    }

    sealed class SettingsText : UserControl {
        public readonly TextBox Input = new TextBox { BorderStyle = BorderStyle.None, BackColor = Palette.Field, ForeColor = Palette.Ink, Font = UiFont.Body, MaxLength = 2048 };
        int Px(int v) => (int)Math.Round(v * DeviceDpi / 96.0);
        PillButton? browse;
        public SettingsText(string value) {
            DoubleBuffered = true; BackColor = Palette.Field; Input.Text = value ?? ""; Controls.Add(Input); Height = 36;
            Input.MultilineChanged += delegate { Parent?.PerformLayout(); PerformLayout(); };
            Input.GotFocus += delegate { Invalidate(); }; Input.LostFocus += delegate { Invalidate(); };
        }
        internal void AddBrowse(Action action) { browse = new PillButton { Caption = "Browse" }; browse.Click += delegate { action(); }; Controls.Add(browse); PerformLayout(); }
        protected override void OnLayout(LayoutEventArgs e) { base.OnLayout(e); if (Input != null) Input.SetBounds(Px(12), Input.Multiline ? Px(9) : Math.Max(0, (Height - Input.PreferredHeight) / 2), Math.Max(1, Width - Px(24) - (browse != null ? Px(82) : 0)), Input.Multiline ? Math.Max(1, Height - Px(18)) : Input.PreferredHeight); browse?.SetBounds(Width - Px(80), Px(3), Px(76), Math.Max(1, Height - Px(6))); }
        protected override void OnPaint(PaintEventArgs e) { PaintKit.ClearBehind(e.Graphics, this); var r = new Rectangle(0, 0, Width - 1, Height - 1); PaintKit.FillRound(e.Graphics, r, Px(7), Palette.Field); PaintKit.StrokeRound(e.Graphics, r, Px(7), Input.Focused ? Palette.Accent : Palette.Line); }
    }

    sealed class SettingsNumber : UserControl {
        readonly PillButton less = new PillButton { Caption = "−" }, more = new PillButton { Caption = "+" };
        public readonly TextBox Input = new TextBox { TextAlign = HorizontalAlignment.Center, BorderStyle = BorderStyle.None, BackColor = Palette.Elevated, ForeColor = Palette.Ink, Font = UiFont.BodyStrong, MaxLength = 6 };
        readonly int min, max;
        int value;
        public event Action? Changed;
        public int Value {
            get { if (int.TryParse(Input.Text, out int typed)) Value = typed; else Value = value; return value; }
            set { int previous = this.value; this.value = Math.Max(min, Math.Min(max, value)); Input.Text = this.value.ToString(); less.Enabled = this.value > min; more.Enabled = this.value < max; if (previous != this.value) Changed?.Invoke(); }
        }
        public SettingsNumber(int min, int max, int value) {
            this.min = min; this.max = max; Controls.AddRange(new Control[] { less, Input, more });
            less.AccessibleName = "Decrease"; more.AccessibleName = "Increase";
            less.Click += delegate { Value = Value - 1; }; more.Click += delegate { Value = Value + 1; }; Value = value;
            Input.TextChanged += delegate { Changed?.Invoke(); };
            Input.Leave += delegate { int committed = Value; };
            Input.KeyPress += (sender, e) => { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true; };
        }
        protected override void OnLayout(LayoutEventArgs e) { base.OnLayout(e); if (less == null) return; int size = Math.Min(Height, Width / 3); less.SetBounds(0, 0, size, Height); more.SetBounds(Width - size, 0, size, Height); Input.SetBounds(size, Math.Max(0, (Height - Input.PreferredHeight) / 2), Math.Max(1, Width - 2 * size), Input.PreferredHeight); }
    }

    sealed class SettingsNavigation : Control {
        public readonly string[] Sections = { "Translation", "In-game chat", "Overlay", "Application", "Models", "About" };
        public int Selected { get; private set; }
        public event Action<int>? Chosen;
        int Px(int v) => (int)Math.Round(v * DeviceDpi / 96.0);
        public SettingsNavigation() { SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true); BackColor = Palette.Bg; Cursor = Cursors.Hand; TabStop = true; AccessibleName = "Settings sections"; }
        public void SelectSection(int index) { Selected = Math.Max(0, Math.Min(index, Sections.Length - 1)); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.Clear(Palette.Bg);
            for (int i = 0; i < Sections.Length; i++) {
                var rect = new Rectangle(Px(8), i * Px(46), Width - Px(16), Px(40));
                if (Selected == i) { PaintKit.FillRound(e.Graphics, rect, Px(8), Palette.Elevated); using (var brush = new SolidBrush(Palette.Accent)) e.Graphics.FillRectangle(brush, rect.Left, rect.Top + Px(11), Px(3), Px(18)); }
                TextRenderer.DrawText(e.Graphics, Sections[i], Selected == i ? UiFont.BodyStrong : UiFont.Body, new Rectangle(rect.X + Px(14), rect.Y, rect.Width - Px(20), rect.Height), Selected == i ? Palette.Ink : Palette.Muted, PaintKit.Single);
            }
        }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left && e.Y / Px(46) < Sections.Length) { Focus(); Chosen?.Invoke(e.Y / Px(46)); } }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Up) { Chosen?.Invoke(Math.Max(0, Math.Min(Sections.Length - 1, Selected + (e.KeyCode == Keys.Down ? 1 : -1)))); e.Handled = true; } base.OnKeyDown(e); }
    }
}
