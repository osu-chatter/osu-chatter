using System;
using System.IO;
using System.Text;

namespace Chatter.Shared {
    internal sealed class CapturedTranslationCommand {
        internal string Id = "", Target = "", Language = "", Text = "", Original = "", Error = "";
        internal int Account;
        internal ulong Deadline;
    }
    internal static class NativeCommandProtocol {
        internal const int MaxPayload = 16384;
        internal static bool IsCommand(string text) {
            if (string.IsNullOrWhiteSpace(text)) return false;
            string first = text.TrimStart().Split(new[] { ' ', '\t' }, 2)[0];
            return first.Equals("/t", StringComparison.OrdinalIgnoreCase) || first.Equals("/translate", StringComparison.OrdinalIgnoreCase);
        }
        internal static void Parse(string input, out string language, out string text) {
            language = text = "";
            if (!IsCommand(input)) throw new InvalidOperationException("Not a translation command");
            string[] parts = input.Trim().Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3) throw new InvalidOperationException("Use /t <language> <message>, for example /t en cześć");
            language = parts[1].ToLowerInvariant(); text = parts[2].Trim();
            if (Array.IndexOf(new[] { "en", "pl", "ru", "ko", "ja", "zh", "de", "fr", "es", "pt", "uk", "he", "ar", "tr" }, language) < 0)
                throw new InvalidOperationException("Unknown target language. Use en, pl, ru, ko, ja, zh, de, fr, es, pt, uk, he, ar or tr.");
            SharedChatRules.ValidateText(text);
        }
        internal static byte[] Encode(CapturedTranslationCommand? item) {
            using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream, new UTF8Encoding(false, true))) {
                writer.Write(item != null);
                if (item != null) { writer.Write(item.Id); writer.Write(item.Target); writer.Write(item.Language); writer.Write(item.Text); writer.Write(item.Original); writer.Write(item.Error); writer.Write(item.Account); writer.Write(item.Deadline); }
                writer.Flush(); return stream.ToArray();
            }
        }
        internal static CapturedTranslationCommand? Decode(byte[] data) {
            if (data.Length > MaxPayload) throw new InvalidDataException();
            using (var stream = new MemoryStream(data)) using (var reader = new BinaryReader(stream, new UTF8Encoding(false, true))) {
                if (!reader.ReadBoolean()) { if (stream.Position != stream.Length) throw new InvalidDataException(); return null; }
                var item = new CapturedTranslationCommand { Id = reader.ReadString(), Target = reader.ReadString(), Language = reader.ReadString(), Text = reader.ReadString(), Original = reader.ReadString(), Error = reader.ReadString(), Account = reader.ReadInt32(), Deadline = reader.ReadUInt64() };
                if (!Guid.TryParseExact(item.Id, "N", out _) || item.Account <= 0 || item.Original.Length > 500 || item.Text.Length > 400 || item.Error.Length > 500 || stream.Position != stream.Length) throw new InvalidDataException();
                SharedChatRules.ValidateTarget(item.Target);
                if (item.Error.Length == 0) { Parse(item.Original, out string language, out string text); if (text != item.Text || language != item.Language) throw new InvalidDataException(); }
                return item;
            }
        }
    }
}
