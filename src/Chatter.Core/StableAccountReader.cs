using System;
using System.Collections.Generic;
using Layout = Chatter.Shared.StableCompatibilityProfile.Account;

namespace Chatter {
    internal sealed class StableAccountSnapshot {
        internal static readonly StableAccountSnapshot Unavailable = new StableAccountSnapshot("", 0, null);

        internal StableAccountSnapshot(string name, int id, int? login) {
            Name = name;
            Id = id;
            Login = login;
        }

        internal string Name { get; }
        internal int Id { get; }
        internal int? Login { get; }
        internal bool? Connected => Login.HasValue ? Login.Value == Layout.ConnectedLogin : (bool?)null;
    }

    /// <summary>Decodes one fresh account observation; failed reads never reuse identity.</summary>
    internal static class StableAccountReader {
        internal static StableAccountSnapshot Read(IProcessMemory memory, IDictionary<string, uint> addresses) {
            uint address;
            if (!addresses.TryGetValue("userProfilePtr", out address)) return StableAccountSnapshot.Unavailable;
            uint? slot = ChatGraph.Word(memory, address);
            if (!slot.HasValue || slot.Value == 0) return StableAccountSnapshot.Unavailable;
            uint? profile = ChatGraph.Word(memory, slot.Value);
            if (!profile.HasValue || profile.Value == 0) return StableAccountSnapshot.Unavailable;
            uint? namePointer = ChatGraph.Word(memory, profile.Value + Layout.Name);
            string? name = namePointer.HasValue ? ReadName(memory, namePointer.Value) : null;
            if (name == null || name.Length == 0) return StableAccountSnapshot.Unavailable;

            uint? idWord = ChatGraph.Word(memory, profile.Value + Layout.Id);
            int id = idWord.HasValue ? unchecked((int)idWord.Value) : 0;
            int? login = null;
            if (addresses.TryGetValue("rawLoginStatusPtr", out address)) {
                uint? loginSlot = ChatGraph.Word(memory, address);
                if (loginSlot.HasValue && loginSlot.Value != 0) {
                    uint? value = ChatGraph.Word(memory, loginSlot.Value);
                    if (value.HasValue) login = unchecked((int)value.Value);
                }
            }
            int accountId = login.HasValue && Layout.LoggedIn(id, name, login.Value) ? id : 0;
            return new StableAccountSnapshot(name, accountId, login);
        }

        internal static int? GameState(IProcessMemory memory, IDictionary<string, uint> addresses) {
            uint address;
            if (!addresses.TryGetValue("statusPtr", out address)) return null;
            uint? slot = ChatGraph.Word(memory, address);
            if (!slot.HasValue || slot.Value == 0) return null;
            uint? state = ChatGraph.Word(memory, slot.Value);
            return state.HasValue ? unchecked((int)state.Value) : (int?)null;
        }

        static string? ReadName(IProcessMemory memory, uint address) {
            uint? length = ChatGraph.Word(memory, address + Clr32Layout.StringLength);
            if (!length.HasValue || length.Value == 0 || length.Value > 1000) return null;
            int size = (int)length.Value * Clr32Layout.CharacterSize;
            byte[] raw = memory.ReadBytes(address + Clr32Layout.StringData, size);
            if (raw == null || raw.Length != size) return null;
            var chars = new char[raw.Length / Clr32Layout.CharacterSize];
            for (int i = 0; i < chars.Length; i++) chars[i] = (char)(raw[i * 2] | (raw[i * 2 + 1] << 8));
            return new string(chars).TrimEnd('\0');
        }
    }
}
