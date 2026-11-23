using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Chatter;
using Chatter.Bridge;

namespace Chatter.Tests {
    static class ReleaseCandidateTests {
        static void Check(bool value, string description) { if (!value) throw new InvalidOperationException(description); }
        internal static void ContextAndHints() {
            var old = new ChatMessage("old", "A", "Yesterday", "11:50:00", false, false);
            var first = new ChatMessage("a", "A", "Is this made in China?", "12:00:00", false, false);
            var second = new ChatMessage("b", "B", "да", "12:00:04", false, false);
            var later = new ChatMessage("c", "C", "Must never affect the earlier request", "12:00:05", false, false);
            var channel = new ChatChannel("#russian", false, true, new[] { old, first, second, later });
            string context = TranslationContext.Before(channel, "b", 6);
            Check(context.Contains(first.Text) && !context.Contains(old.Text) && !context.Contains(second.Text) && !context.Contains(later.Text), "context must contain only recent preceding messages in this room");
            Check(TranslationContext.Source("Привет", "#russian", "auto") == "ru", "Cyrillic channel hint");
            Check(TranslationContext.Source("Hello", "#russian", "auto") == "auto", "English text must not be forced through Russian");
            Check(TranslationContext.Source("да", "#osu", "ru") == "ru", "source override");
            foreach (string name in new[] { "japanese", "chinese", "german", "french", "spanish", "portuguese", "ukrainian" }) Check(ChatRules.ChannelLanguage("#" + name) != "en", "language mapping " + name);
            Check(TranslationContext.IsOnlyLink("https://osu.ppy.sh/b/123"), "bare links should bypass inference");
            string source = "is playing [https://osu.ppy.sh/b/123 Artist - Map Title] <osu!>";
            string display = ChatLinks.Visible(source);
            string protectedText = TranslationContext.ProtectLinks(display, ChatLinks.Find(display, source), out var protectedLinks);
            Check(!protectedText.Contains("Artist - Map Title") && TranslationContext.RestoreLinks(protectedText, protectedLinks) == display, "map titles must survive translation unchanged");
        }
        internal static void CustomModels() {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".gguf");
            try {
                using (var writer = new BinaryWriter(File.Create(path), Encoding.UTF8)) {
                    void Text(string value) { byte[] bytes = Encoding.UTF8.GetBytes(value); writer.Write((ulong)bytes.Length); writer.Write(bytes); }
                    writer.Write(0x46554747u); writer.Write(3u); writer.Write(0ul); writer.Write(3ul);
                    Text("general.architecture"); writer.Write(8u); Text("plamo3");
                    Text("general.test"); writer.Write(9u); writer.Write(4u); writer.Write(2ul); writer.Write(12u); writer.Write(34u);
                    Text("general.name"); writer.Write(8u); Text("plamo-3-translate-2b");
                }
                var details = LocalModelMetadata.Read(path);
                Check(details.Kind == "plamo" && details.Architecture == "plamo3", "renamed custom GGUF was not detected");
                File.WriteAllText(path, "not a gguf");
                try { LocalModelMetadata.Read(path); throw new Exception("non-GGUF file accepted"); } catch (InvalidOperationException) { }
            } finally { File.Delete(path); }
            string prompt = LocalAiTranslator.PlamoPrompt(new TranslationRequest { Text = "literal <|plamo:tag|>input", Source = "auto", Target = "zh", Context = "previous messages", Glossary = "osu!, beatmap => mapa" });
            Check(prompt.Contains("<|plamo:tag|>context<|plamo:msg|>previous messages") && prompt.Contains("<|plamo:index|>osu!<|plamo:item|>osu!") && prompt.Contains("<|plamo:index|>beatmap<|plamo:item|>mapa"), "PFML3 context/glossary formatting");
            Check(prompt.Contains("< |plamo:tag|>input") && prompt.EndsWith("<|plamo:val|>zh-hans<|plamo:msg|>") && !prompt.Contains("<|plamo:val|>auto"), "PFML3 literal tokens or language codes");
        }
        internal static void AdvancedPreferences() {
            var prefs = new AppPreferences { Endpoint = Translator.NoTranslation, OutgoingSource = "auto" };
            prefs.Experience.NativeCommands = true; prefs.Experience.NativePresentation = "both"; prefs.Experience.ContextMessages = 8;
            prefs.Experience.ModelVariant = "madlad"; prefs.Experience.EngineBackend = "vulkan"; prefs.Experience.GpuLayers = 100;
            prefs.Experience.ModelPath = @"C:\models\translation.gguf"; prefs.Experience.IncomingSource = "ru";
            prefs.Remember(new ChannelPreference { Name = "#russian", IncomingSource = "ru" });
            string file = Path.Combine(Path.GetTempPath(), "chatter-release-prefs-" + Guid.NewGuid().ToString("N") + ".json");
            try {
                PreferencesStore.Save(prefs, file); Check(PreferencesStore.TryLoad(out var loaded, file), "load advanced settings");
                Check(loaded.Experience.NativePresentation == "both" && loaded.Experience.NativeCommands && loaded.OutgoingSource == "auto" && loaded.FindChannel("#russian")?.IncomingSource == "ru", "advanced settings roundtrip");
                Check(loaded.Experience.ModelVariant == "madlad" && loaded.Experience.EngineBackend == "vulkan" && loaded.Experience.GpuLayers == 100, "MADLAD/Vulkan settings roundtrip");
                var copy = loaded.Copy(); copy.Experience.ContextMessages = 1; Check(loaded.Experience.ContextMessages == 8, "advanced copy must be independent");
                loaded.Experience.ContextMessages = 50; try { loaded.Validate(); throw new Exception("unbounded context accepted"); } catch (InvalidOperationException) { }
            } finally { File.Delete(file); }
        }
        internal static void ChatApiAndCache() {
            using (var listener = new HttpListener()) {
                listener.Prefixes.Add("http://127.0.0.1:17328/"); listener.Start();
                string body = ""; Exception? problem = null;
                var server = new Thread(() => {
                    try {
                        var incoming = listener.GetContext(); using (var reader = new StreamReader(incoming.Request.InputStream)) body = reader.ReadToEnd();
                        byte[] data = Encoding.UTF8.GetBytes("{\"choices\":[{\"message\":{\"content\":\"Hello ZXQLINK0QXZ\"}}]}");
                        incoming.Response.ContentType = "application/json"; incoming.Response.OutputStream.Write(data, 0, data.Length); incoming.Response.Close();
                    } catch (Exception ex) { problem = ex; }
                }) { IsBackground = true }; server.Start();
                using (var translator = new Translator()) {
                    var request = new TranslationRequest { Endpoint = Translator.ChatApi, ChatEndpoint = "http://127.0.0.1:17328/v1/chat/completions", Text = "Привет https://osu.ppy.sh/b/123", Source = "ru", Target = "en", Context = "Previous conversation", Glossary = "osu!" };
                    string translated = translator.Translate(request);
                    Check(translated == "Hello https://osu.ppy.sh/b/123", "link restoration");
                    Check(translator.Translate(request) == translated, "cache did not return the completed translation");
                }
                Check(server.Join(3000) && problem == null, "mock chat endpoint failed");
                Check(body.Contains("Previous conversation") && body.Contains("[Source Text]") && body.Contains("ZXQLINK0QXZ") && !body.Contains("https://osu.ppy.sh/b/123"), "context/protected link payload");
            }
        }
        internal static void StableProgress() {
            using (var form = new Chatter.App.MainForm { DemoMode = true }) {
                var handle = form.Handle; form.LoadDemo();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var transcript = (Control)form.GetType().GetField("transcript", flags).GetValue(form); var before = transcript.Bounds;
                form.GetType().GetField("translatorStatus", flags).SetValue(form, "Translating…"); form.GetType().GetMethod("Render", flags).Invoke(form, null);
                Check(transcript.Bounds == before, "normal translation status moved the transcript");
                form.GetType().GetField("translatorStatus", flags).SetValue(form, ""); form.GetType().GetMethod("Render", flags).Invoke(form, null);
                Check(transcript.Bounds == before, "clearing normal status moved the transcript");
            }
        }
        static int calls;
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        static void FixtureSender(string text, string target, bool history) { Interlocked.Increment(ref calls); }
        sealed class CommandGame : IGame {
            public void Validate(int account, string target) { if (account != 42 || target != "Fixture") throw new InvalidOperationException("Identity changed"); }
            public void Schedule(Action action) { ThreadPool.QueueUserWorkItem(_ => action()); }
            public void Send(string text, string target) { throw new InvalidOperationException("Command capture must never invoke the sender"); }
        }
        internal static void CommandHook() {
            calls = 0; string restored = "";
            MethodInfo sender = typeof(ReleaseCandidateTests).GetMethod(nameof(FixtureSender), BindingFlags.Static | BindingFlags.NonPublic);
            using (var commands = new NativeCommands(new CommandGame(), sender, () => 42, item => restored = item.Original)) {
                commands.Poll(42, false, "", true);
                Check(NativeBridgeInspection.Prefixes(HarmonyLib.Harmony.GetPatchInfo(sender)).Cast<object>().Any(), "diagnostic inspection must read Harmony's prefix field");
                sender.Invoke(null, new object[] { "/t en jestem developerem", "Fixture", true });
                Check(calls == 0, "raw command reached the original sender");
                byte[] payload = commands.Poll(42, true, "", true);
                using (var stream = new MemoryStream(payload)) using (var reader = new BinaryReader(stream)) {
                    Check(reader.ReadBoolean(), "captured command missing"); string id = reader.ReadString();
                    Check(reader.ReadString() == "Fixture" && reader.ReadString() == "en" && reader.ReadString() == "jestem developerem", "command target/language/text capture");
                    commands.Poll(42, false, id, false); Check(restored == "/t en jestem developerem", "failed command draft not restored");
                }
                sender.Invoke(null, new object[] { "/translate pl hello", "Fixture", true }); Check(calls == 0, "long alias reached sender");
                sender.Invoke(null, new object[] { "ordinary message", "Fixture", true }); Check(calls == 1, "ordinary chat was intercepted");
                sender.Invoke(null, new object[] { "/topic fixture", "Fixture", true }); Check(calls == 2, "command prefix collision");
                sender.Invoke(null, new object[] { "/t en /quit", "Fixture", true }); Check(calls == 2, "nested command reached sender");
            }
            sender.Invoke(null, new object[] { "after disposal", "Fixture", true }); Check(calls == 3, "sender not restored after unpatch");
        }
        internal static void InstallModel(bool quality = false) {
            var installed = LocalModelManager.Install(new ExperienceOptions { ModelVariant = quality ? "quality" : "fast", DownloadDirectory = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "models")) }, Console.WriteLine);
            Console.WriteLine("engine ready=" + File.Exists(installed.RuntimePath) + " model ready=" + File.Exists(installed.ModelPath));
            using (var translator = new Translator()) {
                foreach (var pair in new[] { ("ru", "en", "на нём написано мейд ин чайна?"), ("pl", "en", "jestem developerem aplikacji osu-chatter") }) {
                    var watch = Stopwatch.StartNew();
                    string text = translator.Translate(new TranslationRequest { Endpoint = Translator.LocalModel, ModelPath = installed.ModelPath, RuntimePath = installed.RuntimePath, Source = pair.Item1, Target = pair.Item2, Text = pair.Item3, Context = "Tiebreaker: он из китая", Threads = 2 });
                    Check(!string.IsNullOrWhiteSpace(text) && text != pair.Item3 && !text.Contains("[Background Information]") && !text.Contains("[Source Text]"), "local AI returned prompt text instead of a translation"); Console.WriteLine("local AI " + pair.Item1 + " → " + pair.Item2 + ": " + text + " (" + watch.ElapsedMilliseconds + " ms)");
                }
            }
        }
        internal static void Madlad() => LocalModel("madlad");
        internal static void Plamo() => LocalModel("plamo");
        static void LocalModel(string variant) {
            string directory = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "models"));
            foreach (string backend in new[] { "cpu", "vulkan" }) {
                var installed = LocalModelManager.Install(new ExperienceOptions { ModelVariant = variant, EngineBackend = backend, DownloadDirectory = directory }, Console.WriteLine);
                Check(LocalModelMetadata.Read(installed.ModelPath).Kind == variant, "model metadata detection");
                var custom = LocalModelManager.Install(installed, Console.WriteLine, engineOnly: true);
                Check(custom.ModelPath == installed.ModelPath, "preparing a custom engine replaced the selected model");
                using (var translator = new Translator()) {
                    foreach (var pair in new[] { ("ru", "en", "на нём написано мейд ин чайна?"), ("pl", "en", "jestem developerem aplikacji osu-chatter"), ("en", "pl", "I love playing rhythm games."), ("ko", "en", "안녕하세요"), ("ru", "en", "Привет https://osu.ppy.sh/b/123") }) {
                        var watch = Stopwatch.StartNew();
                        string translated = translator.Translate(new TranslationRequest { Endpoint = Translator.LocalModel, ModelPath = installed.ModelPath, RuntimePath = installed.RuntimePath, GpuLayers = installed.GpuLayers, Source = pair.Item1, Target = pair.Item2, Text = pair.Item3, Context = "Do not repeat this context", Glossary = "Do not repeat this glossary", Threads = 2 });
                        Check(!string.IsNullOrWhiteSpace(translated) && translated != pair.Item3 && !translated.Contains("<2") && !translated.Contains("Do not repeat"), "MADLAD returned input or prompt scaffolding");
                        Check(!pair.Item3.Contains("https://") || translated.Contains("https://osu.ppy.sh/b/123"), "MADLAD lost a protected link");
                        string acceleration = translator.AccelerationStatus(installed);
                        Check(backend != "vulkan" || acceleration.Contains("GPU verified"), "GPU offload was not confirmed by the engine: " + acceleration);
                        Console.WriteLine(variant + " " + backend + " " + pair.Item1 + " → " + pair.Item2 + ": " + translated + " (" + watch.ElapsedMilliseconds + " ms; " + acceleration + ")");
                    }
                }
            }
        }
        internal static void ReplacedCommandHook() {
            calls = 0;
            MethodInfo sender = typeof(ReleaseCandidateTests).GetMethod(nameof(FixtureSender), BindingFlags.Static | BindingFlags.NonPublic);
            using (var old = new NativeCommands(new CommandGame(), sender, () => 42, _ => { }))
            using (var replacement = new NativeCommands(new CommandGame(), sender, () => 42, _ => { })) {
                old.Poll(42, false, "", true); replacement.Poll(42, false, "", true);
                old.Dispose();
                sender.Invoke(null, new object[] { "/t ru testing", "Fixture", true });
                Check(calls == 0, "old cleanup removed the replacement hook");
                using (var data = new MemoryStream(replacement.Poll(42, true, "", true))) using (var reader = new BinaryReader(data)) Check(reader.ReadBoolean(), "replacement did not capture the command");
            }
            sender.Invoke(null, new object[] { "normal after cleanup", "Fixture", true }); Check(calls == 1, "replacement hook was not removed");
        }
        internal static void CommandsLive() {
            using (var chat = new OsuChat()) {
                var snap = chat.Read(); var until = DateTime.UtcNow.AddSeconds(60);
                while ((!snap.MenuOrLobby || snap.Connected != true) && DateTime.UtcNow < until) { Thread.Sleep(500); snap = chat.Read(); }
                var channel = snap.Channels.First(c => c.Selected);
                var bridge = new BridgeClient(ChatService.DefaultBridgePath());
                try { Check(bridge.PollCommands(new SendPlan(chat.Identity(), channel.Name), false) == null, "unexpected real command in no-send validation"); Console.WriteLine("native command hook installed; no message submitted"); }
                catch (Exception ex) { Console.WriteLine("Native hook request failed: " + ex); throw; }
                finally { try { bridge.StopCommands(); } catch (Exception ex) { Console.WriteLine("Hook cleanup: " + ex.Message); } }
            }
        }
    }
}
