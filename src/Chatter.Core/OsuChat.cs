using System;
using System.Collections.Generic;

namespace Chatter {
    public sealed class OsuChat : IDisposable {
        readonly object gate = new object();
        readonly ProcessMemory memory = new ProcessMemory();
        uint root;
        int failures;
        DateTime retryAt = DateTime.MinValue;
        Dictionary<string, uint> bases;
        string account = "";
        bool? connected;
        int accountId;
        int login;

        static readonly Pattern[] ProfilePatterns = {
            new Pattern("statusPtr", "48 83 F8 04 73 1E", -0x4),
            new Pattern("userProfilePtr", "FF 15 ?? ?? ?? ?? A1 ?? ?? ?? ?? 8B 48 54 33 D2", 0x7),
            new Pattern("rawLoginStatusPtr", "B8 0B 00 00 8B 35", -0xB),
        };

        public ChatSnapshot Read() {
            lock (gate) {
                Prepare();
                RefreshProfile();
                ChatSnapshot snap = ChatGraph.Read(memory, root, memory.Pid, account);
                if (snap == null) {
                    failures++;
                    if (failures >= 3) {
                        root = 0;
                        retryAt = DateTime.MinValue;
                    }
                    throw new InvalidOperationException("Chat changed while reading; retrying");
                }
                failures = 0;
                return new ChatSnapshot(snap.Session, connected, account, snap.Channels);
            }
        }

        public PlayerIdentity Identity() {
            lock (gate) {
                Prepare();
                RefreshProfile();
                int? state = GameState();
                if (!state.HasValue || !ChatRules.MenuOrLobby(state.Value))
                    throw new InvalidOperationException("Sending is available outside gameplay, in osu!'s menus or lobby");
                if (string.IsNullOrEmpty(account) && !connected.HasValue)
                    throw new InvalidOperationException("osu! account unavailable");
                if (accountId <= 0 || login != ChatRules.LoginConnected)
                    throw new InvalidOperationException("Sign into osu! before sending");
                return new PlayerIdentity(memory.Pid, accountId);
            }
        }

        void Prepare() {
            if (!memory.Alive) {
                memory.Close();
                root = 0;
                bases = null;
                account = "";
                connected = null;
                accountId = 0;
                login = 0;
                retryAt = DateTime.MinValue;
                failures = 0;
                int pid = ProcessMemory.FindOsu();
                if (pid == 0) throw new InvalidOperationException("Open osu!stable to read chat");
                memory.Open(pid, NativeMethods.ProcessVmRead | NativeMethods.ProcessQueryInformation);
            }
            if (root != 0) return;
            if (retryAt != DateTime.MinValue && DateTime.UtcNow - retryAt < TimeSpan.FromSeconds(5))
                throw new InvalidOperationException("Open chat in osu!stable; waiting for supported chat data");
            retryAt = DateTime.UtcNow;
            var found = new SortedSet<uint>();
            memory.VisitExecutable(delegate(byte[] code) {
                var roots = ChatGraph.Roots(code);
                for (int i = 0; i < roots.Count; i++) {
                    if (ChatGraph.Read(memory, roots[i], memory.Pid, "") != null) found.Add(roots[i]);
                }
            });
            if (found.Count != 1)
                throw new InvalidOperationException("Open chat in osu!stable; this client may need a chat-reader update");
            foreach (uint item in found) { root = item; break; }
        }

        void RefreshProfile() {
            if (bases == null) bases = memory.Scan(ProfilePatterns);
            uint profilePtr;
            if (!bases.TryGetValue("userProfilePtr", out profilePtr)) {
                ClearProfile();
                return;
            }
            uint? staticSlot = ChatGraph.Word(memory, profilePtr);
            if (!staticSlot.HasValue) return;
            if (staticSlot.Value == 0) {
                ClearProfile();
                return;
            }
            uint? profile = ChatGraph.Word(memory, staticSlot.Value);
            if (!profile.HasValue) return;
            if (profile.Value == 0) {
                ClearProfile();
                return;
            }
            uint? namePtr = ChatGraph.Word(memory, profile.Value + 0x30);
            string name = namePtr.HasValue ? ReadProfileString(namePtr.Value) : null;
            if (string.IsNullOrEmpty(name)) return;
            int id = ReadI32(profile.Value + 0x70);
            int status = 0;
            uint loginPtr;
            if (bases.TryGetValue("rawLoginStatusPtr", out loginPtr)) {
                uint? slot = ChatGraph.Word(memory, loginPtr);
                if (slot.HasValue && slot.Value != 0) {
                    uint? value = ChatGraph.Word(memory, slot.Value);
                    if (value.HasValue) status = unchecked((int)value.Value);
                }
            }
            account = name;
            login = status;
            connected = status == ChatRules.LoginConnected;
            accountId = ChatRules.LoggedIn(id, name, status) ? id : 0;
        }

        void ClearProfile() {
            account = "";
            connected = null;
            accountId = 0;
            login = 0;
        }

        int? GameState() {
            if (bases == null) return null;
            uint statusPtr;
            if (!bases.TryGetValue("statusPtr", out statusPtr)) return null;
            uint? staticAddr = ChatGraph.Word(memory, statusPtr);
            if (!staticAddr.HasValue || staticAddr.Value == 0) return null;
            byte[] raw = memory.ReadBytes(staticAddr.Value, 4);
            if (raw == null) return null;
            return BitConverter.ToInt32(raw, 0);
        }

        string ReadProfileString(uint address) {
            uint? length = ChatGraph.Word(memory, address + 4);
            if (!length.HasValue || length.Value == 0 || length.Value > 1000) return null;
            byte[] raw = memory.ReadBytes(address + 8, (int)length.Value * 2);
            if (raw == null) return null;
            var chars = new char[raw.Length / 2];
            for (int i = 0; i < chars.Length; i++) chars[i] = (char)(raw[i * 2] | (raw[i * 2 + 1] << 8));
            return new string(chars).TrimEnd('\0');
        }

        int ReadI32(uint address) {
            byte[] raw = memory.ReadBytes(address, 4);
            return raw == null ? 0 : BitConverter.ToInt32(raw, 0);
        }

        public void Dispose() { memory.Dispose(); }
    }

    public sealed class PlayerIdentity : IEquatable<PlayerIdentity> {
        public PlayerIdentity(int pid, int account) {
            Pid = pid;
            Account = account;
        }

        public int Pid { get; }
        public int Account { get; }

        public bool Equals(PlayerIdentity other) {
            return other != null && Pid == other.Pid && Account == other.Account;
        }

        public override bool Equals(object obj) { return Equals(obj as PlayerIdentity); }
        public override int GetHashCode() { return Pid * 397 ^ Account; }
    }
}
