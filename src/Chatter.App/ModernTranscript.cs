using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Chatter.App {
    sealed class ModernTranscript : UserControl {
        readonly WebView2 browser = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Palette.Bg };
        string latest = "", sent = "";
        bool ready, flushing, pendingStick;
        internal string DocumentStatus { get; private set; } = "Not navigated";
        public readonly HashSet<string> VisibleIds = new HashSet<string>();
        public event Action<string, string, string>? Action;
        public event System.Action? VisibleMessagesChanged;
        public ModernTranscript() { BackColor = Palette.Bg; Dock = DockStyle.Fill; Controls.Add(browser); }
        public async Task Initialize() {
            PrepareLoader();
            string data = Environment.GetEnvironmentVariable("CHATTER_WEBVIEW_ROOT") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "osu-chatter", "webview");
            var environment = await CoreWebView2Environment.CreateAsync(null, data);
            var options = environment.CreateCoreWebView2ControllerOptions(); options.ProfileName = "Chatter"; options.IsInPrivateModeEnabled = true;
            await browser.EnsureCoreWebView2Async(environment, options);
            var settings = browser.CoreWebView2.Settings;
            await browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("window.transcriptErrors=[];window.addEventListener('error',e=>window.transcriptErrors.push(e.message));");
            settings.AreDefaultContextMenusEnabled = false; settings.IsStatusBarEnabled = false; settings.AreDevToolsEnabled = false;
            settings.IsGeneralAutofillEnabled = false; settings.IsPasswordAutosaveEnabled = false;
            string trustedDocument = "data:text/html;charset=utf-8;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(Document));
            browser.CoreWebView2.NavigationStarting += (sender, e) => {
                e.Cancel = e.Uri != "about:blank" && e.Uri != trustedDocument;
                DocumentStatus = e.Cancel ? "External navigation blocked" : "Transcript document loading";
            };
            browser.CoreWebView2.NavigationCompleted += (sender, e) => { DocumentStatus += " · " + e.IsSuccess + " · " + e.WebErrorStatus; };
            browser.CoreWebView2.NewWindowRequested += (sender, e) => { e.Handled = true; };
            browser.CoreWebView2.PermissionRequested += (sender, e) => e.State = CoreWebView2PermissionState.Deny;
            browser.CoreWebView2.WebMessageReceived += (sender, e) => {
                try {
                    var data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(e.WebMessageAsJson);
                    string? kind = data["action"] as string ?? "";
                    if (kind == "ready") { ready = true; Flush(); return; }
                    if (kind == "visible") {
                        var next = new HashSet<string>(); foreach (object id in (System.Collections.IList)data["ids"]) if (id is string value) next.Add(value);
                        if (VisibleIds.SetEquals(next)) return;
                        VisibleIds.Clear(); VisibleIds.UnionWith(next);
                        VisibleMessagesChanged?.Invoke(); return;
                    }
                    Action?.Invoke(kind, data.TryGetValue("id", out object message) ? message as string ?? "" : "", data.TryGetValue("value", out object val) ? val as string ?? "" : "");
                } catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is KeyNotFoundException || ex is InvalidCastException) { }
            };
            browser.NavigateToString(Document);
        }
        public void UpdateRows(object rows, bool stick, bool showTranslate, int font, string empty, IDictionary<string, string>? avatars = null) {
            // Avatars are deduplicated by source image; a bounded GIF is not repeated in every row.
            string json = new JavaScriptSerializer { MaxJsonLength = 64000000 }.Serialize(new { rows, showTranslate, font, empty, avatars });
            pendingStick |= stick;
            if (json == latest && !pendingStick) return; latest = json; Flush();
        }
        async void Flush() {
            if (!ready || IsDisposed || flushing) return;
            flushing = true;
            try {
                // Keep one browser update in flight; bursts of translations retain only
                // the newest snapshot while preserving an explicit conversation change.
                while (!IsDisposed && (latest != sent || pendingStick)) {
                    string update = latest; bool stick = pendingStick; pendingStick = false;
                    await browser.ExecuteScriptAsync("window.update(Object.assign(" + update + ",{stick:" + (stick ? "true" : "false") + "}))");
                    sent = update;
                }
            } catch (Exception ex) when (ex is InvalidOperationException || ex is ObjectDisposedException || ex is COMException) { }
            finally { flushing = false; }
        }
        public void Find() { if (ready) _ = browser.ExecuteScriptAsync("window.showSearch()"); }
        static readonly object loaderGate = new object();
        static bool loaded;
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibrary(string path);
        static void PrepareLoader() {
            lock (loaderGate) {
                if (loaded) return;
                using (var source = typeof(MainForm).Assembly.GetManifestResourceStream("Chatter.Native.WebView2Loader.dll")) {
                    if (source == null) throw new InvalidOperationException("WebView2 loader is missing.");
                    byte[] bytes; using (var stream = new MemoryStream()) { source.CopyTo(stream); bytes = stream.ToArray(); }
                    string hash; using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
                    string directory = Path.Combine(Path.GetTempPath(), "osu-chatter-webview", hash); Directory.CreateDirectory(directory);
                    string path = Path.Combine(directory, "WebView2Loader.dll");
                    if (!File.Exists(path)) File.WriteAllBytes(path, bytes);
                    byte[] existing = File.ReadAllBytes(path); using (var sha = SHA256.Create()) if (BitConverter.ToString(sha.ComputeHash(existing)).Replace("-", "") != hash) throw new InvalidOperationException("WebView2 loader verification failed.");
                    if (LoadLibrary(path) == IntPtr.Zero) throw new InvalidOperationException("WebView2 loader did not load."); loaded = true;
                }
            }
        }
        static readonly string Document = ReadDocument();
        static string ReadDocument() {
            using (var stream = typeof(ModernTranscript).Assembly.GetManifestResourceStream("Chatter.App.Transcript.html"))
            using (var reader = new StreamReader(stream ?? throw new InvalidOperationException("Transcript document is missing."))) return reader.ReadToEnd();
        }
    }
}
