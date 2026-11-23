using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Chatter.App {
    // The native owner-drawn ComboBox repeatedly creates/recreates its HWND while
    // it is populated, parented and sized. Settings has many of them. Keep the
    // closed selector lightweight and create its native popup only on demand.
    sealed class SettingsPicker : Control {
        public List<string> Items { get; } = new List<string>();
        public event EventHandler? SelectedIndexChanged;
        internal Action<int>? ScrollRequested;
        ContextMenuStrip? menu;
        int selectedIndex = -1;
        public int SelectedIndex {
            get => selectedIndex;
            set {
                if (value < -1 || value >= Items.Count) throw new ArgumentOutOfRangeException(nameof(value));
                if (selectedIndex == value) return;
                selectedIndex = value; Text = value < 0 ? "" : Items[value];
                Invalidate(); SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public SettingsPicker() {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Size = new Size(180, 36); Font = UiFont.Body; BackColor = Palette.Field; ForeColor = Palette.Ink;
            TabStop = true; Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.ComboBox;
        }
        protected override void OnPaint(PaintEventArgs e) {
            PaintKit.ClearBehind(e.Graphics, this);
            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            PaintKit.FillRound(e.Graphics, bounds, 7, Palette.Field);
            PaintKit.StrokeRound(e.Graphics, bounds, 7, Focused ? Palette.Accent : Palette.Line);
            TextRenderer.DrawText(e.Graphics, selectedIndex < 0 ? "Choose…" : Items[selectedIndex], Font, new Rectangle(12, 0, Math.Max(0, Width - 38), Height), Enabled ? Palette.Ink : Palette.Faint, PaintKit.Single);
            using (var pen = new Pen(Palette.Muted, 1.5f)) {
                int x = Width - 20, y = Height / 2;
                e.Graphics.DrawLines(pen, new[] { new Point(x - 4, y - 2), new Point(x, y + 2), new Point(x + 4, y - 2) });
            }
        }
        protected override void OnClick(EventArgs e) { Focus(); OpenChoices(); base.OnClick(e); }
        internal void OpenChoices() {
            if (!Enabled || Items.Count == 0) return;
            if (menu?.Visible == true) { menu.Close(); return; }
            if (menu == null) menu = new ContextMenuStrip { BackColor = Palette.Field, ForeColor = Palette.Ink, Font = Font, ShowImageMargin = false, ShowCheckMargin = true, Renderer = new PickerRenderer() };
            if (menu.Items.Count != Items.Count) {
                while (menu.Items.Count > 0) menu.Items[0].Dispose();
                for (int i = 0; i < Items.Count; i++) {
                    int index = i;
                    var item = new ToolStripMenuItem();
                    item.Click += delegate { SelectedIndex = index; Focus(); };
                    menu.Items.Add(item);
                }
            }
            for (int i = 0; i < Items.Count; i++) {
                var item = (ToolStripMenuItem)menu.Items[i];
                item.Text = Items[i]; item.Checked = i == selectedIndex; item.AutoSize = false;
                item.Size = new Size(Math.Max(Width - 5, 120), Math.Max(Font.Height + 12, 28));
            }
            menu.MinimumSize = new Size(Width, 0);
            menu.Show(this, new Point(0, Height));
            if (selectedIndex >= 0) menu.Items[selectedIndex].Select();
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
            if (keyData == Keys.Space || keyData == Keys.Enter || keyData == Keys.F4 || keyData == (Keys.Alt | Keys.Down)) { OpenChoices(); return true; }
            if (keyData == Keys.Up || keyData == Keys.Down || keyData == Keys.Home || keyData == Keys.End) {
                if (Items.Count > 0) SelectedIndex = keyData == Keys.Home ? 0 : keyData == Keys.End ? Items.Count - 1 : Math.Max(0, Math.Min(Items.Count - 1, selectedIndex + (keyData == Keys.Down ? 1 : -1)));
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        protected override void OnKeyPress(KeyPressEventArgs e) {
            if (!char.IsControl(e.KeyChar)) for (int i = 1; i <= Items.Count; i++) {
                int index = (selectedIndex + i) % Items.Count;
                if (Items[index].StartsWith(e.KeyChar.ToString(), StringComparison.CurrentCultureIgnoreCase)) { SelectedIndex = index; e.Handled = true; break; }
            }
            base.OnKeyPress(e);
        }
        protected override void OnMouseWheel(MouseEventArgs e) { ScrollRequested?.Invoke(e.Delta); if (e is HandledMouseEventArgs handled) handled.Handled = true; }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnVisibleChanged(EventArgs e) { if (!Visible) menu?.Close(); base.OnVisibleChanged(e); }
        protected override void Dispose(bool disposing) { if (disposing) menu?.Dispose(); base.Dispose(disposing); }
        protected override AccessibleObject CreateAccessibilityInstance() => new PickerAccessibility(this);
        sealed class PickerAccessibility : ControlAccessibleObject {
            readonly SettingsPicker owner;
            public PickerAccessibility(SettingsPicker owner) : base(owner) { this.owner = owner; }
            public override string Value { get => owner.Text; set { int index = owner.Items.IndexOf(value); if (index >= 0) owner.SelectedIndex = index; } }
            public override string DefaultAction => "Open";
            public override AccessibleStates State => base.State | AccessibleStates.HasPopup | (owner.menu?.Visible == true ? AccessibleStates.Expanded : AccessibleStates.Collapsed);
            public override void DoDefaultAction() { owner.Focus(); owner.OpenChoices(); }
        }
        sealed class PickerRenderer : ToolStripProfessionalRenderer {
            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) {
                var bounds = e.ImageRectangle;
                using (var brush = new SolidBrush(e.Item.Selected ? Palette.Elevated : Palette.Field)) e.Graphics.FillRectangle(brush, bounds);
                using (var pen = new Pen(Palette.AccentHot, 2)) {
                    int x = bounds.Left + bounds.Width / 2, y = bounds.Top + bounds.Height / 2;
                    e.Graphics.DrawLines(pen, new[] { new Point(x - 4, y), new Point(x - 1, y + 3), new Point(x + 5, y - 4) });
                }
            }
            protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) {
                using (var brush = new SolidBrush(Palette.Field)) e.Graphics.FillRectangle(brush, e.AffectedBounds);
            }
            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) {
                using (var brush = new SolidBrush(e.Item.Selected ? Palette.Elevated : Palette.Field)) e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
            }
            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) {
                using (var pen = new Pen(Palette.Line)) e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            }
        }
    }

    sealed class BufferedFlowPanel : FlowLayoutPanel {
        public BufferedFlowPanel() { DoubleBuffered = true; ResizeRedraw = true; }
    }

    // A clipped content surface with an app-drawn scrollbar, rather than a native white gutter.
    sealed class SettingsScroller : Panel {
        public readonly FlowLayoutPanel Content = new BufferedFlowPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = false, BackColor = Palette.Bg };
        int offset, grab;
        bool dragging, sizing, layingOut;
        int layoutWidth = -1;
        int Px(int v) => (int)Math.Round(v * DeviceDpi / 96.0);
        public SettingsScroller() {
            DoubleBuffered = true; BackColor = Palette.Bg; Controls.Add(Content);
            Content.SizeChanged += delegate { PlaceContent(); };
            Content.Layout += delegate { UpdateExtent(); };
            Content.ControlAdded += (sender, e) => FitRow(e.Control, Math.Max(1, Content.Width));
        }
        protected override void OnLayout(LayoutEventArgs e) {
            base.OnLayout(e);
            if (Content == null || layingOut) return;
            int width = Math.Max(1, ClientSize.Width - Px(18));
            // Moving the content to scroll also raises Layout. Only reflow rows when
            // the viewport width changes, never on each scroll position update.
            if (layoutWidth == width) { PlaceContent(); return; }
            layoutWidth = width; layingOut = true;
            Content.SuspendLayout();
            try {
                Content.MinimumSize = new Size(width, 0); Content.MaximumSize = new Size(width, 0); Content.Width = width;
                foreach (Control row in Content.Controls) FitRow(row, width);
            } finally { Content.ResumeLayout(true); layingOut = false; }
            UpdateExtent();
        }
        static void FitRow(Control row, int width) {
            int available = Math.Max(1, width - row.Margin.Horizontal);
            if (row is Label label && label.AutoSize) label.MaximumSize = new Size(available, 0);
            row.Width = available;
        }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible && Content != null) { Content.PerformLayout(); UpdateExtent(); } }
        void UpdateExtent() {
            // Effective child visibility becomes false while a section is hidden.
            // Treating that as empty content erased its extent and reading position.
            if (sizing || !Visible) return; sizing = true;
            try { int height = 0; foreach (Control row in Content.Controls) if (row.Visible) height = Math.Max(height, row.Bottom + row.Margin.Bottom); Content.Height = height; PlaceContent(); }
            finally { sizing = false; }
        }
        bool PlaceContent() {
            offset = Math.Max(0, Math.Min(offset, Math.Max(0, Content.Height - Height)));
            var location = new Point(0, -offset);
            bool moved = Content.Location != location;
            if (moved) {
                Content.Location = location;
                // Moving a child HWND can copy pixels from its old clipped position.
                // Repaint the visible content and its children, not just the gutter.
                Content.Invalidate(new Rectangle(0, offset, Content.Width, Height), true);
                Invalidate();
            }
            Invalidate(new Rectangle(Math.Max(0, Width - Px(18)), 0, Px(18), Height));
            return moved;
        }
        internal void ScrollBy(int delta) { offset -= delta; if (PlaceContent()) PaintKit.Redraw(this); }
        internal void EnsureVisible(Control row) {
            if (row.Top < offset) offset = Math.Max(0, row.Top - Px(8));
            else if (row.Bottom > offset + Height) offset = row.Bottom - Height + Px(8);
            if (PlaceContent()) PaintKit.Redraw(this);
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
        void DragTo(int y) { offset = (int)((long)(y - grab) * Math.Max(0, Content.Height - Height) / Math.Max(1, Height - Thumb().Height)); if (PlaceContent()) PaintKit.Redraw(this); }
        protected override void OnMouseMove(MouseEventArgs e) { if (dragging) DragTo(e.Y); }
        protected override void OnMouseUp(MouseEventArgs e) { dragging = false; Capture = false; Invalidate(); }
        protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture) { dragging = false; Invalidate(); } base.OnMouseCaptureChanged(e); }
    }

    sealed class SettingsRow : Panel {
        readonly string caption, detail;
        Rectangle captionBounds, detailBounds;
        public Control Editor { get; }
        public string Caption => caption;
        readonly bool wide;
        readonly bool hasDescription;
        int measuredWidth = -1, measuredTitle, measuredDetail;
        int Px(int v) => (int)Math.Round(v * DeviceDpi / 96.0);
        public SettingsRow(string title, string description, Control editor, bool fullWidth = false, int initialWidth = 200) {
            SuspendLayout(); Width = initialWidth;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Palette.Elevated; Margin = new Padding(0, 0, 0, 8); wide = fullWidth;
            caption = title; detail = description; hasDescription = description.Length > 0; Editor = editor;
            AccessibleName = title; AccessibleDescription = description; AccessibleRole = AccessibleRole.Grouping;
            editor.AccessibleName = title; editor.AccessibleDescription = description; editor.BackColor = Palette.Elevated;
            // Paint the card and its labels together instead of creating two
            // extra native child windows per row that repaint independently.
            Controls.Add(editor);
            editor.SizeChanged += delegate { if (!layingOut) PerformLayout(); };
            editor.Enter += delegate { EnsureVisible(); };
            foreach (Control child in editor.Controls) child.Enter += delegate { EnsureVisible(); };
            Height = 78;
            ResumeLayout(true);
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
                if (measuredWidth != textW) { measuredWidth = textW; measuredTitle = PaintKit.Measure(caption, UiFont.BodyStrong, textW).Height; measuredDetail = hasDescription ? PaintKit.Measure(detail, UiFont.Small, textW).Height : 0; }
                int titleH = measuredTitle;
                int descriptionH = measuredDetail;
                int textH = titleH + (descriptionH > 0 ? Px(5) + descriptionH : 0);
                int editorH = Editor is SettingsText text && text.Input.Multiline ? Px(84) : Editor is SwitchBox ? Px(24) : Px(36);
                int height = stacked ? pad * 2 + textH + Px(12) + editorH : Math.Max(Px(76), pad * 2 + Math.Max(textH, editorH));
                if (Height != height) Height = height;
                int y = stacked ? pad : Math.Max(pad, (Height - textH) / 2);
                captionBounds = new Rectangle(pad, y, textW, titleH + 1);
                detailBounds = new Rectangle(pad, y + titleH + Px(5), textW, descriptionH + 1);
                Editor.SetBounds(stacked ? pad : Width - pad - editorW, stacked ? pad + textH + Px(12) : (Height - editorH) / 2, editorW, editorH);
            } finally { layingOut = false; }
        }
        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.Clear(Palette.Bg);
            PaintKit.FillRound(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1), Px(10), Palette.Elevated);
            TextRenderer.DrawText(e.Graphics, caption, UiFont.BodyStrong, captionBounds, Palette.Ink, PaintKit.Wrap);
            if (hasDescription) TextRenderer.DrawText(e.Graphics, detail, UiFont.Small, detailBounds, Palette.Muted, PaintKit.Wrap);
        }
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
