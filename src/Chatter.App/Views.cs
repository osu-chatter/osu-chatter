using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Chatter;

namespace Chatter.App {
    sealed class ChannelRow {
        public string Name;
        public bool Private;
        public int Unread;
        public bool Selected;
        public Rectangle Bounds;
    }

    sealed class ChannelRail : Control {
        readonly List<ChannelRow> rows = new List<ChannelRow>();
        int hover = -1;
        public event Action<string> Chosen;
        public string Status = "Looking for osu!";
        public Color StatusColor = Palette.Faint;
        public string Account = "";

        public ChannelRail() {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Palette.Sidebar;
            Width = 252;
        }

        public void SetRows(IList<ChannelRow> next) {
            rows.Clear();
            if (next != null) rows.AddRange(next);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics;
            g.Clear(Palette.Sidebar);
            TextRenderer.DrawText(g, "chatter", UiFont.Title, new Rectangle(20, 18, Width - 32, 28), Palette.Ink, PaintKit.Single);
            var dot = StatusColor;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(dot))
                g.FillEllipse(brush, 22, 52, 7, 7);
            string status = string.IsNullOrEmpty(Account) ? Status : Status + "  ·  " + Account;
            TextRenderer.DrawText(g, status, UiFont.Small, new Rectangle(36, 46, Width - 48, 18), Palette.Muted, PaintKit.Single);
            using (var pen = new Pen(Palette.Line))
                g.DrawLine(pen, Width - 1, 0, Width - 1, Height);

            foreach (var row in rows) row.Bounds = Rectangle.Empty;
            int y = 84;
            y = Section(g, "Channels", y, false);
            y = Section(g, "Direct", y, true);
            if (rows.Count == 0) {
                TextRenderer.DrawText(g, "Open chats show up here.", UiFont.Small, new Rectangle(20, 92, Width - 40, 36), Palette.Faint, PaintKit.Wrap);
            }
        }

        int Section(Graphics g, string title, int y, bool privat) {
            bool any = false;
            for (int i = 0; i < rows.Count; i++) if (rows[i].Private == privat) any = true;
            if (!any) return y;
            TextRenderer.DrawText(g, title.ToUpperInvariant(), UiFont.Section, new Rectangle(20, y, Width - 40, 18), Palette.Faint, PaintKit.Single);
            y += 22;
            for (int i = 0; i < rows.Count; i++) {
                if (rows[i].Private != privat) continue;
                var row = rows[i];
                if (y > Height - 76) break;
                var bounds = new Rectangle(10, y, Width - 22, 36);
                row.Bounds = bounds;
                if (row.Selected || i == hover) PaintKit.FillRound(g, bounds, 8, row.Selected ? Palette.Elevated : Color.FromArgb(28, 31, 38));
                int textWidth = bounds.Width - 28;
                if (row.Unread > 0) textWidth -= 32;
                TextRenderer.DrawText(g, row.Name, row.Selected ? UiFont.BodyStrong : UiFont.Body, new Rectangle(bounds.X + 12, bounds.Y, textWidth, bounds.Height), row.Selected ? Palette.Ink : Palette.Muted, PaintKit.Single);
                if (row.Unread > 0) {
                    string badge = Math.Min(row.Unread, 99).ToString();
                    var badgeRect = new Rectangle(bounds.Right - 36, bounds.Y + 8, 26, 20);
                    PaintKit.FillRound(g, badgeRect, 10, Palette.Accent);
                    TextRenderer.DrawText(g, badge, UiFont.Small, badgeRect, Color.White, PaintKit.Single | TextFormatFlags.HorizontalCenter);
                }
                y += 40;
            }
            return y + 8;
        }

        protected override void OnMouseMove(MouseEventArgs e) {
            int next = Hit(e.Location);
            if (next != hover) { hover = next; Invalidate(); Cursor = next >= 0 ? Cursors.Hand : Cursors.Default; }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) {
            hover = -1;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e) {
            if (e.Button != MouseButtons.Left) return;
            int index = Hit(e.Location);
            if (index >= 0 && Chosen != null) Chosen(rows[index].Name);
            base.OnMouseDown(e);
        }

        int Hit(Point point) {
            for (int i = 0; i < rows.Count; i++) if (rows[i].Bounds.Contains(point)) return i;
            return -1;
        }
    }

    sealed class TranscriptView : Control {
        sealed class Block {
            public ChatMessage Message;
            public Rectangle Bubble;
            public Rectangle Translate;
            public bool HasTranslate;
            public bool ShowSender;
        }

        readonly List<Block> blocks = new List<Block>();
        IReadOnlyList<ChatMessage> messages = new ChatMessage[0];
        Dictionary<string, string> translations = new Dictionary<string, string>();
        Dictionary<string, string> errors = new Dictionary<string, string>();
        int scroll;
        int contentHeight;
        bool pinned = true;
        bool dragging;
        int grab;
        public bool ShowTranslate = true;
        public string EmptyTitle = "";
        public string EmptyBody = "";
        public event Action<ChatMessage> TranslateRequested;

        public TranscriptView() {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Palette.Bg;
        }

        public void SetMessages(IReadOnlyList<ChatMessage> next, Dictionary<string, string> translated, Dictionary<string, string> failed, bool stick) {
            if (!stick && SameMessages(next) && SameValues(translations, translated) && SameValues(errors, failed)
                && lastShowTranslate == ShowTranslate) return;
            SaveAnchor();
            messages = next ?? new ChatMessage[0];
            translations = translated == null ? new Dictionary<string, string>() : new Dictionary<string, string>(translated);
            errors = failed == null ? new Dictionary<string, string>() : new Dictionary<string, string>(failed);
            lastShowTranslate = ShowTranslate;
            if (stick) { pinned = true; anchorId = null; }
            Relayout();
        }

        public void SetEmpty(string title, string body) {
            EmptyTitle = title ?? "";
            EmptyBody = body ?? "";
            messages = new ChatMessage[0];
            blocks.Clear();
            scroll = 0;
            contentHeight = 0;
            pinned = true;
            anchorId = null;
            Invalidate();
        }

        protected override void OnResize(EventArgs e) {
            SaveAnchor();
            base.OnResize(e);
            Relayout();
        }

        void Relayout() {
            blocks.Clear();
            if (messages.Count == 0 || Width < 40) { contentHeight = 0; scroll = 0; Invalidate(); return; }
            int maxText = Math.Max(120, Math.Min(520, (int)((Width - 64) * 0.72))) - 28;
            int y = 18;
            ChatMessage previous = null;
            for (int i = 0; i < messages.Count; i++) {
                ChatMessage message = messages[i];
                var block = new Block { Message = message };
                if (message.System) {
                    var size = PaintKit.Measure(message.Text, UiFont.Small, Width - 80);
                    int width = Math.Min(Width - 80, Math.Max(size.Width, 40));
                    block.Bubble = new Rectangle((Width - width) / 2, y, width, Math.Max(18, size.Height));
                    y += block.Bubble.Height + 16;
                    blocks.Add(block);
                    previous = null;
                    continue;
                }
                bool grouped = previous != null && previous.Sender == message.Sender
                    && previous.Outgoing == message.Outgoing && message.Sender.Length > 0;
                block.ShowSender = !message.Outgoing && !grouped && message.Sender.Length > 0;
                string translated;
                translations.TryGetValue(message.Id, out translated);
                bool hasTranslation = !string.IsNullOrEmpty(translated) && translated != message.Text;
                string error;
                errors.TryGetValue(message.Id, out error);
                var body = PaintKit.Measure(message.Text, UiFont.Message, maxText);
                Size translationSize = Size.Empty;
                Size errorSize = Size.Empty;
                if (hasTranslation) {
                    translationSize = PaintKit.Measure(translated, UiFont.Body, maxText);
                }
                if (!string.IsNullOrEmpty(error)) {
                    errorSize = PaintKit.Measure(error, UiFont.Small, maxText);
                }
                bool link = ShowTranslate && !hasTranslation && string.IsNullOrEmpty(error);
                int nameGap = block.ShowSender ? 22 : 0;
                int bubbleWidth = Math.Max(body.Width, translationSize.Width);
                bubbleWidth = Math.Max(bubbleWidth, errorSize.Width);
                bubbleWidth = Math.Min(maxText, Math.Max(link ? 128 : 96, bubbleWidth)) + 28;
                int textWidth = bubbleWidth - 28;
                body = PaintKit.Measure(message.Text, UiFont.Message, textWidth);
                int extra = 26;
                if (hasTranslation) extra += PaintKit.Measure(translated, UiFont.Body, textWidth).Height + 12;
                if (!string.IsNullOrEmpty(error)) extra += PaintKit.Measure(error, UiFont.Small, textWidth).Height + 8;
                int bubbleHeight = 12 + body.Height + extra;
                int x = message.Outgoing ? Width - bubbleWidth - 22 : 20;
                y += nameGap;
                block.Bubble = new Rectangle(x, y, bubbleWidth, bubbleHeight);
                block.HasTranslate = link;
                if (link) block.Translate = new Rectangle(x + 14, y + bubbleHeight - 22, 70, 18);
                y += bubbleHeight + (grouped ? 6 : 12);
                blocks.Add(block);
                previous = message;
            }
            contentHeight = y + 8;
            int shift = Math.Max(0, Height - (y + 8));
            if (shift > 0) {
                for (int i = 0; i < blocks.Count; i++) {
                    blocks[i].Bubble.Y += shift;
                    if (blocks[i].HasTranslate) blocks[i].Translate.Y += shift;
                }
            }
            int max = MaxScroll();
            if (pinned) scroll = max;
            else {
                if (anchorId != null) {
                    var anchor = blocks.Find(b => b.Message.Id == anchorId);
                    if (anchor != null) scroll = anchor.Bubble.Top - anchorOffset;
                }
                scroll = Clamp(scroll);
            }
            anchorId = null;
            Invalidate();
        }

        int MaxScroll() {
            return Math.Max(0, contentHeight - Height);
        }

        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics;
            g.Clear(Palette.Bg);
            if (messages.Count == 0) {
                var title = PaintKit.Measure(EmptyTitle, UiFont.BodyStrong, Width - 120);
                var body = PaintKit.Measure(EmptyBody, UiFont.Body, Math.Min(420, Width - 120));
                int top = Math.Max(40, (Height - title.Height - body.Height - 8) / 2);
                int left = 48;
                TextRenderer.DrawText(g, EmptyTitle, UiFont.BodyStrong, new Rectangle(left, top, Width - 96, title.Height + 4), Palette.Ink, PaintKit.Wrap);
                TextRenderer.DrawText(g, EmptyBody, UiFont.Body, new Rectangle(left, top + title.Height + 8, Math.Min(420, Width - 96), body.Height + 8), Palette.Muted, PaintKit.Wrap);
                return;
            }
            for (int i = 0; i < blocks.Count; i++) {
                Block block = blocks[i];
                Rectangle bubble = block.Bubble;
                bubble.Y -= scroll;
                if (bubble.Bottom < -20 || bubble.Top > Height + 24) continue;
                ChatMessage message = block.Message;
                if (message.System) {
                    TextRenderer.DrawText(g, message.Text, UiFont.Small, bubble, Palette.Faint, PaintKit.Wrap | TextFormatFlags.HorizontalCenter);
                    continue;
                }
                if (block.ShowSender) {
                    var name = new Rectangle(bubble.X + 4, bubble.Y - 18, bubble.Width, 16);
                    TextRenderer.DrawText(g, message.Sender, UiFont.Name, name, Palette.Muted, PaintKit.Single);
                }
                PaintKit.FillRound(g, bubble, 10, message.Outgoing ? Palette.BubbleOut : Palette.BubbleIn);
                PaintKit.StrokeRound(g, bubble, 10, message.Outgoing ? Color.FromArgb(103, 45, 65) : Color.FromArgb(40, 46, 57));
                int textWidth = bubble.Width - 28;
                var bodyRect = new Rectangle(bubble.X + 14, bubble.Y + 10, textWidth, bubble.Height - 20);
                TextRenderer.DrawText(g, message.Text, UiFont.Message, bodyRect, Palette.Ink, PaintKit.Wrap);
                var bodySize = PaintKit.Measure(message.Text, UiFont.Message, textWidth);
                int cursor = bodyRect.Y + bodySize.Height + 8;
                string translated;
                if (translations.TryGetValue(message.Id, out translated) && translated != message.Text) {
                    using (var pen = new Pen(Color.FromArgb(64, 255, 255, 255)))
                        g.DrawLine(pen, bubble.X + 14, cursor, bubble.Right - 14, cursor);
                    cursor += 6;
                    var translationSize = PaintKit.Measure(translated, UiFont.Body, textWidth);
                    TextRenderer.DrawText(g, translated, UiFont.Body, new Rectangle(bubble.X + 14, cursor, textWidth, translationSize.Height + 2), Color.FromArgb(214, 219, 228), PaintKit.Wrap);
                    cursor += translationSize.Height + 6;
                }
                string error;
                if (errors.TryGetValue(message.Id, out error) && !string.IsNullOrEmpty(error)) {
                    var errorSize = PaintKit.Measure(error, UiFont.Small, textWidth);
                    TextRenderer.DrawText(g, error, UiFont.Small, new Rectangle(bubble.X + 14, cursor, textWidth, errorSize.Height + 2), Color.FromArgb(254, 205, 211), PaintKit.Wrap);
                }
                string time = message.Time != null && message.Time.Length >= 5 ? message.Time.Substring(0, 5) : message.Time;
                var timeRect = new Rectangle(bubble.Right - 58, bubble.Bottom - 20, 46, 16);
                TextRenderer.DrawText(g, time, UiFont.Small, timeRect, Color.FromArgb(150, 156, 168), PaintKit.Single | TextFormatFlags.Right);
                if (block.HasTranslate) {
                    var link = block.Translate;
                    link.Y -= scroll;
                    TextRenderer.DrawText(g, "Translate", UiFont.Small, link, Palette.Muted, PaintKit.Single);
                }
            }
            int max = MaxScroll();
            if (max > 0) {
                int track = Height - 16;
                int thumb = Math.Max(28, track * Height / (Height + max));
                int top = 8 + (track - thumb) * scroll / max;
                PaintKit.FillRound(g, new Rectangle(Width - 8, top, 4, thumb), 2, Color.FromArgb(70, 154, 163, 178));
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e) {
            ScrollBy(e.Delta);
            if (e is HandledMouseEventArgs handled) handled.Handled = true;
        }

        protected override void OnMouseDown(MouseEventArgs e) {
            if (e.Button != MouseButtons.Left) return;
            Focus();
            for (int i = 0; i < blocks.Count; i++) {
                if (!blocks[i].HasTranslate) continue;
                var link = blocks[i].Translate;
                link.Y -= scroll;
                if (link.Contains(e.Location) && TranslateRequested != null) {
                    TranslateRequested(blocks[i].Message);
                    return;
                }
            }
            int max = MaxScroll();
            if (max > 0 && e.X >= Width - 12) {
                dragging = true;
                int track = Math.Max(1, Height - 16);
                int thumb = Math.Max(28, track * Height / (Height + max));
                int top = 8 + (track - thumb) * scroll / max;
                grab = e.Y >= top && e.Y <= top + thumb ? e.Y - top : thumb / 2;
                DragTo(e.Y);
                Capture = true;
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e) {
            bool overLink = blocks.Exists(block => block.HasTranslate && new Rectangle(block.Translate.X, block.Translate.Y - scroll, block.Translate.Width, block.Translate.Height).Contains(e.Location));
            Cursor = overLink || e.X >= Width - 12 ? Cursors.Hand : Cursors.Default;
            if (dragging) {
                DragTo(e.Y);
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e) {
            dragging = false;
            Capture = false;
            base.OnMouseUp(e);
        }

        public void ScrollBy(int delta) {
            scroll = Clamp(scroll - delta);
            pinned = scroll >= MaxScroll() - 24;
            Invalidate();
        }

        string anchorId;
        int anchorOffset;
        bool lastShowTranslate;

        void SaveAnchor() {
            if (pinned) return;
            var block = blocks.Find(b => b.Bubble.Bottom >= scroll);
            if (block != null) { anchorId = block.Message.Id; anchorOffset = block.Bubble.Top - scroll; }
        }

        bool SameMessages(IReadOnlyList<ChatMessage> next) {
            if (next == null || next.Count != messages.Count) return false;
            for (int i = 0; i < next.Count; i++) {
                var a = next[i]; var b = messages[i];
                if (a.Id != b.Id || a.Text != b.Text || a.Sender != b.Sender || a.Time != b.Time
                    || a.Outgoing != b.Outgoing || a.System != b.System) return false;
            }
            return true;
        }

        static bool SameValues(Dictionary<string, string> a, Dictionary<string, string> b) {
            if (b == null) return a.Count == 0;
            if (a.Count != b.Count) return false;
            foreach (var pair in a) if (!b.TryGetValue(pair.Key, out var value) || value != pair.Value) return false;
            return true;
        }

        void DragTo(int y) {
            int max = MaxScroll();
            int track = Math.Max(1, Height - 16);
            int thumb = Math.Max(28, track * Height / Math.Max(1, Height + max));
            scroll = Clamp((int)((long)(y - 8 - grab) * max / Math.Max(1, track - thumb)));
            pinned = scroll >= max - 24;
            Invalidate();
        }

        protected override void OnMouseCaptureChanged(EventArgs e) {
            if (!Capture) dragging = false;
            base.OnMouseCaptureChanged(e);
        }

        int Clamp(int value) {
            if (value < 0) return 0;
            int max = MaxScroll();
            return value > max ? max : value;
        }
    }
}
