using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Chatter;

namespace Chatter.App {
    public sealed class MainForm : Form {
        readonly ChatService service = new ChatService();
        AppPreferences preferences;
        readonly PillButton preferencesButton = new PillButton { Caption = "Translation preferences…" };
        readonly Label providerSummary = new Label { ForeColor = Palette.Muted, Font = UiFont.Body, BackColor = Palette.Sidebar };
        readonly Dictionary<string, string> drafts = new Dictionary<string, string>();
        readonly Dictionary<string, string> outgoingLanguages = new Dictionary<string, string>();
        readonly Dictionary<string, string> translations = new Dictionary<string, string>();
        readonly Dictionary<string, string> translationErrors = new Dictionary<string, string>();
        readonly Dictionary<string, bool> autoChoices = new Dictionary<string, bool>();
        readonly HashSet<string> attempted = new HashSet<string>();
        readonly ChannelRail rail = new ChannelRail();
        readonly ConversationHeader header = new ConversationHeader();
        readonly BannerBar banner = new BannerBar();
        readonly TranscriptView transcript = new TranscriptView();
        readonly LineField draft = new LineField();
        readonly PillButton send = new PillButton { Filled = true, Caption = "Send" };
        readonly PillButton previewButton = new PillButton { Caption = "Preview" };
        readonly PillButton languageButton = new PillButton { Caption = "English" };
        readonly Label preview = new Label();
        readonly Panel composer = new Panel();
        readonly Panel settings = new Panel();
        readonly PillButton incomingButton = new PillButton { Caption = "English" };
        readonly SwitchBox auto = new SwitchBox();
        readonly PillButton check = new PillButton { Caption = "Check sender" };
        readonly PillButton pause = new PillButton { Caption = "Pause" };
        readonly PillButton settingsButton = new PillButton { Caption = "Settings" };
        readonly PillButton closeSettings = new PillButton { Caption = "Close" };

        ChatSnapshot snapshot;
        Dictionary<string, int> unread = new Dictionary<string, int>();
        Dictionary<string, HashSet<string>> historyIds;
        int historySession;
        string historyAccount = "";
        string selected = "";
        string readerError = "";
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

        public bool DemoMode { get; set; }

        public MainForm() : this(new AppPreferences()) { }

        public MainForm(AppPreferences preferences) {
            this.preferences = preferences.Copy();
            Text = "osu-chatter";
            Icon = AppIcon.Create();
            Font = UiFont.Body;
            BackColor = Palette.Bg;
            ForeColor = Palette.Ink;
            ClientSize = new Size(1100, 720);
            MinimumSize = new Size(980, 640);
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            KeyPreview = true;
            Build();
            service.TranslationStatus += status => Post(() => {
                if (preferences.Endpoint != ChatRules.LocalTranslator) return;
                translatorStatus = status;
                Render();
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
            draft.Cue("Write in English");
            if (!DemoMode) {
                ApplyPreferences();
                reading = preferences.StartReading;
                pause.Caption = reading ? "Pause" : "Resume";
                if (reading) StartPoll();
                if (preferences.Endpoint == ChatRules.LocalTranslator) PrepareTranslator();
            }
            LayoutChrome();
        }

        protected override void OnResize(EventArgs e) {
            base.OnResize(e);
            LayoutChrome();
        }

        protected override void OnFormClosed(FormClosedEventArgs e) {
            pollGeneration++;
            translationGeneration++;
            service.Dispose();
            base.OnFormClosed(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e) {
            Point point = PointToClient(Cursor.Position);
            if (transcript.Bounds.Contains(point)) transcript.ScrollBy(e.Delta);
            else base.OnMouseWheel(e);
        }

        protected override void OnKeyDown(KeyEventArgs e) {
            if (e.KeyCode == Keys.Escape && settingsOpen) { settingsOpen = false; LayoutChrome(); e.Handled = true; }
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
                if (preferences.Endpoint == ChatRules.LocalTranslator) PrepareTranslator();
                else ProbeTranslator();
            };
            transcript.TranslateRequested += TranslateOne;
            Controls.Add(header);
            Controls.Add(banner);
            Controls.Add(transcript);

            composer.BackColor = Palette.Bg;
            preview.AutoSize = false;
            preview.ForeColor = Palette.Muted;
            preview.Font = UiFont.Body;
            preview.BackColor = Palette.Bg;
            preview.Visible = false;
            composer.Controls.Add(preview);
            composer.Controls.Add(draft);
            composer.Controls.Add(languageButton);
            composer.Controls.Add(previewButton);
            composer.Controls.Add(send);
            draft.Input.MaxLength = 400;
            draft.Input.KeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; TranslateAndSend(); }
            };
            draft.Input.TextChanged += delegate { if (!suppress) RememberDraft(); RefreshActions(); };
            languageButton.Click += delegate { PickLanguage(languageButton, false); };
            previewButton.Click += delegate { PreviewDraft(); };
            send.Click += delegate { TranslateAndSend(); };
            Controls.Add(composer);

            pause.Click += delegate { ToggleRead(); };
            settingsButton.Click += delegate { settingsOpen = !settingsOpen; LayoutChrome(); };
            rail.Controls.Add(pause);
            rail.Controls.Add(settingsButton);

            settings.BackColor = Palette.Sidebar;
            settings.Visible = false;
            closeSettings.Click += delegate { settingsOpen = false; LayoutChrome(); };
            incomingButton.Click += delegate { PickLanguage(incomingButton, true); };
            auto.Toggled += delegate {
                if (suppress || selected.Length == 0) return;
                autoChoices[ChoiceKey()] = auto.On;
                if (auto.On) {
                    attempted.ExceptWith(translationErrors.Keys);
                    translationErrors.Clear();
                    if (preferences.Endpoint == ChatRules.LocalTranslator) PrepareTranslator();
                    else ProbeTranslator();
                }
            };
            check.Click += delegate { CheckSender(); };
            settings.Controls.Add(closeSettings);
            preferencesButton.Click += delegate { OpenPreferences(); };
            settings.Controls.Add(providerSummary);
            settings.Controls.Add(preferencesButton);
            settings.Controls.Add(incomingButton);
            settings.Controls.Add(auto);
            settings.Controls.Add(check);
            settings.Paint += PaintSettings;
            Controls.Add(settings);
        }

        void LayoutChrome() {
            if (ClientSize.Width < 40 || ClientSize.Height < 40) return;
            int side = 252;
            rail.SetBounds(0, 0, side, ClientSize.Height);
            pause.SetBounds(16, ClientSize.Height - 56, 104, 34);
            settingsButton.SetBounds(128, ClientSize.Height - 56, 108, 34);
            int mainX = side;
            int mainW = ClientSize.Width - side;
            int settingsW = settingsOpen ? Math.Min(320, mainW - 280) : 0;
            settings.Visible = settingsOpen;
            settings.SetBounds(ClientSize.Width - settingsW, 0, settingsW, ClientSize.Height);
            if (settingsOpen) LayoutSettings(settingsW);
            int contentW = mainW - settingsW;
            header.SetBounds(mainX, 0, contentW, 72);
            int top = 72;
            if (banner.Visible) {
                banner.SetBounds(mainX, top, contentW, 44);
                top += 44;
            }
            int composerH = preview.Visible ? 156 : 124;
            composer.SetBounds(mainX, ClientSize.Height - composerH, contentW, composerH);
            LayoutComposer();
            transcript.SetBounds(mainX, top, contentW, Math.Max(80, ClientSize.Height - composerH - top));
            settings.BringToFront();
            pause.BringToFront();
            settingsButton.BringToFront();
        }

        void LayoutComposer() {
            int width = composer.Width;
            int y = 14;
            preview.SetBounds(20, 10, Math.Max(40, width - 40), 22);
            if (preview.Visible) y = 38;
            draft.SetBounds(16, y, Math.Max(80, width - 32), 42);
            y += 52;
            int right = width - 16;
            send.SetBounds(right - 76, y, 76, 34);
            previewButton.SetBounds(send.Left - 92, y, 84, 34);
            languageButton.SetBounds(previewButton.Left - 116, y, 108, 34);
        }

        void LayoutSettings(int width) {
            closeSettings.SetBounds(width - 92, 18, 76, 32);
            providerSummary.SetBounds(20, 96, width - 40, 56);
            preferencesButton.SetBounds(20, 168, width - 40, 36);
            incomingButton.SetBounds(20, 262, 140, 34);
            auto.SetBounds(20, 322, 40, 22);
            check.SetBounds(20, 424, 148, 34);
        }

        void PaintSettings(object sender, PaintEventArgs e) {
            var g = e.Graphics;
            g.Clear(Palette.Sidebar);
            using (var pen = new Pen(Palette.Line))
                g.DrawLine(pen, 0, 0, 0, settings.Height);
            TextRenderer.DrawText(g, "Settings", UiFont.Title, new Rectangle(20, 18, 180, 30), Palette.Ink, PaintKit.Single);
            TextRenderer.DrawText(g, "Translate incoming messages to", UiFont.Small, new Rectangle(20, 240, settings.Width - 40, 18), Palette.Muted, PaintKit.Single);
            TextRenderer.DrawText(g, "Auto-translate this conversation", UiFont.Body, new Rectangle(68, 320, settings.Width - 88, 24), Palette.Ink, PaintKit.Single);
            TextRenderer.DrawText(g, "Preview never sends. Send runs once, and only from a menu or lobby.", UiFont.Small, new Rectangle(20, 360, settings.Width - 40, 48), Palette.Faint, PaintKit.Wrap);
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
                        try { Post(delegate { if (generation == pollGeneration) ApplyError(message); }); }
                        catch (InvalidOperationException) { return; }
                    }
                    System.Threading.Thread.Sleep(250);
                }
            });
        }

        void ToggleRead() {
            reading = !reading;
            pause.Caption = reading ? "Pause" : "Resume";
            pollGeneration++;
            if (reading) StartPoll();
            else readerError = "";
            Render();
        }

        void Apply(ChatSnapshot next) {
            bool same = historyIds != null && historySession == next.Session && historyAccount == next.Account;
            if (!same) {
                translationGeneration++;
                drafts.Clear();
                translations.Clear();
                translationErrors.Clear();
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
            unread = UnreadCounter.Count(unread, historyIds, same, selected, reading, next.Channels);
            historyIds = ids;
            historySession = next.Session;
            historyAccount = next.Account;
            snapshot = next;
            readerError = "";
            bool keep = false;
            if (same) for (int i = 0; i < next.Channels.Count; i++) if (next.Channels[i].Name == selected) keep = true;
            if (!keep) {
                selected = "";
                for (int i = 0; i < next.Channels.Count; i++) if (next.Channels[i].Selected) selected = next.Channels[i].Name;
                if (selected.Length == 0 && next.Channels.Count > 0) selected = next.Channels[0].Name;
                LoadAuto();
                LoadDraft();
            }
            if (selected.Length > 0 && unread.ContainsKey(selected)) unread[selected] = 0;
            Render();
            KickAuto();
        }

        void PruneTranslationState(Dictionary<string, HashSet<string>> channelIds) {
            var currentIds = new HashSet<string>();
            if (channelIds != null) foreach (HashSet<string> ids in channelIds.Values) currentIds.UnionWith(ids);
            RemoveExpired(translations, currentIds);
            RemoveExpired(translationErrors, currentIds);
            attempted.RemoveWhere(id => !currentIds.Contains(id));
        }

        static void RemoveExpired<T>(Dictionary<string, T> cache, HashSet<string> currentIds) {
            var expired = new List<string>();
            foreach (string id in cache.Keys) if (!currentIds.Contains(id)) expired.Add(id);
            for (int i = 0; i < expired.Count; i++) cache.Remove(expired[i]);
        }

        void ApplyError(string message) {
            bool gone = message.IndexOf("Open osu!stable to read chat", StringComparison.Ordinal) >= 0
                || message.IndexOf("administrator", StringComparison.OrdinalIgnoreCase) >= 0;
            bool transient = message.IndexOf("Chat changed", StringComparison.Ordinal) >= 0
                || message.IndexOf("waiting for supported", StringComparison.Ordinal) >= 0;
            if (transient && snapshot != null) return;
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
            ChatChannel current = Current();
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
            else if (snapshot != null && snapshot.Connected == true) { rail.Status = "Live"; rail.StatusColor = Palette.Live; }
            else if (snapshot != null && snapshot.Connected == false) { rail.Status = "Offline"; rail.StatusColor = Palette.Faint; }
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
                string link = snapshot.Connected == true ? "Live from osu!" : snapshot.Connected == false ? "Retained history" : "Current buffer";
                header.Subtitle = kind + "  ·  " + link;
                header.Pill = !reading ? "Paused" : snapshot.Connected == true ? "Live" : snapshot.Connected == false ? "Offline" : "";
                header.PillColor = snapshot.Connected == true && reading ? Palette.Live : Palette.Muted;
            }
            header.Invalidate();

            if (preparingTranslator || translatorStatus.Length > 0) {
                banner.Danger = false;
                banner.ActionCaption = "";
                banner.Message = translatorStatus.Length > 0 ? translatorStatus : "Preparing local translation…";
            } else if (translatorOffline) {
                banner.Danger = true;
                banner.ActionCaption = "Retry";
                banner.Message = preferences.Endpoint == ChatRules.LocalTranslator
                    ? "Local translation needs setup. Check your connection, then retry."
                    : "Cannot reach the translation server. Check Settings, then retry.";
            } else if (actionError.Length > 0) {
                banner.Danger = true;
                banner.ActionCaption = "";
                banner.Message = actionError;
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
                transcript.ShowTranslate = preferences.Endpoint.Length > 0;
                transcript.SetMessages(current.Messages, translations, translationErrors, stick);
            }
            RefreshActions();
        }

        void EmptyCopy(out string title, out string body) {
            if (!reading) { title = "Reading is paused"; body = "Resume to keep watching the chats open in osu!."; return; }
            if (readerError.IndexOf("administrator", StringComparison.OrdinalIgnoreCase) >= 0) {
                title = "Can't read osu!";
                body = "Run chatter with the same administrator rights as osu!.";
                return;
            }
            if (readerError.IndexOf("Open chat", StringComparison.Ordinal) >= 0 || readerError.IndexOf("chat-reader", StringComparison.Ordinal) >= 0) {
                title = "No open chat";
                body = "Open a channel or a private message in osu!. Closed tabs stay hidden.";
                return;
            }
            if (readerError.Length > 0) { title = "osu! isn't open"; body = "Open osu!stable and leave a chat tab open."; return; }
            title = "Waiting for osu!";
            body = "Open osu!stable and leave a chat tab open.";
        }

        void RefreshActions() {
            ChatChannel channel = Current();
            bool hasDraft = draft.Input.Text.Trim().Length > 0;
            bool live = snapshot != null && snapshot.Connected == true && reading;
            send.Enabled = !busy && !preparingTranslator && channel != null && hasDraft && live && preferences.Endpoint.Length > 0;
            previewButton.Enabled = !busy && !preparingTranslator && hasDraft && preferences.Endpoint.Length > 0;
            check.Enabled = !busy && channel != null && live;
            preferencesButton.Enabled = !busy && !preparingTranslator;
            draft.Input.Enabled = channel != null;
            languageButton.Caption = LanguageLabel(OutgoingLanguage());
        }

        ChatChannel Current() {
            if (snapshot == null) return null;
            for (int i = 0; i < snapshot.Channels.Count; i++) if (snapshot.Channels[i].Name == selected) return snapshot.Channels[i];
            return null;
        }

        void Select(string name) {
            if (selected == name) return;
            selected = name;
            viewSignature = "";
            LoadAuto();
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

        void LoadAuto() {
            suppress = true;
            bool choice;
            if (autoChoices.TryGetValue(ChoiceKey(), out choice)) auto.On = choice;
            else auto.On = preferences.AutoTranslateIncoming;
            suppress = false;
        }

        string OutgoingLanguage() {
            string language;
            if (outgoingLanguages.TryGetValue(selected, out language)) return language;
            return preferences.OutgoingLanguage == "channel" ? ChatRules.ChannelLanguage(selected) : preferences.OutgoingLanguage;
        }

        string ChoiceKey() {
            return preferences.Endpoint + "\0" + LanguageId(true) + "\0" + selected;
        }

        string LanguageId(bool incoming) {
            string caption = incoming ? incomingButton.Caption : languageButton.Caption;
            for (int i = 0; i < ChatRules.Languages.Length; i++) if (ChatRules.LanguageNames[i] == caption) return ChatRules.Languages[i];
            return "en";
        }

        static string LanguageLabel(string id) {
            for (int i = 0; i < ChatRules.Languages.Length; i++) if (ChatRules.Languages[i] == id) return ChatRules.LanguageNames[i];
            return "English";
        }

        void PickLanguage(PillButton source, bool incoming) {
            var menu = new LanguageMenu();
            for (int i = 0; i < ChatRules.Languages.Length; i++)
                menu.Add(ChatRules.Languages[i], ChatRules.LanguageNames[i], (incoming ? LanguageId(true) : OutgoingLanguage()) == ChatRules.Languages[i]);
            var origin = source.PointToScreen(new Point(0, source.Height + 6));
            menu.ShowAt(this, origin, delegate(string id) {
                if (incoming) {
                    incomingButton.Caption = LanguageLabel(id);
                    preferences.IncomingLanguage = id;
                    SavePreferences();
                    BumpTranslation();
                    KickAuto();
                } else if (selected.Length > 0) {
                    outgoingLanguages[selected] = id;
                    languageButton.Caption = LanguageLabel(id);
                    if (preview.Visible) preview.Visible = false;
                    LayoutChrome();
                }
            });
        }

        void ApplyPreferences() {
            suppress = true;
            incomingButton.Caption = LanguageLabel(preferences.IncomingLanguage);
            auto.On = preferences.AutoTranslateIncoming;
            providerSummary.Text = preferences.Endpoint == ChatRules.LocalTranslator ? "Local translation · on this PC"
                : preferences.Endpoint == Translator.DeepLFree ? "DeepL API Free · cloud translation"
                : preferences.Endpoint == Translator.DeepLPro ? "DeepL API Pro · cloud translation" : "Custom LibreTranslate server";
            suppress = false;
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
                preferences = dialog.Result;
            }
            autoChoices.Clear();
            outgoingLanguages.Clear();
            preview.Visible = false;
            actionError = notice = "";
            ApplyPreferences();
            BumpTranslation();
            if (preferences.Endpoint == ChatRules.LocalTranslator) PrepareTranslator();
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
                        Render();
                        KickAuto();
                    });
                } catch (Exception ex) {
                    Post(() => {
                        preparingTranslator = false;
                        translatorStatus = "";
                        translatorOffline = true;
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
            if (editingPreferences || preparingTranslator || !reading || !auto.On || translatorOffline || preferences.Endpoint.Length == 0 || translating) return;
            ChatChannel channel = Current();
            if (channel == null) return;
            for (int i = channel.Messages.Count - 1; i >= 0; i--) {
                ChatMessage candidate = channel.Messages[i];
                if (!candidate.Outgoing && !candidate.System && !attempted.Contains(candidate.Id)) {
                    TranslateOne(candidate);
                    return;
                }
            }
        }

        void TranslateOne(ChatMessage message) {
            if (translating || preparingTranslator || message.System) return;
            int generation = translationGeneration;
            attempted.Add(message.Id);
            translating = true;
            var request = new TranslationRequest { Endpoint = preferences.Endpoint, ApiKey = preferences.ApiKey, Text = message.Text, Source = "auto", Target = LanguageId(true) };
            Task.Run(delegate {
                try {
                    string text = service.Translate(request);
                    Post(delegate {
                        translating = false;
                        if (generation != translationGeneration) { KickAuto(); return; }
                        translatorOffline = false;
                        translations[message.Id] = text;
                        translationErrors.Remove(message.Id);
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
                            if (generation != translationGeneration) { KickAuto(); return; }
                            if (offline) {
                                translatorOffline = true;
                                attempted.Remove(message.Id);
                            } else {
                                translationErrors[message.Id] = error;
                                if (Translator.IsDeepL(request.Endpoint)) {
                                    auto.On = false;
                                    autoChoices[ChoiceKey()] = false;
                                    actionError = error;
                                }
                            }
                            PruneTranslationState(historyIds);
                            Render();
                            if (!offline && !Translator.IsDeepL(request.Endpoint)) KickAuto();
                        });
                    } catch (InvalidOperationException) { }
                }
            });
        }

        void PreviewDraft() {
            if (busy || preparingTranslator || draft.Input.Text.Trim().Length == 0) return;
            busy = true;
            actionError = "";
            notice = "";
            string text = draft.Input.Text;
            string language = OutgoingLanguage();
            int generation = translationGeneration;
            string previewChannel = selected;
            RefreshActions();
            var request = new TranslationRequest { Endpoint = preferences.Endpoint, ApiKey = preferences.ApiKey, Text = text, Source = "en", Target = language };
            Task.Run(delegate {
                try {
                    string translated = service.Translate(request);
                    Post(delegate {
                        busy = false;
                        translatorOffline = false;
                        if (generation == translationGeneration && selected == previewChannel && draft.Input.Text == text && OutgoingLanguage() == language) {
                            preview.Text = translated;
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
            ChatChannel channel = Current();
            if (busy || preparingTranslator || !reading || channel == null || draft.Input.Text.Trim().Length == 0 || preferences.Endpoint.Length == 0) return;
            if (snapshot == null || snapshot.Connected != true) return;
            busy = true;
            actionError = "";
            notice = "";
            string target = channel.Name;
            string text = draft.Input.Text;
            string language = OutgoingLanguage();
            RefreshActions();
            var request = new TranslationRequest { Endpoint = preferences.Endpoint, ApiKey = preferences.ApiKey, Text = text, Source = "en", Target = language };
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
            ChatChannel channel = Current();
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
            var osu = new List<ChatMessage> {
                new ChatMessage("s", "", "you joined #osu.", "19:40:58", false, true),
                new ChatMessage("a", "you", "is anyone around for a lobby?", "19:41:18", true, false),
                new ChatMessage("b", "friend", "привет, через минуту", "19:41:26", false, false),
                new ChatMessage("c", "friend", "я на русском канале если что — можем сыграть пару карт", "19:41:40", false, false),
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

        public void OpenSettings() {
            settingsOpen = true;
            LayoutChrome();
        }

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
        Action<string> chosen;

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
            if (screen.Y + height > screenBounds.Bottom) screen.Y -= height + 40;
            Location = screen;
            Size = new Size(180, height);
            Show(owner);
        }
    }
}
