using System;
using System.Collections.Generic;
using Stable = Chatter.Shared.StableCompatibilityProfile;
using Layout = Chatter.Shared.StableCompatibilityProfile.Account;

namespace Chatter {
    public sealed class OsuChat : IDisposable {
        readonly object gate = new object();
        readonly ProcessMemory memory = new ProcessMemory();
        uint root;
        int failures;
        DateTime retryAt = DateTime.MinValue;
        Dictionary<string, uint> bases;
        StableAccountSnapshot account = StableAccountSnapshot.Unavailable;

        static readonly Pattern[] ProfilePatterns = {
            new Pattern("statusPtr", Layout.StateSignature, Layout.StateOperandOffset),
            new Pattern("userProfilePtr", Layout.ProfileSignature, Layout.ProfileOperandOffset),
            new Pattern("rawLoginStatusPtr", Layout.LoginSignature, Layout.LoginOperandOffset),
        };

        public ChatSnapshot Read() {
            lock (gate) {
                Prepare();
                RefreshProfile();
                ChatSnapshot snap = ChatGraph.Read(memory, root, memory.Pid, account.Name);
                if (snap == null) {
                    failures++;
                    if (failures >= 3) {
                        root = 0;
                        retryAt = DateTime.MinValue;
                    }
                    throw new InvalidOperationException("Chat changed while reading; retrying");
                }
                failures = 0;
                return new ChatSnapshot(snap.Session, account.Connected, account.Name, snap.Channels, account.Id);
            }
        }

        public PlayerIdentity Identity() {
            lock (gate) {
                Prepare();
                RefreshProfile();
                int? state = StableAccountReader.GameState(memory, bases);
                if (!state.HasValue || !Stable.MenuOrLobby(state.Value))
                    throw new InvalidOperationException("Sending is available outside gameplay, in osu!'s menus or lobby");
                if (string.IsNullOrEmpty(account.Name) && !account.Connected.HasValue)
                    throw new InvalidOperationException("osu! account unavailable");
                if (account.Id <= 0 || account.Login != Layout.ConnectedLogin)
                    throw new InvalidOperationException("Sign into osu! before sending");
                return new PlayerIdentity(memory.Pid, account.Id);
            }
        }

        void Prepare() {
            if (!memory.Alive) {
                memory.Close();
                root = 0;
                bases = null;
                account = StableAccountSnapshot.Unavailable;
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
            account = StableAccountReader.Read(memory, bases);
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
