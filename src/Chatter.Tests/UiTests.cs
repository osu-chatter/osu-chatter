using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Chatter;
using Chatter.App;

namespace Chatter.Tests {
    static class UiTests {
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        static object Get(object target, string name) => target.GetType().GetField(name, Fields).GetValue(target);
        static void Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Fields).Invoke(target, args);
        static void Check(bool value, string error) { if (!value) throw new InvalidOperationException(error); }
        static List<ChatMessage> Messages(int count) {
            var list = new List<ChatMessage>();
            for (int i = 0; i < count; i++) list.Add(new ChatMessage(i.ToString(), "Friend", "Message " + i + " with some readable text.", "12:30:00", false, false));
            return list;
        }
        static Control View() {
            var type = typeof(MainForm).Assembly.GetType("Chatter.App.TranscriptView");
            var view = (Control)Activator.CreateInstance(type, true);
            view.Size = new Size(700, 420);
            var handle = view.Handle;
            return view;
        }
        static int Scroll(object view) => (int)Get(view, "scroll");
        static int Max(object view) => Math.Max(0, (int)Get(view, "contentHeight") - ((Control)view).Height);
        static (string Id, int Top) Anchor(object view) {
            foreach (var block in (IList)Get(view, "blocks")) {
                var bubble = (Rectangle)Get(block, "Bubble");
                if (bubble.Bottom >= Scroll(view)) return (((ChatMessage)Get(block, "Message")).Id, bubble.Top - Scroll(view));
            }
            throw new Exception("no anchor");
        }
        public static void Transcript() {
            using (var view = View()) {
                var messages = Messages(40);
                var translations = new Dictionary<string, string>();
                var errors = new Dictionary<string, string>();
                Call(view, "SetMessages", messages, translations, errors, true);
                Check(Scroll(view) == Max(view), "initial bottom");
                Call(view, "ScrollBy", 900);
                var anchor = Anchor(view);
                Check(Scroll(view) < Max(view), "wheel moves away from bottom");
                messages = new List<ChatMessage>(messages) { new ChatMessage("new", "Friend", "New message", "12:31:00", false, false) };
                Call(view, "SetMessages", messages, translations, errors, false);
                Check(Anchor(view) == anchor, "new message moved reading position");
                translations["0"] = new string('x', 400);
                Call(view, "SetMessages", messages, translations, errors, false);
                Check(Anchor(view) == anchor, "translation above viewport moved anchor");
                messages.RemoveRange(0, 4);
                Call(view, "SetMessages", messages, translations, errors, false);
                Check(Anchor(view) == anchor, "trimmed history moved anchor");
                view.Width = 540;
                Check(Anchor(view) == anchor, "resize moved anchor");
                Call(view, "ScrollBy", int.MinValue / 2);
                Check(Scroll(view) == Max(view), "bottom range");
                messages.Add(new ChatMessage("new2", "Other", "Another message", "12:32:00", false, false));
                Call(view, "SetMessages", messages, translations, errors, false);
                Check(Scroll(view) == Max(view), "pinned view follows new messages");
                var first = ((IList)Get(view, "blocks"))[0];
                Call(view, "SetMessages", new List<ChatMessage>(messages), translations, errors, false);
                Check(ReferenceEquals(first, ((IList)Get(view, "blocks"))[0]), "unchanged poll rebuilt layout");
                Call(view, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, view.Width - 4, 8, 0));
                Call(view, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, view.Width - 4, 8, 0));
                Check(Scroll(view) == 0, "scrollbar top endpoint");
                Call(view, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, view.Width - 4, view.Height - 8, 0));
                Call(view, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, view.Width - 4, view.Height - 8, 0));
                Check(Scroll(view) == Max(view), "scrollbar bottom endpoint");
            }
        }
        public static void Refresh() {
            using (var form = new MainForm { DemoMode = true }) {
                var handle = form.Handle;
                var messages = Messages(40);
                Action apply = () => Call(form, "Apply", new ChatSnapshot(1, true, "Me", new[] { new ChatChannel("#osu", false, true, new List<ChatMessage>(messages)) }));
                apply();
                var view = (Control)Get(form, "transcript");
                Call(view, "ScrollBy", 900);
                var anchor = Anchor(view);
                messages.Add(new ChatMessage("new", "Other", "Arrived during reading", "12:35:00", false, false));
                apply();
                Check(Anchor(view) == anchor, "main form repinned new snapshot");
                var draft = Get(form, "draft");
                var input = (TextBox)Get(draft, "Input");
                input.Text = "Draft for the previous account";
                Call(form, "Apply", new ChatSnapshot(2, true, "Someone else", new[] { new ChatChannel("#osu", false, true, messages) }));
                Check(input.Text.Length == 0, "account change kept the previous draft");
            }
        }
        public static void LocalTranslation() {
            using (var translator = new Translator()) {
                translator.Status += status => { if (status.Length > 0) Console.WriteLine(status); };
                translator.PrepareLocal();
                foreach (var pair in new[] { ("en", "ru", "Hello, how are you?"), ("auto", "en", "Привет, как дела?"), ("en", "pl", "Hello, how are you?"), ("en", "ko", "Hello, how are you?"), ("auto", "en", "안녕하세요 오늘 잘 지내세요?") }) {
                    string result = translator.Translate(new TranslationRequest { Endpoint = ChatRules.LocalTranslator, Source = pair.Item1, Target = pair.Item2, Text = pair.Item3 });
                    Check(!string.IsNullOrWhiteSpace(result) && result != pair.Item3, "translation did not change text: " + pair.Item2);
                    Check(System.Text.RegularExpressions.Regex.IsMatch(result, pair.Item2 == "ru" ? "[А-Яа-я]" : pair.Item2 == "ko" ? "[가-힣]" : "[A-Za-z]"), "translation lost Unicode: " + pair.Item2);
                    Console.WriteLine("translated " + pair.Item1 + " -> " + pair.Item2);
                }
                var local = Get(translator, "local");
                var worker = (System.Diagnostics.Process)Get(local, "worker");
                worker.Kill();
                worker.WaitForExit(5000);
                string restarted = translator.Translate(new TranslationRequest { Endpoint = ChatRules.LocalTranslator, Source = "en", Target = "ru", Text = "Good morning!" });
                Check(!string.IsNullOrWhiteSpace(restarted), "worker restart");
                var restartedWorker = (System.Diagnostics.Process)Get(local, "worker");
                translator.Dispose();
                Check(restartedWorker.WaitForExit(5000), "worker survives app disposal");
            }
        }
        public static void Profiles() {
            using (var view = View()) {
                string playing = "is playing xi - Blue Zenith [FOUR DIMENSIONS]";
                string source = "[https://osu.ppy.sh/b/658127 " + playing + "]";
                var messages = new List<ChatMessage> {
                    new ChatMessage("a", "Friend", playing, "12:30:00", false, false, source),
                    new ChatMessage("b", "Friend", "still here", "12:30:20", false, false),
                    new ChatMessage("c", "Other", "hello", "12:31:00", false, false)
                };
                view.GetType().GetField("Players", Fields).SetValue(view, (Func<string, PlayerFace>)(name =>
                    name == "Friend" ? new PlayerFace(0, true, null) : new PlayerFace(15, false, null)));
                string opened = null;
                bool translated = false;
                view.GetType().GetField("Open", Fields).SetValue(view, (Func<string, bool>)(url => { opened = url; return true; }));
                view.GetType().GetEvent("TranslateRequested").AddEventHandler(view, (Action<ChatMessage>)(message => translated = true));
                Call(view, "SetMessages", messages, new Dictionary<string, string>(), new Dictionary<string, string>(), true);
                IList blocks = (IList)Get(view, "blocks");
                Check(blocks.Count == 3, "blocks");
                Check((bool)Get(blocks[0], "ShowAvatar") && !(bool)Get(blocks[1], "ShowAvatar") && (bool)Get(blocks[2], "ShowAvatar"), "avatar only on the first of a group");
                var friendLinks = (IList)Get(blocks[0], "Links");
                Check(friendLinks.Count > 0, "playing line has a link");
                int scroll = Scroll(view);
                var friendBubble = (Rectangle)Get(blocks[0], "Bubble");
                var friendAvatar = (Rectangle)Get(blocks[0], "Avatar");
                var otherBubble = (Rectangle)Get(blocks[2], "Bubble");
                var otherAvatar = (Rectangle)Get(blocks[2], "Avatar");
                using (var bitmap = Paint(view)) {
                    Check(HasGold(bitmap, new Rectangle(friendAvatar.X, friendBubble.Y - scroll + friendAvatar.Y, friendAvatar.Width, friendAvatar.Height)), "supporter ring");
                    Check(!HasGold(bitmap, new Rectangle(otherAvatar.X, otherBubble.Y - scroll + otherAvatar.Y, otherAvatar.Width, otherAvatar.Height)), "plain avatar has no ring");
                }
                var translate = (Rectangle)Get(blocks[0], "Translate");
                Call(view, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, translate.X + 4, translate.Y - scroll + 4, 0));
                Check(translated && opened == null, "translate click opened a link");
                var linkBounds = (Rectangle)Get(friendLinks[0], "Bounds");
                int linkX = friendBubble.X + 14 + linkBounds.X + Math.Min(8, Math.Max(0, linkBounds.Width / 4));
                int linkY = friendBubble.Y - scroll + 10 + linkBounds.Y + Math.Max(1, linkBounds.Height / 2);
                Check(!translate.Contains(linkX, linkY + scroll), "link overlaps translate");
                Call(view, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, linkX, linkY, 0));
                Check(opened == "https://osu.ppy.sh/b/658127", "map link " + opened);
                var friendHit = new Rectangle(friendAvatar.X, friendBubble.Y - scroll + friendAvatar.Y, friendAvatar.Width, friendAvatar.Height);
                opened = null;
                Call(view, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, friendHit.X + 8, friendHit.Y + 8, 0));
                Check(opened == null, "placeholder id opened a profile");
                var otherHit = new Rectangle(otherAvatar.X, otherBubble.Y - scroll + otherAvatar.Y, otherAvatar.Width, otherAvatar.Height);
                Call(view, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, otherHit.X + 8, otherHit.Y + 8, 0));
                Check(opened == "https://osu.ppy.sh/users/15", "avatar profile " + opened);
            }
        }
        public static void LinkLines() {
            string body = "is listening to Aerosmith - Dream On";
            string source = "is listening to [https://osu.ppy.sh/b/123 " + "Aerosmith - Dream On]";
            using (var view = View()) {
                view.GetType().GetField("ShowTranslate", Fields).SetValue(view, false);
                string opened = null;
                view.GetType().GetField("Open", Fields).SetValue(view, (Func<string, bool>)(url => { opened = url; return true; }));
                Call(view, "SetMessages", new List<ChatMessage> {
                    new ChatMessage("a", "db2k", body, "23:11:00", false, false, source)
                }, new Dictionary<string, string>(), new Dictionary<string, string>(), true);
                IList blocks = (IList)Get(view, "blocks");
                var links = (IList)Get(blocks[0], "Links");
                Check(links.Count == 1, "listening line is one link, got " + links.Count);
                var bounds = (Rectangle)Get(links[0], "Bounds");
                var bubble = (Rectangle)Get(blocks[0], "Bubble");
                int scroll = Scroll(view);
                int originX = bubble.X + 14;
                int originY = bubble.Y - scroll + 10;
                var area = new Rectangle(originX, originY, Math.Max(1, bubble.Width - 28), bounds.Bottom + 3);
                using (var bitmap = Paint(view)) {
                    var words = WordSpans(bitmap, area);
                    var line = UnderlineSpan(bitmap, area);
                    Check(words.Count >= 6, "heard " + words.Count + " words");
                    Check(Math.Abs(line.X - words[3].X) <= 4, "underline starts at " + line.X + ", title at " + words[3].X);
                    Check(Math.Abs(line.Y - words[words.Count - 1].Y) <= 6, "underline ends at " + line.Y + ", text at " + words[words.Count - 1].Y);
                    int titleX = originX + words[3].X + 2;
                    int midY = originY + bounds.Y + Math.Max(1, bounds.Height / 2);
                    Call(view, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, titleX, midY, 0));
                    Check(opened == "https://osu.ppy.sh/b/123", "title click " + opened);
                    opened = null;
                    Call(view, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, originX + words[0].X + 1, midY, 0));
                    Check(opened == null, "prefix opened " + opened);
                }
            }
            string wrapped = "is listening to Aerosmith - Dream On with a long enough title that the line wraps";
            string wrappedSource = "is listening to [https://osu.ppy.sh/b/456 " + wrapped.Substring("is listening to ".Length) + "]";
            using (var view = View()) {
                view.Size = new Size(340, 420);
                view.GetType().GetField("ShowTranslate", Fields).SetValue(view, false);
                Call(view, "SetMessages", new List<ChatMessage> {
                    new ChatMessage("a", "db2k", wrapped, "23:11:00", false, false, wrappedSource)
                }, new Dictionary<string, string>(), new Dictionary<string, string>(), true);
                IList blocks = (IList)Get(view, "blocks");
                var links = (IList)Get(blocks[0], "Links");
                Check(links.Count >= 2, "wrapped listening line has " + links.Count + " rows");
                var first = (Rectangle)Get(links[0], "Bounds");
                var second = (Rectangle)Get(links[1], "Bounds");
                Check(second.Y >= first.Bottom - 1, "second row is below the first");
                Check(second.X <= 2, "wrapped row starts at " + second.X);
                var bubble = (Rectangle)Get(blocks[0], "Bubble");
                int scroll = Scroll(view);
                int originX = bubble.X + 14;
                int originY = bubble.Y - scroll + 10;
                using (var bitmap = Paint(view)) {
                    int top = UnderlineRow(bitmap, new Rectangle(originX, originY + first.Y, bubble.Width - 28, first.Height));
                    int bottom = UnderlineRow(bitmap, new Rectangle(originX, originY + second.Y, bubble.Width - 28, second.Height));
                    int firstStart = UnderlineStart(bitmap, originY + first.Y + top, originX, bubble.Width - 28);
                    int secondStart = UnderlineStart(bitmap, originY + second.Y + bottom, originX, bubble.Width - 28);
                    Check(Math.Abs(firstStart - first.X) <= 4, "first row underline " + firstStart + " bounds " + first.X);
                    Check(secondStart <= 4, "second row underline starts at " + secondStart);
                }
            }
        }
        static List<Point> WordSpans(Bitmap bitmap, Rectangle area) {
            var ink = new bool[Math.Max(0, area.Width)];
            for (int x = 0; x < ink.Length; x++) {
                for (int y = 0; y < area.Height && area.Y + y < bitmap.Height; y++) {
                    Color color = bitmap.GetPixel(area.X + x, area.Y + y);
                    if (color.R > 90 && color.G > 90 && color.B > 90 && !IsUnderline(color)) { ink[x] = true; break; }
                }
            }
            var words = new List<Point>();
            int start = -1;
            int gap = 0;
            for (int x = 0; x < ink.Length; x++) {
                if (ink[x]) { if (start < 0) start = x; gap = 0; }
                else if (start >= 0 && ++gap > 3) { words.Add(new Point(start, x - gap)); start = -1; gap = 0; }
            }
            if (start >= 0) words.Add(new Point(start, ink.Length - 1));
            return words;
        }
        static Point UnderlineSpan(Bitmap bitmap, Rectangle area) {
            int row = UnderlineRow(bitmap, area);
            Check(row >= 0, "listening underline missing");
            int min = -1;
            int max = -1;
            for (int x = 0; x < area.Width; x++) {
                if (!IsUnderline(bitmap.GetPixel(area.X + x, area.Y + row))) continue;
                if (min < 0) min = x;
                max = x;
            }
            return new Point(min, max);
        }
        static int UnderlineRow(Bitmap bitmap, Rectangle area) {
            int bestRow = -1;
            int best = 0;
            for (int y = 0; y < area.Height && area.Y + y < bitmap.Height; y++) {
                int count = 0;
                for (int x = 0; x < area.Width && area.X + x < bitmap.Width; x++) if (IsUnderline(bitmap.GetPixel(area.X + x, area.Y + y))) count++;
                if (count > best) { best = count; bestRow = y; }
            }
            return best > 8 ? bestRow : -1;
        }
        static int UnderlineStart(Bitmap bitmap, int y, int originX, int width) {
            for (int x = 0; x < width && originX + x < bitmap.Width; x++) if (IsUnderline(bitmap.GetPixel(originX + x, y))) return x;
            return -1;
        }
        static bool IsUnderline(Color color) {
            return color.R >= 120 && color.R <= 155 && color.G >= 130 && color.G <= 165 && color.B >= 145 && color.B <= 175 && color.B > color.R + 5;
        }
        static bool HasGold(Bitmap bitmap, Rectangle rect) {
            int right = Math.Min(bitmap.Width - 1, rect.Right - 1);
            int bottom = Math.Min(bitmap.Height - 1, rect.Bottom - 1);
            for (int y = Math.Max(0, rect.Top); y <= bottom; y++) {
                for (int x = Math.Max(0, rect.Left); x <= right; x++) {
                    if (x != rect.Left && x != right && y != rect.Top && y != bottom && x != rect.Left + 1 && y != rect.Top + 1) continue;
                    Color pixel = bitmap.GetPixel(x, y);
                    if (pixel.R > 160 && pixel.G > 120 && pixel.B < 180 && pixel.G > pixel.B + 30) return true;
                }
            }
            return false;
        }
        public static void RoundedControls() {
            Color sidebar = Color.FromArgb(18, 20, 26);
            Color page = Color.FromArgb(12, 13, 16);
            using (var host = new Panel { BackColor = sidebar, Size = new Size(420, 120) }) {
                var pill = Chrome("PillButton", new Size(200, 36));
                pill.GetType().GetProperty("Caption").SetValue(pill, "Translation preferences…", null);
                var filled = Chrome("PillButton", new Size(88, 34));
                filled.GetType().GetProperty("Filled").SetValue(filled, true, null);
                filled.GetType().GetProperty("Caption").SetValue(filled, "Send", null);
                var toggle = Chrome("SwitchBox", new Size(40, 22));
                var field = Chrome("LineField", new Size(180, 40));
                host.Controls.Add(pill);
                host.Controls.Add(filled);
                host.Controls.Add(toggle);
                host.Controls.Add(field);
                ExpectCorners(host, pill, "pill");
                ExpectCorners(host, toggle, "switch");
                ExpectCorners(host, field, "field");
                Color face = Paint(pill).GetPixel(16, pill.Height / 2);
                Check(Same(face, Color.FromArgb(30, 34, 43)), "pill face " + face);
                Color send = Paint(filled).GetPixel(14, filled.Height / 2);
                Check(Same(send, Color.FromArgb(225, 29, 72)), "filled face " + send);
                Color box = Paint(field).GetPixel(field.Width / 2, field.Height / 2);
                Check(Same(box, Color.FromArgb(30, 34, 43)), "field face " + box);
                host.BackColor = page;
                ExpectCorners(host, pill, "pill on page");
                ExpectCorners(host, filled, "filled on page");
            }
        }
        static Control Chrome(string name, Size size) {
            var type = typeof(MainForm).Assembly.GetType("Chatter.App." + name, true);
            var control = (Control)Activator.CreateInstance(type, true);
            control.Size = size;
            return control;
        }
        static void ExpectCorners(Control host, Control control, string name) {
            using (var bitmap = Paint(control)) {
                foreach (var point in new[] { new Point(0, 0), new Point(control.Width - 1, 0), new Point(0, control.Height - 1), new Point(2, 2) }) {
                    Color pixel = bitmap.GetPixel(point.X, point.Y);
                    Check(Same(pixel, host.BackColor), name + " corner " + point + " is " + pixel);
                }
            }
        }
        static Bitmap Paint(Control control) {
            var bitmap = new Bitmap(control.Width, control.Height);
            using (var graphics = Graphics.FromImage(bitmap)) {
                var args = new PaintEventArgs(graphics, new Rectangle(Point.Empty, control.Size));
                control.GetType().GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(control, new object[] { args });
            }
            return bitmap;
        }
        static bool Same(Color left, Color right) => left.R == right.R && left.G == right.G && left.B == right.B;
    }
}
