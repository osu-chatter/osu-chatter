using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using Chatter;
using Chatter.Bridge;

namespace Chatter.Tests {
    public static class StubProbe {
        public static int Start(string argument) {
            return argument == "ping" ? 42 : 99;
        }
    }

    static class Program {
        static int failed;

        [STAThread]
        static int Main(string[] args) {
            if (IntPtr.Size != 4) {
                Console.WriteLine("tests must run as 32-bit");
                return 2;
            }
            if (args.Length == 1 && args[0] == "--stub-missing") Environment.Exit(RunMissing());
            if (args.Length == 1 && args[0] == "--stub-call") Environment.Exit(RunCall());
            if (args.Length == 1 && args[0] == "--live") return Live();
            if (args.Length == 1 && args[0] == "--translation-live") { Run("local translation", UiTests.LocalTranslation); return failed == 0 ? 0 : 1; }
            RunInterfaceChecks();
            RunChatReadChecks();
            RunTranslationChecks();
            RunPreferenceChecks();
            RunMessageChecks();
            RunBridgeChecks();
            Console.WriteLine(failed == 0 ? "all checks passed" : failed + " failed");
            return failed == 0 ? 0 : 1;
        }

        static void RunInterfaceChecks() {
            Run("transcript keeps its place", UiTests.Transcript);
            Run("conversation refresh keeps selection", UiTests.Refresh);
            Run("rounded controls match their parent", UiTests.RoundedControls);
            Run("profile pictures and chat links", UiTests.Profiles);
            Run("listening underlines track the title", UiTests.LinkLines);
        }

        static void RunChatReadChecks() {
            Run("chat text and direction", GraphTests.Unicode);
            Run("incomplete chat data", GraphTests.Rejects);
            Run("sender labels", GraphTests.SenderLabel);
            Run("chat links", ProfileTests.Links);
            Run("presence list", ProfileTests.Presence);
            Run("link markup and action names", ProfileTests.Markup);
            Run("empty private conversation", GraphTests.PrivateEmpty);
            Run("message identity", GraphTests.Identity);
            Run("chat root scan", GraphTests.RootPattern);
            Run("chat signature boundaries", CompatibilityTests.RootSignatures);
            Run("chat layout mutation safeguards", CompatibilityTests.GraphConsistency);
            Run("fresh account layout reads", CompatibilityTests.AccountReads);
            Run("reader and bridge share one profile", CompatibilityTests.SharedProfile);
        }

        static void RunTranslationChecks() {
            Run("translation endpoint URLs", TranslationTests.Urls);
            Run("translation request body", TranslationTests.Body);
            Run("translation response parsing", TranslationTests.JsonAndPassthrough);
        }

        static void RunPreferenceChecks() {
            Run("DeepL settings", PreferenceTests.DeepLProtocol);
            Run("saved preferences", PreferenceTests.Persistence);
            Run("first-run setup", PreferenceTests.Setup);
            Run("conversation settings", PreferenceTests.Channels);
        }

        static void RunMessageChecks() {
            Run("message send rules", RuleTests.Text);
            Run("account identity", RuleTests.Account);
            Run("unread messages", RuleTests.Unread);
            Run("channel language", RuleTests.Language);
        }

        static void RunBridgeChecks() {
            Run("bridge payload", StubTests.Image);
            Run("bridge protocol", BridgeProtocolTests.RunAll);
            Run("bridge handles a missing module", delegate { StubProcess("--stub-missing", 1); });
            Run("bridge calls managed code", delegate { StubProcess("--stub-call", 42); });
        }

        static void Run(string name, Action action) {
            try {
                action();
                Console.WriteLine("ok  " + name);
            } catch (Exception ex) {
                failed++;
                Console.WriteLine("FAIL " + name + ": " + ex.Message);
            }
        }

        static void StubProcess(string arg, int expect) {
            var start = new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, arg) {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using (Process process = Process.Start(start)) {
                if (!process.WaitForExit(20000)) {
                    try { process.Kill(); } catch { }
                    throw new InvalidOperationException(arg + " hung");
                }
                string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                if (process.ExitCode != expect) throw new InvalidOperationException(arg + " exited " + process.ExitCode + " " + output);
            }
        }

        static int RunMissing() {
            var request = LocalRequest();
            request.ClrModule = "chatter-missing.dll";
            return ClrStub.RunLocal(request);
        }

        static int RunCall() {
            var request = LocalRequest();
            request.AssemblyPath = Assembly.GetExecutingAssembly().Location;
            request.TypeName = "Chatter.Tests.StubProbe";
            request.MethodName = "Start";
            request.Argument = "ping";
            return ClrStub.RunLocal(request);
        }

        static StubRequest LocalRequest() {
            IntPtr kernel = NativeMethods.GetModuleHandleA("kernel32.dll");
            return new StubRequest {
                GetModuleHandleW = NativeMethods.GetProcAddress(kernel, "GetModuleHandleW").ToInt32(),
                GetProcAddress = NativeMethods.GetProcAddress(kernel, "GetProcAddress").ToInt32(),
                AssemblyPath = "",
                TypeName = "",
                MethodName = "",
                Argument = ""
            };
        }

        static int Live() {
            try {
                using (var chat = new OsuChat()) {
                    ChatSnapshot snap = chat.Read();
                    int messages = 0;
                    for (int i = 0; i < snap.Channels.Count; i++) messages += snap.Channels[i].Messages.Count;
                    Console.WriteLine("live tabs=" + snap.Channels.Count + " messages=" + messages + " connected=" + (snap.Connected.HasValue ? snap.Connected.Value.ToString() : "unknown"));
                    return snap.Channels.Count > 0 ? 0 : 1;
                }
            } catch (Exception ex) {
                Console.WriteLine("live " + ex.Message);
                return 1;
            }
        }
    }

    static class GraphTests {
        const uint Root = 0x10100;

        public static void Unicode() {
            Fixture fixture = Sample();
            ChatSnapshot snap = ChatGraph.Read(fixture, Root, 42, "Test User");
            if (snap == null || snap.Channels.Count != 1) throw new InvalidOperationException("channels");
            ChatChannel channel = snap.Channels[0];
            if (channel.Name != "#korean" || channel.Private || !channel.Selected) throw new InvalidOperationException("channel");
            if (channel.Messages[0].Text != "안녕하세요 Привет 👋" || channel.Messages[0].Outgoing) throw new InvalidOperationException("incoming");
            if (!channel.Messages[1].Outgoing) throw new InvalidOperationException("outgoing");
            if (!channel.Messages[2].System || channel.Messages[2].Text != "Joined #korean!") throw new InvalidOperationException("system");
            if (channel.Messages[0].Time.Length != 8) throw new InvalidOperationException("time");
        }

        public static void Rejects() {
            Fixture fixture = Sample();
            fixture.SetWord(0x12104, uint.MaxValue);
            if (ChatGraph.Read(fixture, Root, 1, "") != null) throw new InvalidOperationException("length");
            fixture = Sample();
            fixture.SetWord(0x1300c, 10001);
            if (ChatGraph.Read(fixture, Root, 1, "") != null) throw new InvalidOperationException("count");
            fixture = Sample();
            for (int i = 0; i < 8; i++) fixture.Data[0x14024 + i] = 0;
            if (ChatGraph.Read(fixture, Root, 1, "") != null) throw new InvalidOperationException("time");
            fixture = Sample();
            fixture.Mutate = true;
            if (ChatGraph.Read(fixture, Root, 1, "") != null) throw new InvalidOperationException("mutate");
        }

        public static void SenderLabel() {
            Fixture fixture = Sample();
            string time = ChatGraph.Read(fixture, Root, 1, "Test User").Channels[0].Messages[1].Time;
            fixture.SetString(0x21200, time.Substring(0, 5) + " Test_User:");
            ChatMessage message = ChatGraph.Read(fixture, Root, 1, "Test User").Channels[0].Messages[1];
            if (message.Sender != "Test_User" || !message.Outgoing) throw new InvalidOperationException("label");
        }

        public static void PrivateEmpty() {
            Fixture fixture = Sample();
            fixture.SetString(0x12100, "Other User");
            fixture.List(0x13000, 0x13100, new uint[0]);
            ChatChannel channel = ChatGraph.Read(fixture, Root, 1, "").Channels[0];
            if (!channel.Private || channel.Messages.Count != 0) throw new InvalidOperationException("private");
        }

        public static void Identity() {
            Fixture fixture = Sample();
            string id = ChatGraph.Read(fixture, Root, 1, "").Channels[0].Messages[0].Id;
            Buffer.BlockCopy(fixture.Data, 0x14000, fixture.Data, 0x19000, 0x2c);
            fixture.SetWord(0x13108, 0x19000);
            if (ChatGraph.Read(fixture, Root, 1, "").Channels[0].Messages[0].Id != id) throw new InvalidOperationException("moved");
        }

        public static void RootPattern() {
            var code = new byte[27];
            code[0] = 0x8B; code[1] = 0x0D;
            code[2] = 0x00; code[3] = 0x00; code[4] = 0x00; code[5] = 0x01;
            code[6] = 0x8D; code[7] = 0x57; code[8] = 0x04; code[9] = 0xE8;
            code[14] = 0xB8;
            code[19] = 0x89; code[20] = 0x47; code[21] = 0x0C; code[22] = 0x8B; code[23] = 0xD7;
            var roots = ChatGraph.Roots(code);
            if (roots.Count != 1 || roots[0] != 0x01000000) throw new InvalidOperationException("root");
        }

        public static Fixture Sample() {
            var fixture = new Fixture();
            fixture.SetWord(Root, 0x11000);
            fixture.SetWord(Root - 20, 0x12000);
            fixture.SetWord(Root + 4, 0x16000);
            fixture.List(0x11000, 0x11100, new uint[] { 0x12000 });
            fixture.SetWord(0x12000, 0x30000);
            fixture.SetWord(0x12004, 0x12100);
            fixture.SetString(0x12100, "#korean");
            fixture.SetWord(0x12010, 0x13000);
            fixture.List(0x13000, 0x13100, new uint[] { 0x14000, 0x14100, 0x14200 });
            var rows = new[] {
                new object[] { 0x14000u, "Other", "안녕하세요 Привет 👋" },
                new object[] { 0x14100u, "Test_User", "Hello" },
                new object[] { 0x14200u, "Joined #korean!", "" }
            };
            for (int i = 0; i < rows.Length; i++) {
                uint address = (uint)rows[i][0];
                uint sender = (uint)(0x21000 + (address - 0x14000) * 2);
                uint text = (uint)(0x22000 + (address - 0x14000) * 2);
                fixture.SetWord(address + 4, text);
                fixture.SetWord(address + 8, sender);
                fixture.SetString(sender, (string)rows[i][1]);
                fixture.SetString(text, (string)rows[i][2]);
                Array.Copy(BitConverter.GetBytes(638000000000000000UL), 0, fixture.Data, (int)address + 36, 8);
            }
            fixture.SetWord(0x1601c, 0x17000);
            fixture.List(0x17000, 0x17100, new uint[] { 0x18000, 0x18100 });
            fixture.SetWord(0x1800c, 0x12000);
            return fixture;
        }
    }

    sealed class Fixture : IProcessMemory {
        public readonly byte[] Data = new byte[0x40000];
        public bool Mutate;
        bool changed;

        public byte[] ReadBytes(uint address, int size) {
            if (Mutate && address == 0x13010) {
                bool was = changed;
                changed = true;
                return BitConverter.GetBytes(was ? 1u : 0u);
            }
            if (address > Data.Length || size < 0 || address + size > Data.Length) return null;
            var buffer = new byte[size];
            Buffer.BlockCopy(Data, (int)address, buffer, 0, size);
            return buffer;
        }

        public void SetWord(uint address, uint value) {
            Array.Copy(BitConverter.GetBytes(value), 0, Data, (int)address, 4);
        }

        public void SetString(uint address, string text) {
            byte[] units = Encoding.Unicode.GetBytes(text);
            SetWord(address + 4, (uint)(units.Length / 2));
            Array.Copy(units, 0, Data, (int)address + 8, units.Length);
        }

        public void List(uint address, uint array, uint[] items) {
            SetWord(address + 4, array);
            SetWord(address + 12, (uint)items.Length);
            SetWord(array + 4, (uint)items.Length);
            for (int i = 0; i < items.Length; i++) SetWord(array + 8 + (uint)i * 4, items[i]);
        }
    }

    static class TranslationTests {
        public static void JsonAndPassthrough() {
            string text = "Привет 안녕하세요 👋\n\"quoted\"";
            if (Translator.JsonString("{\"translatedText\":" + Translator.Json(text) + "}", "translatedText") != text)
                throw new InvalidOperationException("Unicode JSON roundtrip");
            foreach (string malformed in new[] { "{\"translatedText\":null,\"other\":\"wrong\"}", "{\"translatedText\":42}", "{\"nested\":{\"translatedText\":\"wrong\"}}", "not JSON" })
                if (Translator.JsonString(malformed, "translatedText") != null) throw new InvalidOperationException("invalid response accepted");
            using (var translator = new Translator()) {
                string same = translator.Translate(new TranslationRequest { Endpoint = ChatRules.LocalTranslator, Source = "en", Target = "en", Text = "hello" });
                if (same != "hello") throw new InvalidOperationException("same language");
            }
        }

        public static void Urls() {
            var request = new TranslationRequest { Endpoint = "http://localhost:5000/translate", ApiKey = "", Text = "Привет", Target = "ko", Source = "auto" };
            request.Validate();
            foreach (string url in new[] { "http://example.com/translate", "file:///secrets", "https://user:secret@example.com/translate", "https://example.com/translate#fragment" }) {
                request.Endpoint = url;
                try { request.Validate(); throw new InvalidOperationException(url); }
                catch (InvalidOperationException ex) { if (ex.Message == url) throw; }
            }
            request.Endpoint = "https://example.com/translate";
            request.Validate();
            request.Text = new string('a', 8193);
            try { request.Validate(); throw new InvalidOperationException("long"); }
            catch (InvalidOperationException ex) { if (ex.Message == "long") throw; }
        }

        public static void Body() {
            int port = 17321;
            using (var listener = new HttpListener()) {
                listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
                listener.Start();
                var server = new Thread(new ThreadStart(delegate {
                    HttpListenerContext context = listener.GetContext();
                    byte[] body = Encoding.UTF8.GetBytes("{\"translatedText\":\"Здравствуйте\"}");
                    context.Response.ContentType = "application/json";
                    context.Response.ContentLength64 = body.Length;
                    context.Response.OutputStream.Write(body, 0, body.Length);
                    context.Response.Close();
                }));
                server.IsBackground = true;
                server.Start();
                string text = new Translator().Translate(new TranslationRequest {
                    Endpoint = "http://127.0.0.1:" + port + "/translate",
                    ApiKey = "",
                    Text = "Hello",
                    Source = "en",
                    Target = "ru"
                });
                server.Join(3000);
                listener.Stop();
                if (text != "Здравствуйте") throw new InvalidOperationException(text);
            }
        }
    }

    static class RuleTests {
        public static void Text() {
            var waves = new StringBuilder();
            for (int i = 0; i < 201; i++) waves.Append("\U0001F44B");
            foreach (string text in new[] { "", " /quit", "hello\n/quit", "hello\r", "hello\0", new string('x', 401), waves.ToString() }) {
                try { ChatRules.ValidateText(text); throw new InvalidOperationException(text); }
                catch (InvalidOperationException ex) { if (!ex.Message.StartsWith("Use a single")) throw; }
            }
            ChatRules.ValidateText("Привет! 안녕하세요 👋");
        }

        public static void Account() {
            var one = new PlayerIdentity(1, 10);
            if (!one.Equals(new PlayerIdentity(1, 10))) throw new InvalidOperationException("same");
            if (one.Equals(new PlayerIdentity(2, 10)) || one.Equals(new PlayerIdentity(1, 11))) throw new InvalidOperationException("changed");
            if (!ChatRules.LoggedIn(99, "Account", ChatRules.LoginConnected)) throw new InvalidOperationException("logged in");
            if (ChatRules.LoggedIn(0, "TEST2", ChatRules.LoginConnected) || ChatRules.LoggedIn(99, "Guest", ChatRules.LoginConnected))
                throw new InvalidOperationException("logged out");
            if (!ChatRules.MenuOrLobby(0) || !ChatRules.MenuOrLobby(11) || ChatRules.MenuOrLobby(2)) throw new InvalidOperationException("state");
        }

        public static void Unread() {
            var previous = new Dictionary<string, int> { { "#osu", 2 } };
            var ids = new Dictionary<string, HashSet<string>> { { "#osu", new HashSet<string> { "a" } } };
            var channels = new List<ChatChannel> {
                new ChatChannel("#osu", false, true, new[] { new ChatMessage("a", "a", "old", "00:00:00", false, false), new ChatMessage("b", "a", "new", "00:00:01", false, false) })
            };
            var next = UnreadCounter.Count(previous, ids, true, "#lobby", true, channels);
            if (next["#osu"] != 3) throw new InvalidOperationException("added");
            next = UnreadCounter.Count(previous, ids, true, "#osu", true, channels);
            if (next["#osu"] != 0) throw new InvalidOperationException("active");
            next = UnreadCounter.Count(previous, ids, false, "#lobby", true, channels);
            if (next["#osu"] != 0) throw new InvalidOperationException("session");
        }

        public static void Language() {
            if (ChatRules.Languages.Length != ChatRules.LanguageNames.Length) throw new InvalidOperationException("language list");
            if (Array.IndexOf(ChatRules.Languages, "he") < 0 || Array.IndexOf(ChatRules.Languages, "ar") < 0 || Array.IndexOf(ChatRules.Languages, "tr") < 0)
                throw new InvalidOperationException("missing language");
            if (ChatRules.ChannelLanguage("#russian") != "ru" || ChatRules.ChannelLanguage("ko-chat") != "ko" || ChatRules.ChannelLanguage("#polish") != "pl")
                throw new InvalidOperationException("named");
            if (ChatRules.ChannelLanguage("#hebrew") != "he" || ChatRules.ChannelLanguage("#arab") != "ar" || ChatRules.ChannelLanguage("#turkish") != "tr")
                throw new InvalidOperationException("named");
            if (ChatRules.ChannelLanguage("#osu") != "en" || ChatRules.ChannelLanguage("certual") != "en") throw new InvalidOperationException("default");
            if (ChatRules.ChannelLanguage("heart") != "en" || ChatRules.ChannelLanguage("arcade") != "en" || ChatRules.ChannelLanguage("intro") != "en")
                throw new InvalidOperationException("default");
            if (!ChatRules.RightToLeft("he") || !ChatRules.RightToLeft("ar") || ChatRules.RightToLeft("tr") || ChatRules.RightToLeft("en"))
                throw new InvalidOperationException("direction");
            if (!ChatRules.RightToLeftText("שלום") || !ChatRules.RightToLeftText("مرحبا") || ChatRules.RightToLeftText("merhaba") || ChatRules.RightToLeftText("Hello"))
                throw new InvalidOperationException("script");
        }
    }

    static class StubTests {
        public static void Image() {
            if (ClrStub.Code[0] != 0x55 || ClrStub.Code[1] != 0x8B || ClrStub.Code[2] != 0xEC) throw new InvalidOperationException("prologue");
            int n = ClrStub.Code.Length;
            if (ClrStub.Code[n - 3] != 0xC2 || ClrStub.Code[n - 2] != 0x04 || ClrStub.Code[n - 1] != 0x00) throw new InvalidOperationException("return");
            var request = new StubRequest { GetModuleHandleW = 0x11111111, GetProcAddress = 0x22222222, AssemblyPath = @"C:\chat\bridge.dll", TypeName = "Chatter.Bridge.Entry", MethodName = "Start", Argument = "ping" };
            byte[] data = ClrStub.BuildData(0x20000, request);
            if (BitConverter.ToUInt32(data, 0) != 0x11111111 || BitConverter.ToUInt32(data, 4) != 0x22222222) throw new InvalidOperationException("imports");
            uint clr = BitConverter.ToUInt32(data, 8);
            if (clr < 0x20000 || clr >= 0x20000 + data.Length) throw new InvalidOperationException("clr pointer");
            string text = Encoding.Unicode.GetString(data, (int)(clr - 0x20000), 14);
            if (!text.StartsWith("clr.dll")) throw new InvalidOperationException(text);
        }
    }

    static class BridgeProtocolTests {
        sealed class FakeGame : IGame {
            public int sent, validated;
            public bool wrongAccount;
            public readonly ManualResetEvent pause = new ManualResetEvent(true);
            public void Validate(int account, string target) {
                Interlocked.Increment(ref validated);
                if (wrongAccount || account != 42 || target != "Fixture") throw new InvalidOperationException();
            }
            public void Schedule(Action action) { ThreadPool.QueueUserWorkItem(delegate { pause.WaitOne(); action(); }); }
            public void Send(string text, string target) {
                if (text != "Привет 👋" || target != "Fixture") throw new Exception();
                Interlocked.Increment(ref sent);
            }
        }

        sealed class Fixture : IDisposable {
            public readonly byte[] key = new byte[32];
            public readonly FakeGame game = new FakeGame();
            public readonly string name = "chatter-test-" + Guid.NewGuid().ToString("N");
            readonly Session session;
            public Fixture() {
                for (int i = 0; i < key.Length; i++) key[i] = (byte)i;
                using (Process process = Process.GetCurrentProcess())
                    session = new Session(name, key, process.Id, process.StartTime.ToUniversalTime().ToFileTimeUtc(), game);
                session.Start();
            }
            public System.IO.Pipes.NamedPipeClientStream Request(byte operation, byte[] secret = null, string text = "Привет 👋") {
                var pipe = new System.IO.Pipes.NamedPipeClientStream(".", name, System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous);
                pipe.Connect(2000);
                using (var payload = new System.IO.MemoryStream()) {
                    var writer = new System.IO.BinaryWriter(payload, Encoding.UTF8, true);
                    writer.Write(0x52424331u);
                    writer.Write(secret ?? key);
                    writer.Write(operation);
                    writer.Write(42);
                    byte[] target = Encoding.UTF8.GetBytes("Fixture");
                    byte[] body = Encoding.UTF8.GetBytes(operation == 0 ? "" : text);
                    writer.Write(target.Length);
                    writer.Write(target);
                    writer.Write(body.Length);
                    writer.Write(body);
                    writer.Write(123UL);
                    writer.Flush();
                    byte[] bytes = payload.ToArray();
                    pipe.Write(bytes, 0, bytes.Length);
                    pipe.Flush();
                }
                return pipe;
            }
            public void Dispose() { game.pause.Set(); session.Stop(); }
        }

        static byte Status(System.IO.Stream pipe, int timeout = 3000) { return Session.Read(pipe, 1, timeout)[0]; }
        static void Commit(System.IO.Stream pipe, ulong nonce = 123, ulong? deadline = null) {
            var writer = new System.IO.BinaryWriter(pipe, Encoding.UTF8, true);
            writer.Write(nonce);
            writer.Write(deadline ?? (Session.GetTickCount64() + 75));
            writer.Flush();
        }
        static bool NoSuccess(System.IO.Stream pipe) { try { return Status(pipe) != 2; } catch { return true; } }
        static void Check(bool yes, string name) { if (!yes) throw new InvalidOperationException(name); }

        public static void RunAll() {
            using (var fixture = new Fixture()) using (var pipe = fixture.Request(0)) { Check(Status(pipe) == 3, "check"); Check(fixture.game.sent == 0, "check never sends"); }
            using (var fixture = new Fixture()) using (var pipe = fixture.Request(1)) { Check(Status(pipe) == 1, "ready"); Commit(pipe); Check(Status(pipe) == 2, "ack"); Check(fixture.game.sent == 1, "one call"); Check(fixture.game.validated == 2, "execution recheck"); }
            using (var fixture = new Fixture()) using (var pipe = fixture.Request(1, new byte[32])) { Check(NoSuccess(pipe), "bad key"); Check(fixture.game.sent == 0, "bad key no effects"); }
            using (var fixture = new Fixture()) using (var pipe = fixture.Request(1, text: "/quit")) { Check(NoSuccess(pipe), "command rejected"); Check(fixture.game.sent == 0, "command no effects"); }
            using (var fixture = new Fixture()) using (var pipe = fixture.Request(1, text: "hello\n/quit")) { Check(NoSuccess(pipe), "newline rejected"); Check(fixture.game.sent == 0, "newline no effects"); }
            using (var fixture = new Fixture()) using (var pipe = fixture.Request(1)) { Check(Status(pipe) == 1, "ready stale"); Commit(pipe, deadline: Session.GetTickCount64() - 1); Check(NoSuccess(pipe), "expired commit"); Check(fixture.game.sent == 0, "expired no effects"); }
            using (var fixture = new Fixture()) using (var pipe = fixture.Request(1)) { Check(Status(pipe) == 1, "ready nonce"); Commit(pipe, nonce: 124); Check(NoSuccess(pipe), "wrong nonce"); Check(fixture.game.sent == 0, "nonce no effects"); }
            using (var fixture = new Fixture()) using (var pipe = fixture.Request(1)) { Check(Status(pipe) == 1, "ready account"); fixture.game.wrongAccount = true; Commit(pipe); Check(NoSuccess(pipe), "account changed"); Check(fixture.game.sent == 0, "account no effects"); }
            using (var fixture = new Fixture()) { using (var pipe = fixture.Request(1)) { Check(Status(pipe) == 1, "ready disconnect"); } Thread.Sleep(150); Check(fixture.game.sent == 0, "disconnected before grant"); }
            using (var fixture = new Fixture()) {
                fixture.game.pause.Reset();
                using (var pipe = fixture.Request(1)) Thread.Sleep(2400);
                fixture.game.pause.Set();
                Thread.Sleep(100);
                Check(fixture.game.sent == 0, "late queued work cancelled");
            }
        }
    }
}
