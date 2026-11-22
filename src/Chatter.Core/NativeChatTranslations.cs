using System.Collections.Generic;
using System.IO;
using System.Text;
using Chatter.Shared;

namespace Chatter {
    public static class NativeChatTranslations {
        public static Dictionary<string, string> Ready(ChatChannel channel, IDictionary<string, string> translations, bool showOriginal = false) {
            var result = new Dictionary<string, string>();
            int bytes = 4;
            for (int i = channel.Messages.Count - 1; i >= System.Math.Max(0, channel.Messages.Count - NativeChatProtocol.MaxMessages); i--) {
                ChatMessage message = channel.Messages[i];
                if (message.Outgoing || message.System || !translations.TryGetValue(message.Id, out string text)) continue;
                if (text == null) continue;
                text = text.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
                if (showOriginal && text != message.Text) text += "  [" + message.Text + "]";
                try { NativeChatProtocol.Validate(message.Id, text); }
                catch (InvalidDataException) { continue; }
                // Two bounded 7-bit string prefixes, with room for either prefix to grow.
                int cost = Encoding.UTF8.GetByteCount(message.Id) + Encoding.UTF8.GetByteCount(text) + 10;
                if (bytes + cost > NativeChatProtocol.MaxPayload) continue;
                result[message.Id] = text;
                bytes += cost;
            }
            return result;
        }
    }
}
