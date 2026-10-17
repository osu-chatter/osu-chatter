using System;

namespace Chatter.Shared {
    /// <summary>Rules shared with the bridge loaded into osu!'s separate process.</summary>
    internal static class SharedChatRules {
        static readonly int[] MenuStates = { 0, 5, 7, 11, 12, 13, 14, 15, 17, 18 };

        internal static bool MenuOrLobby(int state) {
            for (int i = 0; i < MenuStates.Length; i++) if (MenuStates[i] == state) return true;
            return false;
        }

        internal static void ValidateText(string text) {
            if (string.IsNullOrEmpty(text) || text.Trim().Length == 0 || text.Length > 400
                || HasControl(text) || text.TrimStart().StartsWith("/", StringComparison.Ordinal))
                throw new InvalidOperationException("Use a single message of at most 400 characters; chat commands are not sent");
        }

        internal static void ValidateTarget(string target) {
            if (string.IsNullOrWhiteSpace(target) || target.Length > 160 || HasControl(target)
                || target == "#userlog" || target == "#highlight" || target[0] == '/')
                throw new InvalidOperationException("Invalid conversation");
        }

        internal static bool HasControl(string text) {
            for (int i = 0; i < text.Length; i++) if (char.IsControl(text[i])) return true;
            return false;
        }
    }
}
