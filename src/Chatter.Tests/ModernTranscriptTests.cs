using System;
using System.Collections;
using System.Collections.Generic;
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
                async Task Wait(string script, string expected) { var watch=Stopwatch.StartNew(); while(await Eval(script)!=expected) { if(watch.ElapsedMilliseconds>10000) throw new Exception("Script did not become ready: "+script+" expected "+expected+" got "+await Eval(script)+" "+await Eval("JSON.stringify({url:location.href,state:document.readyState,errors:window.transcriptErrors})")); await Task.Delay(30); } }
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
                await Eval("window.bodyKept=kept.querySelector('.body');window.avatarKept=kept.querySelector('.avatar')");
                rows[5]=Row(5,"translation on the selected message"); transcript.UpdateRows(rows,false,true,14,"");
                await Wait("document.querySelector('[data-id=row5] .translation').textContent", "\"translation on the selected message\"");
                Check(await Eval("bodyKept===kept.querySelector('.body') && avatarKept===kept.querySelector('.avatar') && getSelection().toString()==='mess'")=="true","translation replaced original text, selection or avatar");
                Console.WriteLine("WebView2 literal text, RTL, emoji link offsets, keyed refresh, selection and scroll verified");

                // Exercise the real WinForms host too: its old Relayout path sent a
                // forced-bottom update before the otherwise-correct browser update.
                using (var host = new TranscriptView { Dock = DockStyle.Fill }) {
                    form.Controls.Add(host); host.BringToFront(); host.UseModern(true,14);
                    var until=Stopwatch.StartNew();
                    while(host.ModernStatus.Length==0) { if(until.ElapsedMilliseconds>15000) throw new TimeoutException("host initialization"); await Task.Delay(30); }
                    Check(host.ModernStatus.StartsWith("Color emoji"),host.ModernStatus);
                    var hosted=(ModernTranscript)typeof(TranscriptView).GetField("modern",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(host);
                    browser=(WebView2)field.GetValue(hosted); await Wait("typeof window.update","\"function\"");
                    var messages=new List<Chatter.ChatMessage>();
                    for(int i=0;i<200;i++) messages.Add(new Chatter.ChatMessage("m"+i,"Fixture","Message "+i+" with a link https://osu.ppy.sh/ and readable text.","12:30:00",false,false));
                    var translated=new Dictionary<string,string>(); var errors=new Dictionary<string,string>();
                    host.SetMessages(messages,translated,errors,true); await Wait("document.querySelectorAll('article').length","200");
                    await Eval("log.scrollTop=1000;window.savedAnchor=anchor();window.updateCount=0;window.originalUpdate=window.update;window.update=d=>{updateCount++;originalUpdate(d)}");
                    translated["m0"]=new string('x',600); host.SetMessages(messages,translated,errors,false);
                    await Wait("document.querySelector('[data-id=m0] .translation').textContent.length","600");
                    Check(await Eval("anchor().id===savedAnchor.id && Math.abs(anchor().offset-savedAnchor.offset)<2 && updateCount===1")=="true","host update jumped or rendered twice");
                    Check(((IList)typeof(TranscriptView).GetField("blocks",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(host)).Count==0,"modern host measured fallback text layout");
                    host.Statuses["m10"]="Queued"; host.SetMessages(messages,translated,errors,false);
                    await Wait("document.querySelector('[data-id=m10] .state').textContent","\"Queued\"");
                    host.Statuses["m10"]="Translating"; host.SetMessages(messages,translated,errors,false);
                    await Wait("document.querySelector('[data-id=m10] .state').textContent","\"Translating\"");
                    await Eval("window.savedAnchor=anchor()"); form.Width+=100;
                    await Task.Delay(150);
                    Check(await Eval("anchor().id===savedAnchor.id && Math.abs(anchor().offset-savedAnchor.offset)<2")=="true","resizing moved the reading position");
                    host.Visible=false; host.Height-=20; await Task.Delay(100); host.Visible=true; await Task.Delay(150);
                    Check(await Eval("anchor().id===savedAnchor.id && Math.abs(anchor().offset-savedAnchor.offset)<2")=="true","hiding chat for settings moved the reading position");
                    await Eval("window.savedAnchor=anchor();window.bodyKept=document.querySelector('[data-id=m10] .body')");
                    var watch=Stopwatch.StartNew();
                    for(int i=0;i<40;i++) { translated["m10"]="translation "+i; host.SetMessages(messages,translated,errors,false); }
                    await Wait("document.querySelector('[data-id=m10] .translation').textContent","\"translation 39\"");
                    Console.WriteLine("200-row translation burst: "+watch.ElapsedMilliseconds+" ms");
                    Check(await Eval("bodyKept===document.querySelector('[data-id=m10] .body') && anchor().id===savedAnchor.id && Math.abs(anchor().offset-savedAnchor.offset)<2 && updateCount<15")=="true","burst replaced original nodes, moved scroll or flooded browser updates");
                    messages.RemoveRange(0,2); host.SetMessages(messages,translated,errors,false);
                    await Wait("document.querySelectorAll('article').length","198");
                    Check(await Eval("anchor().id===savedAnchor.id && Math.abs(anchor().offset-savedAnchor.offset)<2")=="true","pruning retained history moved the reading position");
                    await Eval("log.scrollTop=20");
                    messages.RemoveRange(0,2); host.SetMessages(messages,translated,errors,false);
                    await Wait("document.querySelectorAll('article').length","196");
                    Check(await Eval("log.scrollTop<2")=="true","expired reading anchor skipped the first retained message");
                    await Wait("window.transcriptErrors.length","0");
                    await Eval("jump.click()");
                    messages.Add(new Chatter.ChatMessage("last","Fixture","Latest message","12:31:00",false,false));
                    host.SetMessages(messages,translated,errors,false); await Wait("document.querySelectorAll('article').length","197");
                    Check(await Eval("log.scrollHeight-log.scrollTop-log.clientHeight<2")=="true","pinned transcript did not follow latest");
                    host.UseModern(false,14);
                    Check(((IList)typeof(TranscriptView).GetField("blocks",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(host)).Count==197,"switching to fallback left transcript empty");
                    Console.WriteLine("Modern host updates, statuses, resize/settings anchoring, burst coalescing and fallback verified");
                }
                }
            }
        }
    }
}
