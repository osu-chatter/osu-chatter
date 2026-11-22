using System;
using System.IO;
using Chatter;

namespace Chatter.Tests {
    static class StableVersionTests {
        static void Check(bool valid, string error) { if (!valid) throw new InvalidOperationException(error); }
        internal static void Parsing() {
            foreach (string input in new[] { "b20261118.1", "b 20261118.1", "20261118.1", " \"b20261118.1\" " })
                Check(StableVersionReader.Normalize(input) == "20261118.1", "valid game build rejected");
            Check(StableVersionReader.Normalize("b20261118") == "20261118", "build without revision rejected");
            foreach (string input in new[] { "", "1.3.3.8", "20261318.1", "20260230", "20261118.", "b20261118.1junk", "202611180" })
                Check(StableVersionReader.Normalize(input) == null, "invalid game build accepted: " + input);
            string config = "# LastVersion = b20000101\nLastVersionPermissionsFailed = b20000101\nLastVersion = b20261118.1\n_ReleaseStream = Stable40";
            Check(StableVersionReader.ConfigValue(config, "LastVersion") == "b20261118.1", "wrong configuration key selected");
        }
        static Fixture Configuration() {
            var memory = new Fixture();
            memory.SetWord(0x10000, 0x11000); memory.SetWord(0x11000, 0x12000);
            memory.SetWord(0x12008, 0x13000); memory.SetWord(0x1201C, 2); memory.SetWord(0x13004, 2);
            memory.SetWord(0x13008, 0x14000); memory.SetWord(0x1300C, 0x15000);
            memory.SetWord(0x13018, 0x14100); memory.SetWord(0x1301C, 0x15100);
            memory.SetString(0x14000, "LastVersionPermissionsFailed"); memory.SetString(0x14100, "LastVersion");
            memory.SetWord(0x15004, 0x16000); memory.SetString(0x16000, "b20000101");
            memory.SetWord(0x15104, 0x16100); memory.SetString(0x16100, "b20261118.1");
            return memory;
        }
        internal static void Memory() {
            var memory = Configuration();
            Check(StableVersionReader.FromMemory(memory, 0x10000) == "20261118.1", "live configuration traversal failed");
            Check(StableVersionReader.FromMemory(new ChangedConfiguration(Configuration()), 0x10000) == null, "changing configuration was accepted");
            memory.SetWord(0x13004, 1);
            Check(StableVersionReader.FromMemory(memory, 0x10000) == null, "short dictionary array accepted");
            memory = Configuration(); memory.SetWord(0x1201C, 4097);
            Check(StableVersionReader.FromMemory(memory, 0x10000) == null, "unbounded configuration traversal");
            memory = Configuration(); memory.SetWord(0x15104, 0);
            Check(StableVersionReader.FromMemory(memory, 0x10000) == null, "missing version reused stale data");
            memory = Configuration(); memory.SetString(0x16100, "1.3.3.8");
            Check(StableVersionReader.FromMemory(memory, 0x10000) == null, "file version mistaken for game build");
        }
        sealed class ChangedConfiguration : IProcessMemory {
            readonly Fixture inner; int countReads;
            internal ChangedConfiguration(Fixture inner) { this.inner = inner; }
            public byte[]? ReadBytes(uint address, int size) {
                if (address == 0x1201C && ++countReads > 1) return BitConverter.GetBytes(3u);
                return inner.ReadBytes(address, size);
            }
        }
        internal static void ConfigurationFiles() {
            string folder = Path.Combine(Path.GetTempPath(), "chatter-version-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string common = Path.Combine(folder, "osu!.cfg"), user = Path.Combine(folder, "osu!.fixture.cfg"), other = Path.Combine(folder, "osu!.other.cfg");
            try {
                File.WriteAllText(common, "_ReleaseStream = Stable40\nLastVersion = b20261117");
                File.WriteAllText(user, "LastVersionPermissionsFailed = b20000101\nLastVersion = b20261118.1");
                var version = StableVersionReader.FromConfiguration(folder);
                Check(version.Build == "20261118.1" && version.Stream == "stable40" && version.Source.Contains("configuration file"), "configuration fallback failed");
                File.WriteAllText(other, "LastVersion = b20261116");
                version = StableVersionReader.FromConfiguration(folder);
                Check(version.Build == null && version.Source.Contains("conflicting"), "conflicting user configurations were silently guessed");
                File.Delete(other); File.Delete(user);
                Check(StableVersionReader.FromConfiguration(folder).Build == "20261117", "shared configuration fallback failed");
                File.WriteAllText(common, "LastVersionPermissionsFailed = b20000101");
                Check(StableVersionReader.FromConfiguration(folder).Build == null, "permissions failure version treated as current");
            } finally {
                foreach (string file in new[] { common, user, other }) if (File.Exists(file)) File.Delete(file);
                Directory.Delete(folder);
            }
        }
        internal static void InspectConfiguration(string executable) {
            var info = StableVersionReader.Inspect(0, executable);
            Check(info.Build != null, "No game build found in configuration");
            Console.WriteLine("Game build: b " + info.Build + "\nSource: " + info.Source + "\nConfigured stream: " + info.Stream);
        }
    }
}
