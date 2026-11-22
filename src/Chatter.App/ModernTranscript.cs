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
        bool ready;
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
                        VisibleIds.Clear(); foreach (object id in (System.Collections.IList)data["ids"]) if (id is string value) VisibleIds.Add(value);
                        VisibleMessagesChanged?.Invoke(); return;
                    }
                    Action?.Invoke(kind, data.TryGetValue("id", out object message) ? message as string ?? "" : "", data.TryGetValue("value", out object val) ? val as string ?? "" : "");
                } catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is KeyNotFoundException || ex is InvalidCastException) { }
            };
            browser.NavigateToString(Document);
        }
        public void UpdateRows(object rows, bool stick, bool showTranslate, int font, string empty, IDictionary<string, string>? avatars = null) {
            // Avatars are deduplicated by source image; a bounded GIF is not repeated in every row.
            string json = new JavaScriptSerializer { MaxJsonLength = 64000000 }.Serialize(new { rows, stick, showTranslate, font, empty, avatars });
            if (json == latest) return; latest = json; Flush();
        }
        async void Flush() {
            if (!ready || IsDisposed || latest == sent) return;
            sent = latest;
            try { await browser.ExecuteScriptAsync("window.update(" + sent + ")"); } catch (Exception ex) when (ex is InvalidOperationException || ex is ObjectDisposedException || ex is COMException) { }
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
        const string Document = @"<!doctype html><html><head><meta charset='utf-8'><meta http-equiv='Content-Security-Policy' content=""default-src 'none'; img-src data:; style-src 'unsafe-inline'; script-src 'unsafe-inline'""><style>
*{box-sizing:border-box}html,body{margin:0;height:100%;background:#0c0d10;color:#f3f4f6;font:14px 'Segoe UI','Segoe UI Emoji',sans-serif}body{overflow:hidden}#log{height:100%;overflow:auto;padding:20px 24px 65px;scrollbar-color:#414650 #0c0d10;overflow-anchor:none}article{display:flex;gap:10px;margin:0 0 18px;align-items:flex-end}article.own{justify-content:flex-end}article.system{justify-content:center;color:#78808e;font-size:12px}.avatar{width:30px;height:30px;border-radius:50%;object-fit:cover;background:#262c37;display:grid;place-items:center;flex:none;color:#b0bac9}.avatar.gold{border:2px solid #e8c47a}.content{max-width:80%;min-width:80px}.sender{color:#a0a9b9;font-size:12px;margin:0 0 5px 3px;background:none;border:0;padding:0;cursor:pointer}.bubble{border:1px solid #282e39;border-radius:12px;background:#1c2028;padding:11px 14px;overflow-wrap:anywhere;line-height:1.5}.own .bubble{background:#4a1c2a;border-color:#672d41}.body,.translation{white-space:pre-wrap;unicode-bidi:plaintext}.translation{border-top:1px solid #ffffff24;margin-top:10px;padding-top:8px;color:#d6dbe4}.meta{display:flex;gap:10px;justify-content:space-between;color:#929baa;font-size:11px;margin-top:7px}.error{color:#fecdd3;font-size:12px;margin-top:8px}a{color:inherit;text-decoration-color:#8b9bac;text-underline-offset:3px}a:hover{color:#fff;text-decoration-color:#e11d48}button{font:inherit;cursor:pointer}.translate{background:none;border:0;color:#a7b0c0;padding:0;font-size:11px}.state{color:#7c8798}#latest{position:absolute;right:20px;bottom:16px;border:1px solid #4c5360;border-radius:20px;background:#252a34;color:#fff;padding:8px 15px;box-shadow:0 4px 18px #0007}#search{position:absolute;top:8px;right:20px;z-index:2;padding:8px;border:1px solid #454d5c;border-radius:8px;background:#181b22}#search input{background:#1e222b;color:white;border:0;outline:0;font:inherit}#menu{position:absolute;z-index:3;background:#1e222b;border:1px solid #454d5c;border-radius:9px;padding:5px;box-shadow:0 8px 30px #0008}#menu button{display:block;text-align:left;background:none;border:0;color:#eee;padding:8px 14px;width:100%}#menu button:hover{background:#353b48}#empty{text-align:center;color:#838d9f;padding:60px 20px}small{color:#8893a5}article.action .body{font-style:italic} [hidden]{display:none!important}
</style></head><body><div id='log'><div id='empty'></div></div><button id='latest' hidden>Jump to latest ↓</button><div id='search' hidden><input placeholder='Find in this conversation'><button>×</button></div><div id='menu' hidden></div><script>
const log=document.querySelector('#log'),jump=document.querySelector('#latest'),menu=document.querySelector('#menu');let rows=new Map(),scheduled=false;
function post(action,id='',value=''){chrome.webview.postMessage({action,id,value})}function visible(){if(scheduled)return;scheduled=true;requestAnimationFrame(()=>{scheduled=false;chrome.webview.postMessage({action:'visible',ids:[...log.querySelectorAll('article')].filter(e=>e.offsetTop+e.offsetHeight>=log.scrollTop&&e.offsetTop<=log.scrollTop+log.clientHeight).map(e=>e.dataset.id)})})}
function element(tag,cls,text){const e=document.createElement(tag);if(cls)e.className=cls;if(text!==undefined)e.textContent=text;return e}
function linked(e,text,links){let at=0;for(const l of links||[]){if(l.Index<at)continue;e.append(document.createTextNode(text.slice(at,l.Index)));const a=element('a','',text.slice(l.Index,l.Index+l.Length));a.href=l.Url;a.title=l.Url;a.onclick=v=>{v.preventDefault();post('open','',l.Url)};e.append(a);at=l.Index+l.Length}e.append(document.createTextNode(text.slice(at)))}
window.update=data=>{let bottom=log.scrollHeight-log.scrollTop-log.clientHeight<35;const anchor=[...log.querySelectorAll('article')].find(e=>e.offsetTop+e.offsetHeight>=log.scrollTop);const aid=anchor?.dataset.id,offset=anchor?anchor.offsetTop-log.scrollTop:0;let next=new Map();document.documentElement.style.fontSize=data.font+'px';document.body.style.fontSize=data.font+'px';
 for(const r of data.rows){let e=rows.get(r.id);if(!e)e=element('article');e.dataset.id=r.id;const signature=JSON.stringify([r,data.showTranslate]);if(e.dataset.signature!==signature){e.dataset.signature=signature;e.replaceChildren();e.className=r.system?'system':r.own?'own':'';if(r.action)e.classList.add('action');if(r.system)e.append(element('span','',r.text));else{if(!r.own){const a=r.avatar?element('img','avatar'+(r.supporter?' gold':'')):element('span','avatar'+(r.supporter?' gold':''),r.sender[0]||'?');if(r.avatar)a.src=data.avatars?.[r.avatar]||r.avatar;a.onclick=()=>post('profile',r.id);e.append(a)}const content=element('div','content');const sender=element('button','sender',r.sender);sender.onclick=()=>post('profile',r.id);content.append(sender);const bubble=element('div','bubble'),body=element('div','body');body.dir='auto';linked(body,r.text,r.links);bubble.append(body);if(r.translated&&r.translated!==r.text){const tr=element('div','translation',r.translated);tr.dir='auto';bubble.append(tr)}if(r.error)bubble.append(element('div','error',r.error));const meta=element('div','meta');if(data.showTranslate&&!r.own){const b=element('button','translate',r.translated?'Translate again':'Translate');b.onclick=()=>post('translate',r.id);meta.append(b)}meta.append(element('span','state',r.state||''));meta.append(element('span','',r.time.slice(0,5)));bubble.append(meta);content.append(bubble);e.append(content)}e.oncontextmenu=v=>{v.preventDefault();menu.replaceChildren();for(const [label,action,value] of [['Copy message','copy',r.text],['Copy translation','copy',r.translated],['Copy link','copy',r.links?.[0]?.Url],['Translate again with context','translate',''],['Translate to…','translate-to',''],['Open osu! profile','profile','']]){if(value===undefined||value===null)continue;const b=element('button','',label);b.onclick=()=>{post(action,r.id,value||'');menu.hidden=true};menu.append(b)}menu.style.left=Math.min(v.clientX,innerWidth-245)+'px';menu.style.top=Math.min(v.clientY,innerHeight-230)+'px';menu.hidden=false}}
 next.set(r.id,e)}
 for(const [id,e]of rows)if(!next.has(id))e.remove();let preceding=null;for(const e of next.values()){const expected=preceding?preceding.nextSibling:log.firstChild;if(expected!==e)log.insertBefore(e,expected);preceding=e}rows=next;let empty=document.querySelector('#empty');if(!data.rows.length){if(!empty){empty=element('div');empty.id='empty';log.append(empty)}empty.textContent=data.empty||'No messages yet'}else empty?.remove();
 if(data.stick||bottom){log.scrollTop=log.scrollHeight;jump.hidden=true}else{const restored=rows.get(aid);if(restored)log.scrollTop=restored.offsetTop-offset;jump.hidden=false}visible()};
log.onscroll=()=>{jump.hidden=log.scrollHeight-log.scrollTop-log.clientHeight<35;visible()};jump.onclick=()=>{log.scrollTop=log.scrollHeight;jump.hidden=true};document.onclick=e=>{if(!menu.contains(e.target))menu.hidden=true};window.showSearch=()=>{const s=document.querySelector('#search');s.hidden=false;s.querySelector('input').focus()};document.querySelector('#search button').onclick=()=>document.querySelector('#search').hidden=true;document.querySelector('#search input').onkeydown=e=>{if(e.key==='Enter'){e.preventDefault();window.find(e.target.value,false,e.shiftKey,true)}if(e.key==='Escape')document.querySelector('#search').hidden=true};post('ready');
</script></body></html>";
    }
}
