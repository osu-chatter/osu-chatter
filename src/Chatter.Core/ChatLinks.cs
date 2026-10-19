using System;
using System.Collections.Generic;
using System.Text;

namespace Chatter {
    /// <summary>
    /// Turns osu! chat markup into the text already shown in the bubble, and finds the links inside it.
    /// Opening a link stays in the app. Only http, https, and osu schemes are reported.
    /// </summary>
    public static class ChatLinks {
        public sealed class Span {
            public Span(int index, int length, string url) {
                Index = index;
                Length = length;
                Url = url;
            }

            public int Index { get; }
            public int Length { get; }
            public string Url { get; }
        }

        public static string Visible(string? raw) {
            if (raw == null || raw.Length == 0) return "";
            return Scan(raw, new List<Span>());
        }

        public static List<Span> Find(string? display, string? source) {
            var found = new List<Span>();
            string raw = source ?? display ?? "";
            string visible = raw.Length == 0 ? "" : Scan(raw, found);
            if (!string.Equals(visible, display ?? "", StringComparison.Ordinal)) return new List<Span>();
            return found;
        }

        public static bool CanOpen(string? url) {
            if (url == null || url.Length == 0) return false;
            for (int i = 0; i < url.Length; i++) if (url[i] < 32 || url[i] == 127 || url[i] == '\\') return false;
            int scheme = Scheme(url, 0);
            if (scheme < 0 || url.Length <= scheme) return false;
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri == null) return false;
            return uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals("osu", StringComparison.OrdinalIgnoreCase);
        }

        static string Scan(string raw, List<Span> spans) {
            var visible = new StringBuilder();
            int i = 0;
            while (i < raw.Length) {
                int next;
                if (raw[i] == '[' && (TryBracket(raw, i, visible, spans, out next) || TryMarkdown(raw, i, visible, spans, out next))) {
                    i = next;
                    continue;
                }
                if (raw[i] == '(' && TryOld(raw, i, visible, spans, out next)) {
                    i = next;
                    continue;
                }
                if (TryBare(raw, i, visible, spans, out next)) {
                    i = next;
                    continue;
                }
                visible.Append(raw[i]);
                i++;
            }
            return visible.ToString();
        }

        static bool TryBracket(string raw, int start, StringBuilder visible, List<Span> spans, out int next) {
            next = start;
            int i = start + 1;
            while (i < raw.Length && raw[i] != ' ' && raw[i] != '[' && raw[i] != ']') i++;
            if (i >= raw.Length || raw[i] != ' ') return false;
            string url = raw.Substring(start + 1, i - (start + 1));
            if (!IsUrl(url)) return false;
            int close = Matching(raw, start);
            if (close < 0) return false;
            AppendText(raw, i + 1, close, url, visible, spans);
            next = close + 1;
            return true;
        }

        static bool TryMarkdown(string raw, int start, StringBuilder visible, List<Span> spans, out int next) {
            next = start;
            int close = Matching(raw, start);
            if (close < 0 || close + 1 >= raw.Length || raw[close + 1] != '(') return false;
            int end = raw.IndexOf(')', close + 2);
            if (end < 0) return false;
            string url = raw.Substring(close + 2, end - (close + 2));
            if (!IsUrl(url)) return false;
            AppendText(raw, start + 1, close, url, visible, spans);
            next = end + 1;
            return true;
        }

        static bool TryOld(string raw, int start, StringBuilder visible, List<Span> spans, out int next) {
            next = start;
            int close = raw.IndexOf(')', start + 1);
            if (close < 0 || close + 1 >= raw.Length || raw[close + 1] != '[') return false;
            int end = raw.IndexOf(']', close + 2);
            if (end < 0) return false;
            string url = raw.Substring(close + 2, end - (close + 2));
            if (!IsUrl(url)) return false;
            AppendText(raw, start + 1, close, url, visible, spans);
            next = end + 1;
            return true;
        }

        static bool TryBare(string raw, int start, StringBuilder visible, List<Span> spans, out int next) {
            next = start;
            int scheme = Scheme(raw, start);
            if (scheme < 0) return false;
            int i = start + scheme;
            while (i < raw.Length && !char.IsWhiteSpace(raw[i]) && raw[i] != '<' && raw[i] != '[') i++;
            int end = i;
            while (end > start + scheme && IsTrailing(raw[end - 1])) end--;
            if (end <= start + scheme) return false;
            string url = raw.Substring(start, end - start);
            if (!IsUrl(url)) return false;
            AppendText(raw, start, end, url, visible, spans);
            if (i > end) visible.Append(raw, end, i - end);
            next = i;
            return true;
        }

        static void AppendText(string raw, int from, int to, string url, StringBuilder visible, List<Span> spans) {
            int length = to - from;
            if (length <= 0) return;
            if (CanOpen(url)) spans.Add(new Span(visible.Length, length, url));
            visible.Append(raw, from, length);
        }

        static int Matching(string raw, int open) {
            int depth = 0;
            for (int i = open; i < raw.Length; i++) {
                if (raw[i] == '[') depth++;
                else if (raw[i] == ']') {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        static bool IsUrl(string value) {
            if (string.IsNullOrEmpty(value)) return false;
            int scheme = Scheme(value, 0);
            if (scheme < 0 || value.Length <= scheme) return false;
            for (int i = 0; i < value.Length; i++) {
                char c = value[i];
                if (char.IsWhiteSpace(c) || c < 32 || c == 127) return false;
            }
            return true;
        }

        static int Scheme(string value, int index) {
            if (Match(value, index, "https://")) return 8;
            if (Match(value, index, "http://")) return 7;
            if (Match(value, index, "osu://")) return 6;
            return -1;
        }

        static bool Match(string value, int index, string prefix) {
            if (index < 0 || index + prefix.Length > value.Length) return false;
            return string.Compare(value, index, prefix, 0, prefix.Length, StringComparison.OrdinalIgnoreCase) == 0;
        }

        static bool IsTrailing(char value) {
            return value == '.' || value == ',' || value == ';' || value == ':' || value == '!' || value == '?' || value == ')' || value == ']';
        }
    }
}
