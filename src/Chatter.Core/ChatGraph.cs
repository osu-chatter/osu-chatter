using System;
using System.Collections.Generic;
using System.Text;

namespace Chatter {
    /// <summary>
    /// Read-only view of the open osu!stable chat tabs.
    /// One static holds the room list. The selected room is 20 bytes before it,
    /// and the tab manager is 4 bytes after it. Room name is +4, message list is +16.
    /// A message stores body +4, sender +8, and a local DateTime at +36.
    /// CLR object headers mean those field offsets already include the method table.
    /// </summary>
    public static class ChatGraph {
        public static List<uint> Roots(byte[] code) {
            var found = new List<uint>();
            if (code == null || code.Length < 27) return found;
            for (int i = 0; i + 27 <= code.Length; i++) {
                if (code[i] != 0x8B || code[i + 1] != 0x0D) continue;
                if (code[i + 6] != 0x8D || code[i + 7] != 0x57 || code[i + 8] != 0x04 || code[i + 9] != 0xE8) continue;
                if (code[i + 14] != 0xB8) continue;
                if (code[i + 19] != 0x89 || code[i + 20] != 0x47 || code[i + 21] != 0x0C || code[i + 22] != 0x8B || code[i + 23] != 0xD7) continue;
                uint address = BitConverter.ToUInt32(code, i + 2);
                if (address >= 0x10000 && address < 0x7fff0000 && (address % 4) == 0) found.Add(address);
            }
            return found;
        }

        public static ChatSnapshot Read(IProcessMemory memory, uint root, int session, string ownName) {
            if (memory == null || root < 20) return null;
            uint? allPtr = Word(memory, root);
            var all = allPtr.HasValue ? ListRead(memory, allPtr.Value, 512) : null;
            if (all == null || all.Count == 0) return null;
            uint first = all.Items[0];
            uint? roomType = Word(memory, first);
            if (!roomType.HasValue || roomType.Value < 0x10000) return null;
            uint? current = Word(memory, root - 20);
            uint? manager = Word(memory, root + 4);
            if (!current.HasValue || !manager.HasValue) return null;
            uint? tabsPtr = Word(memory, manager.Value + 0x1C);
            var tabs = tabsPtr.HasValue ? ListRead(memory, tabsPtr.Value, 64) : null;
            if (tabs == null) return null;

            var channels = new List<ChatChannel>();
            for (int i = 0; i < tabs.Count; i++) {
                uint tab = tabs.Items[i];
                uint? room = Word(memory, tab + 12);
                if (!room.HasValue || room.Value == 0) continue;
                uint? tag = Word(memory, room.Value);
                if (!tag.HasValue || tag.Value != roomType.Value) continue;
                uint? namePtr = Word(memory, room.Value + 4);
                string name = namePtr.HasValue ? String(memory, namePtr.Value) : null;
                if (name == null || name.Length == 0 || name.Length > 160 || ChatRules.HasControl(name)) return null;
                uint? listPtr = Word(memory, room.Value + 16);
                var list = listPtr.HasValue ? ListRead(memory, listPtr.Value, 10000) : null;
                if (list == null) return null;

                var messages = new List<ChatMessage>();
                var occurrences = new Dictionary<ulong, int>();
                int start = list.Count > 200 ? list.Count - 200 : 0;
                for (int j = start; j < list.Count; j++) {
                    uint obj = list.Items[j];
                    uint? textPtr = Word(memory, obj + 4);
                    uint? senderPtr = Word(memory, obj + 8);
                    if (!textPtr.HasValue || !senderPtr.HasValue) return null;
                    string text = String(memory, textPtr.Value);
                    string sender = String(memory, senderPtr.Value);
                    if (text == null || sender == null) return null;
                    bool system = text.Length == 0 || sender.Length == 0;
                    byte[] tickBytes = memory.ReadBytes(obj + 36, 8);
                    if (tickBytes == null) return null;
                    ulong ticks = BitConverter.ToUInt64(tickBytes, 0) & 0x3fffffffffffffffUL;
                    if (ticks < 600000000000000000UL || ticks > 3155378975999999999UL) return null;
                    ulong seconds = (ticks / 10000000UL) % 86400UL;
                    string time = string.Format("{0:00}:{1:00}:{2:00}", seconds / 3600UL, (seconds / 60UL) % 60UL, seconds % 60UL);
                    if (!system) {
                        string stamp = time.Substring(0, 5) + " ";
                        if (sender.StartsWith(stamp, StringComparison.Ordinal) && sender.EndsWith(":", StringComparison.Ordinal))
                            sender = sender.Substring(stamp.Length, sender.Length - stamp.Length - 1);
                    }
                    bool outgoing = !system && !string.IsNullOrEmpty(ownName)
                        && ChatRules.NormalizeName(sender).Equals(ChatRules.NormalizeName(ownName), StringComparison.OrdinalIgnoreCase);
                    if (text.Length == 0) {
                        text = sender;
                        sender = "";
                    }
                    if (Word(memory, obj + 4) != textPtr || Word(memory, obj + 8) != senderPtr) return null;
                    ulong hash = Hash(ticks, name, sender, text);
                    int occurrence;
                    occurrences.TryGetValue(hash, out occurrence);
                    occurrences[hash] = occurrence + 1;
                    messages.Add(new ChatMessage(hash.ToString("x") + "-" + occurrence.ToString(), sender, text, time, outgoing, system));
                }
                if (!Unchanged(memory, list) || Word(memory, room.Value + 16) != listPtr) return null;
                channels.Add(new ChatChannel(name, name[0] != '#', room.Value == current.Value, messages));
            }

            if (channels.Count == 0 || !AnySelected(channels)) return null;
            if (!Unchanged(memory, tabs) || !Unchanged(memory, all)) return null;
            if (Word(memory, root) != allPtr || Word(memory, root + 4) != manager) return null;
            if (Word(memory, manager.Value + 0x1C) != tabsPtr || Word(memory, root - 20) != current) return null;
            return new ChatSnapshot(session, null, ownName ?? "", channels);
        }

        static bool AnySelected(List<ChatChannel> channels) {
            for (int i = 0; i < channels.Count; i++) if (channels[i].Selected) return true;
            return false;
        }

        sealed class ClrList {
            public uint Address;
            public uint Array;
            public uint Version;
            public int Count;
            public uint[] Items;
        }

        static ClrList ListRead(IProcessMemory memory, uint address, int max) {
            if (address < 0x10000) return null;
            uint? array = Word(memory, address + 4);
            uint? countWord = Word(memory, address + 12);
            if (!array.HasValue || !countWord.HasValue) return null;
            uint? capacity = Word(memory, array.Value + 4);
            if (!capacity.HasValue) return null;
            if (countWord.Value > (uint)max || countWord.Value > capacity.Value || capacity.Value > 65536) return null;
            uint? version = Word(memory, address + 16);
            if (!version.HasValue) return null;
            int count = (int)countWord.Value;
            var items = new uint[count];
            if (count > 0) {
                byte[] raw = memory.ReadBytes(array.Value + 8, count * 4);
                if (raw == null) return null;
                for (int i = 0; i < count; i++) items[i] = BitConverter.ToUInt32(raw, i * 4);
            }
            return new ClrList { Address = address, Array = array.Value, Version = version.Value, Count = count, Items = items };
        }

        static bool Unchanged(IProcessMemory memory, ClrList list) {
            return Word(memory, list.Address + 4) == list.Array
                && Word(memory, list.Address + 12) == (uint)list.Count
                && Word(memory, list.Address + 16) == list.Version;
        }

        public static uint? Word(IProcessMemory memory, uint address) {
            byte[] raw = memory.ReadBytes(address, 4);
            if (raw == null || raw.Length != 4) return null;
            return BitConverter.ToUInt32(raw, 0);
        }

        public static string String(IProcessMemory memory, uint address) {
            if (address < 0x10000) return null;
            uint? length = Word(memory, address + 4);
            if (!length.HasValue || length.Value > 4096) return null;
            if (length.Value == 0) return "";
            byte[] raw = memory.ReadBytes(address + 8, (int)length.Value * 2);
            if (raw == null) return null;
            return DecodeUtf16(raw);
        }

        static string DecodeUtf16(byte[] raw) {
            if ((raw.Length & 1) != 0) return null;
            var chars = new char[raw.Length / 2];
            for (int i = 0; i < chars.Length; i++) chars[i] = (char)(raw[i * 2] | (raw[i * 2 + 1] << 8));
            for (int i = 0; i < chars.Length; i++) {
                if (char.IsHighSurrogate(chars[i])) {
                    if (i + 1 >= chars.Length || !char.IsLowSurrogate(chars[i + 1])) return null;
                    i++;
                } else if (char.IsLowSurrogate(chars[i])) return null;
            }
            return new string(chars);
        }

        static ulong Hash(ulong ticks, string name, string sender, string text) {
            ulong hash = 14695981039346656037UL;
            Add(ref hash, BitConverter.GetBytes(ticks));
            Add(ref hash, Encoding.UTF8.GetBytes(name));
            Add(ref hash, Encoding.UTF8.GetBytes(sender));
            Add(ref hash, Encoding.UTF8.GetBytes(text));
            return hash;
        }

        static void Add(ref ulong hash, byte[] bytes) {
            for (int i = 0; i < bytes.Length; i++) {
                hash ^= bytes[i];
                hash *= 1099511628211UL;
            }
        }
    }
}
