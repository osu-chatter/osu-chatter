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
    }
}
