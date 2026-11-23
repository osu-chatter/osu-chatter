using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Chatter;

namespace Chatter.App {
    sealed partial class TranscriptView {
        ModernTranscript? modern;
        int textSize = 14;
        public Dictionary<string, string> Statuses = new Dictionary<string, string>();
        readonly Dictionary<Image, string> imageData = new Dictionary<Image, string>();
        readonly Dictionary<Image, string> imageKeys = new Dictionary<Image, string>();
        int nextImageKey;
        bool modernSyncPending, modernStick;
        public void RefreshPlayers() { SyncModern(false); Invalidate(); }
        public void RefreshAnimation() { if (modern == null && Visible) Invalidate(); }
        public string ModernStatus { get; private set; } = "";
        public async void UseModern(bool enabled, int fontSize) {
            textSize = fontSize;
            if (!enabled) { if (modern == null) return; modern.Dispose(); modern = null; Relayout(); return; }
            if (modern != null) { SyncModern(false); return; }
            var view = new ModernTranscript(); modern = view; blocks.Clear();
            view.Action += HandleModernAction;
            view.VisibleMessagesChanged += delegate { VisibleMessagesChanged?.Invoke(); };
            try {
                Controls.Add(view); view.BringToFront(); await view.Initialize();
                if (!IsDisposed && ReferenceEquals(modern, view)) { ModernStatus = "Color emoji and text selection enabled"; SyncModern(true); }
            } catch (Exception ex) {
                if (IsDisposed || !ReferenceEquals(modern, view)) return; ModernStatus = "Using built-in transcript: " + ex.GetType().Name;
                view.Dispose(); if (ReferenceEquals(modern, view)) { modern = null; Relayout(); }
            }
        }
        public event Action? VisibleMessagesChanged;
        public event Action<ChatMessage, string>? TargetTranslationRequested;
        ContextMenuStrip? translationTargetMenu;
        ContextMenuStrip? messageMenu;
        ChatMessage? translationTargetMessage;
        void PickTranslationTarget(ChatMessage message) {
            if (IsDisposed || Disposing) return;
            translationTargetMessage = message;
            if (translationTargetMenu == null) {
                translationTargetMenu = new ContextMenuStrip();
                for (int i = 0; i < ChatRules.Languages.Length; i++) {
                    string target = ChatRules.Languages[i];
                    translationTargetMenu.Items.Add(ChatRules.LanguageNames[i], null, delegate {
                        if (translationTargetMessage != null) TargetTranslationRequested?.Invoke(translationTargetMessage, target);
                    });
                }
            }
            translationTargetMenu.Show(Cursor.Position);
        }
        protected override void Dispose(bool disposing) {
            if (disposing) {
                // Closed runs before WinForms finishes reparenting its dropdown window.
                // Reuse these menus and dispose them with the transcript, never inside Closed.
                messageMenu?.Dispose();
                translationTargetMenu?.Dispose();
                translationTargetMessage = null;
            }
            base.Dispose(disposing);
        }
        public HashSet<string> VisibleMessageIds() {
            if (modern != null) return new HashSet<string>(modern.VisibleIds);
            return new HashSet<string>(blocks.Where(b => b.Bubble.Bottom >= scroll && b.Bubble.Top <= scroll + Height).Select(b => b.Message.Id));
        }
        public void FindInConversation() {
            if (modern != null) { modern.Find(); return; }
            using (var form = new Form { Text = "Find in conversation", Width = 380, Height = 140, StartPosition = FormStartPosition.CenterParent }) {
                var input = new TextBox { Dock = DockStyle.Top }; var find = new Button { Dock = DockStyle.Bottom, Text = "Find" };
                form.Controls.Add(input); form.Controls.Add(find);
                find.Click += delegate { var block = blocks.Find(b => b.Message.Text.IndexOf(input.Text, StringComparison.CurrentCultureIgnoreCase) >= 0 || (translations.TryGetValue(b.Message.Id, out string text) && text.IndexOf(input.Text, StringComparison.CurrentCultureIgnoreCase) >= 0)); if (block != null) { scroll = Clamp(block.Bubble.Top); pinned = false; Invalidate(); form.Close(); } };
                form.AcceptButton = find; form.ShowDialog(this);
            }
        }
        void HandleModernAction(string kind, string id, string value) {
            ChatMessage message = messages.FirstOrDefault(m => m.Id == id);
            if (kind == "open") { if (ChatLinks.CanOpen(value)) OpenExternal(value); return; }
            if (message == null) return;
            if (kind == "translate") TranslateRequested?.Invoke(message);
            else if (kind == "translate-to") PickTranslationTarget(message);
            else if (kind == "copy" && !string.IsNullOrEmpty(value)) { try { Clipboard.SetText(value); } catch (System.Runtime.InteropServices.ExternalException) { } }
            else if (kind == "profile") { var face = FaceOf(message.Sender); if (face?.Id > 0) OpenExternal("https://osu.ppy.sh/users/" + face.Id); }
        }
        void SyncModern(bool stick) {
            if (modern == null) return;
            modernStick |= stick;
            if (modernSyncPending) return;
            if (!IsHandleCreated) { bool immediateStick = modernStick; modernStick = false; SyncModernCore(immediateStick); return; }
            modernSyncPending = true;
            BeginInvoke(new Action(() => {
                modernSyncPending = false;
                bool pendingStick = modernStick; modernStick = false;
                if (!IsDisposed) SyncModernCore(pendingStick);
            }));
        }
        void SyncModernCore(bool stick) {
            if (modern == null) return;
            var avatars = new Dictionary<string, string>();
            var rows = messages.Select(message => {
                var face = FaceOf(message.Sender); string avatar = "";
                if (face?.Image != null) {
                    if (!imageData.TryGetValue(face.Image, out avatar)) {
                        try {
                            using (var stream = new MemoryStream()) {
                                bool animated = face.Image.RawFormat.Guid == ImageFormat.Gif.Guid;
                                if (animated) face.Image.Save(stream, ImageFormat.Gif);
                                if (!animated || stream.Length > 131072) {
                                    animated = false; stream.SetLength(0);
                                    using (var small = new Bitmap(32, 32)) { using (var graphics = Graphics.FromImage(small)) graphics.DrawImage(face.Image, new Rectangle(0, 0, 32, 32)); small.Save(stream, ImageFormat.Png); }
                                }
                                avatar = (animated ? "data:image/gif;base64," : "data:image/png;base64,") + Convert.ToBase64String(stream.ToArray());
                            }
                        }
                        catch { avatar = ""; }
                        if (imageData.Count > 512) { imageData.Clear(); imageKeys.Clear(); }
                        imageData[face.Image] = avatar; imageKeys[face.Image] = "avatar" + ++nextImageKey;
                    }
                    if (avatar.Length > 0) { string key = imageKeys[face.Image]; avatars[key] = avatar; avatar = key; }
                }
                translations.TryGetValue(message.Id, out string translated); errors.TryGetValue(message.Id, out string error); Statuses.TryGetValue(message.Id, out string state);
                return new { id = message.Id, sender = message.Sender, text = message.Text, time = message.Time ?? "", own = message.Outgoing, system = message.System,
                    action = message.Action, translated, error, state, avatar, supporter = face?.Supporter == true, links = ChatLinks.Find(message.Text, message.Source) };
            }).ToArray();
            modern.UpdateRows(rows, stick, ShowTranslate, textSize, EmptyTitle + "\n" + EmptyBody, avatars);
        }
    }
}
