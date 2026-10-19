using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Chatter;

namespace Chatter.Tests {
    static class ProfileTests {
        public static void Links() {
            string visible = "is playing xi - Blue Zenith [FOUR DIMENSIONS]";
            string raw = "[https://osu.ppy.sh/b/658127 " + visible + "]";
            Check(ChatLinks.Visible(raw) == visible, "playing text");
            var spans = ChatLinks.Find(visible, raw);
            Check(spans.Count == 1 && spans[0].Index == 0 && spans[0].Length == visible.Length, "playing span");
            Check(spans[0].Url == "https://osu.ppy.sh/b/658127", "playing url");

            string listening = "is listening to Song [Rebellion]";
            string listenRaw = "[osu://dl/1 " + listening + "]";
            Check(ChatLinks.Visible(listenRaw) == listening, "listening text");
            spans = ChatLinks.Find(listening, listenRaw);
            Check(spans.Count == 1 && spans[0].Url == "osu://dl/1", "listening url");

            string markdown = "see [the map](https://osu.ppy.sh/b/2) now";
            Check(ChatLinks.Visible(markdown) == "see the map now", "markdown text");
            spans = ChatLinks.Find("see the map now", markdown);
            Check(spans.Count == 1 && spans[0].Index == 4 && spans[0].Length == 7 && spans[0].Url == "https://osu.ppy.sh/b/2", "markdown span");

            string old = "see (the map)[https://osu.ppy.sh/b/3] now";
            Check(ChatLinks.Visible(old) == "see the map now", "old text");
            spans = ChatLinks.Find("see the map now", old);
            Check(spans.Count == 1 && spans[0].Url == "https://osu.ppy.sh/b/3", "old span");

            string bare = "listen https://osu.ppy.sh/s/4.";
            Check(ChatLinks.Visible(bare) == bare, "bare stays visible");
            spans = ChatLinks.Find(bare, bare);
            Check(spans.Count == 1 && spans[0].Url == "https://osu.ppy.sh/s/4", "bare trims punctuation");

            string two = "[https://osu.ppy.sh/b/1 first] and [https://osu.ppy.sh/b/2 second]";
            Check(ChatLinks.Visible(two) == "first and second", "two links");
            spans = ChatLinks.Find("first and second", two);
            Check(spans.Count == 2 && spans[0].Url.EndsWith("/1", StringComparison.Ordinal) && spans[1].Index == 10, "two spans");

            Check(ChatLinks.Find("nope", raw).Count == 0, "mismatch stays plain");
            Check(ChatLinks.Visible("[https://osu.ppy.sh/b/1 ]") == "", "empty label");
            Check(ChatLinks.Find("", "[https://osu.ppy.sh/b/1 ]").Count == 0, "empty label has no hit");
            Check(ChatLinks.Visible("see [[FAQ]]") == "see [[FAQ]]", "wiki stays");
            Check(ChatLinks.Find("see [[FAQ]]", "see [[FAQ]]").Count == 0, "wiki is not a link");
            Check(ChatLinks.Find("click javascript:alert(1)", null).Count == 0, "script is not a link");
            Check(ChatLinks.Find("open file:///c:/windows", null).Count == 0, "file is not a link");
            Check(ChatLinks.CanOpen("https://osu.ppy.sh/b/1"), "https opens");
            Check(ChatLinks.CanOpen("osu://dl/1"), "osu opens");
            Check(!ChatLinks.CanOpen("javascript:alert(1)"), "script blocked");
            Check(!ChatLinks.CanOpen("file:///c:/windows"), "file blocked");
            Check(!ChatLinks.CanOpen("https://"), "empty host blocked");
        }

        public static void Presence() {
            string path = Path.Combine(Path.GetTempPath(), "chatter-presence-" + Guid.NewGuid().ToString("n") + ".db");
            try {
                WritePresence(path);
                PresenceIndex index = PresenceIndex.Read(path);
                int id;
                bool supporter;
                Check(index.TryFind("Friend_Name", out id, out supporter) && id == 10 && supporter, "supporter name");
                Check(index.TryFind("friend name", out id, out supporter) && id == 10 && supporter, "normalized name");
                Check(index.TryFind("Plain User", out id, out supporter) && id == 11 && !supporter, "plain user");
                Check(index.TryFind("Bat User", out id, out supporter) && id == 12 && !supporter, "bat is not supporter");
                Check(!index.TryFind("missing", out id, out supporter) && id == 0 && !supporter, "unknown name");
                Check(index.IsSupporter(10) && !index.IsSupporter(11) && !index.IsSupporter(12) && !index.IsSupporter(0), "id supporter");
            } finally {
                TryDelete(path);
            }

            try {
                File.WriteAllBytes(path, new byte[] { 1, 0, 0, 0 });
                try {
                    PresenceIndex.Read(path);
                    throw new InvalidOperationException("short presence accepted");
                } catch (InvalidDataException) { }
            } finally {
                TryDelete(path);
            }
        }

        public static void Markup() {
            Fixture plain = GraphTests.Sample();
            ChatMessage untouched = ChatGraph.Read(plain, 0x10100, 1, "Test User").Channels[0].Messages[0];
            Check(untouched.Source == untouched.Text, "missing markup stays plain");

            Fixture fixture = GraphTests.Sample();
            string visible = "is playing xi - Blue Zenith [FOUR DIMENSIONS]";
            string raw = "[https://osu.ppy.sh/b/1 " + visible + "]";
            fixture.SetString(0x22000, visible);
            fixture.SetWord(0x14010, 0x25000);
            fixture.SetWord(0x2500c, 0x26000);
            fixture.SetString(0x26000, raw);
            ChatMessage message = ChatGraph.Read(fixture, 0x10100, 1, "Test User").Channels[0].Messages[0];
            Check(message.Text == visible && message.Source == raw, "markup source");
            Check(ChatLinks.Find(message.Text, message.Source).Count == 1, "markup link");

            fixture.SetString(0x26000, "no");
            message = ChatGraph.Read(fixture, 0x10100, 1, "Test User").Channels[0].Messages[0];
            Check(message.Source == message.Text && message.Text == visible, "mismatched markup stays plain");

            fixture.SetString(0x26000, raw);
            var flip = new FlipWord(fixture, 0x14010);
            Check(ChatGraph.Read(flip, 0x10100, 1, "Test User") == null, "markup pointer changed");

            string time = ChatGraph.Read(fixture, 0x10100, 1, "Test User").Channels[0].Messages[1].Time;
            fixture.SetString(0x21200, time.Substring(0, 5) + " *Test_User");
            ChatMessage action = ChatGraph.Read(fixture, 0x10100, 1, "Test User").Channels[0].Messages[1];
            Check(action.Sender == "Test_User" && action.Outgoing, "action name");

            var snapshot = new ChatSnapshot(1, true, "you", new ChatChannel[0], 42);
            Check(snapshot.AccountId == 42, "account id");
            Check(new ChatSnapshot(1, true, "you", new ChatChannel[0]).AccountId == 0, "default account");
        }

        static void WritePresence(string path) {
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
            using (var writer = new BinaryWriter(stream, Encoding.UTF8)) {
                writer.Write(20260924);
                writer.Write(3);
                WritePlayer(writer, 10, "Friend_Name", (byte)(4 | 0x60), false);
                WritePlayer(writer, 11, "Plain User", 1, true);
                WritePlayer(writer, 12, "Bat User", 2, false);
            }
        }

        static void WritePlayer(BinaryWriter writer, int id, string name, byte permissions, bool wideLength) {
            writer.Write(id);
            byte[] utf8 = Encoding.UTF8.GetBytes(name);
            writer.Write((byte)0x0b);
            if (wideLength) {
                writer.Write((byte)(utf8.Length | 0x80));
                writer.Write((byte)0);
            } else {
                writer.Write((byte)utf8.Length);
            }
            writer.Write(utf8);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write(permissions);
            writer.Write(0f);
            writer.Write(0f);
            writer.Write(1);
            writer.Write(0L);
        }

        static void TryDelete(string path) {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { }
        }

        static void Check(bool value, string message) {
            if (!value) throw new InvalidOperationException(message);
        }

        sealed class FlipWord : IProcessMemory {
            readonly Fixture fixture;
            readonly uint address;
            int reads;

            public FlipWord(Fixture fixture, uint address) {
                this.fixture = fixture;
                this.address = address;
            }

            public byte[] ReadBytes(uint at, int size) {
                byte[] bytes = fixture.ReadBytes(at, size);
                if (at == address && size == 4 && ++reads == 2) return BitConverter.GetBytes(BitConverter.ToUInt32(bytes, 0) ^ 1u);
                return bytes;
            }
        }
    }
}
