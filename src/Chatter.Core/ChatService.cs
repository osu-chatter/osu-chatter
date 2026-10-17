using System;
using System.Collections.Generic;
using System.Threading;

namespace Chatter {
    public sealed class ChatService : IDisposable {
        readonly OsuChat chat = new OsuChat();
        readonly Translator translator = new Translator();
        readonly BridgeClient bridge;
        int sending;

        public ChatService() : this(DefaultBridgePath()) { }

        public ChatService(string bridgePath) {
            bridge = new BridgeClient(bridgePath);
        }

        public static string DefaultBridgePath() {
            return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Chatter.Bridge.dll");
        }

        public ChatSnapshot Read() { return chat.Read(); }

        public string Translate(TranslationRequest request) { return translator.Translate(request); }
        public event Action<string> TranslationStatus { add { translator.Status += value; } remove { translator.Status -= value; } }
        public void PrepareTranslator() { translator.PrepareLocal(); }

        public void Check(string target) {
            if (Interlocked.CompareExchange(ref sending, 1, 0) != 0)
                throw new InvalidOperationException("A chat submission is already in progress");
            try {
                SendPlan plan = Plan(target);
                bridge.Request(plan, null);
            } finally {
                Interlocked.Exchange(ref sending, 0);
            }
        }

        public SendReceipt TranslateAndSend(string target, TranslationRequest request) {
            SendPlan plan = Plan(target);
            string translated = translator.Translate(request);
            return Send(plan, translated);
        }

        SendReceipt Send(SendPlan plan, string text) {
            ChatRules.ValidateText(text);
            if (Interlocked.CompareExchange(ref sending, 1, 0) != 0)
                throw new InvalidOperationException("A chat submission is already in progress");
            try {
                PlayerIdentity now = chat.Identity();
                if (!now.Equals(plan.Identity))
                    throw new InvalidOperationException("osu! account or process changed while translating; nothing was sent");
                ChatSnapshot before = chat.Read();
                if (before.Session != plan.Identity.Pid || !Has(before, plan.Target))
                    throw new InvalidOperationException("This conversation is no longer open");
                bridge.Request(plan, text);
                Thread.Sleep(150);
                var seen = new HashSet<string>();
                for (int i = 0; i < before.Channels.Count; i++) {
                    var messages = before.Channels[i].Messages;
                    for (int j = 0; j < messages.Count; j++) seen.Add(messages[j].Id);
                }
                bool echoed = false;
                try {
                    ChatSnapshot after = chat.Read();
                    for (int i = 0; i < after.Channels.Count; i++) {
                        ChatChannel channel = after.Channels[i];
                        if (channel.Name != plan.Target) continue;
                        for (int j = 0; j < channel.Messages.Count; j++) {
                            ChatMessage message = channel.Messages[j];
                            if (!seen.Contains(message.Id) && message.Outgoing && message.Text == text) echoed = true;
                        }
                    }
                } catch (InvalidOperationException) { }
                return new SendReceipt(text, echoed);
            } finally {
                Interlocked.Exchange(ref sending, 0);
            }
        }

        SendPlan Plan(string target) {
            PlayerIdentity identity = chat.Identity();
            ChatSnapshot snap = chat.Read();
            if (snap.Session != identity.Pid || !Has(snap, target))
                throw new InvalidOperationException("Open this conversation in osu! first");
            return new SendPlan(identity, target);
        }

        static bool Has(ChatSnapshot snap, string target) {
            for (int i = 0; i < snap.Channels.Count; i++) if (snap.Channels[i].Name == target) return true;
            return false;
        }

        public void Dispose() { translator.Dispose(); chat.Dispose(); }
    }

    public static class UnreadCounter {
        public static Dictionary<string, int> Count(
            IDictionary<string, int> previous,
            IDictionary<string, HashSet<string>> previousIds,
            bool sameSession,
            string active,
            bool viewing,
            IReadOnlyList<ChatChannel> channels) {
            var next = new Dictionary<string, int>();
            for (int i = 0; i < channels.Count; i++) {
                ChatChannel channel = channels[i];
                int added = 0;
                HashSet<string> seen;
                if (sameSession && previousIds != null && previousIds.TryGetValue(channel.Name, out seen)) {
                    for (int j = 0; j < channel.Messages.Count; j++) {
                        ChatMessage message = channel.Messages[j];
                        if (!message.Outgoing && !message.System && !seen.Contains(message.Id)) added++;
                    }
                }
                int prior = 0;
                if (sameSession && previous != null) previous.TryGetValue(channel.Name, out prior);
                bool live = viewing && active == channel.Name;
                next[channel.Name] = live ? 0 : (sameSession ? prior : 0) + added;
            }
            return next;
        }
    }
}
