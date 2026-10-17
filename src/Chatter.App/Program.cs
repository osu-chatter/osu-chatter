using System;
using System.Drawing;
using System.Windows.Forms;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Chatter.App {
    static class Program {
        [STAThread]
        static void Main(string[] args) {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveLibrary;
            Run(args);
        }

        static Assembly ResolveLibrary(object sender, ResolveEventArgs args) {
            string name = new AssemblyName(args.Name).Name;
            if (name != "Chatter.Core" && name != "Chatter.Bridge") return null;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Chatter.Bundle." + name + ".dll")) {
                if (stream == null) return null;
                using (var bytes = new MemoryStream()) { stream.CopyTo(bytes); return Assembly.Load(bytes.ToArray()); }
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Run(string[] args) {
            if (IntPtr.Size != 4) {
                MessageBox.Show("chatter has to run as a 32-bit process.", "chatter");
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args != null && args.Length > 1 && (args[0] == "--shot-setup" || args[0] == "--shot-deepl")) {
                using (var setup = new SetupForm(new Chatter.AppPreferences {
                    Endpoint = args[0] == "--shot-deepl" ? Chatter.Translator.DeepLFree : Chatter.ChatRules.LocalTranslator,
                    AutoTranslateIncoming = args[0] != "--shot-deepl"
                }, true, false)) {
                    setup.StartPosition = FormStartPosition.Manual;
                    setup.Location = new Point(-2400, -2400);
                    setup.Show();
                    Application.DoEvents();
                    using (var bitmap = new Bitmap(setup.Width, setup.Height)) {
                        setup.DrawToBitmap(bitmap, new Rectangle(0, 0, setup.Width, setup.Height));
                        bitmap.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                return;
            }
            if (args != null && args.Length > 0 && (args[0] == "--shot" || args[0] == "--shot-empty" || args[0] == "--shot-settings")) {
                string path = args.Length > 1 ? args[1] : System.IO.Path.Combine(System.IO.Path.GetTempPath(), "chatter-shot.png");
                Capture(path, args[0]);
                return;
            }
            if (args != null && args.Length == 1 && args[0] == "--smoke") {
                using (var form = new MainForm()) {
                    IntPtr handle = form.Handle;
                    if (handle == IntPtr.Zero) Environment.Exit(1);
                    new Chatter.BridgeClient(Chatter.ChatService.DefaultBridgePath()).PrepareLibrary();
                }
                return;
            }
            using (var instance = new Mutex(true, @"Local\osu-chatter-app", out bool first)) {
                if (!first) { MessageBox.Show("chatter is already running.", "chatter"); return; }
                try {
                    if (!Chatter.PreferencesStore.TryLoad(out var preferences)) {
                        using (var setup = new SetupForm(preferences)) {
                            if (setup.ShowDialog() != DialogResult.OK) return;
                            preferences = setup.Result;
                        }
                    }
                    Application.Run(new MainForm(preferences));
                }
                finally { instance.ReleaseMutex(); }
            }
        }

        static void Capture(string path, string mode) {
            using (var form = new MainForm()) {
                form.DemoMode = true;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-2400, -2400);
                form.ClientSize = new Size(1100, 720);
                form.Show();
                if (mode == "--shot-empty") form.LoadOffline();
                else form.LoadDemo();
                if (mode == "--shot-settings") form.OpenSettings();
                Application.DoEvents();
                form.Refresh();
                Point origin = form.PointToScreen(Point.Empty);
                int trimX = origin.X - form.Left;
                int trimY = origin.Y - form.Top;
                using (var full = new Bitmap(form.Width, form.Height)) {
                    form.DrawToBitmap(full, new Rectangle(0, 0, form.Width, form.Height));
                    var client = new Rectangle(trimX, trimY, form.ClientSize.Width, form.ClientSize.Height);
                    if (client.Right > full.Width) client.Width = full.Width - client.X;
                    if (client.Bottom > full.Height) client.Height = full.Height - client.Y;
                    using (var bitmap = full.Clone(client, full.PixelFormat))
                        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }
            }
        }
    }
}
