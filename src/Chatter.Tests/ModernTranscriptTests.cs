using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Chatter.App;
using Microsoft.Web.WebView2.WinForms;

namespace Chatter.Tests {
    static class ModernTranscriptTests {
        sealed class OffscreenForm : Form { protected override bool ShowWithoutActivation => true; }
        static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
        internal static void Run() {
            using(var form=new OffscreenForm { ShowInTaskbar=false, StartPosition=FormStartPosition.Manual, Location=new Point(-10000,-10000), Size=new Size(560,420) })
            using(var transcript=new ModernTranscript()) {
                Exception? failure = null;
                var timeout = new System.Windows.Forms.Timer { Interval = 45000 };
                timeout.Tick += delegate { failure = new TimeoutException("WebView2 integration timed out"); form.Close(); };
                form.Shown += async delegate {
                  try { await Verify(); }
                  catch(Exception ex) { Console.WriteLine(transcript.DocumentStatus); failure = ex; }
                  finally { timeout.Stop(); form.Close(); }
                };
                form.Controls.Add(transcript); timeout.Start(); Application.Run(form); timeout.Dispose();
                if(failure != null) throw failure;
                async Task Verify() {
                Console.WriteLine("Initializing WebView2"); await transcript.Initialize();
                var field=typeof(ModernTranscript).GetField("browser",BindingFlags.NonPublic|BindingFlags.Instance);
                var browser=(WebView2)field.GetValue(transcript);
                async Task<string> Eval(string code) => await browser.ExecuteScriptAsync(code);
                async Task Wait(string script, string expected) { var watch=Stopwatch.StartNew(); while(await Eval(script)!=expected) { if(watch.ElapsedMilliseconds>10000) throw new Exception("Script did not become ready: "+await Eval("JSON.stringify({url:location.href,state:document.readyState,errors:window.transcriptErrors,html:document.documentElement.outerHTML.slice(0,300)})")); await Task.Delay(30); } }
                Console.WriteLine("Waiting for transcript document");
                await Wait("typeof window.update", "\"function\"");
                object Row(int i,string translated="") => new {id="row"+i,sender="Fixture",text=i==2?"😀 مرحبا <script>window.bad=1</script> https://osu.ppy.sh/":"message "+i,time="12:34:00",own=false,system=false,action=false,translated,error="",state="Ready",avatar="",supporter=false,links=i==2?new[]{new{Index=46,Length=19,Url="https://osu.ppy.sh/"}}:new object[0]};
                // Match real UTF-16 link offsets, including the emoji surrogate pair.
                var text="😀 مرحبا <script>window.bad=1</script> https://osu.ppy.sh/";
                object Special() => new {id="row2",sender="Fixture",text,time="12:34:00",own=false,system=false,action=false,translated="",error="",state="Ready",avatar="",supporter=false,links=new[]{new{Index=text.IndexOf("https",StringComparison.Ordinal),Length="https://osu.ppy.sh/".Length,Url="https://osu.ppy.sh/"}}};
                var rows=new object[40]; for(int i=0;i<rows.Length;i++) rows[i]=i==2?Special():Row(i);
                transcript.UpdateRows(rows,true,true,14,""); await Wait("document.querySelectorAll('article').length", "40");
                Check(await Eval("typeof window.bad")=="\"undefined\"","chat markup executed script");
                Check(new JavaScriptSerializer().Deserialize<string>(await Eval("document.querySelector('[data-id=row2] .body').textContent"))==text,"emoji/RTL/markup text changed");
                Check(await Eval("document.querySelector('[data-id=row2] a').textContent")=="\"https://osu.ppy.sh/\"","UTF-16 link range changed");
                Check(await Eval("document.querySelector('[data-id=row2] .body').dir")=="\"auto\"","RTL auto direction missing");
                await Eval("log.scrollTop=400;window.saved=log.scrollTop;window.kept=document.querySelector('[data-id=row5]');let t=kept.querySelector('.body').firstChild;let range=document.createRange();range.setStart(t,0);range.setEnd(t,4);getSelection().removeAllRanges();getSelection().addRange(range)");
                rows[25]=Row(25,"new translation"); transcript.UpdateRows(rows,false,true,14,"");
                await Wait("document.querySelector('[data-id=row25] .translation')!==null", "true");
                Check(await Eval("kept===document.querySelector('[data-id=row5]') && getSelection().toString()==='mess' && Math.abs(log.scrollTop-saved)<2")=="true","translation refresh changed node, selection or scroll");
                Console.WriteLine("WebView2 literal text, RTL, emoji link offsets, keyed refresh, selection and scroll verified");
                }
            }
        }
    }
}
