using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using Chatter.Shared;

namespace Chatter.Bridge {
    internal interface INativeCommandGame {
        byte[] PollCommands(int account, bool take, string completed, bool success);
        void StopCommands();
    }
    internal sealed class NativeCommands : IDisposable {
        readonly string patchId = "osu-chatter.native-commands." + Guid.NewGuid().ToString("N");
        static NativeCommands? owner;
        readonly IGame game;
        readonly Func<int> currentAccount;
        readonly Action<CapturedTranslationCommand> restore;
        readonly MethodInfo sender;
        readonly Queue<CapturedTranslationCommand> queue = new Queue<CapturedTranslationCommand>();
        readonly Dictionary<string, CapturedTranslationCommand> pending = new Dictionary<string, CapturedTranslationCommand>();
        readonly object gate = new object();
        Harmony? harmony;
        ulong expires;
        int account;
        internal NativeCommands(IGame game, MethodInfo sender, Func<int> currentAccount, Action<CapturedTranslationCommand> restore) {
            this.game = game; this.sender = sender; this.currentAccount = currentAccount; this.restore = restore;
        }
        internal byte[] Poll(int requestedAccount, bool take, string completed, bool success) {
            lock (gate) {
                if (account != 0 && account != requestedAccount) { pending.Clear(); queue.Clear(); }
                account = requestedAccount;
                if (harmony == null) {
                    var args = sender.GetParameters();
                    if (!sender.IsStatic || sender.ReturnType != typeof(void) || args.Length != 3 || args[0].ParameterType != typeof(string) || args[1].ParameterType != typeof(string) || args[2].ParameterType != typeof(bool) || sender.GetMethodBody() == null)
                        throw new InvalidOperationException("Unsupported chat command submission method");
                    harmony = new Harmony(patchId); owner = this;
                    harmony.Patch(sender, prefix: new HarmonyMethod(typeof(NativeCommands).GetMethod(nameof(Prefix), BindingFlags.NonPublic | BindingFlags.Static)));
                    if (!Harmony.GetPatchInfo(sender).Prefixes.Any(p => p.owner == patchId)) { Dispose(); throw new InvalidOperationException("Could not install the translation command handler"); }
                }
                expires = Session.GetTickCount64() + 3000;
                if (!string.IsNullOrEmpty(completed) && pending.TryGetValue(completed, out var done)) {
                    pending.Remove(completed); if (!success && currentAccount() == done.Account) restore(done);
                }
                foreach (var old in pending.Values.Where(c => Session.GetTickCount64() > c.Deadline).ToArray()) {
                    pending.Remove(old.Id); if (currentAccount() == old.Account) restore(old);
                }
                CapturedTranslationCommand? next = null;
                if (take) while (queue.Count > 0) { var item = queue.Dequeue(); if (pending.ContainsKey(item.Id)) { next = item; break; } }
                return NativeCommandProtocol.Encode(next);
            }
        }
        static bool Prefix(string __0, string __1, bool __2) {
            NativeCommands? active = Volatile.Read(ref owner);
            if (active == null || Session.GetTickCount64() > active.expires || !NativeCommandProtocol.IsCommand(__0)) return true;
            // A recognized local command is consumed even when validation fails.
            // Nothing waits for translation on the game's update thread.
            active.Capture(__0, __1); return false;
        }
        internal void Capture(string input, string target) {
            lock (gate) {
                var item = new CapturedTranslationCommand { Id = Guid.NewGuid().ToString("N"), Original = input, Target = target, Account = account, Deadline = Session.GetTickCount64() + 45000 };
                try {
                    game.Validate(account, target);
                    if (currentAccount() != account) throw new InvalidOperationException("Account changed");
                    NativeCommandProtocol.Parse(input, out item.Language, out item.Text);
                    if (pending.Count >= 8) throw new InvalidOperationException("Too many translation commands are pending. Wait for the current request.");
                } catch (Exception ex) { item.Error = ex is InvalidOperationException ? ex.Message : "The command could not be captured."; }
                if (pending.Count >= 8) { game.Schedule(() => restore(item)); return; }
                pending.Add(item.Id, item); queue.Enqueue(item);
            }
        }
        public void Dispose() {
            lock (gate) {
                expires = 0;
                if (ReferenceEquals(owner, this)) owner = null;
                foreach (var item in pending.Values) if (currentAccount() == item.Account) restore(item);
                queue.Clear(); pending.Clear();
                if (harmony != null) { harmony.Unpatch(sender, HarmonyPatchType.Prefix, patchId); harmony = null; }
            }
        }
    }
}
