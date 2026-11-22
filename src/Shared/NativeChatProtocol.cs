using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Chatter.Shared {
    // Display updates have their own bounded payload and never use the sender.
    internal static class NativeChatProtocol {
        internal const int MaxMessages = 200;
        internal const int MaxPayload = 262144;
        internal const int MaxText = 4096;

        internal static byte[] Encode(IDictionary<string, string> translations) {
            if (translations == null || translations.Count > MaxMessages) throw new InvalidDataException("Too many chat translations");
            using (var stream = new MemoryStream()) {
                using (var writer = new BinaryWriter(stream, new UTF8Encoding(false, true), true)) {
                    writer.Write(translations.Count);
                    foreach (var item in translations) { Validate(item.Key, item.Value); writer.Write(item.Key); writer.Write(item.Value); }
                }
                if (stream.Length > MaxPayload) throw new InvalidDataException("Chat translations are too large");
                return stream.ToArray();
            }
        }

        internal static Dictionary<string, string> Decode(byte[] payload) {
            if (payload.Length > MaxPayload) throw new InvalidDataException();
            using (var stream = new MemoryStream(payload)) using (var reader = new BinaryReader(stream, new UTF8Encoding(false, true))) {
                int count = reader.ReadInt32();
                if (count < 0 || count > MaxMessages) throw new InvalidDataException();
                var result = new Dictionary<string, string>(StringComparer.Ordinal);
                for (int i = 0; i < count; i++) {
                    string id = reader.ReadString(), text = reader.ReadString();
                    Validate(id, text);
                    result.Add(id, text);
                }
                if (stream.Position != stream.Length) throw new InvalidDataException();
                return result;
            }
        }

        internal static void Validate(string id, string text) {
            if (string.IsNullOrEmpty(id) || id.Length > 40 || string.IsNullOrWhiteSpace(text) || text.Length > MaxText)
                throw new InvalidDataException("Invalid chat translation");
            foreach (char c in id) if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f') && c != '-') throw new InvalidDataException();
            for (int i = 0; i < text.Length; i++) {
                char c = text[i];
                if (char.IsControl(c)) throw new InvalidDataException("Chat translations must fit on one line");
                if (char.IsHighSurrogate(c)) {
                    if (++i >= text.Length || !char.IsLowSurrogate(text[i])) throw new InvalidDataException();
                } else if (char.IsLowSurrogate(c)) throw new InvalidDataException();
            }
        }
    }

    internal static class ChatMessageIdentity {
        internal static string DisplaySender(string sender, string time) {
            string stamp = time.Substring(0, 5) + " ";
            if (!sender.StartsWith(stamp, StringComparison.Ordinal)) return sender;
            sender = sender.Substring(stamp.Length);
            if (sender.EndsWith(":", StringComparison.Ordinal)) sender = sender.Substring(0, sender.Length - 1);
            if (sender.StartsWith("*", StringComparison.Ordinal)) sender = sender.Substring(1);
            return sender.Trim();
        }

        internal static ulong Hash(ulong ticks, string name, string sender, string text) {
            ulong hash = 14695981039346656037UL;
            Add(ref hash, BitConverter.GetBytes(ticks));
            Add(ref hash, Encoding.UTF8.GetBytes(name));
            Add(ref hash, Encoding.UTF8.GetBytes(sender));
            Add(ref hash, Encoding.UTF8.GetBytes(text));
            return hash;
        }

        static void Add(ref ulong hash, byte[] bytes) {
            foreach (byte value in bytes) { hash ^= value; hash *= 1099511628211UL; }
        }
    }
}
