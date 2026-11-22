using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Chatter {
    public sealed class ExperienceOptions {
        public bool AlwaysOnTop { get; set; }
        public bool MinimizeToTray { get; set; }
        public bool NotifyPrivateMessages { get; set; }
        public bool SendWithoutTranslation { get; set; }
        public bool NativeCommands { get; set; }
        public string NativePresentation { get; set; } = "translation";
        public string HistoryPolicy { get; set; } = "recent";
        public int HistoryCount { get; set; } = 40;
        public int ContextMessages { get; set; } = 6;
        public string IncomingSource { get; set; } = "auto";
        public string Glossary { get; set; } = "osu!, Bancho, beatmap, pp, FC, DT, HD, HR";
        public bool OverlayInteractive { get; set; } = true;
        public int OverlayX { get; set; } = -1;
        public int OverlayY { get; set; } = -1;
        public int OverlayWidth { get; set; } = 500;
        public int OverlayHeight { get; set; } = 380;
        public int OverlayOpacity { get; set; } = 95;
        public int MessageFontSize { get; set; } = 14;
        public string ReplyShortcut { get; set; } = "Control, Shift, Enter";
        public string OverlayShortcut { get; set; } = "Control, Shift, O";
        public string ModelVariant { get; set; } = "fast";
        public string DownloadDirectory { get; set; } = "";
        public string ModelPath { get; set; } = "";
        public string RuntimePath { get; set; } = "";
        public string ChatEndpoint { get; set; } = "http://127.0.0.1:8080/v1/chat/completions";
        public string ChatModel { get; set; } = "local";
        public int CpuThreads { get; set; } = 2;
        public int GpuLayers { get; set; }
        public int WindowX { get; set; } = int.MinValue;
        public int WindowY { get; set; } = int.MinValue;
        public int WindowWidth { get; set; } = 1100;
        public int WindowHeight { get; set; } = 760;
        public bool Maximized { get; set; }
        public bool ModernTranscript { get; set; } = true;
        public ExperienceOptions Copy() => (ExperienceOptions)MemberwiseClone();
        public void Validate() {
            new TranslationRequest { Endpoint = ChatEndpoint, Text = "test", Source = "en", Target = "pl" }.Validate();
            if (string.IsNullOrWhiteSpace(ChatModel) || ChatModel.Length > 200) throw new InvalidOperationException("Choose a valid chat model identifier.");
            if (ModelVariant != "fast" && ModelVariant != "quality") throw new InvalidOperationException("Choose a valid local-model size.");
            if (!new[] { "translation", "both", "original" }.Contains(NativePresentation)
                || !new[] { "new", "recent", "all" }.Contains(HistoryPolicy)
                || (IncomingSource != "auto" && Array.IndexOf(ChatRules.Languages, IncomingSource) < 0)
                || ContextMessages < 0 || ContextMessages > 12 || HistoryCount < 1 || HistoryCount > 200
                || OverlayOpacity < 30 || OverlayOpacity > 100 || MessageFontSize < 11 || MessageFontSize > 24
                || CpuThreads < 1 || CpuThreads > 32 || GpuLayers < 0 || GpuLayers > 100
                || OverlayWidth < 320 || OverlayWidth > 2400 || OverlayHeight < 220 || OverlayHeight > 1600
                || WindowWidth < 800 || WindowWidth > 7680 || WindowHeight < 550 || WindowHeight > 4320
                || (DownloadDirectory ?? "").Length > 2048 || (Glossary ?? "").Length > 2000 || (ModelPath ?? "").Length > 2048 || (RuntimePath ?? "").Length > 2048
                || (ReplyShortcut ?? "").Length > 80 || (OverlayShortcut ?? "").Length > 80)
                throw new InvalidOperationException("Check the appearance, model and translation options.");
        }
    }

    public static class TranslationContext {
        static readonly Regex Urls = new Regex(@"(?:https?://|osu://|www\.)[^\s<>\[\]]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        public static string Recent(ChatChannel? channel, int limit) {
            if (channel == null) return "";
            var list = new List<ChatMessage>(channel.Messages) { new ChatMessage("context-draft", "", "", DateTime.Now.ToString("HH:mm:ss"), true, false) };
            return Before(new ChatChannel(channel.Name, channel.Private, channel.Selected, list), "context-draft", limit);
        }
        public static string Before(ChatChannel channel, string id, int limit) {
            int at = -1;
            for (int i = 0; i < channel.Messages.Count; i++) if (channel.Messages[i].Id == id) { at = i; break; }
            if (at <= 0 || limit <= 0) return "";
            var rows = new List<string>(); int size = 0;
            for (int i = at - 1; i >= 0 && rows.Count < Math.Min(12, limit); i--) {
                ChatMessage message = channel.Messages[i];
                if (message.System) continue;
                // A short, fixed snapshot: later messages never rewrite earlier translations.
                if (TimeSpan.TryParse(channel.Messages[at].Time, out var end) && TimeSpan.TryParse(message.Time, out var start)
                    && end >= start && end - start > TimeSpan.FromMinutes(3)) break;
                string row = message.Sender + ": " + message.Text;
                if (size + row.Length > 2200) break;
                rows.Add(row); size += row.Length;
            }
            rows.Reverse(); return string.Join("\n", rows);
        }
        public static string Source(string text, string channel, string configured) {
            if (configured != "auto" && Array.IndexOf(ChatRules.Languages, configured) >= 0) return configured;
            // Only use a channel hint where the message actually uses that script.
            string hint = ChatRules.ChannelLanguage(channel);
            if ((hint == "ru" || hint == "uk") && Regex.IsMatch(text, @"[\u0400-\u04ff]")) return hint;
            if (hint == "ar" && Regex.IsMatch(text, @"[\u0600-\u06ff]")) return "ar";
            if (hint == "he" && Regex.IsMatch(text, @"[\u0590-\u05ff]")) return "he";
            if (hint == "ko" && Regex.IsMatch(text, @"[\uac00-\ud7af]")) return "ko";
            if (hint == "ja" && Regex.IsMatch(text, @"[\u3040-\u30ff]")) return "ja";
            return "auto";
        }
        internal static string ProtectLinks(string text, out Dictionary<string, string> links) {
            return ProtectLinks(text, new List<ChatLinks.Span>(), out links);
        }
        internal static string ProtectLinks(string text, IEnumerable<ChatLinks.Span> spans, out Dictionary<string, string> links) {
            var ranges = spans.Where(s => s.Index >= 0 && s.Length > 0 && s.Index <= text.Length - s.Length).Select(s => Tuple.Create(s.Index, s.Length)).ToList();
            foreach (Match match in Urls.Matches(text)) ranges.Add(Tuple.Create(match.Index, match.Length));
            var saved = new Dictionary<string, string>(); var result = new System.Text.StringBuilder(); int at = 0;
            foreach (var range in ranges.OrderBy(r => r.Item1).ThenByDescending(r => r.Item2)) {
                if (range.Item1 < at) continue;
                result.Append(text.Substring(at, range.Item1 - at));
                string key = "ZXQLINK" + saved.Count + "QXZ"; saved[key] = text.Substring(range.Item1, range.Item2); result.Append(key); at = range.Item1 + range.Item2;
            }
            result.Append(text.Substring(at)); links = saved; return result.ToString();
        }
        internal static string RestoreLinks(string text, Dictionary<string, string> links) {
            foreach (var link in links) text = text.Replace(link.Key, link.Value);
            if (Regex.IsMatch(text, @"ZXQLINK\d+QXZ")) throw new InvalidOperationException("The translator changed a protected link. Try another engine.");
            foreach (var link in links) if (!text.Contains(link.Value)) throw new InvalidOperationException("The translator omitted a protected link. Try another engine.");
            return text;
        }
        public static bool IsOnlyLink(string text) => Urls.Replace(text ?? "", "").Trim().Length == 0;
    }
}
