using System;
using System.Collections.Generic;
using System.Threading;
using Chatter;

namespace Chatter.Tests {
    internal static class NativeChatTests {
        internal static void Batch() {
            var messages = new List<ChatMessage> {
                new ChatMessage("abc-0", "Friend", "Original", "12:00:00", false, false),
                new ChatMessage("abc-1", "Me", "Outgoing", "12:00:00", true, false),
                new ChatMessage("abc-2", "", "System", "12:00:00", false, true),
                new ChatMessage("abc-3", "Friend", "Other", "12:00:00", false, false)
            };
            var translations = new Dictionary<string, string> { ["abc-0"] = "Hello\nworld 👋", ["abc-1"] = "Own", ["abc-2"] = "System", ["abc-3"] = "bad\0text" };
            var ready = NativeChatTranslations.Ready(new ChatChannel("#test", false, true, messages), translations);
            if (ready.Count != 1 || ready["abc-0"] != "Hello world 👋" || messages[0].Text != "Original" || translations["abc-0"] != "Hello\nworld 👋")
                throw new InvalidOperationException("Native batch changed original data or included ineligible messages");
            messages.Clear(); translations.Clear();
            for (int i = 0; i < 250; i++) {
                string id = i.ToString("x") + "-0";
                messages.Add(new ChatMessage(id, "Friend", "Original", "12:00:00", false, false));
                translations[id] = new string('漢', 4096);
            }
            ready = NativeChatTranslations.Ready(new ChatChannel("#test", false, true, messages), translations);
            if (!ready.ContainsKey("f9-0") || ready.Count >= 200 || ready.Count == 0 || ready.ContainsKey("0-0"))
                throw new InvalidOperationException("Native batch must bound bytes and prioritize recent messages");
        }

        // Explicit opt-in live check. Changes only local render caches, restores
        // in finally, and never calls Check/TranslateAndSend/the network sender.
        internal static void Live() {
            using (var chat = new OsuChat()) {
                ChatSnapshot before = chat.Read();
                ChatChannel? selected = null;
                foreach (ChatChannel channel in before.Channels) if (channel.Selected) selected = channel;
                if (selected == null) throw new InvalidOperationException("Open osu! chat first");
                var rows = new Dictionary<string, string>();
                foreach (ChatMessage message in selected.Messages)
                    if (!message.Outgoing && !message.System) rows[message.Id] = "[Chatter display test] " + message.Text;
                if (rows.Count == 0) throw new InvalidOperationException("Select a conversation with incoming messages");
                var bridge = new BridgeClient(ChatService.DefaultBridgePath());
                try {
                    int count = bridge.Display(new SendPlan(chat.Identity(), selected.Name), rows);
                    Console.WriteLine("native rendered=" + count);
                    if (count == 0) throw new InvalidOperationException("Open the chat panel in osu! so messages are rendered");
                    ChatSnapshot after = chat.Read();
                    var observed = new Dictionary<string, ChatMessage>();
                    foreach (ChatChannel channel in after.Channels) if (channel.Name == selected.Name)
                        foreach (ChatMessage current in channel.Messages) observed[current.Id] = current;
                    foreach (ChatMessage original in selected.Messages) {
                        if (!rows.ContainsKey(original.Id)) continue;
                        if (!observed.TryGetValue(original.Id, out ChatMessage current) || current.Text != original.Text || current.Source != original.Source)
                            throw new InvalidOperationException("Native display changed original message identity, text or link source");
                    }
                    for (int i = 0; i < 6; i++) {
                        bridge.Display(new SendPlan(chat.Identity(), selected.Name), rows);
                        Thread.Sleep(1000);
                    }
                    Thread.Sleep(3500); // The bridge must also restore without a clear request.
                } finally { bridge.RestoreDisplay(); }
            }
        }
    }
}
