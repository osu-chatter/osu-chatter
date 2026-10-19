using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
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
        const int AvatarSize = 28;
        const int AvatarGap = 8;
        const int Side = 16;
        static readonly Color SupporterName = Color.FromArgb(244, 211, 140);
        static readonly Color SupporterRing = Color.FromArgb(232, 196, 122);

        sealed class Block {
            public ChatMessage Message;
            public Rectangle Bubble;
            public Rectangle Translate;
            public bool HasTranslate;
            public bool ShowSender;
            public bool ShowAvatar;
            public Rectangle Avatar;
            public string LinkCaption = "";
            public List<TextLink> Links = new List<TextLink>();
        }

        sealed class TextLink {
            public Rectangle Bounds;
            public string Url = "";
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
        public bool AllowUpgrade;
        public HashSet<string> ManualTranslations = new HashSet<string>();
        public string EmptyTitle = "";
        public string EmptyBody = "";
        public event Action<ChatMessage> TranslateRequested;
        public Func<string, PlayerFace?>? Players;
        internal Func<string, bool>? Open;
        string hotUrl = "";
        readonly HashSet<string> appliedManual = new HashSet<string>();
        bool appliedUpgrade;

        public TranscriptView() {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Palette.Bg;
            Open = null;
        }

        public void SetMessages(IReadOnlyList<ChatMessage> next, Dictionary<string, string> translated, Dictionary<string, string> failed, bool stick) {
            if (!stick && SameMessages(next) && SameValues(translations, translated) && SameValues(errors, failed)
                && lastShowTranslate == ShowTranslate && appliedUpgrade == AllowUpgrade && SameManual()) return;
            SaveAnchor();
            messages = next ?? new ChatMessage[0];
            translations = translated == null ? new Dictionary<string, string>() : new Dictionary<string, string>(translated);
            errors = failed == null ? new Dictionary<string, string>() : new Dictionary<string, string>(failed);
            lastShowTranslate = ShowTranslate;
            RememberManual();
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
                bool upgraded = appliedManual.Contains(message.Id);
                bool link = ShowTranslate && !upgraded && (AllowUpgrade || (!hasTranslation && string.IsNullOrEmpty(error)));
                string linkCaption = hasTranslation ? "Retranslate" : "Translate";
                int linkWidth = hasTranslation ? 84 : 70;
                int nameGap = block.ShowSender ? 22 : 0;
                int bubbleWidth = Math.Max(body.Width, translationSize.Width);
                bubbleWidth = Math.Max(bubbleWidth, errorSize.Width);
                bubbleWidth = Math.Min(maxText, Math.Max(link ? (hasTranslation ? 156 : 128) : 96, bubbleWidth)) + 28;
                int textWidth = bubbleWidth - 28;
                body = PaintKit.Measure(message.Text, UiFont.Message, textWidth);
                int extra = 26;
                if (hasTranslation) extra += PaintKit.Measure(translated, UiFont.Body, textWidth).Height + 12;
                if (!string.IsNullOrEmpty(error)) extra += PaintKit.Measure(error, UiFont.Small, textWidth).Height + 8;
                if (link && !string.IsNullOrEmpty(error)) extra += 16;
                int bubbleHeight = 12 + body.Height + extra;
                int gutter = Side + AvatarSize + AvatarGap;
                int x = message.Outgoing ? Width - bubbleWidth - gutter : gutter;
                y += nameGap;
                block.Bubble = new Rectangle(x, y, bubbleWidth, bubbleHeight);
                block.ShowAvatar = !grouped;
                block.Avatar = new Rectangle(message.Outgoing ? x + bubbleWidth + AvatarGap : Side, block.ShowSender ? -20 : 0, AvatarSize, AvatarSize);
                block.Links = LayoutLinks(message.Text, ChatLinks.Find(message.Text, message.Source), textWidth);
                block.HasTranslate = link;
                block.LinkCaption = linkCaption;
                if (link) block.Translate = new Rectangle(x + 14, y + bubbleHeight - 22, linkWidth, 18);
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
                if (block.ShowAvatar) DrawAvatar(g, new Rectangle(block.Avatar.X, bubble.Y + block.Avatar.Y, block.Avatar.Width, block.Avatar.Height), message);
                if (block.ShowSender) {
                    var name = new Rectangle(bubble.X + 4, bubble.Y - 18, bubble.Width, 16);
                    PlayerFace? face = FaceOf(message.Sender);
                    TextRenderer.DrawText(g, message.Sender, UiFont.Name, name, face != null && face.Supporter ? SupporterName : Palette.Muted, PaintKit.Single);
                }
                PaintKit.FillRound(g, bubble, 10, message.Outgoing ? Palette.BubbleOut : Palette.BubbleIn);
                PaintKit.StrokeRound(g, bubble, 10, message.Outgoing ? Color.FromArgb(103, 45, 65) : Color.FromArgb(40, 46, 57));
                int textWidth = bubble.Width - 28;
                var bodyRect = new Rectangle(bubble.X + 14, bubble.Y + 10, textWidth, bubble.Height - 20);
                TextRenderer.DrawText(g, message.Text, UiFont.Message, bodyRect, Palette.Ink, PaintKit.Wrap);
                DrawLinkLines(g, block, bodyRect);
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
                    TextRenderer.DrawText(g, block.LinkCaption, UiFont.Small, link, Palette.Muted, PaintKit.Single);
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
            TextLink? chatLink;
            if (HitLink(e.Location, out chatLink) && chatLink != null) {
                OpenExternal(chatLink.Url);
                return;
            }
            int profileId;
            if (HitAvatar(e.Location, out profileId) && profileId > 0) {
                OpenExternal("https://osu.ppy.sh/users/" + profileId.ToString(CultureInfo.InvariantCulture));
                return;
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
            bool overTranslate = blocks.Exists(block => block.HasTranslate && new Rectangle(block.Translate.X, block.Translate.Y - scroll, block.Translate.Width, block.Translate.Height).Contains(e.Location));
            TextLink? chatLink;
            bool overLink = HitLink(e.Location, out chatLink);
            int profileId;
            bool overAvatar = HitAvatar(e.Location, out profileId) && profileId > 0;
            Cursor = overTranslate || overLink || overAvatar || e.X >= Width - 12 ? Cursors.Hand : Cursors.Default;
            string next = overLink && chatLink != null ? chatLink.Url : "";
            if (next != hotUrl) { hotUrl = next; Invalidate(); }
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

        void RememberManual() {
            appliedUpgrade = AllowUpgrade;
            appliedManual.Clear();
            if (ManualTranslations == null) return;
            foreach (string id in ManualTranslations) appliedManual.Add(id);
        }

        bool SameManual() {
            HashSet<string> next = ManualTranslations;
            int count = next == null ? 0 : next.Count;
            if (count != appliedManual.Count) return false;
            if (next == null) return true;
            foreach (string id in next) if (!appliedManual.Contains(id)) return false;
            return true;
        }

        bool SameMessages(IReadOnlyList<ChatMessage> next) {
            if (next == null || next.Count != messages.Count) return false;
            for (int i = 0; i < next.Count; i++) {
                var a = next[i]; var b = messages[i];
                if (a.Id != b.Id || a.Text != b.Text || a.Sender != b.Sender || a.Time != b.Time
                    || a.Outgoing != b.Outgoing || a.System != b.System || a.Source != b.Source) return false;
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

        PlayerFace? FaceOf(string name) {
            if (Players == null || string.IsNullOrEmpty(name)) return null;
            try { return Players(name); }
            catch { return null; }
        }

        void DrawAvatar(Graphics g, Rectangle rect, ChatMessage message) {
            PlayerFace? face = FaceOf(message.Sender);
            Image? image = face == null ? null : face.Image;
            bool supporter = face != null && face.Supporter;
            GraphicsState state = g.Save();
            try {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (image != null) {
                    using (var path = new GraphicsPath()) {
                        path.AddEllipse(rect);
                        g.SetClip(path);
                        if (ImageAnimator.CanAnimate(image)) ImageAnimator.UpdateFrames(image);
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(image, rect);
                    }
                } else {
                    using (var brush = new SolidBrush(Palette.Field)) g.FillEllipse(brush, rect);
                    TextRenderer.DrawText(g, Initial(message.Sender), UiFont.Name, rect, Palette.Ink, PaintKit.Single | TextFormatFlags.HorizontalCenter);
                }
            } catch {
                using (var brush = new SolidBrush(Palette.Field)) g.FillEllipse(brush, rect);
            }
            g.Restore(state);
            if (!supporter) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(SupporterRing, 2f))
                g.DrawEllipse(pen, rect.X + 1, rect.Y + 1, rect.Width - 3, rect.Height - 3);
        }

        void DrawLinkLines(Graphics g, Block block, Rectangle body) {
            if (block.Links == null || block.Links.Count == 0) return;
            SmoothingMode previous = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.None;
            for (int i = 0; i < block.Links.Count; i++) {
                TextLink link = block.Links[i];
                bool hot = hotUrl.Length > 0 && link.Url == hotUrl;
                using (var pen = new Pen(hot ? Palette.Accent : Color.FromArgb(170, 186, 196, 210))) {
                    int y = body.Y + link.Bounds.Bottom - 2;
                    g.DrawLine(pen, body.X + link.Bounds.X, y, body.X + link.Bounds.Right, y);
                }
            }
            g.SmoothingMode = previous;
        }

        bool HitLink(Point point, out TextLink? found) {
            found = null;
            for (int i = 0; i < blocks.Count; i++) {
                Block block = blocks[i];
                if (block.HasTranslate) {
                    var translate = block.Translate;
                    translate.Y -= scroll;
                    if (translate.Contains(point)) return false;
                }
                if (block.Links == null) continue;
                int originX = block.Bubble.X + 14;
                int originY = block.Bubble.Y - scroll + 10;
                for (int j = 0; j < block.Links.Count; j++) {
                    TextLink link = block.Links[j];
                    var rect = new Rectangle(originX + link.Bounds.X, originY + link.Bounds.Y, link.Bounds.Width, link.Bounds.Height);
                    if (!rect.Contains(point)) continue;
                    found = link;
                    return true;
                }
            }
            return false;
        }

        bool HitAvatar(Point point, out int id) {
            id = 0;
            for (int i = 0; i < blocks.Count; i++) {
                Block block = blocks[i];
                if (!block.ShowAvatar || block.Message == null) continue;
                var rect = new Rectangle(block.Avatar.X, block.Bubble.Y - scroll + block.Avatar.Y, block.Avatar.Width, block.Avatar.Height);
                if (!rect.Contains(point)) continue;
                PlayerFace? face = FaceOf(block.Message.Sender);
                id = face == null ? 0 : face.Id;
                return true;
            }
            return false;
        }

        void OpenExternal(string url) {
            if (!ChatLinks.CanOpen(url)) return;
            if (Open != null) {
                try { if (Open(url)) return; }
                catch { return; }
            }
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { }
        }

        static string Initial(string name) {
            int i = 0;
            while (i < name.Length && char.IsWhiteSpace(name[i])) i++;
            if (i >= name.Length) return "?";
            if (char.IsHighSurrogate(name[i]) && i + 1 < name.Length) return name.Substring(i, 2);
            return char.ToUpperInvariant(name[i]).ToString();
        }

        static List<TextLink> LayoutLinks(string text, List<ChatLinks.Span> spans, int width) {
            var links = new List<TextLink>();
            if (spans == null || spans.Count == 0 || string.IsNullOrEmpty(text) || width <= 0) return links;
            int lineHeight = TextRenderer.MeasureText("Ay", UiFont.Message, new Size(Math.Max(1, width), int.MaxValue), PaintKit.Single).Height;
            if (lineHeight < 1) lineHeight = UiFont.Message.Height;
            int limit = PaintKit.Measure(text, UiFont.Message, width).Height;
            if (limit < lineHeight) limit = lineHeight;
            int x = 0;
            int y = 0;
            int index = 0;
            while (index < text.Length && y < limit) {
                char c = text[index];
                if (c == '\n') { x = 0; y += lineHeight; index++; continue; }
                if (c == '\r') { index++; continue; }
                int end = index + 1;
                if (char.IsWhiteSpace(c)) {
                    while (end < text.Length && char.IsWhiteSpace(text[end]) && text[end] != '\n' && text[end] != '\r') end++;
                } else {
                    while (end < text.Length && !char.IsWhiteSpace(text[end]) && text[end] != '\n' && text[end] != '\r') end++;
                }
                PlaceLink(text, spans, links, index, end, width, lineHeight, limit, ref x, ref y);
                index = end;
            }
            if (links.Count == 0) {
                for (int i = 0; i < spans.Count; i++) {
                    if (spans[i].Index > 0 || spans[i].Length < text.Length) continue;
                    Size size = PaintKit.Measure(text, UiFont.Message, width);
                    links.Add(new TextLink { Bounds = new Rectangle(0, 0, Math.Max(1, size.Width), Math.Max(1, size.Height)), Url = spans[i].Url });
                    break;
                }
            }
            return links;
        }

        static void PlaceLink(string text, List<ChatLinks.Span> spans, List<TextLink> links, int start, int end, int width, int lineHeight, int limit, ref int x, ref int y) {
            if (start >= end || y >= limit) return;
            int tokenWidth = TextWidth(text.Substring(start, end - start));
            if (x > 0 && x + tokenWidth > width) {
                x = 0;
                y += lineHeight;
                if (y >= limit) return;
            }
            if (tokenWidth <= width || end == start + 1) {
                EmitLink(text, spans, links, start, end, x, y, lineHeight, width);
                x += tokenWidth;
                return;
            }
            int chunk = start;
            while (chunk < end && y < limit) {
                int fit = 1;
                int fitWidth = TextWidth(text.Substring(chunk, 1));
                while (chunk + fit < end) {
                    int wider = TextWidth(text.Substring(chunk, fit + 1));
                    if (wider > width) break;
                    fit++;
                    fitWidth = wider;
                }
                if (x > 0 && x + fitWidth > width) {
                    x = 0;
                    y += lineHeight;
                    if (y >= limit) return;
                }
                EmitLink(text, spans, links, chunk, chunk + fit, x, y, lineHeight, width);
                x += fitWidth;
                chunk += fit;
                if (chunk < end) { x = 0; y += lineHeight; }
            }
        }

        static void EmitLink(string text, List<ChatLinks.Span> spans, List<TextLink> links, int start, int end, int x, int y, int lineHeight, int width) {
            for (int i = 0; i < spans.Count; i++) {
                ChatLinks.Span span = spans[i];
                int from = Math.Max(start, span.Index);
                int to = Math.Min(end, span.Index + span.Length);
                if (from >= to) continue;
                int prefix = from == start ? 0 : TextWidth(text.Substring(start, from - start));
                int piece = TextWidth(text.Substring(from, to - from));
                if (piece < 1) piece = 1;
                int left = x + prefix;
                if (left >= width) continue;
                if (left + piece > width) piece = width - left;
                links.Add(new TextLink { Bounds = new Rectangle(left, y, piece, lineHeight), Url = span.Url });
            }
        }

        static int TextWidth(string value) {
            if (string.IsNullOrEmpty(value)) return 0;
            return TextRenderer.MeasureText(value, UiFont.Message, new Size(10000, 100), PaintKit.Single).Width;
        }
    }
}
