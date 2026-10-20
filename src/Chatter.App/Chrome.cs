using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Chatter;

namespace Chatter.App {
    static class Palette {
        public static readonly Color Bg = Color.FromArgb(12, 13, 16);
        public static readonly Color Sidebar = Color.FromArgb(18, 20, 26);
        public static readonly Color Elevated = Color.FromArgb(24, 27, 34);
        public static readonly Color Field = Color.FromArgb(30, 34, 43);
        public static readonly Color Line = Color.FromArgb(44, 49, 60);
        public static readonly Color Ink = Color.FromArgb(243, 244, 246);
        public static readonly Color Muted = Color.FromArgb(154, 163, 178);
        public static readonly Color Faint = Color.FromArgb(107, 114, 128);
        public static readonly Color Accent = Color.FromArgb(225, 29, 72);
        public static readonly Color AccentHot = Color.FromArgb(244, 63, 94);
        public static readonly Color BubbleIn = Color.FromArgb(28, 32, 40);
        public static readonly Color BubbleOut = Color.FromArgb(74, 28, 42);
        public static readonly Color Live = Color.FromArgb(52, 211, 153);
        public static readonly Color Banner = Color.FromArgb(42, 24, 30);
        public static readonly Color Info = Color.FromArgb(24, 32, 42);
    }

    static class UiFont {
        public static readonly Font Body = Face(9.5f, false);
        public static readonly Font BodyStrong = Face(9.5f, true);
        public static readonly Font Title = Face(16f, true);
        public static readonly Font Message = Face(10.5f, false);
        public static readonly Font Name = Face(9f, true);
        public static readonly Font Small = Face(8.25f, false);
        public static readonly Font Section = Face(8f, true);

        static Font Face(float size, bool strong) {
            if (strong) {
                try { return new Font("Segoe UI Semibold", size, FontStyle.Regular, GraphicsUnit.Point); }
                catch { return new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Point); }
            }
            return new Font("Segoe UI", size, FontStyle.Regular, GraphicsUnit.Point);
        }
    }

    static class PaintKit {
        public const TextFormatFlags Wrap = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl;
        public const TextFormatFlags Single = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;

        public static GraphicsPath Round(Rectangle bounds, int radius) {
            var path = new GraphicsPath();
            int limit = Math.Min(bounds.Width, bounds.Height) / 2;
            int r = Math.Max(0, Math.Min(radius, limit));
            int d = r * 2;
            if (d <= 0) {
                path.AddRectangle(bounds);
                return path;
            }
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        // A rounded fill leaves the control's square corners. Those pixels keep BackColor unless cleared to the parent.
        public static void ClearBehind(Graphics graphics, Control control) {
            graphics.Clear(control.Parent == null ? control.BackColor : control.Parent.BackColor);
        }

        public static void FillRound(Graphics graphics, Rectangle bounds, int radius, Color color) {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Round(bounds, radius))
            using (var brush = new SolidBrush(color))
                graphics.FillPath(brush, path);
        }

        public static void StrokeRound(Graphics graphics, Rectangle bounds, int radius, Color color) {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Round(bounds, radius))
            using (var pen = new Pen(color))
                graphics.DrawPath(pen, path);
        }

        public static TextFormatFlags Direction(string text) {
            return ChatRules.RightToLeftText(text) ? Wrap | TextFormatFlags.RightToLeft | TextFormatFlags.Right : Wrap;
        }

        public static Size Measure(string text, Font font, int width) {
            return Measure(text, font, width, Wrap);
        }

        public static Size Measure(string text, Font font, int width, TextFormatFlags flags) {
            if (string.IsNullOrEmpty(text)) return new Size(0, 0);
            return TextRenderer.MeasureText(text, font, new Size(Math.Max(1, width), int.MaxValue), flags);
        }
    }

    sealed class PillButton : Control {
        bool hover;
        string caption = "";
        public bool Filled { get; set; }

        public string Caption {
            get { return caption; }
            set { caption = value ?? ""; Invalidate(); }
        }

        public PillButton() {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Size = new Size(88, 34);
            Font = UiFont.BodyStrong;
            BackColor = Palette.Bg;
        }

        protected override void OnPaint(PaintEventArgs e) {
            PaintKit.ClearBehind(e.Graphics, this);
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill = !Enabled ? Palette.Field : Filled ? (hover ? Palette.AccentHot : Palette.Accent) : (hover ? Color.FromArgb(36, 40, 50) : Palette.Field);
            Color text = !Enabled ? Palette.Faint : Filled ? Color.White : Palette.Ink;
            PaintKit.FillRound(e.Graphics, rect, Height / 2, fill);
            if (!Filled) PaintKit.StrokeRound(e.Graphics, rect, Height / 2, Palette.Line);
            TextRenderer.DrawText(e.Graphics, caption, Font, rect, text, PaintKit.Single | TextFormatFlags.HorizontalCenter);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    }

    sealed class SwitchBox : Control {
        bool on;
        bool hover;
        public event EventHandler Toggled;

        public bool On {
            get { return on; }
            set { if (on == value) return; on = value; Invalidate(); }
        }

        public SwitchBox() {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Size = new Size(40, 22);
            BackColor = Palette.Bg;
        }

        protected override void OnPaint(PaintEventArgs e) {
            PaintKit.ClearBehind(e.Graphics, this);
            var rect = new Rectangle(0, 2, 38, 18);
            PaintKit.FillRound(e.Graphics, rect, 9, on ? Palette.Accent : (hover ? Color.FromArgb(55, 60, 72) : Palette.Field));
            int knob = on ? 20 : 2;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(Color.White))
                e.Graphics.FillEllipse(brush, knob, 4, 14, 14);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e) { on = !on; Invalidate(); if (Toggled != null) Toggled(this, EventArgs.Empty); base.OnClick(e); }
    }

    sealed class LineField : Panel {
        public readonly TextBox Input = new TextBox();

        public LineField() {
            Height = 40;
            BackColor = Palette.Field;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Input.BorderStyle = BorderStyle.None;
            Input.BackColor = Palette.Field;
            Input.ForeColor = Palette.Ink;
            Input.Font = UiFont.Message;
            Controls.Add(Input);
        }

        public void Cue(string text) {
            if (!Input.IsHandleCreated) return;
            SendMessage(Input.Handle, 0x1501, (IntPtr)1, text ?? "");
        }

        protected override void OnHandleCreated(EventArgs e) {
            base.OnHandleCreated(e);
            Place();
        }

        protected override void OnResize(EventArgs e) {
            base.OnResize(e);
            Place();
        }

        void Place() {
            int y = Math.Max(8, (Height - Input.PreferredHeight) / 2);
            Input.SetBounds(14, y, Math.Max(20, Width - 28), Input.PreferredHeight);
        }

        protected override void OnPaint(PaintEventArgs e) {
            PaintKit.ClearBehind(e.Graphics, this);
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            PaintKit.FillRound(e.Graphics, rect, 10, Palette.Field);
            PaintKit.StrokeRound(e.Graphics, rect, 10, Palette.Line);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, string text);
    }

    sealed class ConversationHeader : Control {
        public string TitleText = "chatter";
        public string Subtitle = "";
        public string Pill = "";
        public Color PillColor = Palette.Faint;

        public ConversationHeader() {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 72;
            BackColor = Palette.Bg;
        }

        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics;
            g.Clear(Palette.Bg);
            TextRenderer.DrawText(g, TitleText, UiFont.Title, new Rectangle(24, 14, Width - 140, 28), Palette.Ink, PaintKit.Single);
            if (!string.IsNullOrEmpty(Subtitle))
                TextRenderer.DrawText(g, Subtitle, UiFont.Small, new Rectangle(24, 42, Width - 140, 18), Palette.Muted, PaintKit.Single);
            if (!string.IsNullOrEmpty(Pill)) {
                var size = TextRenderer.MeasureText(g, Pill, UiFont.Small, new Size(120, 18), PaintKit.Single);
                var pill = new Rectangle(Width - size.Width - 36, 24, size.Width + 16, 22);
                PaintKit.FillRound(g, pill, 11, Color.FromArgb(28, 32, 40));
                TextRenderer.DrawText(g, Pill, UiFont.Small, pill, PillColor, PaintKit.Single | TextFormatFlags.HorizontalCenter);
            }
            using (var pen = new Pen(Palette.Line))
                g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
        }
    }

    sealed class BannerBar : Control {
        string message = "";
        public event EventHandler ActionClick;
        public string ActionCaption { get; set; }

        public string Message {
            get { return message; }
            set { message = value ?? ""; Visible = message.Length > 0; Invalidate(); }
        }

        public bool Danger { get; set; }

        public BannerBar() {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 44;
            Visible = false;
            BackColor = Palette.Bg;
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e) {
            PaintKit.ClearBehind(e.Graphics, this);
            var card = new Rectangle(16, 6, Width - 32, Height - 10);
            PaintKit.FillRound(e.Graphics, card, 8, Danger ? Palette.Banner : Palette.Info);
            int textWidth = card.Width - (string.IsNullOrEmpty(ActionCaption) ? 24 : 110);
            TextRenderer.DrawText(e.Graphics, message, UiFont.Small, new Rectangle(card.X + 12, card.Y, textWidth, card.Height), Danger ? Color.FromArgb(254, 205, 211) : Palette.Ink, PaintKit.Single);
            if (!string.IsNullOrEmpty(ActionCaption))
                TextRenderer.DrawText(e.Graphics, ActionCaption, UiFont.BodyStrong, new Rectangle(card.Right - 96, card.Y, 84, card.Height), Palette.Ink, PaintKit.Single | TextFormatFlags.Right);
        }

        protected override void OnClick(EventArgs e) {
            if (!string.IsNullOrEmpty(ActionCaption) && ActionClick != null) ActionClick(this, EventArgs.Empty);
            base.OnClick(e);
        }
    }
}
