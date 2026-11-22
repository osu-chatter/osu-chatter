using System;
namespace Chatter {
    public sealed class GameTranslationCommand {
        internal GameTranslationCommand(string id, int pid, int account, string target, string language, string text, string original, string error, ulong deadline) {
            Id = id; Identity = new PlayerIdentity(pid, account); Target = target; Language = language; Text = text; Original = original; Error = error; Deadline = deadline;
        }
        public string Id { get; }
        public PlayerIdentity Identity { get; }
        public string Target { get; }
        public string Language { get; }
        public string Text { get; }
        public string Original { get; }
        public string Error { get; }
        public ulong Deadline { get; }
    }
}
