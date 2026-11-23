using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using Chatter.App;
using Microsoft.Web.WebView2.WinForms;

namespace Chatter.Tests {
    static class SettingsTransitionTests {
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        static object Get(object target, string name) => target.GetType().GetField(name, Private)!.GetValue(target);
        static void Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private)!.Invoke(target, args);
        static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
        internal static void Run() {
            using (var form = new MainForm { DemoMode = true, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-10000, -10000) })
            using (var refresh = new Timer { Interval = 250 })
            using (var timeout = new Timer { Interval = 45000 }) {
                Exception? failure = null;
                timeout.Tick += delegate { failure = new TimeoutException("Settings transitions timed out"); form.Close(); };
                refresh.Tick += delegate { Call(form, "Render"); };
                form.Shown += async delegate {
                    try {
                        form.LoadOffline(); form.EnableReleasePreview();
                        var host = (TranscriptView)Get(form, "transcript");
                        var watch = Stopwatch.StartNew();
                        while (host.ModernStatus.Length == 0) { Check(watch.ElapsedMilliseconds < 15000, "WebView2 did not initialize"); await Task.Delay(20); }
                        Check(host.ModernStatus.StartsWith("Color emoji"), host.ModernStatus);
                        var modern = (ModernTranscript)Get(host, "modern");
                        var browser = (WebView2)Get(modern, "browser");
                        while (await browser.ExecuteScriptAsync("typeof window.update") != "\"function\"") await Task.Delay(20);
                        refresh.Start();
                        int overlapping = 0;
                        SettingsView? activeSettings = null;
                        host.VisibleChanged += delegate { if (host.Visible && activeSettings?.Visible == true) overlapping++; };
                        for (int i = 0; i < 8; i++) {
                            watch.Restart(); form.OpenSettings(); long open = watch.ElapsedMilliseconds;
                            var settings = (SettingsView)Get(form, "settingsView");
                            activeSettings = settings;
                            await Task.Delay(50);
                            Call(form, "Render"); Call(form, "Render");
                            Check(settings.Visible && !host.Visible && !browser.Visible, "WebView2 remained visible over Settings");
                            settings.SelectPage(i % 2 == 0 ? "Application" : "Translation");
                            watch.Restart(); Call(form, "CloseEmbeddedSettings", true); long close = watch.ElapsedMilliseconds;
                            await Task.Delay(50);
                            Check(!form.Controls.Contains(settings) && host.Visible && browser.Visible, "settings did not yield the content area to chat");
                            Console.WriteLine("Full-window settings cycle " + i + ": open " + open + " ms, close " + close + " ms");
                            if (i == 3) form.LoadDemo();
                        }
                        Console.WriteLine("Overlapping content transitions: " + overlapping);
                        Check(overlapping == 0, "chat became visible before Settings was hidden");
                    } catch (Exception ex) { failure = ex; }
                    finally { refresh.Stop(); timeout.Stop(); form.Close(); }
                };
                timeout.Start(); Application.Run(form);
                if (failure != null) throw failure;
            }
        }
    }
}
