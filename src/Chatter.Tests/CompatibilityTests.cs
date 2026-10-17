using System;
using System.Collections.Generic;
using System.Reflection;
using Chatter;

namespace Chatter.Tests {
    static class CompatibilityTests {
        // Deliberately independent of the production profile, like GraphTests.Sample.
        static byte[] RootCode(uint root) {
            byte[] code = {
                0x8B, 0x0D, 0, 0, 0, 0, 0x8D, 0x57, 0x04, 0xE8,
                0xA1, 0xB2, 0xC3, 0xD4, 0xB8, 0x12, 0x34, 0x56, 0x78,
                0x89, 0x47, 0x0C, 0x8B, 0xD7, 0xFF, 0xFE, 0xFD
            };
            Buffer.BlockCopy(BitConverter.GetBytes(root), 0, code, 2, 4);
            return code;
        }

        internal static void RootSignatures() {
            Check(ChatGraph.Roots(null!).Count == 0, "null code");
            Check(ChatGraph.Roots(new byte[26]).Count == 0, "short code");
            byte[] first = RootCode(0x10100);
            Check(ChatGraph.Roots(first).Count == 1, "wildcard operands");
            foreach (int fixedByte in new[] { 0, 1, 6, 7, 8, 9, 14, 19, 20, 21, 22, 23 }) {
                byte[] changed = (byte[])first.Clone();
                changed[fixedByte] ^= 0xFF;
                Check(ChatGraph.Roots(changed).Count == 0, "fixed byte " + fixedByte);
            }
            foreach (uint invalid in new uint[] { 0, 0xFFFC, 0x10101, 0x7FFF0000, uint.MaxValue })
                Check(ChatGraph.Roots(RootCode(invalid)).Count == 0, "invalid root " + invalid);

            var both = new byte[3 + first.Length * 2];
            Buffer.BlockCopy(first, 0, both, 3, first.Length);
            Buffer.BlockCopy(RootCode(0x20100), 0, both, 3 + first.Length, first.Length);
            List<uint> roots = ChatGraph.Roots(both);
            Check(roots.Count == 2 && roots[0] == 0x10100 && roots[1] == 0x20100, "all candidates including final position");
        }

        internal static void GraphConsistency() {
            // Every one of these fields must be rechecked, including the CLR list fields.
            foreach (uint address in new uint[] {
                0x14004, 0x14008, 0x12010, 0x10100, 0x10104, 0x100EC, 0x1601C,
                0x11004, 0x1100C, 0x11010, 0x17004, 0x1700C, 0x17010,
                0x13004, 0x1300C, 0x13010
            }) {
                var changing = new ChangingWord(GraphTests.Sample(), address);
                Check(ChatGraph.Read(changing, 0x10100, 1, "") == null, "mutation at " + address.ToString("x"));
                Check(changing.Reads >= 2, "field must be rechecked");
            }
            Fixture malformed = GraphTests.Sample();
            malformed.SetWord(0x12104, 1);
            malformed.Data[0x12108] = 0;
            malformed.Data[0x12109] = 0xD8; // Unpaired high surrogate in room name.
            Check(ChatGraph.Read(malformed, 0x10100, 1, "") == null, "invalid UTF-16");
            malformed = GraphTests.Sample();
            malformed.SetWord(0x100EC, 0x22000);
            Check(ChatGraph.Read(malformed, 0x10100, 1, "") == null, "selected room absent");
        }

        static Dictionary<string, uint> AccountAddresses() {
            return new Dictionary<string, uint> {
                { "userProfilePtr", 0x10000 }, { "rawLoginStatusPtr", 0x10004 }, { "statusPtr", 0x10008 }
            };
        }

        static Fixture AccountMemory() {
            var memory = new Fixture();
            memory.SetWord(0x10000, 0x10020);
            memory.SetWord(0x10020, 0x11000);
            memory.SetWord(0x11030, 0x12000);
            memory.SetString(0x12000, "Test User");
            memory.SetWord(0x11070, 99);
            memory.SetWord(0x10004, 0x10024);
            memory.SetWord(0x10024, 65793);
            memory.SetWord(0x10008, 0x10028);
            memory.SetWord(0x10028, 11);
            return memory;
        }

        internal static void AccountReads() {
            Fixture memory = AccountMemory();
            Dictionary<string, uint> addresses = AccountAddresses();
            StableAccountSnapshot account = StableAccountReader.Read(memory, addresses);
            Check(account.Name == "Test User" && account.Id == 99 && account.Connected == true, "connected identity");
            Check(StableAccountReader.GameState(memory, addresses) == 11, "game state");

            foreach (uint address in new uint[] { 0x10000, 0x10020, 0x11030, 0x12004, 0x12008, 0x11070, 0x10004, 0x10024 }) {
                StableAccountSnapshot unavailable = StableAccountReader.Read(new Unreadable(memory, address), addresses);
                Check(unavailable.Id == 0, "failed read revokes identity at " + address.ToString("x"));
                Check(StableAccountReader.Read(memory, addresses).Id == 99, "fresh read recovers");
            }
            foreach (uint address in new uint[] { 0x10008, 0x10028 })
                Check(!StableAccountReader.GameState(new Unreadable(memory, address), addresses).HasValue, "unreadable state");

            addresses.Remove("rawLoginStatusPtr");
            account = StableAccountReader.Read(memory, addresses);
            Check(account.Id == 0 && !account.Connected.HasValue, "missing login is unknown");
            addresses.Clear();
            Check(StableAccountReader.Read(memory, addresses).Id == 0, "missing profile");
            Check(!StableAccountReader.GameState(memory, addresses).HasValue, "missing state");

            addresses = AccountAddresses();
            memory.SetWord(0x10024, 256);
            account = StableAccountReader.Read(memory, addresses);
            Check(account.Connected == false && account.Id == 0, "disconnected");
            memory.SetWord(0x10024, 65793);
            memory.SetString(0x12000, " Guest ");
            Check(StableAccountReader.Read(memory, addresses).Id == 0, "guest");
            memory = AccountMemory();
            memory.SetWord(0x12004, 1001);
            Check(StableAccountReader.Read(memory, addresses).Id == 0, "oversized name");
            memory = AccountMemory();
            memory.SetWord(0x10020, 0);
            Check(StableAccountReader.Read(memory, addresses).Id == 0, "null profile");
        }

        internal static void SharedProfile() {
            // Both binaries must actually contain the same source-linked profile.
            const string profileName = "Chatter.Shared.StableCompatibilityProfile";
            Type reader = typeof(OsuChat).Assembly.GetType(profileName, true)!;
            Type sender = typeof(Chatter.Bridge.Entry).Assembly.GetType(profileName, true)!;
            var readerValues = new Dictionary<string, object>();
            var senderValues = new Dictionary<string, object>();
            Constants(reader, "", readerValues);
            Constants(sender, "", senderValues);
            Check(readerValues.Count > 0 && readerValues.Count == senderValues.Count, "profile members");
            foreach (var item in readerValues) {
                object? value;
                Check(senderValues.TryGetValue(item.Key, out value) && Equals(value, item.Value), "profile disagreement: " + item.Key);
            }
            MethodInfo readerStates = reader.GetMethod("MenuOrLobby", BindingFlags.Static | BindingFlags.NonPublic)!;
            MethodInfo senderStates = sender.GetMethod("MenuOrLobby", BindingFlags.Static | BindingFlags.NonPublic)!;
            for (int state = -1; state <= 20; state++) {
                object[] args = { state };
                Check(Equals(readerStates.Invoke(null, args), senderStates.Invoke(null, args)), "state disagreement");
            }
        }

        static void Constants(Type type, string path, Dictionary<string, object> values) {
            foreach (FieldInfo field in type.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
                if (field.IsLiteral) values.Add(path + field.Name, field.GetRawConstantValue()!);
            foreach (Type nested in type.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public))
                Constants(nested, path + nested.Name + ".", values);
        }

        sealed class Unreadable : IProcessMemory {
            readonly IProcessMemory memory;
            readonly uint address;
            internal Unreadable(IProcessMemory memory, uint address) { this.memory = memory; this.address = address; }
            public byte[] ReadBytes(uint at, int size) { return at == address ? null! : memory.ReadBytes(at, size); }
        }

        sealed class ChangingWord : IProcessMemory {
            readonly IProcessMemory memory;
            readonly uint address;
            internal int Reads;
            internal ChangingWord(IProcessMemory memory, uint address) { this.memory = memory; this.address = address; }
            public byte[] ReadBytes(uint at, int size) {
                byte[] bytes = memory.ReadBytes(at, size);
                if (at == address && ++Reads > 1) return BitConverter.GetBytes(BitConverter.ToUInt32(bytes, 0) ^ 1u);
                return bytes;
            }
        }

        static void Check(bool condition, string message) {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
