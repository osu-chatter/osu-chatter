using System;
using System.Collections.Generic;
using System.Text;
using Layout = Chatter.Shared.StableCompatibilityProfile.Chat;

namespace Chatter {
    /// <summary>
    /// Read-only view of open chat tabs using the shared Stable compatibility profile.
    /// Game field offsets live in that profile; CLR offsets live in Clr32Layout.
    /// </summary>
    public static class ChatGraph {
        static readonly Pattern RootPattern = new Pattern("chatRoot", Layout.RootSignature, Layout.RootOperandOffset);

        public static List<uint> Roots(byte[] code) {
            var found = new List<uint>();
            if (code == null) return found;
            for (int i = 0; i <= code.Length - RootPattern.Bytes.Length; i++) {
                if (!RootPattern.Matches(code, i)) continue;
                uint address = BitConverter.ToUInt32(code, i + RootPattern.Offset);
                if (address >= Clr32Layout.MinimumAddress && address < Clr32Layout.AddressLimit && (address % Clr32Layout.WordSize) == 0) found.Add(address);
            }
            return found;
        }

        public static ChatSnapshot Read(IProcessMemory memory, uint root, int session, string ownName) {
            if (memory == null || root < Layout.SelectedRoomBeforeRoot) return null;
            uint? allPtr = Word(memory, root + Layout.AllRooms);
            var all = allPtr.HasValue ? ListRead(memory, allPtr.Value, 512) : null;
            if (all == null || all.Count == 0) return null;
            uint first = all.Items[0];
            uint? roomType = Word(memory, first + Clr32Layout.MethodTable);
            if (!roomType.HasValue || roomType.Value < Clr32Layout.MinimumAddress) return null;
            uint? current = Word(memory, root - Layout.SelectedRoomBeforeRoot);
            uint? manager = Word(memory, root + Layout.TabManager);
            if (!current.HasValue || !manager.HasValue) return null;
            uint? tabsPtr = Word(memory, manager.Value + Layout.ManagerTabs);
            var tabs = tabsPtr.HasValue ? ListRead(memory, tabsPtr.Value, 64) : null;
            if (tabs == null) return null;

            var channels = new List<ChatChannel>();
            for (int i = 0; i < tabs.Count; i++) {
                uint tab = tabs.Items[i];
                uint? room = Word(memory, tab + Layout.TabRoom);
                if (!room.HasValue || room.Value == 0) continue;
                uint? tag = Word(memory, room.Value + Clr32Layout.MethodTable);
                if (!tag.HasValue || tag.Value != roomType.Value) continue;
                uint? namePtr = Word(memory, room.Value + Layout.RoomName);
                string name = namePtr.HasValue ? String(memory, namePtr.Value) : null;
                if (name == null || name.Length == 0 || name.Length > 160 || ChatRules.HasControl(name)) return null;
                uint? listPtr = Word(memory, room.Value + Layout.RoomMessages);
                var list = listPtr.HasValue ? ListRead(memory, listPtr.Value, 10000) : null;
                if (list == null) return null;

                var messages = new List<ChatMessage>();
                var occurrences = new Dictionary<ulong, int>();
                int start = list.Count > 200 ? list.Count - 200 : 0;
                for (int j = start; j < list.Count; j++) {
                    uint obj = list.Items[j];
                    uint? textPtr = Word(memory, obj + Layout.MessageBody);
                    uint? senderPtr = Word(memory, obj + Layout.MessageSender);
                    if (!textPtr.HasValue || !senderPtr.HasValue) return null;
                    string text = String(memory, textPtr.Value);
                    string sender = String(memory, senderPtr.Value);
                    if (text == null || sender == null) return null;
                    bool system = text.Length == 0 || sender.Length == 0;
                    byte[] tickBytes = memory.ReadBytes(obj + Layout.MessageTime, Clr32Layout.DateTimeSize);
                    if (tickBytes == null) return null;
                    ulong ticks = BitConverter.ToUInt64(tickBytes, 0) & Clr32Layout.DateTimeTicksMask;
                    if (ticks < 600000000000000000UL || ticks > 3155378975999999999UL) return null;
                    ulong seconds = (ticks / 10000000UL) % 86400UL;
                    string time = string.Format("{0:00}:{1:00}:{2:00}", seconds / 3600UL, (seconds / 60UL) % 60UL, seconds % 60UL);
                    if (!system) sender = DisplaySender(sender, time);
                    bool outgoing = !system && !string.IsNullOrEmpty(ownName)
                        && ChatRules.NormalizeName(sender).Equals(ChatRules.NormalizeName(ownName), StringComparison.OrdinalIgnoreCase);
                    if (text.Length == 0) {
                        text = sender;
                        sender = "";
                    }
                    if (Word(memory, obj + Layout.MessageBody) != textPtr || Word(memory, obj + Layout.MessageSender) != senderPtr) return null;
                    string? source = null;
                    if (!system && !TryMarkup(memory, obj, text, out source)) return null;
                    ulong hash = Hash(ticks, name, sender, text);
                    int occurrence;
                    occurrences.TryGetValue(hash, out occurrence);
                    occurrences[hash] = occurrence + 1;
                    messages.Add(new ChatMessage(hash.ToString("x") + "-" + occurrence.ToString(), sender, text, time, outgoing, system, source));
                }
                if (!Unchanged(memory, list) || Word(memory, room.Value + Layout.RoomMessages) != listPtr) return null;
                channels.Add(new ChatChannel(name, name[0] != '#', room.Value == current.Value, messages));
            }

            if (channels.Count == 0 || !AnySelected(channels)) return null;
            if (!Unchanged(memory, tabs) || !Unchanged(memory, all)) return null;
            if (Word(memory, root + Layout.AllRooms) != allPtr || Word(memory, root + Layout.TabManager) != manager) return null;
            if (Word(memory, manager.Value + Layout.ManagerTabs) != tabsPtr || Word(memory, root - Layout.SelectedRoomBeforeRoot) != current) return null;
            return new ChatSnapshot(session, null, ownName ?? "", channels);
        }

        static string DisplaySender(string sender, string time) {
            string stamp = time.Substring(0, 5) + " ";
            if (!sender.StartsWith(stamp, StringComparison.Ordinal)) return sender;
            sender = sender.Substring(stamp.Length);
            if (sender.EndsWith(":", StringComparison.Ordinal)) sender = sender.Substring(0, sender.Length - 1);
            if (sender.StartsWith("*", StringComparison.Ordinal)) sender = sender.Substring(1);
            return sender.Trim();
        }

        static bool TryMarkup(IProcessMemory memory, uint obj, string text, out string? source) {
            source = null;
            uint? markupPtr = Word(memory, obj + Layout.MessageMarkup);
            if (!markupPtr.HasValue) return false;
            if (markupPtr.Value < Clr32Layout.MinimumAddress) return true;
            uint? rawPtr = Word(memory, markupPtr.Value + Layout.MarkupText);
            string? raw = rawPtr.HasValue ? String(memory, rawPtr.Value) : null;
            if (!rawPtr.HasValue
                || Word(memory, obj + Layout.MessageMarkup) != markupPtr
                || Word(memory, markupPtr.Value + Layout.MarkupText) != rawPtr)
                return false;
            if (raw != null && ChatLinks.Visible(raw) == text) source = raw;
            return true;
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
            if (address < Clr32Layout.MinimumAddress) return null;
            uint? array = Word(memory, address + Clr32Layout.ListArray);
            uint? countWord = Word(memory, address + Clr32Layout.ListCount);
            if (!array.HasValue || !countWord.HasValue) return null;
            uint? capacity = Word(memory, array.Value + Clr32Layout.ArrayLength);
            if (!capacity.HasValue) return null;
            if (countWord.Value > (uint)max || countWord.Value > capacity.Value || capacity.Value > 65536) return null;
            uint? version = Word(memory, address + Clr32Layout.ListVersion);
            if (!version.HasValue) return null;
            int count = (int)countWord.Value;
            var items = new uint[count];
            if (count > 0) {
                byte[] raw = memory.ReadBytes(array.Value + Clr32Layout.ArrayData, count * Clr32Layout.WordSize);
                if (raw == null) return null;
                for (int i = 0; i < count; i++) items[i] = BitConverter.ToUInt32(raw, i * Clr32Layout.WordSize);
            }
            return new ClrList { Address = address, Array = array.Value, Version = version.Value, Count = count, Items = items };
        }

        static bool Unchanged(IProcessMemory memory, ClrList list) {
            return Word(memory, list.Address + Clr32Layout.ListArray) == list.Array
                && Word(memory, list.Address + Clr32Layout.ListCount) == (uint)list.Count
                && Word(memory, list.Address + Clr32Layout.ListVersion) == list.Version;
        }

        public static uint? Word(IProcessMemory memory, uint address) {
            byte[] raw = memory.ReadBytes(address, Clr32Layout.WordSize);
            if (raw == null || raw.Length != Clr32Layout.WordSize) return null;
            return BitConverter.ToUInt32(raw, 0);
        }

        public static string String(IProcessMemory memory, uint address) {
            if (address < Clr32Layout.MinimumAddress) return null;
            uint? length = Word(memory, address + Clr32Layout.StringLength);
            if (!length.HasValue || length.Value > 4096) return null;
            if (length.Value == 0) return "";
            byte[] raw = memory.ReadBytes(address + Clr32Layout.StringData, (int)length.Value * Clr32Layout.CharacterSize);
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
