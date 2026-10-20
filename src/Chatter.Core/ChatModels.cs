using System;
using System.Collections.Generic;
using Chatter.Shared;

namespace Chatter {
    public sealed class ChatMessage {
        public ChatMessage(string id, string sender, string text, string time, bool outgoing, bool system, string? source = null) {
            Id = id;
            Sender = sender;
            Text = text;
            Time = time;
            Outgoing = outgoing;
            System = system;
            Source = string.IsNullOrEmpty(source) ? text : source!;
        }

        public string Id { get; }
        public string Sender { get; }
        public string Text { get; }
        public string Time { get; }
        public bool Outgoing { get; }
        public bool System { get; }
        /// <summary>Original message text when it carries link markup. Otherwise the same as <see cref="Text"/>.</summary>
        public string Source { get; }
    }

    public sealed class ChatChannel {
        public ChatChannel(string name, bool privat, bool selected, IReadOnlyList<ChatMessage> messages) {
            Name = name;
            Private = privat;
            Selected = selected;
            Messages = messages;
        }

        public string Name { get; }
        public bool Private { get; }
        public bool Selected { get; }
        public IReadOnlyList<ChatMessage> Messages { get; }
    }

    public sealed class ChatSnapshot {
        public ChatSnapshot(int session, bool? connected, string account, IReadOnlyList<ChatChannel> channels, int accountId = 0) {
            Session = session;
            Connected = connected;
            Account = account ?? "";
            Channels = channels;
            AccountId = accountId;
        }

        public int Session { get; }
        public bool? Connected { get; }
        public string Account { get; }
        public IReadOnlyList<ChatChannel> Channels { get; }
        public int AccountId { get; }
    }

    public sealed class SendReceipt {
        public SendReceipt(string translated, bool echoed) {
            Translated = translated;
            Echoed = echoed;
        }

        public string Translated { get; }
        public bool Echoed { get; }
    }

    public interface IProcessMemory {
        byte[] ReadBytes(uint address, int size);
    }

    public static class ChatRules {
        // Compatibility wrappers retained for callers; raw game values live in the profile.
        public const int LoginConnected = StableCompatibilityProfile.Account.ConnectedLogin;
        public const string LocalTranslator = "local";

        public static readonly string[] Languages = {
            "en", "pl", "ru", "ko", "ja", "zh", "de", "fr", "es", "pt", "uk", "he", "ar", "tr"
        };
        public static readonly string[] LanguageNames = {
            "English", "Polish", "Russian", "Korean", "Japanese", "Chinese", "German", "French", "Spanish", "Portuguese", "Ukrainian", "Hebrew", "Arabic", "Turkish"
        };

        public static bool RightToLeft(string id) {
            return id == "he" || id == "ar";
        }

        public static bool RightToLeftText(string text) {
            if (string.IsNullOrEmpty(text)) return false;
            for (int i = 0; i < text.Length; i++) {
                char c = text[i];
                if (c >= '\u0590' && c <= '\u08FF') return true;
                if (c >= '\uFB1D' && c <= '\uFB4F') return true;
                if (c >= '\uFB50' && c <= '\uFDFF') return true;
                if (c >= '\uFE70' && c <= '\uFEFF') return true;
            }
            return false;
        }

        public static bool MenuOrLobby(int state) {
            return StableCompatibilityProfile.MenuOrLobby(state);
        }

        public static bool LoggedIn(int id, string name, int login) {
            return StableCompatibilityProfile.Account.LoggedIn(id, name, login);
        }

        public static string ChannelLanguage(string name) {
            if (string.IsNullOrEmpty(name)) return "en";
            if (System.Text.RegularExpressions.Regex.IsMatch(name, @"russian|(^|\W)ru($|\W)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return "ru";
            if (System.Text.RegularExpressions.Regex.IsMatch(name, @"korean|(^|\W)ko($|\W)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return "ko";
            if (System.Text.RegularExpressions.Regex.IsMatch(name, @"polish|(^|\W)pl($|\W)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return "pl";
            if (System.Text.RegularExpressions.Regex.IsMatch(name, @"hebrew|(^|\W)he($|\W)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return "he";
            if (System.Text.RegularExpressions.Regex.IsMatch(name, @"arabic|(^|\W)arab($|\W)|(^|\W)ar($|\W)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return "ar";
            if (System.Text.RegularExpressions.Regex.IsMatch(name, @"turkish|(^|\W)tr($|\W)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return "tr";
            return "en";
        }

        public static string NormalizeName(string value) {
            return (value ?? "").Replace('_', ' ');
        }

        public static void ValidateText(string text) {
            SharedChatRules.ValidateText(text);
        }

        public static bool HasControl(string text) {
            return SharedChatRules.HasControl(text);
        }
    }
}
