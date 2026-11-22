using System;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Chatter;
using Chatter.App;

namespace Chatter.Tests {
    static class SettingsTests {
        static object Get(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static void Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
        internal static void Embedded() {
            using (var form = new MainForm { DemoMode = true, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-10000, -10000) }) {
                form.Show(); form.LoadDemo();
                var draft = ((LineField)Get(form, "draft")).Input;
                draft.Text = "unfinished draft";
                int windows = Application.OpenForms.Count;
                var opening = Stopwatch.StartNew(); form.OpenSettings(); Application.DoEvents(); opening.Stop();
                Console.WriteLine("Settings open: " + opening.ElapsedMilliseconds + " ms");
                var view = (SettingsView)Get(form, "settingsView");
                Check(view.Parent == form && view.FindForm() == form && Application.OpenForms.Count == windows, "settings opened another window");
                Check(!((Control)Get(form, "transcript")).Visible && !((Control)Get(form, "composer")).Visible, "chat remained over the settings page");
                var viewport = view.Controls.OfType<SettingsScroller>().First();
                Check(viewport.Content.Visible && viewport.Content.Height > viewport.Height && viewport.Content.Controls.OfType<SettingsRow>().Count(r => r.Visible) >= 4, "translation rows were hidden or clipped");
                int translated = ((System.Collections.Generic.Dictionary<string, string>)Get(form, "translations")).Count;
                Check(view.SaveChanges() && ((System.Collections.Generic.Dictionary<string, string>)Get(form, "translations")).Count == translated, "saving unchanged settings cleared translations");
                form.Size = form.MinimumSize; Application.DoEvents();
                foreach (var row in viewport.Content.Controls.OfType<SettingsRow>().Where(r => r.Visible)) Check(row.Editor.Right <= row.Width && row.Editor.Bottom <= row.Height, "settings editor clipped at minimum window size");
                viewport.ScrollBy(-100000); Check(viewport.Content.Top < 0 && viewport.Content.Bottom <= viewport.Height, "settings scroll did not reach the last fields");
                Find<DarkPicker>(view, "Read messages in").SelectedIndex = 1;
                Call(form, "CloseEmbeddedSettings", true);
                Check(draft.Text == "unfinished draft" && ((Control)Get(form, "composer")).Visible && view.IsDisposed, "returning to chat lost the draft or settings resources");
                Check(((AppPreferences)Get(form, "preferences")).IncomingLanguage == "en", "back to chat applied unsaved settings");
                form.Close();
            }
        }
        internal static void Providers() {
            var original = new AppPreferences { Endpoint = Translator.DeepLFree, ApiKey = "fixture-deepl" };
            using (var form = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-10000, -10000), Size = new Size(900, 760) })
            using (var view = new SettingsView(original, "#russian") { Dock = DockStyle.Fill }) {
                form.Controls.Add(view); form.Show(); Application.DoEvents();
                var picker = Find<DarkPicker>(view, "Automatic provider");
                var key = Find<TextBox>(view, "API key"); var url = Find<TextBox>(view, "Server URL");
                Check(key.Visible && !url.Visible, "DeepL displayed a LibreTranslate URL");
                picker.SelectedIndex = 0; Check(!key.Visible && !url.Visible, "local engine displayed API credentials");
                picker.SelectedIndex = 4; Check(key.Text == "" && url.Visible && key.Visible, "credential leaked between engines");
                url.Text = "https://example.test/translate"; key.Text = "fixture-libre";
                var manual = Find<DarkPicker>(view, "Manual provider"); manual.SelectedIndex = 6;
                var endpoint = Find<TextBox>(view, "Chat API endpoint"); Check(endpoint.Visible, "chat API controls stayed hidden");
                AppPreferences? saved = null; view.Saved += value => saved = value;
                Check(view.SaveChanges() && saved != null && saved.Endpoint == url.Text && saved.ManualEndpoint == Translator.ChatApi && saved.ApiKey == "fixture-libre", "inline provider settings were not saved");
                Check(original.Endpoint == Translator.DeepLFree && original.ApiKey == "fixture-deepl", "editing changed original preferences");
                picker.SelectedIndex = 2; Check(key.Text == "fixture-deepl", "switching back lost the engine's draft key");
                view.SelectPage("Models"); view.SelectPage("Translation"); Check(url.Text == "https://example.test/translate", "section switching lost unsaved fields");
                form.Close();
            }
        }
        internal static void LazyPages() {
            using (var form = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-10000, -10000), Size = new Size(900, 760) })
            using (var view = new SettingsView(new AppPreferences(), "#osu") { Dock = DockStyle.Fill })
            using (var entered = new ManualResetEventSlim()) using (var release = new ManualResetEventSlim()) {
                form.Controls.Add(view); form.Show();
                int uiThread = Thread.CurrentThread.ManagedThreadId, scanThread = uiThread;
                var sections = view.Controls.OfType<SettingsScroller>().ToArray();
                Check(sections.Count(s => s.Content.Controls.Count > 0) == 1, "opening settings constructed unopened sections");
                view.ModelStatsReader = path => { scanThread = Thread.CurrentThread.ManagedThreadId; entered.Set(); if (scanThread != uiThread) release.Wait(5000); return "fixture inventory"; };
                try {
                    view.SelectPage("Models");
                    Check(entered.Wait(5000) && scanThread != uiThread, "model inventory blocked the UI thread");
                    view.SelectPage("Translation");
                    Check(sections.First(s => s.Name == "Translation").Visible, "settings could not navigate during an inventory scan");
                } finally { release.Set(); }
                form.Close();
            }
        }
        internal static void Render(string directory) {
            Console.WriteLine("Starting settings render");
            Directory.CreateDirectory(directory);
            using (var form = new MainForm { DemoMode = true, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-10000, -10000) }) {
                Console.WriteLine("Showing preview"); form.Show(); form.LoadDemo(); Console.WriteLine("Opening settings"); form.OpenSettings();
                var view = (SettingsView)Get(form, "settingsView");
                foreach (var section in new[] { "Translation", "Models", "In-game chat", "Overlay", "Application", "About", "Narrow", "DeepL" }) {
                    Console.WriteLine("Rendering " + section);
                    form.ClientSize = section == "Narrow" ? new Size(980, 640) : new Size(1100, 720);
                    view.SelectPage(section == "Narrow" || section == "DeepL" ? "Translation" : section);
                    if (section == "DeepL") Find<DarkPicker>(view, "Automatic provider").SelectedIndex = 2;
                    Application.DoEvents(); form.Refresh();
                    using (var full = new Bitmap(form.Width, form.Height)) {
                        form.DrawToBitmap(full, new Rectangle(Point.Empty, full.Size));
                        Point origin = form.PointToScreen(Point.Empty);
                        using (var client = full.Clone(new Rectangle(origin.X - form.Left, origin.Y - form.Top, form.ClientSize.Width, form.ClientSize.Height), full.PixelFormat)) client.Save(Path.Combine(directory, section + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                Console.WriteLine("Closing settings preview"); form.Close();
            }
        }
        static T Find<T>(Control root, string name) where T : Control {
            foreach (Control child in root.Controls) { if (child is T match && child.Name == name) return match; try { return Find<T>(child, name); } catch (InvalidOperationException) { } }
            throw new InvalidOperationException("Missing control: " + name);
        }
    }
}
