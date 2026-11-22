using System;
using System.Collections.Generic;
using System.Threading;

namespace Chatter.Bridge {
    // A short lease restores originals even when Chatter exits unexpectedly or
    // the reader stops making fresh observations. No timer thread touches UI.
    internal sealed class NativeDisplayLease : IDisposable {
        readonly IGame game;
        readonly Timer timer;
        State? state;
        int queued, disposed;
        sealed class State {
            internal int Account;
            internal string Target = "";
            internal IDictionary<string, string> Translations = new Dictionary<string, string>();
            internal ulong Expires;
        }
        internal NativeDisplayLease(IGame game) {
            this.game = game;
            timer = new Timer(Tick, null, 250, 250);
        }
        internal int Update(int account, string target, IDictionary<string, string> translations) {
            if (Volatile.Read(ref disposed) != 0) throw new ObjectDisposedException(nameof(NativeDisplayLease));
            var native = game as INativeChatGame;
            if (native == null) throw new InvalidOperationException("In-game translation is unavailable");
            int count = native.Display(account, target, translations);
            Volatile.Write(ref state, new State { Account = account, Target = target, Translations = translations, Expires = Session.GetTickCount64() + 3000 });
            return count;
        }
        internal void Clear() {
            Volatile.Write(ref state, null);
            (game as INativeChatGame)?.RestoreDisplay();
        }
        void Tick(object? unused) {
            State? current = Volatile.Read(ref state);
            if (current == null || Interlocked.CompareExchange(ref queued, 1, 0) != 0) return;
            try {
                game.Schedule(() => {
                    try {
                        if (!ReferenceEquals(Volatile.Read(ref state), current)) return;
                        if (Volatile.Read(ref disposed) != 0 || Session.GetTickCount64() > current.Expires) { Clear(); return; }
                        try { ((INativeChatGame)game).Display(current.Account, current.Target, current.Translations); }
                        catch { Clear(); }
                    } finally { Interlocked.Exchange(ref queued, 0); }
                });
            } catch { Interlocked.Exchange(ref queued, 0); }
        }
        public void Dispose() {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            timer.Dispose();
            Volatile.Write(ref state, null);
            try { game.Schedule(Clear); } catch { }
        }
    }
}
