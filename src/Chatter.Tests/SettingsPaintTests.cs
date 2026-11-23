using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Chatter.App;

namespace Chatter.Tests {
    // Opt-in: compares actual on-screen pixels with a subsequent complete repaint.
    // DrawToBitmap alone cannot detect stale pixels left by scrolling child HWNDs.
    static class SettingsPaintTests {
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        static object Get(object target, string name) => target.GetType().GetField(name, Private)!.GetValue(target);
        static void Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private)!.Invoke(target, args);
        internal static void Run(string directory) {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Directory.CreateDirectory(directory);
            using (var form = new MainForm { DemoMode = true, TopMost = true, StartPosition = FormStartPosition.CenterScreen })
            using (var timeout = new Timer { Interval = 45000 }) {
                Exception? failure = null;
                timeout.Tick += delegate { failure = new TimeoutException("Retained-pixel checks timed out"); form.Close(); };
                form.Shown += async delegate {
                    try {
                        form.LoadOffline(); form.EnableReleasePreview();
                        var host = (TranscriptView)Get(form, "transcript"); var watch = Stopwatch.StartNew();
                        while (host.ModernStatus.Length == 0) { if (watch.ElapsedMilliseconds > 15000) throw new TimeoutException("WebView2 initialization"); await Task.Delay(20); }
                        await Task.Delay(250);
                        form.OpenSettings();
                        var settings = (SettingsView)Get(form, "settingsView");
                        settings.ModelStatsReader = _ => "Installed language pairs:\n" + string.Join("\n", Enumerable.Range(0, 32).Select(i => "en → pl · 81 MB · fixture " + i));
                        settings.SelectPage("Models"); await Task.Delay(150);
                        var models = settings.Controls.OfType<SettingsScroller>().Single(s => s.Name == "Models");
                        long scrollTicks = 0; int scrolls = 0;
                        for (int i = 0; i < 12; i++) {
                            Scroll(i % 3 == 0 ? 340 : -230);
                            for (int step = 0; step < 8; step++) { Scroll(i % 2 == 0 ? -12 : 12); await Task.Delay(8); }
                            await Task.Delay(40);
                            await Compare("scroll-" + i);
                        }
                        for (int i = 0; i < 3; i++) {
                            settings.SelectPage("Translation"); await Task.Delay(20); await Compare("section-" + i);
                            settings.SelectPage("Models"); await Task.Delay(20); await Compare("models-" + i);
                        }
                        Call(form, "CloseEmbeddedSettings", true); await Task.Delay(100); await Compare("closed-settings");
                        Console.WriteLine(scrolls + " scroll redraws: " + (scrollTicks * 1000.0 / Stopwatch.Frequency).ToString("F1") + " ms total");
                        Console.WriteLine("Retained pixels matched a fresh repaint after scrolling, section changes and returning to chat");
                        void Scroll(int delta) { long start = Stopwatch.GetTimestamp(); models.ScrollBy(delta); scrollTicks += Stopwatch.GetTimestamp() - start; scrolls++; }
                        async Task Compare(string name) {
                            Point pointerBefore = form.PointToClient(Cursor.Position);
                            using (var before = Capture(form)) {
                                form.Invalidate(true); form.Update(); await Task.Delay(40);
                                using (var after = Capture(form)) {
                                    Point pointerAfter = form.PointToClient(Cursor.Position);
                                    int[] beforePixels = Pixels(before), afterPixels = Pixels(after);
                                    int changed = 0;
                                    // Skip window borders; no clocks, caret or live chat in this fixture.
                                    for (int y = 0; y < before.Height; y++) for (int x = 252; x < before.Width; x++)
                                        if (!NearPointer(x, y, pointerBefore) && !NearPointer(x, y, pointerAfter) && beforePixels[y * before.Width + x] != afterPixels[y * before.Width + x]) changed++;
                                    Console.WriteLine(name + ": " + changed + " changed pixels");
                                    if (changed > 30) {
                                        before.Save(Path.Combine(directory, name + "-retained.png"), ImageFormat.Png);
                                        after.Save(Path.Combine(directory, name + "-repainted.png"), ImageFormat.Png);
                                        throw new InvalidOperationException(name + " left " + changed + " stale pixels");
                                    }
                                }
                            }
                        }
                    } catch (Exception ex) { failure = ex; }
                    finally { timeout.Stop(); form.Close(); }
                };
                timeout.Start(); Application.Run(form); if (failure != null) throw failure;
            }
        }
        static Bitmap Capture(Control control) {
            var bitmap = new Bitmap(control.ClientSize.Width, control.ClientSize.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(control.PointToScreen(Point.Empty), Point.Empty, bitmap.Size);
            return bitmap;
        }
        static bool NearPointer(int x, int y, Point pointer) => Math.Abs(x - pointer.X) < 100 && Math.Abs(y - pointer.Y) < 100;
        static int[] Pixels(Bitmap bitmap) {
            var data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try { var pixels = new int[bitmap.Width * bitmap.Height]; Marshal.Copy(data.Scan0, pixels, 0, pixels.Length); return pixels; }
            finally { bitmap.UnlockBits(data); }
        }
    }
}
