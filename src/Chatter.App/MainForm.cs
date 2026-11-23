using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Chatter;

namespace Chatter.App {
    public sealed partial class MainForm : Form {
        readonly ChatService service = new ChatService();
        AppPreferences preferences;
        readonly PillButton preferencesButton = new PillButton { Caption = "Translation preferences…" };
        readonly Label providerSummary = new Label { ForeColor = Palette.Muted, Font = UiFont.Body, BackColor = Palette.Sidebar };
        readonly Dictionary<string, string> drafts = new Dictionary<string, string>();
        readonly Dictionary<string, string> translations = new Dictionary<string, string>();
        readonly Dictionary<string, string> translationErrors = new Dictionary<string, string>();
        readonly HashSet<string> manualTranslations = new HashSet<string>();
        readonly HashSet<string> attempted = new HashSet<string>();
        readonly ChannelRail rail = new ChannelRail();
        readonly ConversationHeader header = new ConversationHeader();
        readonly BannerBar banner = new BannerBar();
        readonly TranscriptView transcript = new TranscriptView();
        readonly PlayerDirectory profiles;
        readonly LineField draft = new LineField();
        readonly PillButton send = new PillButton { Filled = true, Caption = "Send" };
        readonly PillButton previewButton = new PillButton { Caption = "Preview" };
        readonly PillButton fromButton = new PillButton { Caption = "English", OpensMenu = true };
        readonly SwapButton swapButton = new SwapButton();
        readonly PillButton languageButton = new PillButton { Caption = "English", OpensMenu = true };
        readonly Label fromCaption = LanguageCaption("From");
        readonly Label toCaption = LanguageCaption("To");
        readonly ToolTip tips = new ToolTip();
        readonly Label preview = new Label();
        readonly Panel composer = new Panel();
        readonly Panel settings = new Panel();
        readonly PillButton incomingButton = new PillButton { Caption = "English" };
        readonly SwitchBox auto = new SwitchBox();
        readonly PillButton check = new PillButton { Caption = "Check sender" };
        readonly PillButton overlayButton = new PillButton { Caption = "Game overlay: Off" };
        readonly PillButton nativeChatButton = new PillButton { Caption = "In-game translation: Off" };
        readonly Label nativeChatHint = new Label { ForeColor = Palette.Muted, Font = UiFont.Small, BackColor = Palette.Sidebar,
            Text = "Uses this conversation's language and auto-translate setting. Originals stay in Chatter." };
        bool nativeUpdating, nativeWasActive;
        string nativeError = "";
        DateTime nativeUpdateAt = DateTime.MinValue;
        readonly PillButton pause = new PillButton { Caption = "Pause" };
        readonly PillButton settingsButton = new PillButton { Caption = "Settings" };
        readonly PillButton closeSettings = new PillButton { Caption = "Close" };

        ChatSnapshot? snapshot;
        Dictionary<string, int> unread = new Dictionary<string, int>();
        Dictionary<string, HashSet<string>>? historyIds;
        int historySession;
        string historyAccount = "";
        string selected = "";
        string readerError = "";
        CompatibilityCode? readerCode;
        string actionError = "";
        string notice = "";
        bool reading = true;
        bool busy;
        bool translating;
        bool translatorOffline;
        bool preparingTranslator;
        bool editingPreferences;
        string translatorStatus = "";
        bool settingsOpen;
        int pollGeneration;
        int translationGeneration;
        bool suppress;
        string viewSignature = "";
        string draftCue = "";
        GameChatOverlay? gameOverlay;
        GameReplyForm? gameReply;

        public bool DemoMode { get; set; }

        public MainForm() : this(new AppPreferences()) { }

        public MainForm(AppPreferences preferences) {
            this.preferences = preferences.Copy();
            profiles = new PlayerDirectory(this);
            profiles.Changed += delegate { if (!IsDisposed) transcript.RefreshPlayers(); };
            profiles.AnimationFrame += transcript.RefreshAnimation;
            transcript.Players = profiles.Face;
            transcript.VisibleMessagesChanged += KickAuto;
            Text = "osu-chatter";
            Icon = AppIcon.Create();
            Font = UiFont.Body;
            BackColor = Palette.Bg;
            ForeColor = Palette.Ink;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            ClientSize = new Size(1100, 720);
            MinimumSize = new Size(980, 640);
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            KeyPreview = true;
            Build();
            service.TranslationStatus += status => Post(() => {
                if (!this.preferences.UsesLocalTranslator) return;
                translatorStatus = status;
                UpdateTranslationStatus();
            });
        }

        protected override void OnHandleCreated(EventArgs e) {
            base.OnHandleCreated(e);
            int use = 1;
            DwmSetWindowAttribute(Handle, 19, ref use, 4);
            DwmSetWindowAttribute(Handle, 20, ref use, 4);
            int caption = ColorTranslator.ToWin32(Palette.Bg);
            int text = ColorTranslator.ToWin32(Palette.Ink);
            DwmSetWindowAttribute(Handle, 35, ref caption, 4);
            DwmSetWindowAttribute(Handle, 36, ref text, 4);
        }

        protected override void OnShown(EventArgs e) {
            base.OnShown(e);
            if (!DemoMode) {
                RestoreWindowPlacement();
                ApplyPreferences();
                reading = preferences.StartReading;
                pause.Caption = reading ? "Pause" : "Resume";
                if (reading) StartPoll();
                if (preferences.UsesLocalTranslator) PrepareTranslator();
            }
            LayoutChrome();
        }

        protected override void OnResize(EventArgs e) {
            base.OnResize(e);
            LayoutChrome();
        }

        protected override void OnFormClosed(FormClosedEventArgs e) {
            System.Threading.Interlocked.Increment(ref commandGeneration);
            SaveWindowPlacement();
            settingsView?.Dispose(); settingsView = null;
            tray.Dispose();
            pollGeneration++;
            translationGeneration++;
            gameOverlay?.Dispose();
            gameReply?.Dispose();
            service.Dispose();
            profiles.Dispose();
            transcript.UseModern(false, preferences.Experience.MessageFontSize);
            tips.Dispose();
            base.OnFormClosed(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e) {
            if (settingsView != null) { base.OnMouseWheel(e); return; }
            Point point = PointToClient(Cursor.Position);
            if (transcript.Bounds.Contains(point)) transcript.ScrollBy(e.Delta);
            else base.OnMouseWheel(e);
        }

        protected override void OnKeyDown(KeyEventArgs e) {
            if (e.KeyCode == Keys.Escape && settingsOpen) { CloseEmbeddedSettings(); e.Handled = true; }
            base.OnKeyDown(e);
        }

        void Build() {
            rail.Dock = DockStyle.None;
            rail.Chosen += Select;
            Controls.Add(rail);

            header.Dock = DockStyle.None;
            banner.Dock = DockStyle.None;
            banner.ActionCaption = "Retry";
            banner.ActionClick += delegate {
                attempted.Clear();
                translationErrors.Clear();
                foreach (string id in manualTranslations) attempted.Add(id);
                if (preferences.UsesLocalTranslator) PrepareTranslator();
                else ProbeTranslator();
            };
            transcript.TranslateRequested += delegate(ChatMessage message) {
                ChatChannel? channel = Current();
                if (channel != null) RequestManual(channel, message);
            };
            transcript.TargetTranslationRequested += (message, target) => { var channel = Current(); if (channel != null) RequestManual(channel, message, target); };
            Controls.Add(header);
            Controls.Add(banner);
            Controls.Add(transcript);
            Controls.Add(translationProgress);
            composer.Controls.Add(characterCount);

            composer.BackColor = Palette.Bg;
            preview.AutoSize = false;
            preview.ForeColor = Palette.Muted;
            preview.Font = UiFont.Body;
            preview.BackColor = Palette.Bg;
            preview.Visible = false;
            composer.Controls.Add(preview);
            composer.Controls.Add(draft);
            composer.Controls.Add(fromCaption);
            composer.Controls.Add(toCaption);
            composer.Controls.Add(fromButton);
            composer.Controls.Add(swapButton);
            composer.Controls.Add(languageButton);
            composer.Controls.Add(previewButton);
            composer.Controls.Add(send);
            draft.Input.MaxLength = 400;
            draft.Input.KeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; TranslateAndSend(); }
            };
            draft.Input.TextChanged += delegate { if (!suppress) RememberDraft(); RefreshActions(); };
            fromButton.Click += delegate { PickLanguage(fromButton, LanguagePick.Source); };
            swapButton.Click += delegate { SwapLanguages(); };
            languageButton.Click += delegate { PickLanguage(languageButton, LanguagePick.Outgoing); };
            tips.SetToolTip(fromButton, "Language to translate from");
            tips.SetToolTip(swapButton, "Swap languages");
            tips.SetToolTip(languageButton, "Language to translate to");
            previewButton.Click += delegate { PreviewDraft(); };
            send.Click += delegate { TranslateAndSend(); };
            Controls.Add(composer);

            pause.Click += delegate { ToggleRead(); };
            settingsButton.Click += delegate { if (settingsView != null) CloseEmbeddedSettings(); else OpenAdvancedSettings(); };
            rail.Controls.Add(pause);
            rail.Controls.Add(settingsButton);

            settings.BackColor = Palette.Sidebar;
            settings.Visible = false;
            closeSettings.Click += delegate { CloseEmbeddedSettings(); };
            incomingButton.Click += delegate { PickLanguage(incomingButton, LanguagePick.Incoming); };
            auto.Toggled += delegate {
                if (suppress || selected.Length == 0) return;
                StoreFor(selected, entry => entry.AutoTranslate = auto.On);
                if (auto.On) {
                    attempted.ExceptWith(translationErrors.Keys);
                    translationErrors.Clear();
                    if (preferences.Endpoint == ChatRules.LocalTranslator) PrepareTranslator();
                    else ProbeTranslator();
                }
            };
            check.Click += delegate { CheckSender(); };
            overlayButton.Click += delegate {
                preferences.ShowGameOverlay = !preferences.ShowGameOverlay;
                SavePreferences();
                Render();
            };
            settings.Controls.Add(closeSettings);
            preferencesButton.Click += delegate { OpenPreferences(); };
            settings.Controls.Add(providerSummary);
            settings.Controls.Add(preferencesButton);
            settings.Controls.Add(incomingButton);
            settings.Controls.Add(auto);
            settings.Controls.Add(check);
            settings.Controls.Add(overlayButton);
            nativeChatButton.Click += delegate {
                preferences.TranslateGameChat = !preferences.TranslateGameChat;
                nativeError = "";
                nativeUpdateAt = DateTime.MinValue;
                SavePreferences();
                Render();
                KickAuto();
            };
            settings.Controls.Add(nativeChatButton);
            settings.Controls.Add(nativeChatHint);
            settings.Paint += PaintSettings;
            Controls.Add(settings);
        }

        int Px(int value) => (int)Math.Round(value * DeviceDpi / 96.0);

        void LayoutChrome() {
            if (ClientSize.Width < Px(40) || ClientSize.Height < Px(40)) return;
            int side = Px(252);
            rail.SetBounds(0, 0, side, ClientSize.Height);
            pause.SetBounds(Px(16), ClientSize.Height - Px(56), Px(104), Px(34));
            settingsButton.SetBounds(Px(128), ClientSize.Height - Px(56), Px(108), Px(34));
            int mainX = side;
            int mainW = ClientSize.Width - side;
            // Retained bindings for conversation controls; settings itself lives in the main content area.
            settings.Visible = false;
            settings.SetBounds(0, 0, Px(320), ClientSize.Height); LayoutSettings(Px(320));
            int contentW = mainW;
            if (settingsOpen) {
                header.Visible = transcript.Visible = composer.Visible = translationProgress.Visible = banner.Visible = false;
                if (settingsView != null) { settingsView.SetBounds(mainX, 0, mainW, ClientSize.Height); settingsView.BringToFront(); }
                return;
            }
            header.Visible = transcript.Visible = composer.Visible = translationProgress.Visible = true;
            header.SetBounds(mainX, 0, contentW, Px(72));
            translationProgress.SetBounds(mainX + Px(20), Px(74), Math.Max(Px(80), contentW - Px(40)), Px(22));
            int top = Px(100);
            if (banner.Visible) {
                banner.SetBounds(mainX, top, contentW, Px(44));
                top += Px(44);
            }
            int composerH = ComposerHeight();
            composer.SetBounds(mainX, ClientSize.Height - composerH, contentW, composerH);
            LayoutComposer();
            transcript.SetBounds(mainX, top, contentW, Math.Max(Px(80), ClientSize.Height - composerH - top));

            pause.BringToFront();
            settingsButton.BringToFront();
        }

        int ComposerHeight() => (preview.Visible ? Px(36) : Px(12)) + Px(128);

        void LayoutComposer() {
            int width = composer.Width;
            characterCount.SetBounds(Px(20), composer.Height - Px(21), Math.Max(Px(80), width - Px(40)), Px(18));
            int draftY = preview.Visible ? Px(36) : Px(12);
            int captionY = draftY + Px(46);
            int buttonY = captionY + Px(16);
            preview.SetBounds(Px(20), Px(8), Math.Max(Px(40), width - Px(40)), Px(22));
            draft.SetBounds(Px(16), draftY, Math.Max(Px(80), width - Px(32)), Px(42));
            int margin = Px(16);
            int gap = Px(8);
            int sendW = Px(76);
            int previewW = Px(84);
            int swapW = Px(34);
            int buttonH = Px(34);
            int right = width - margin;
            send.SetBounds(right - sendW, buttonY, sendW, buttonH);
            previewButton.SetBounds(send.Left - gap - previewW, buttonY, previewW, buttonH);
            int available = Math.Max(0, previewButton.Left - gap - margin);
            int buttonW = Math.Min(Px(156), Math.Max(0, (available - swapW - gap * 2) / 2));
            int pairLeft = previewButton.Left - gap - (buttonW * 2 + swapW + gap * 2);
            fromButton.SetBounds(pairLeft, buttonY, buttonW, buttonH);
            swapButton.SetBounds(fromButton.Right + gap, buttonY, swapW, buttonH);
            languageButton.SetBounds(swapButton.Right + gap, buttonY, buttonW, buttonH);
            fromCaption.SetBounds(fromButton.Left, captionY, buttonW, Px(16));
            toCaption.SetBounds(languageButton.Left, captionY, buttonW, Px(16));
        }

        static Label LanguageCaption(string text) => new Label {
            Text = text, AutoSize = false, ForeColor = Palette.Muted, BackColor = Palette.Bg, Font = UiFont.Small, TextAlign = ContentAlignment.BottomLeft
        };

        void LayoutSettings(int width) {
            closeSettings.SetBounds(width - Px(92), Px(18), Px(76), Px(32));
            providerSummary.SetBounds(Px(20), Px(96), width - Px(40), Px(56));
            preferencesButton.SetBounds(Px(20), Px(168), width - Px(40), Px(36));
            incomingButton.SetBounds(Px(20), Px(262), Px(140), Px(34));
            auto.SetBounds(Px(20), Px(322), Px(40), Px(22));
            check.SetBounds(Px(20), Px(424), Px(148), Px(34));
            overlayButton.SetBounds(Px(20), Px(470), width - Px(40), Px(34));
            nativeChatButton.SetBounds(Px(20), Px(512), width - Px(40), Px(34));
            nativeChatHint.SetBounds(Px(20), Px(558), width - Px(40), Px(36));
        }

        void PaintSettings(object sender, PaintEventArgs e) {
            var g = e.Graphics;
            g.Clear(Palette.Sidebar);
            using (var pen = new Pen(Palette.Line))
                g.DrawLine(pen, 0, 0, 0, settings.Height);
            TextRenderer.DrawText(g, "Settings", UiFont.Title, new Rectangle(Px(20), Px(18), Px(180), Px(30)), Palette.Ink, PaintKit.Single);
            TextRenderer.DrawText(g, "Translate this conversation to", UiFont.Small, new Rectangle(Px(20), Px(240), settings.Width - Px(40), Px(18)), Palette.Muted, PaintKit.Single);
            TextRenderer.DrawText(g, "Auto-translate this conversation", UiFont.Body, new Rectangle(Px(68), Px(320), settings.Width - Px(88), Px(24)), Palette.Ink, PaintKit.Single);
            TextRenderer.DrawText(g, "Preview never sends. Send runs once, and only from a menu or lobby.", UiFont.Small, new Rectangle(Px(20), Px(360), settings.Width - Px(40), Px(48)), Palette.Faint, PaintKit.Wrap);
        }

        void StartPoll() {
            int generation = ++pollGeneration;
            Task.Run(delegate {
                while (generation == pollGeneration) {
                    try {
                        ChatSnapshot next = service.Read();
                        if (generation != pollGeneration || IsDisposed) return;
                        Post(delegate { if (generation == pollGeneration) Apply(next); });
                    } catch (Exception ex) {
                        if (generation != pollGeneration || IsDisposed) return;
                        string message = ex.Message;
                        var code = (ex as CompatibilityException)?.Code;
                        try { Post(delegate { if (generation == pollGeneration) { readerCode = code; ApplyError(message); } }); }
                        catch (InvalidOperationException) { return; }
                    }
                    System.Threading.Thread.Sleep(250);
                }
            });
        }

        void ToggleRead() {
            System.Threading.Interlocked.Increment(ref commandGeneration);
            reading = !reading;
            pause.Caption = reading ? "Pause" : "Resume";
            pollGeneration++;
            if (reading) StartPoll();
            else readerError = "";
            Render();
        }

        void Apply(ChatSnapshot next) {
            bool same = historyIds != null && historySession == next.Session && historyAccount == next.Account
                && snapshot?.AccountId == next.AccountId;
            if (!same) {
                manualQueue.Clear();
                baseline.Clear();
                foreach (var room in next.Channels) foreach (var message in room.Messages) baseline.Add(message.Id);
                translationStates.Clear();
                translationGeneration++;
                drafts.Clear();
                translations.Clear();
                translationErrors.Clear();
                manualTranslations.Clear();
                attempted.Clear();
                unread = new Dictionary<string, int>();
                translatorOffline = false;
            }
            var ids = new Dictionary<string, HashSet<string>>();
            for (int i = 0; i < next.Channels.Count; i++) {
                var set = new HashSet<string>();
                var messages = next.Channels[i].Messages;
                for (int j = 0; j < messages.Count; j++) set.Add(messages[j].Id);
                ids[next.Channels[i].Name] = set;
            }
            PruneTranslationState(ids);
            NotifyNewPrivateMessages(next, same);
            unread = UnreadCounter.Count(unread, historyIds, same, selected, reading, next.Channels);
            historyIds = ids;
            historySession = next.Session;
            historyAccount = next.Account;
            snapshot = next;
            readerError = ""; readerCode = null;
            bool keep = false;
            if (same) for (int i = 0; i < next.Channels.Count; i++) if (next.Channels[i].Name == selected) keep = true;
            if (!keep) {
                selected = "";
                for (int i = 0; i < next.Channels.Count; i++) if (next.Channels[i].Selected) selected = next.Channels[i].Name;
                if (selected.Length == 0 && next.Channels.Count > 0) selected = next.Channels[0].Name;
                LoadChannelSettings();
                LoadDraft();
            }
            if (selected.Length > 0 && unread.ContainsKey(selected)) unread[selected] = 0;
            Render();
            KickAuto();
        }

        void PruneTranslationState(Dictionary<string, HashSet<string>>? channelIds) {
            var currentIds = new HashSet<string>();
            if (channelIds != null) foreach (HashSet<string> ids in channelIds.Values) currentIds.UnionWith(ids);
            RemoveExpired(translations, currentIds);
            RemoveExpired(translationErrors, currentIds);
            manualTranslations.RemoveWhere(id => !currentIds.Contains(id));
            attempted.RemoveWhere(id => !currentIds.Contains(id));
            RemoveExpired(translationStates, currentIds);
        }

        static void RemoveExpired<T>(Dictionary<string, T> cache, HashSet<string> currentIds) {
            var expired = new List<string>();
            foreach (string id in cache.Keys) if (!currentIds.Contains(id)) expired.Add(id);
            for (int i = 0; i < expired.Count; i++) cache.Remove(expired[i]);
        }

        void ApplyError(string message) {
            bool gone = readerCode == CompatibilityCode.NotRunning || readerCode == CompatibilityCode.AccessDenied;
            bool transient = readerCode == CompatibilityCode.ChatChanged || readerCode == CompatibilityCode.WaitingForChat;
            if (transient && snapshot != null) gameOverlay?.SetChat(0, null, false, translations, translationErrors);
            readerError = message;
            if (gone) {
                snapshot = null;
                historyIds = null;
                selected = "";
            }
            Render();
        }

        void Render() {
            var rows = new List<ChannelRow>();
            ChatChannel? current = Current();
            if (snapshot != null) {
                for (int i = 0; i < snapshot.Channels.Count; i++) {
                    ChatChannel channel = snapshot.Channels[i];
                    int count;
                    unread.TryGetValue(channel.Name, out count);
                    rows.Add(new ChannelRow { Name = channel.Name, Private = channel.Private, Unread = count, Selected = channel.Name == selected });
                }
            }
            rail.Account = snapshot == null ? "" : snapshot.Account;
            if (!reading) { rail.Status = "Paused"; rail.StatusColor = Palette.Faint; }
            else if (snapshot != null && snapshot?.Connected == true) { rail.Status = "Live"; rail.StatusColor = Palette.Live; }
            else if (snapshot != null && snapshot?.Connected == false) { rail.Status = "Offline"; rail.StatusColor = Palette.Faint; }
            else if (readerError.Length > 0) { rail.Status = "Not connected"; rail.StatusColor = Palette.Faint; }
            else { rail.Status = "Looking for osu!"; rail.StatusColor = Palette.Muted; }
            rail.SetRows(rows);

            if (current == null) {
                header.TitleText = "chatter";
                header.Subtitle = reading ? "Channels you have open in osu!" : "Reading is paused";
                header.Pill = "";
            } else {
                header.TitleText = current.Name;
                string kind = current.Private ? "Private message" : "Channel";
                string link = snapshot?.Connected == true ? "Live from osu!" : snapshot?.Connected == false ? "Retained history" : "Current buffer";
                header.Subtitle = kind + "  ·  " + link;
                header.Pill = !reading ? "Paused" : snapshot?.Connected == true ? "Live" : snapshot?.Connected == false ? "Offline" : "";
                header.PillColor = snapshot?.Connected == true && reading ? Palette.Live : Palette.Muted;
            }
            header.Invalidate();

            if (translatorOffline) {
                banner.Danger = true;
                banner.ActionCaption = "Retry";
                banner.Message = preferences.Endpoint == ChatRules.LocalTranslator
                    ? "Local translation needs setup. Check your connection, then retry."
                    : "Cannot reach the translation server. Check Settings, then retry.";
            } else if (actionError.Length > 0) {
                banner.Danger = true;
                banner.ActionCaption = "";
                banner.Message = actionError;
            } else if (nativeError.Length > 0 && preferences.TranslateGameChat) {
                banner.Danger = true;
                banner.ActionCaption = "";
                banner.Message = nativeError;
            } else if (notice.Length > 0) {
                banner.Danger = false;
                banner.ActionCaption = "";
                banner.Message = notice;
            } else {
                banner.Message = "";
            }
            LayoutChrome();

            string signature = snapshot == null || current == null ? "" : snapshot.Session + "\0" + snapshot.Account + "\0" + current.Name;
            bool stick = signature != viewSignature;
            viewSignature = signature;
            if (current == null) {
                string title;
                string body;
                EmptyCopy(out title, out body);
                transcript.SetEmpty(title, body);
            } else if (current.Messages.Count == 0) {
                transcript.SetEmpty("No messages yet", "New lines in " + current.Name + " will show up here.");
            } else {
                transcript.ShowTranslate = (preferences.Endpoint.Length > 0 && preferences.Endpoint != Translator.NoTranslation) || (!string.IsNullOrEmpty(preferences.ManualEndpoint) && preferences.ManualEndpoint != Translator.NoTranslation);
                transcript.AllowUpgrade = preferences.HasDistinctManualProvider;
                transcript.ManualTranslations = manualTranslations;
                transcript.Statuses = translationStates;
                transcript.SetMessages(current.Messages, translations, translationErrors, stick);
            }
            UpdateTranslationStatus();
            transcript.Statuses = translationStates;
            WatchProfiles();
            RefreshActions();
            UpdateGameOverlay();
            UpdateNativeChat();
            UpdateNativeCommands();
        }

        ChatChannel? GameSelectedChannel() {
            if (snapshot == null) return null;
            foreach (ChatChannel channel in snapshot.Channels) if (channel.Selected) return channel;
            return null;
        }

        void UpdateNativeChat() {
            nativeChatButton.Caption = preferences.TranslateGameChat ? "In-game translation: On" : "In-game translation: Off";
            if (DemoMode || nativeUpdating) return;
            ChatChannel? channel = GameSelectedChannel();
            ChatSnapshot? observation = snapshot;
            bool active = preferences.TranslateGameChat && reading && readerError.Length == 0
                && observation?.MenuOrLobby == true && observation.Connected == true && observation.AccountId > 0
                && channel != null && preferences.Experience.NativePresentation != "original" && ChannelAutoTranslate(channel.Name)
                && channel.Name != "#highlight" && channel.Name != "#userlog";
            if (!active && !nativeWasActive) return;
            if (active && DateTime.UtcNow < nativeUpdateAt) return;
            var ready = active ? NativeChatTranslations.Ready(channel!, translations, preferences.Experience.NativePresentation == "both") : new Dictionary<string, string>();
            nativeUpdating = true;
            nativeWasActive = active;
            nativeUpdateAt = DateTime.UtcNow.AddSeconds(1);
            string target = channel?.Name ?? "";
            Task.Run(() => {
                string error = "";
                try {
                    if (active) service.DisplayTranslations(observation!, target, ready);
                    else service.RestoreDisplay();
                } catch (Exception ex) { error = ex.Message; }
                Post(() => {
                    nativeUpdating = false;
                    bool changed = nativeError != error;
                    nativeError = error;
                    if (changed) Render();
                    else if (!preferences.TranslateGameChat || !reading) UpdateNativeChat();
                });
            });
        }

        bool ChannelAutoTranslate(string name) => preferences.FindChannel(name)?.AutoTranslate ?? preferences.AutoTranslateIncoming;

        void UpdateGameOverlay() {
            overlayButton.Caption = preferences.ShowGameOverlay ? "Game overlay: On" : "Game overlay: Off";
            if (DemoMode || !preferences.ShowGameOverlay) {
                gameOverlay?.Dispose();
                gameOverlay = null;
                gameReply?.Close();
                return;
            }
            if (gameOverlay == null) {
                gameOverlay = new GameChatOverlay();
                gameOverlay.Configure(preferences.Experience);
                gameOverlay.OptionsChanged += changed => {
                    preferences.Experience.OverlayX = changed.OverlayX; preferences.Experience.OverlayY = changed.OverlayY;
                    preferences.Experience.OverlayWidth = changed.OverlayWidth; preferences.Experience.OverlayHeight = changed.OverlayHeight;
                    preferences.Experience.OverlayInteractive = changed.OverlayInteractive; SavePreferences();
                };
                gameOverlay.VisibleMessagesChanged += KickAuto;
                gameOverlay.ReplyRequested += OpenGameReply;
                gameOverlay.VisibleChanged += delegate { if (gameOverlay?.Visible == true) KickAuto(); };
            }
            ChatSnapshot? current = snapshot;
            ChatChannel? channel = null;
            if (reading && current?.MenuOrLobby == true && readerError.Length == 0) {
                foreach (ChatChannel candidate in current.Channels) {
                    if (!candidate.Selected) continue;
                    channel = candidate;
                    break;
                }
            }
            bool canReply = channel != null && current?.Connected == true && current.AccountId > 0
                && channel.Name != "#highlight" && channel.Name != "#userlog";
            gameOverlay.SetChat(current?.Session ?? 0, channel, canReply, translations, translationErrors);
        }

        void OpenGameReply(Rectangle gameBounds) {
            ChatSnapshot? current = snapshot;
            ChatChannel? channel = gameOverlay?.Channel;
            if (current?.Connected != true || channel == null || !reading || !current.MenuOrLobby) return;
            if (gameReply != null) {
                gameReply.Activate();
                return;
            }
            string name = channel.Name;
            string source = ChannelOutgoingSource(name);
            string target = ChannelOutgoingLanguage(name);
            var account = new PlayerIdentity(current.Session, current.AccountId);
            AppPreferences settings = preferences.Copy();
            var reply = new GameReplyForm(name, LanguageLabel(source), LanguageLabel(target), ChatRules.RightToLeft(source),
                text => {
                    var request = new TranslationRequest { Text = text, Source = source, Target = target };
                    settings.ApplyProvider(request, false);
                    if (settings.Experience.SendWithoutTranslation) request.Endpoint = Translator.NoTranslation;
                    if (request.Endpoint == Translator.LocalModel || request.Endpoint == Translator.ChatApi) request.Context = TranslationContext.Recent(channel, settings.Experience.ContextMessages);
                    return request;
                },
                request => Task.Run(() => service.Translate(request)),
                request => Task.Run(() => service.TranslateAndSend(name, request, account)));
            gameReply = reply;
            reply.FormClosed += delegate { gameReply = null; if (gameOverlay != null) gameOverlay.ReplyWindow = IntPtr.Zero; };
            var anchor = gameOverlay?.Bounds ?? gameBounds;
            reply.Location = new Point(Math.Max(gameBounds.Left, Math.Min(anchor.Left, gameBounds.Right - reply.Width)), Math.Max(gameBounds.Top, Math.Min(anchor.Bottom + 8, gameBounds.Bottom - reply.Height)));
            reply.Show();
            if (gameOverlay != null) gameOverlay.ReplyWindow = reply.Handle;
        }

        void WatchProfiles() {
            if (DemoMode || snapshot == null) return;
            var names = new List<string>();
            if (snapshot.Account.Length > 0) names.Add(snapshot.Account);
            for (int i = 0; i < snapshot.Channels.Count; i++) {
                var messages = snapshot.Channels[i].Messages;
                for (int j = 0; j < messages.Count; j++) {
                    if (!messages[j].System && messages[j].Sender.Length > 0) names.Add(messages[j].Sender);
                }
            }
            profiles.Watch(names, snapshot.Account, snapshot.AccountId);
        }

        void EmptyCopy(out string title, out string body) {
            if (!reading) { title = "Reading is paused"; body = "Resume to keep watching the chats open in osu!."; return; }
            if (readerCode == CompatibilityCode.AccessDenied) {
                title = "Can't read osu!";
                body = "Run chatter with the same administrator rights as osu!.";
                return;
            }
            if (readerCode == CompatibilityCode.WaitingForChat || readerCode == CompatibilityCode.UnsupportedLayout) {
                title = "No open chat";
                body = "Open a channel or a private message in osu!. Closed tabs stay hidden.";
                return;
            }
            if (readerError.Length > 0) { title = "osu! isn't open"; body = "Open osu!stable and leave a chat tab open."; return; }
            title = "Waiting for osu!";
            body = "Open osu!stable and leave a chat tab open.";
        }

        void RefreshActions() {
            ChatChannel? channel = Current();
            bool hasDraft = draft.Input.Text.Trim().Length > 0;
            if (previewedDraft != draft.Input.Text || !preview.Visible) previewTooLong = false;
            characterCount.Text = draft.Input.TextLength + "/400 · " + (preferences.Experience.SendWithoutTranslation || preferences.Endpoint == Translator.NoTranslation ? "Sending original text" : "Enter translates and sends once");
            bool live = snapshot != null && snapshot?.Connected == true && reading;
            bool waiting = preparingTranslator && preferences.Endpoint == ChatRules.LocalTranslator;
            if (previewTooLong) characterCount.Text = "Translation exceeds 400 characters · shorten the draft before sending";
            send.Enabled = !previewTooLong && !busy && !waiting && channel != null && hasDraft && live && preferences.Endpoint.Length > 0;
            previewButton.Enabled = !busy && !waiting && hasDraft && preferences.Endpoint.Length > 0;
            check.Enabled = !busy && channel != null && live;
            preferencesButton.Enabled = !busy && !preparingTranslator;
            draft.Input.Enabled = channel != null;
            bool ready = selected.Length > 0;
            incomingButton.Enabled = ready;
            auto.Enabled = ready;
            fromButton.Enabled = ready;
            swapButton.Enabled = ready;
            languageButton.Enabled = ready;
            fromButton.Caption = LanguageLabel(OutgoingSource());
            languageButton.Caption = LanguageLabel(OutgoingLanguage());
            ApplyWritingDirection();
        }

        void ApplyWritingDirection() {
            bool writeRightToLeft = ChatRules.RightToLeft(OutgoingSource());
            RightToLeft writing = writeRightToLeft ? RightToLeft.Yes : RightToLeft.No;
            bool directionChanged = draft.Input.RightToLeft != writing;
            if (directionChanged) draft.Input.RightToLeft = writing;
            string cue = "Write in " + LanguageLabel(OutgoingSource());
            if (directionChanged || draftCue != cue) {
                draftCue = cue;
                draft.Cue(cue);
            }
            bool sendRightToLeft = ChatRules.RightToLeft(OutgoingLanguage());
            RightToLeft sent = sendRightToLeft ? RightToLeft.Yes : RightToLeft.No;
            ContentAlignment align = sendRightToLeft ? ContentAlignment.MiddleRight : ContentAlignment.TopLeft;
            if (preview.RightToLeft != sent) preview.RightToLeft = sent;
            if (preview.TextAlign != align) preview.TextAlign = align;
        }

        ChatChannel? Current() {
            if (snapshot == null) return null;
            for (int i = 0; i < snapshot.Channels.Count; i++) if (snapshot.Channels[i].Name == selected) return snapshot.Channels[i];
            return null;
        }

        void Select(string name) {
            bool leavingSettings = settingsView != null;
            if (leavingSettings) CloseEmbeddedSettings(false);
            if (selected == name) { if (leavingSettings) Render(); return; }
            selected = name;
            viewSignature = "";
            LoadChannelSettings();
            LoadDraft();
            Render();
            KickAuto();
            draft.Input.Focus();
        }

        void RememberDraft() {
            if (selected.Length == 0) return;
            drafts[selected] = draft.Input.Text;
            if (preview.Visible) { preview.Visible = false; LayoutChrome(); }
        }

        void LoadDraft() {
            suppress = true;
            string text;
            drafts.TryGetValue(selected, out text);
            draft.Input.Text = text ?? "";
            preview.Visible = false;
            suppress = false;
            LayoutChrome();
        }

        void LoadChannelSettings() {
            suppress = true;
            incomingButton.Caption = LanguageLabel(IncomingLanguage());
            fromButton.Caption = LanguageLabel(OutgoingSource());
            languageButton.Caption = LanguageLabel(OutgoingLanguage());
            ChannelPreference? channel = preferences.FindChannel(selected);
            auto.On = channel != null && channel.AutoTranslate.HasValue ? channel.AutoTranslate.Value : preferences.AutoTranslateIncoming;
            bool ready = selected.Length > 0;
            incomingButton.Enabled = ready;
            auto.Enabled = ready;
            fromButton.Enabled = ready;
            swapButton.Enabled = ready;
            languageButton.Enabled = ready;
            ApplyWritingDirection();
            suppress = false;
        }

        string IncomingLanguage() => ChannelIncomingLanguage(selected);

        string ChannelIncomingLanguage(string name) {
            ChannelPreference? channel = preferences.FindChannel(name);
            if (channel != null && channel.IncomingLanguage.Length > 0) return channel.IncomingLanguage;
            return preferences.IncomingLanguage;
        }

        string OutgoingLanguage() => ChannelOutgoingLanguage(selected);

        string ChannelOutgoingLanguage(string name) {
            ChannelPreference? channel = preferences.FindChannel(name);
            if (channel != null && channel.OutgoingLanguage.Length > 0) return channel.OutgoingLanguage;
            return preferences.OutgoingLanguage == "channel" ? ChatRules.ChannelLanguage(name) : preferences.OutgoingLanguage;
        }

        string OutgoingSource() => ChannelOutgoingSource(selected);

        string ChannelOutgoingSource(string name) {
            ChannelPreference? channel = preferences.FindChannel(name);
            if (channel != null && channel.OutgoingSource.Length > 0) return channel.OutgoingSource;
            return string.IsNullOrEmpty(preferences.OutgoingSource) ? "en" : preferences.OutgoingSource;
        }

        bool StoreFor(string name, Action<ChannelPreference> edit) {
            if (string.IsNullOrEmpty(name)) return false;
            ChannelPreference? current = preferences.FindChannel(name);
            var entry = current == null ? new ChannelPreference { Name = name } : current.Copy();
            edit(entry);
            if (!preferences.Remember(entry)) return false;
            SavePreferences();
            return true;
        }

        static string LanguageLabel(string id) {
            if (id == "auto") return "Automatic";
            for (int i = 0; i < ChatRules.Languages.Length; i++) if (ChatRules.Languages[i] == id) return ChatRules.LanguageNames[i];
            return "English";
        }

        enum LanguagePick { Incoming, Outgoing, Source }

        void PickLanguage(PillButton source, LanguagePick pick) {
            string current = pick == LanguagePick.Incoming ? IncomingLanguage() : pick == LanguagePick.Source ? OutgoingSource() : OutgoingLanguage();
            var menu = new LanguageMenu();
            if (pick == LanguagePick.Source) menu.Add("auto", "Automatic", current == "auto");
            for (int i = 0; i < ChatRules.Languages.Length; i++)
                menu.Add(ChatRules.Languages[i], ChatRules.LanguageNames[i], current == ChatRules.Languages[i]);
            var origin = source.PointToScreen(new Point(0, source.Height + 6));
            menu.ShowAt(this, origin, delegate(string id) {
                if (pick == LanguagePick.Incoming) SetIncoming(id);
                else if (pick == LanguagePick.Source) SetOutgoingSource(id);
                else SetOutgoing(id);
            });
        }

        void SetIncoming(string id) {
            incomingButton.Caption = LanguageLabel(id);
            if (selected.Length > 0) StoreFor(selected, entry => entry.IncomingLanguage = id);
            else {
                preferences.IncomingLanguage = id;
                SavePreferences();
            }
            BumpTranslation();
            KickAuto();
        }

        void SetOutgoing(string id) {
            if (selected.Length == 0) return;
            if (!StoreFor(selected, entry => entry.OutgoingLanguage = id)) return;
            languageButton.Caption = LanguageLabel(id);
            ApplyWritingDirection();
            if (preview.Visible) { preview.Visible = false; LayoutChrome(); }
        }

        void SetOutgoingSource(string id) {
            if (selected.Length == 0 || (id != "auto" && Array.IndexOf(ChatRules.Languages, id) < 0)) return;
            if (!StoreFor(selected, entry => entry.OutgoingSource = id)) return;
            fromButton.Caption = LanguageLabel(id);
            ApplyWritingDirection();
            if (preview.Visible) { preview.Visible = false; LayoutChrome(); }
        }

        void SwapLanguages() {
            if (selected.Length == 0) return;
            string from = OutgoingSource();
            string to = OutgoingLanguage();
            if (from == to) return;
            if (!StoreFor(selected, entry => { entry.OutgoingSource = to; entry.OutgoingLanguage = from; })) return;
            fromButton.Caption = LanguageLabel(to);
            languageButton.Caption = LanguageLabel(from);
            ApplyWritingDirection();
            if (preview.Visible) { preview.Visible = false; LayoutChrome(); }
        }

        TranslationRequest DraftRequest(string text) {
            var request = new TranslationRequest { Text = text, Source = OutgoingSource(), Target = OutgoingLanguage() };
            preferences.ApplyProvider(request, false);
            if (request.Endpoint == Translator.LocalModel || request.Endpoint == Translator.ChatApi) request.Context = TranslationContext.Recent(Current(), preferences.Experience.ContextMessages);
            if (preferences.Experience.SendWithoutTranslation) request.Endpoint = Translator.NoTranslation;
            return request;
        }

        void ApplyPreferences() {
            TopMost = preferences.Experience.AlwaysOnTop;
            if (IsHandleCreated && !DemoMode) transcript.UseModern(preferences.Experience.ModernTranscript, preferences.Experience.MessageFontSize);
            providerSummary.Text = preferences.ProviderSummary();
            LoadChannelSettings();
        }

        void OpenPreferences() {
            if (busy || preparingTranslator) {
                notice = "Wait for the current translation to finish before changing providers.";
                Render();
                return;
            }
            editingPreferences = true;
            DialogResult result;
            using (var dialog = new SetupForm(preferences, false)) {
                try { result = dialog.ShowDialog(this); }
                finally { editingPreferences = false; }
                if (result != DialogResult.OK) { KickAuto(); return; }
                preferences = dialog.Result ?? throw new InvalidOperationException("No translation settings were saved.");
            }
            preview.Visible = false;
            actionError = notice = "";
            ApplyPreferences();
            BumpTranslation();
            if (preferences.UsesLocalTranslator) PrepareTranslator();
            else KickAuto();
        }

        void SavePreferences() {
            if (DemoMode) return;
            try { PreferencesStore.Save(preferences); }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException || ex is System.Security.Cryptography.CryptographicException) {
                actionError = "Could not save your preferences. Check access to your local app data folder.";
            }
        }

        void BumpTranslation() {
            translationGeneration++;
            attempted.Clear();
            translations.Clear();
            translationErrors.Clear();
            manualTranslations.Clear();
            translatorOffline = false;
            Render();
        }

        void Post(Action action) {
            if (IsDisposed || Disposing || !IsHandleCreated) return;
            try { BeginInvoke(new Action(() => { if (!IsDisposed && !Disposing) action(); })); }
            catch (InvalidOperationException) { }
        }

        void PrepareTranslator() {
            if (preparingTranslator || DemoMode) return;
            preparingTranslator = true;
            translatorOffline = false;
            Render();
            Task.Run(() => {
                try {
                    service.PrepareTranslator();
                    Post(() => {
                        preparingTranslator = false;
                        translatorStatus = "";
                        translatorOffline = false;
                        actionError = "";
                        attempted.Clear();
                        translationErrors.Clear();
                        foreach (string id in manualTranslations) attempted.Add(id);
                        Render();
                        KickAuto();
                    });
                } catch (Exception ex) {
                    Post(() => {
                        preparingTranslator = false;
                        translatorStatus = "";
                        translatorOffline = preferences.Endpoint == ChatRules.LocalTranslator;
                        actionError = ex.Message;
                        Render();
                    });
                }
            });
        }

        void ProbeTranslator() {
            string target = preferences.Endpoint;
            if (Translator.IsDeepL(target)) {
                translatorOffline = false;
                actionError = "";
                Render();
                KickAuto();
                return;
            }
            Task.Run(delegate {
                bool up = Translator.Available(target);
                try {
                    Post(delegate {
                        if (target != preferences.Endpoint) return;
                        translatorOffline = !up && target.Length > 0 && auto.On;
                        if (up) actionError = "";
                        Render();
                        if (up) KickAuto();
                    });
                } catch (InvalidOperationException) { }
            });
        }

        void KickAuto() {
            if (DemoMode) return;
            bool waiting = preparingTranslator && preferences.Endpoint == ChatRules.LocalTranslator;
            if (editingPreferences || waiting || !reading || translating) return;
            while (manualQueue.Count > 0) {
                var next = manualQueue.Dequeue();
                if (snapshot == null) break;
                foreach (var room in snapshot.Channels) if (room.Name == next.Item1)
                    foreach (var message in room.Messages) if (message.Id == next.Item2) { TranslateOne(room, message, true, next.Item3); return; }
            }
            if (translatorOffline || preferences.Endpoint.Length == 0 || preferences.Endpoint == Translator.NoTranslation) return;
            if (preferences.Endpoint == Translator.LocalModel && (!System.IO.File.Exists(preferences.Experience.ModelPath) || !System.IO.File.Exists(preferences.Experience.RuntimePath))) return;
            var channels = new List<ChatChannel>();
            ChatChannel? gameChannel = preferences.TranslateGameChat && snapshot?.MenuOrLobby == true ? GameSelectedChannel() : null;
            if (gameChannel != null) channels.Add(gameChannel);
            ChatChannel? overlayChannel = gameOverlay?.Visible == true ? gameOverlay.Channel : null;
            if (overlayChannel != null && !channels.Contains(overlayChannel)) channels.Add(overlayChannel);
            ChatChannel? appChannel = Current();
            if (appChannel != null && !channels.Contains(appChannel)) channels.Add(appChannel);
            foreach (ChatChannel channel in channels) {
                if (!ChannelAutoTranslate(channel.Name)) continue;
                var visible = channel == appChannel ? transcript.VisibleMessageIds() : channel == overlayChannel ? gameOverlay?.VisibleMessageIds() ?? new HashSet<string>() : new HashSet<string>();
                int oldest = preferences.Experience.HistoryPolicy == "all" ? Math.Max(0, channel.Messages.Count - 200) : Math.Max(0, channel.Messages.Count - preferences.Experience.HistoryCount);
                for (int pass = 0; pass < 2; pass++) for (int i = channel.Messages.Count - 1; i >= 0; i--) {
                    ChatMessage candidate = channel.Messages[i];
                    bool onScreen = visible.Contains(candidate.Id);
                    if ((pass == 0) != onScreen || (i < oldest && !onScreen)) continue;
                    if (preferences.Experience.HistoryPolicy == "new" && baseline.Contains(candidate.Id)) continue;
                    if (!candidate.Outgoing && !candidate.System && !attempted.Contains(candidate.Id)) { TranslateOne(channel, candidate, false); return; }
                }
            }
            UpdateTranslationStatus();
        }

        void TranslateOne(ChatChannel channel, ChatMessage message, bool manual, string target = "") {
            if (message == null || message.System || translating) return;
            bool separate = manual && preferences.HasDistinctManualProvider;
            string channelName = channel.Name;
            var request = new TranslationRequest { Text = message.Text, Source = "auto", Target = ChannelIncomingLanguage(channelName), ProtectedSpans = ChatLinks.Find(message.Text, message.Source) };
            preferences.ApplyProvider(request, manual);
            if (Array.IndexOf(ChatRules.Languages, target) >= 0) request.Target = target;
            string? configuredSource = preferences.FindChannel(channelName)?.IncomingSource;
            if (string.IsNullOrEmpty(configuredSource)) configuredSource = preferences.Experience.IncomingSource;
            request.Source = TranslationContext.Source(message.Text, channelName, configuredSource ?? "auto");
            if (request.Endpoint == Translator.LocalModel || request.Endpoint == Translator.ChatApi)
                request.Context = TranslationContext.Before(channel, message.Id, preferences.Experience.ContextMessages);
            if (string.IsNullOrEmpty(request.Endpoint)) return;
            if (preparingTranslator && request.Endpoint == ChatRules.LocalTranslator) return;
            int generation = translationGeneration;
            attempted.Add(message.Id);
            translationStates[message.Id] = "Translating";
            translating = true;
            UpdateTranslationStatus();
            Task.Run(delegate {
                try {
                    string text = service.Translate(request);
                    Post(delegate {
                        translating = false;
                        if (!TranslationCurrent(generation, channelName, message.Id)) { KickAuto(); return; }
                        translatorOffline = false;
                        translations[message.Id] = text;
                        translationStates[message.Id] = text == message.Text ? "Original retained" : "Translated";
                        translationErrors.Remove(message.Id);
                        if (separate) manualTranslations.Add(message.Id);
                        else manualTranslations.Remove(message.Id);
                        PruneTranslationState(historyIds);
                        Render();
                        KickAuto();
                    });
                } catch (Exception ex) {
                    string error = ex.Message;
                    bool offline = error == Translator.OfflineMessage || request.Endpoint == ChatRules.LocalTranslator;
                    try {
                        Post(delegate {
                            translating = false;
                            if (!TranslationCurrent(generation, channelName, message.Id)) { KickAuto(); return; }
                            translationStates[message.Id] = "Failed · retry available";
                            if (separate) translationErrors[message.Id] = error;
                            else {
                                translationErrors[message.Id] = error;
                                if (Translator.IsDeepL(request.Endpoint)) {
                                    StoreFor(channelName, entry => entry.AutoTranslate = false);
                                    if (selected == channelName) auto.On = false;
                                    actionError = error;
                                }
                            }
                            PruneTranslationState(historyIds);
                            Render();
                            KickAuto();
                        });
                    } catch (InvalidOperationException) { }
                }
            });
        }

        bool TranslationCurrent(int generation, string channel, string id) {
            return generation == translationGeneration
                && historyIds != null && historyIds.TryGetValue(channel, out HashSet<string> ids) && ids.Contains(id);
        }

        void PreviewDraft() {
            bool waiting = preparingTranslator && preferences.Endpoint == ChatRules.LocalTranslator;
            if (busy || waiting || draft.Input.Text.Trim().Length == 0) return;
            busy = true;
            actionError = "";
            notice = "";
            string text = draft.Input.Text;
            string language = OutgoingLanguage();
            string source = OutgoingSource();
            int generation = translationGeneration;
            string previewChannel = selected;
            RefreshActions();
            var request = DraftRequest(text);
            Task.Run(delegate {
                try {
                    string translated = service.Translate(request);
                    Post(delegate {
                        busy = false;
                        translatorOffline = false;
                        if (generation == translationGeneration && selected == previewChannel && draft.Input.Text == text && OutgoingLanguage() == language && OutgoingSource() == source) {
                            previewedDraft = text; previewTooLong = translated.Length > 400;
                            preview.Text = previewTooLong ? translated.Length + "/400 characters · Shorten the draft. " + translated : translated;
                            preview.ForeColor = previewTooLong ? Color.FromArgb(254, 205, 211) : Palette.Muted;
                            preview.Visible = true;
                            LayoutChrome();
                        }
                        RefreshActions();
                    });
                } catch (Exception ex) {
                    string error = ex.Message;
                    Post(delegate {
                        busy = false;
                        if (generation != translationGeneration) { RefreshActions(); return; }
                        if (error == Translator.OfflineMessage || request.Endpoint == ChatRules.LocalTranslator) translatorOffline = true;
                        else actionError = error;
                        Render();
                    });
                }
            });
        }

        void TranslateAndSend() {
            if (previewTooLong && previewedDraft == draft.Input.Text && preview.Visible) return;
            ChatChannel? channel = Current();
            bool waiting = preparingTranslator && preferences.Endpoint == ChatRules.LocalTranslator;
            if (busy || waiting || !reading || channel == null || draft.Input.Text.Trim().Length == 0 || preferences.Endpoint.Length == 0) return;
            if (snapshot == null || snapshot.Connected != true) return;
            busy = true;
            actionError = "";
            notice = "";
            string target = channel.Name;
            string text = draft.Input.Text;
            lastSubmitted = text;
            RefreshActions();
            var request = DraftRequest(text);
            Task.Run(delegate {
                try {
                    SendReceipt receipt = service.TranslateAndSend(target, request);
                    Post(delegate {
                        busy = false;
                        translatorOffline = false;
                        if (drafts.ContainsKey(target) && drafts[target] == text) drafts[target] = "";
                        if (selected == target && draft.Input.Text == text) {
                            suppress = true;
                            draft.Input.Text = "";
                            suppress = false;
                        }
                        preview.Visible = false;
                        notice = (receipt.Echoed ? "Visible in osu!." : "Sent to osu!. Check the conversation if it does not appear.") + "  " + receipt.Translated;
                        Render();
                    });
                } catch (Exception ex) {
                    string error = ex.Message;
                    Post(delegate {
                        busy = false;
                        if (error == Translator.OfflineMessage || request.Endpoint == ChatRules.LocalTranslator) translatorOffline = true;
                        else actionError = error;
                        Render();
                    });
                }
            });
        }

        void CheckSender() {
            ChatChannel? channel = Current();
            if (busy || channel == null) return;
            busy = true;
            actionError = "";
            notice = "";
            string target = channel.Name;
            RefreshActions();
            Task.Run(delegate {
                try {
                    service.Check(target);
                    Post(delegate { busy = false; notice = "Direct sender is ready. No message was sent."; Render(); });
                } catch (Exception ex) {
                    string error = ex.Message;
                    Post(delegate { busy = false; actionError = error; Render(); });
                }
            });
        }

        public void LoadDemo() {
            DemoMode = true;
            pollGeneration++;
            profiles.Remember("friend", 0, true, null);
            string playing = "is playing xi - Blue Zenith [FOUR DIMENSIONS]";
            var osu = new List<ChatMessage> {
                new ChatMessage("s", "", "you joined #osu.", "19:40:58", false, true),
                new ChatMessage("a", "you", "is anyone around for a lobby?", "19:41:18", true, false),
                new ChatMessage("b", "friend", "привет, через минуту", "19:41:26", false, false),
                new ChatMessage("c", "friend", "я на русском канале если что — можем сыграть пару карт", "19:41:40", false, false),
                new ChatMessage("p", "friend", playing, "19:41:48", false, false, "[https://osu.ppy.sh/b/658127 " + playing + "]"),
                new ChatMessage("d", "you", "sure, i'll be there", "19:42:01", true, false)
            };
            translations["b"] = "hi, in a minute";
            var direct = new List<ChatMessage> {
                new ChatMessage("e", "friend", "hey, you free later?", "18:02:11", false, false)
            };
            snapshot = new ChatSnapshot(1, true, "you", new[] {
                new ChatChannel("#osu", false, true, osu),
                new ChatChannel("#russian", false, false, new ChatMessage[0]),
                new ChatChannel("friend", true, false, direct)
            });
            unread = new Dictionary<string, int> { { "#russian", 4 }, { "friend", 1 } };
            selected = "#osu";
            reading = true;
            suppress = true;
            preferences.Endpoint = ChatRules.LocalTranslator;
            preferences.ApiKey = "";
            incomingButton.Caption = "English";
            auto.On = true;
            suppress = false;
            historyIds = new Dictionary<string, HashSet<string>>();
            viewSignature = "";
            Render();
        }

        public void OpenSettings() { OpenAdvancedSettings(); }
        internal void OpenSettingsPage(string page) { OpenAdvancedSettings(); settingsView?.SelectPage(page); }

        public void LoadOffline() {
            DemoMode = true;
            pollGeneration++;
            snapshot = null;
            selected = "";
            readerError = "Open osu!stable to read chat";
            reading = true;
            Render();
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
    }

    sealed class LanguageMenu : Form {
        readonly FlowLayoutPanel stack = new FlowLayoutPanel();
        Action<string>? chosen;

        public LanguageMenu() {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Palette.Elevated;
            Padding = new Padding(6);
            Width = 180;
            stack.Dock = DockStyle.Fill;
            stack.FlowDirection = FlowDirection.TopDown;
            stack.WrapContents = false;
            stack.BackColor = Palette.Elevated;
            Controls.Add(stack);
            Deactivate += delegate { Close(); };
        }

        public void Add(string id, string label, bool selected) {
            var item = new Label {
                Text = label,
                Width = 156,
                Height = 30,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0),
                ForeColor = selected ? Palette.Ink : Palette.Muted,
                BackColor = Palette.Elevated,
                Font = selected ? UiFont.BodyStrong : UiFont.Body,
                Cursor = Cursors.Hand
            };
            item.MouseEnter += delegate { item.BackColor = Palette.Field; };
            item.MouseLeave += delegate { item.BackColor = Palette.Elevated; };
            item.Click += delegate { if (chosen != null) chosen(id); Close(); };
            stack.Controls.Add(item);
        }

        public void ShowAt(IWin32Window owner, Point screen, Action<string> onChoice) {
            chosen = onChoice;
            int height = 12 + stack.Controls.Count * 30;
            var screenBounds = Screen.FromPoint(screen).WorkingArea;
            int limit = Math.Max(180, screenBounds.Height - 16);
            if (height > limit) {
                height = limit;
                stack.AutoScroll = true;
                Width = 196;
            }
            if (screen.Y + height > screenBounds.Bottom) screen.Y -= height + 40;
            if (screen.Y < screenBounds.Top) screen.Y = screenBounds.Top;
            Location = screen;
            Size = new Size(Width, height);
            Show(owner);
        }
    }
}
