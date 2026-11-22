using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Chatter;

namespace Chatter.App {
    public sealed partial class MainForm {
        readonly Label translationProgress = new Label { ForeColor = Palette.Muted, Font = UiFont.Small, BackColor = Palette.Bg };
        readonly Label characterCount = new Label { ForeColor = Palette.Muted, Font = UiFont.Small, BackColor = Palette.Bg };
        readonly Dictionary<string, string> translationStates = new Dictionary<string, string>();
        readonly HashSet<string> baseline = new HashSet<string>();
        readonly Queue<Tuple<string, string, string>> manualQueue = new Queue<Tuple<string, string, string>>();
        readonly NotifyIcon tray = new NotifyIcon { Text = "osu-chatter", Icon = AppIcon.Create() };
        string lastSubmitted = "";
        string previewedDraft = "";
        bool previewTooLong;
        bool quitting;
        public void EnableReleasePreview() { Text = "osu-chatter · release preview"; transcript.UseModern(true, preferences.Experience.MessageFontSize); }
        internal string TranscriptState => transcript.ModernStatus;
        static string TranslationSettingsKey(AppPreferences value) {
            var request = new TranslationRequest { Text = "settings", Source = "en", Target = "pl" };
            value.ApplyProvider(request, false);
            return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new { request, value.IncomingLanguage, value.OutgoingSource, value.OutgoingLanguage, value.Channels, value.Experience.IncomingSource, value.Experience.ContextMessages, value.Experience.Glossary });
        }
        DateTime lastPrivateNotification;
        void NotifyNewPrivateMessages(ChatSnapshot next, bool same) {
            if (!same || !reading || DemoMode || !preferences.Experience.NotifyPrivateMessages || ContainsFocus || DateTime.UtcNow - lastPrivateNotification < TimeSpan.FromSeconds(5)) return;
            foreach (var room in next.Channels) {
                if (!room.Private || historyIds == null || !historyIds.TryGetValue(room.Name, out var known)) continue;
                if (!room.Messages.Any(m => !m.Outgoing && !m.System && !known.Contains(m.Id))) continue;
                lastPrivateNotification = DateTime.UtcNow; tray.Visible = true;
                tray.ShowBalloonTip(5000, "New osu! private message", "Open Chatter to read your conversation.", ToolTipIcon.Info);
                break;
            }
        }
        void UpdateTranslationStatus() {
            if (IsDisposed) return;
            string text = translatorStatus;
            if (text.Length == 0) {
                if (preferences.Endpoint == Translator.NoTranslation) text = "Chat mode · translation is off";
                else if (preferences.Endpoint == Translator.LocalModel && !System.IO.File.Exists(preferences.Experience.ModelPath)) text = "Local AI needs a model · open Settings → Models";
                else if (translating) text = "Translating · " + preferences.ProviderSummary().Replace("\n", " · ");
                else text = "Translation ready · " + translations.Count + " completed" + (translationErrors.Count > 0 ? " · " + translationErrors.Count + " failed (right-click to retry)" : "");
            }
            if (preferences.Experience.NativeCommands) text += " · " + (commandsReady ? "/t ready" : !reading ? "/t paused" : snapshot?.MenuOrLobby == false ? "/t requires menus or lobby" : commandError.Length > 0 ? "/t unavailable" : "/t connecting");
            if (translationProgress.Text != text) translationProgress.Text = text;
        }
        void RequestManual(ChatChannel channel, ChatMessage message, string target = "") {
            if (translating) { manualQueue.Enqueue(Tuple.Create(channel.Name, message.Id, target)); translationStates[message.Id] = "Queued"; return; }
            TranslateOne(channel, message, true, target);
        }
        SettingsView? settingsView;
        void OpenAdvancedSettings() {
            if (settingsView != null) { settingsView.Focus(); return; }
            if (busy) { notice = "Finish the current send before changing settings."; Render(); return; }
            var view = new SettingsView(preferences, selected, new Size(Math.Max(1, ClientSize.Width - Px(252)), ClientSize.Height)); settingsView = view; settingsOpen = true;
            view.BackRequested += delegate { CloseEmbeddedSettings(); };
            view.Saved += ApplySettings;
            Controls.Add(view); settingsButton.Filled = true; LayoutChrome(); view.Focus();
        }
        void ApplySettings(AppPreferences updated) {
            if (busy) throw new InvalidOperationException("Finish the current send before saving settings.");
            updated.Validate();
            if (!DemoMode) PreferencesStore.Save(updated);
            var previous = preferences; preferences = updated.Copy();
            System.Threading.Interlocked.Increment(ref commandGeneration);
            gameOverlay?.Dispose(); gameOverlay = null;
            ApplyPreferences();
            if (TranslationSettingsKey(previous) != TranslationSettingsKey(preferences)) BumpTranslation();
            else Render();
            if (preferences.UsesLocalTranslator && !preparingTranslator && (!previous.UsesLocalTranslator || previous.Endpoint != preferences.Endpoint || previous.ManualEndpoint != preferences.ManualEndpoint)) PrepareTranslator();
            KickAuto();
        }
        void CloseEmbeddedSettings(bool render = true) {
            var view = settingsView; settingsView = null; settingsOpen = false; settingsButton.Filled = false;
            if (view != null) { Controls.Remove(view); view.Dispose(); }
            if (render) { Render(); KickAuto(); draft.Input.Focus(); }
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
            if (keyData == (Keys.Control | Keys.S) && settingsView != null) { settingsView.SaveChanges(); return true; }
            if (keyData == (Keys.Control | Keys.F) && settingsView != null) return true;
            if (keyData == Keys.Escape && settingsView != null) { CloseEmbeddedSettings(); return true; }
            if (DemoMode && keyData == (Keys.Control | Keys.Shift | Keys.R)) { ShowPreviewReply(); return true; }
            if (DemoMode && keyData == (Keys.Control | Keys.Shift | Keys.O)) {
                if (gameOverlay != null) { gameOverlay.Dispose(); gameOverlay = null; return true; }
                gameOverlay = new GameChatOverlay { PreviewBounds = () => RectangleToScreen(ClientRectangle) };
                gameOverlay.Configure(preferences.Experience);
                gameOverlay.ReplyRequested += _ => ShowPreviewReply();
                gameOverlay.SetChat(System.Diagnostics.Process.GetCurrentProcess().Id, Current(), true, translations, translationErrors); return true;
            }
            if (keyData == (Keys.Control | Keys.Oemcomma)) { OpenAdvancedSettings(); return true; }
            if (keyData == (Keys.Control | Keys.F)) { transcript.FindInConversation(); return true; }
            if (keyData == (Keys.Control | Keys.Tab) || keyData == (Keys.Control | Keys.Shift | Keys.Tab)) {
                if (snapshot != null && snapshot.Channels.Count > 0) {
                    int at = 0; for (int i = 0; i < snapshot.Channels.Count; i++) if (snapshot.Channels[i].Name == selected) at = i;
                    at = (at + ((keyData & Keys.Shift) != 0 ? snapshot.Channels.Count - 1 : 1)) % snapshot.Channels.Count; Select(snapshot.Channels[at].Name);
                }
                return true;
            }
            if (keyData == Keys.Up && draft.Input.Focused && draft.Input.TextLength == 0 && lastSubmitted.Length > 0) { draft.Input.Text = lastSubmitted; draft.Input.SelectionStart = draft.Input.TextLength; return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        void ShowPreviewReply() {
            var reply = new GameReplyForm("Preview · no messages sent", "English", "Russian", false,
                text => new TranslationRequest { Text = text, Source = "en", Target = "ru" },
                request => System.Threading.Tasks.Task.FromResult("Preview: " + request.Text),
                request => System.Threading.Tasks.Task.FromException<SendReceipt>(new InvalidOperationException("Preview never submits chat messages.")));
            reply.StartPosition = FormStartPosition.CenterParent; reply.Show(this);
        }
        void RestoreWindowPlacement() {
            var options = preferences.Experience;
            var area = Screen.FromControl(this).WorkingArea;
            MinimumSize = new Size(Math.Min(Px(980), area.Width), Math.Min(Px(640), area.Height));
            Size = new Size(Math.Min(options.WindowWidth, area.Width), Math.Min(options.WindowHeight, area.Height));
            var saved = new Rectangle(options.WindowX, options.WindowY, Width, Height);
            if (options.WindowX != int.MinValue && Screen.AllScreens.Any(s => Rectangle.Intersect(s.WorkingArea, saved).Width >= 100)) { StartPosition = FormStartPosition.Manual; Location = saved.Location; }
            if (options.Maximized) WindowState = FormWindowState.Maximized;
            tray.DoubleClick += delegate { Show(); WindowState = FormWindowState.Normal; Activate(); tray.Visible = false; };
            var menu = new ContextMenuStrip(); menu.Items.Add("Show Chatter", null, delegate { Show(); Activate(); tray.Visible = false; });
            menu.Items.Add("Quit", null, delegate { quitting = true; Close(); }); tray.ContextMenuStrip = menu;
            Resize += delegate { if (WindowState == FormWindowState.Minimized && preferences.Experience.MinimizeToTray) { tray.Visible = true; Hide(); } };
        }
        void SaveWindowPlacement() {
            if (DemoMode) return;
            Rectangle bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            var options = preferences.Experience; options.WindowX = bounds.X; options.WindowY = bounds.Y;
            options.WindowWidth = Math.Max(800, bounds.Width); options.WindowHeight = Math.Max(550, bounds.Height); options.Maximized = WindowState == FormWindowState.Maximized;
            SavePreferences();
        }
        protected override void OnFormClosing(FormClosingEventArgs e) {
            if (!DemoMode && !quitting && e.CloseReason == CloseReason.UserClosing && preferences.Experience.MinimizeToTray) { e.Cancel = true; tray.Visible = true; Hide(); }
            base.OnFormClosing(e);
        }
    }
}
