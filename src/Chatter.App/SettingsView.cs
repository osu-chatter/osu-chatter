using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Chatter;

namespace Chatter.App {
    sealed partial class SettingsView : UserControl {
        public AppPreferences Result { get; private set; }
        public event Action<AppPreferences>? Saved;
        public event Action? BackRequested;
        readonly AppPreferences working;
        readonly CancellationTokenSource stop = new CancellationTokenSource();
        readonly List<Action> save = new List<Action>();
        readonly List<SettingsScroller> pages = new List<SettingsScroller>();
        readonly List<Action?> pageBuilders = new List<Action?>();
        int modelStatsGeneration;
        internal Func<string, string> ModelStatsReader { get; set; } = directory => "Local AI disk use: " + (LocalModelManager.DiskBytes(directory) / 1048576) + " MB\n" + (string.IsNullOrWhiteSpace(directory) ? LocalModelManager.DirectoryPath : directory) + "\n\nLightweight language pairs:\n" + LocalPackageManager.Describe();
        readonly SettingsNavigation navigation = new SettingsNavigation();
        readonly Label title = new Label { Text = "Settings", ForeColor = Palette.Ink, Font = UiFont.Title };
        readonly Label subtitle = new Label { Text = "Translation, overlay and app preferences", ForeColor = Palette.Muted, Font = UiFont.Small };
        readonly Label pageTitle = new Label { ForeColor = Palette.Ink, Font = UiFont.Title };
        readonly Label pageDetail = new Label { ForeColor = Palette.Muted, Font = UiFont.Small };
        readonly Label status = new Label { ForeColor = Palette.Muted, Font = UiFont.Small, Text = "Save to apply · Ctrl+S" };
        readonly PillButton apply = new PillButton { Caption = "Save changes", Filled = true };
        readonly PillButton back = new PillButton { Caption = "Back to chat" };
        TextBox runtime = null!, model = null!;
        SettingsNumber gpuLayers = null!;
        bool downloadActive;
        bool initializing = true;
        bool downloading { get => downloadActive; set { downloadActive = value; apply.Enabled = !value; } }
        int Px(int v) => (int)Math.Round(v * DeviceDpi / 96.0);
        public SettingsView(AppPreferences preferences, string channel, Size? initialSize = null) {
            working = preferences.Copy(); Result = working.Copy();
            Name = "SettingsView"; AccessibleName = "Settings"; Font = UiFont.Body; BackColor = Palette.Bg; ForeColor = Palette.Ink;
            Size = initialSize ?? new Size(848, 720);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            SuspendLayout();
            Controls.AddRange(new Control[] { title, subtitle, pageTitle, pageDetail, navigation, back, status, apply });
            back.Click += delegate { BackRequested?.Invoke(); };
            apply.Click += delegate { SaveChanges(); };
            navigation.Chosen += SelectPage;
            Page("Translation", translation => {
            Heading(translation, "Translation engine");
            AddProvider(translation, false);
            AddProvider(translation, true);
            AddChatConfiguration(translation);
            Check(translation, "Automatic translation", working.AutoTranslateIncoming, v => working.AutoTranslateIncoming = v, "Translate incoming messages by default. Each conversation can override this.");
            Check(translation, "Send original text", working.Experience.SendWithoutTranslation, v => working.Experience.SendWithoutTranslation = v, "Skip translation when sending from Chatter or its reply panel.");
            Heading(translation, "Default languages");
            Choice(translation, "Read messages in", ChatRules.LanguageNames, ChatRules.Languages, working.IncomingLanguage, v => working.IncomingLanguage = v);
            var outgoingNames = new string[ChatRules.Languages.Length + 1]; var outgoingCodes = new string[outgoingNames.Length];
            outgoingNames[0] = "Follow the channel language"; outgoingCodes[0] = "channel";
            Array.Copy(ChatRules.LanguageNames, 0, outgoingNames, 1, ChatRules.Languages.Length); Array.Copy(ChatRules.Languages, 0, outgoingCodes, 1, ChatRules.Languages.Length);
            Choice(translation, "Send messages in", outgoingNames, outgoingCodes, working.OutgoingLanguage, v => working.OutgoingLanguage = v);
            Source(translation, "Language you write in", working.OutgoingSource, v => working.OutgoingSource = v);
            Source(translation, "Incoming source language", working.Experience.IncomingSource, v => working.Experience.IncomingSource = v);
            if (!string.IsNullOrEmpty(channel)) {
                Heading(translation, "This conversation · " + channel);
                var sourceNames = new string[ChatRules.Languages.Length + 2]; var sourceCodes = new string[sourceNames.Length];
                sourceNames[0] = "Follow default source"; sourceNames[1] = "Automatic"; sourceCodes[0] = ""; sourceCodes[1] = "auto";
                Array.Copy(ChatRules.LanguageNames, 0, sourceNames, 2, ChatRules.Languages.Length); Array.Copy(ChatRules.Languages, 0, sourceCodes, 2, ChatRules.Languages.Length);
                Choice(translation, "Automatic translation", new[] { "Follow default", "On", "Off" }, new[] { "default", "on", "off" }, working.FindChannel(channel)?.AutoTranslate is bool enabled ? (enabled ? "on" : "off") : "default", v => {
                    var row = working.FindChannel(channel)?.Copy() ?? new ChannelPreference { Name = channel }; row.AutoTranslate = v == "default" ? (bool?)null : v == "on"; RememberConversation(row);
                });
                Choice(translation, "Read this conversation in", IncomingChoiceNames(), outgoingCodes, working.FindChannel(channel)?.IncomingLanguage is string incoming && incoming.Length > 0 ? incoming : "channel", v => {
                    var row = working.FindChannel(channel)?.Copy() ?? new ChannelPreference { Name = channel }; row.IncomingLanguage = v == "channel" ? "" : v; RememberConversation(row);
                });
                Choice(translation, "Incoming source", sourceNames, sourceCodes, working.FindChannel(channel)?.IncomingSource ?? "", v => {
                    var row = working.FindChannel(channel)?.Copy() ?? new ChannelPreference { Name = channel }; row.IncomingSource = v; RememberConversation(row);
                });
            }
            Heading(translation, "History & context");
            Choice(translation, "Existing messages", new[] { "New messages only", "Recent history", "All retained history (up to 200)" }, new[] { "new", "recent", "all" }, working.Experience.HistoryPolicy, v => working.Experience.HistoryPolicy = v);
            Number(translation, "Recent history limit", 1, 200, working.Experience.HistoryCount, v => working.Experience.HistoryCount = v);
            Number(translation, "Context messages", 0, 12, working.Experience.ContextMessages, v => working.Experience.ContextMessages = v, "Previous messages from this conversation. Used by Local AI and chat API engines.");
            var glossary = Input(translation, "Terms and names to preserve", working.Experience.Glossary); glossary.Multiline = true; save.Add(() => working.Experience.Glossary = glossary.Text);
            });
            Page("In-game chat", game => {
            Heading(game, "The game's own chat panel");
            Check(game, "Translate incoming messages in osu!", working.TranslateGameChat, v => working.TranslateGameChat = v);
            Choice(game, "Message presentation", new[] { "Translation only", "Translation + original", "Original only" }, new[] { "translation", "both", "original" }, working.Experience.NativePresentation, v => working.Experience.NativePresentation = v);
            Check(game, "Enable /t and /translate commands in osu!", working.Experience.NativeCommands, v => working.Experience.NativeCommands = v);
            Note(game, "Example: /t en jestem developerem aplikacji osu-chatter\nEnter submits one translated message to the conversation captured at that time. Commands work while Chatter is connected in menus/lobby. On failure, the original game draft is restored if the same account and conversation are selected and the input is empty.");

            });
            Page("Overlay", overlay => {
            Check(overlay, "Show the game overlay", working.ShowGameOverlay, v => working.ShowGameOverlay = v);
            Check(overlay, "Allow mouse interaction, dragging and resizing", working.Experience.OverlayInteractive, v => working.Experience.OverlayInteractive = v);
            Number(overlay, "Opacity (%)", 30, 100, working.Experience.OverlayOpacity, v => working.Experience.OverlayOpacity = v);
            Number(overlay, "Width", 320, 2400, working.Experience.OverlayWidth, v => working.Experience.OverlayWidth = v);
            Number(overlay, "Height", 220, 1600, working.Experience.OverlayHeight, v => working.Experience.OverlayHeight = v);
            var replyKey = Input(overlay, "Reply shortcut", working.Experience.ReplyShortcut); save.Add(() => working.Experience.ReplyShortcut = ValidateShortcut(replyKey.Text));
            var overlayKey = Input(overlay, "Lock / unlock shortcut", working.Experience.OverlayShortcut); save.Add(() => working.Experience.OverlayShortcut = ValidateShortcut(overlayKey.Text));
            Note(overlay, "Shortcut format: Control, Shift, Enter or Control, Shift, O. Locked mode passes mouse input to osu!. Unlocked mode supports history scrolling, links, dragging and resizing.");
            Button(overlay, "Reset overlay position", () => { working.Experience.OverlayX = working.Experience.OverlayY = -1; status.Text = "Overlay position will reset when you save."; });

            });
            Page("Application", app => {
            Check(app, "Always on top", working.Experience.AlwaysOnTop, v => working.Experience.AlwaysOnTop = v);
            Check(app, "Minimize to notification area", working.Experience.MinimizeToTray, v => working.Experience.MinimizeToTray = v);
            Check(app, "Notify about private messages while unfocused", working.Experience.NotifyPrivateMessages, v => working.Experience.NotifyPrivateMessages = v);
            Check(app, "Use modern selectable transcript with color emoji", working.Experience.ModernTranscript, v => working.Experience.ModernTranscript = v);
            Check(app, "Start reading when Chatter opens", working.StartReading, v => working.StartReading = v);
            Number(app, "Message text size (modern transcript)", 11, 24, working.Experience.MessageFontSize, v => working.Experience.MessageFontSize = v);
            Note(app, "Ctrl+Tab / Ctrl+Shift+Tab: next / previous conversation\nCtrl+,: Settings · Ctrl+F: search this conversation\nUp in an empty composer: recall the last submitted draft\nWindow placement and maximized state are saved automatically.");

            });
            Page("Models", models => {
            Heading(models, "Local AI · no translation API quota");
            Note(models, "Download a free model to translate on your PC. Choose Fast, Quality, or MADLAD-400 to compare translations. Installed models work offline.");
            var downloadDirectory = Input(models, "Model storage folder", string.IsNullOrWhiteSpace(working.Experience.DownloadDirectory) ? LocalModelManager.DirectoryPath : working.Experience.DownloadDirectory, "Choose any drive for AI models and engines. Existing files stay in their current folder; select them below to use them.");
            save.Add(() => working.Experience.DownloadDirectory = downloadDirectory.Text.Trim());
            Button(models, "Choose model storage folder…", () => {
                using (var picker = new FolderBrowserDialog { SelectedPath = downloadDirectory.Text }) if (picker.ShowDialog(FindForm()) == DialogResult.OK) {
                    downloadDirectory.Text = picker.SelectedPath; working.Experience.DownloadDirectory = picker.SelectedPath;
                    string relocated = Path.Combine(picker.SelectedPath, Path.GetFileName(model.Text));
                    if (File.Exists(relocated)) {
                        model.Text = relocated;
                        string executable = LocalModelMetadata.Read(relocated).Kind == "madlad" ? "llama-completion.exe" : "llama-server.exe";
                        string engine = Path.Combine(picker.SelectedPath, working.Experience.EngineBackend == "vulkan" ? "engine-vulkan" : "engine", executable);
                        runtime.Text = File.Exists(engine) ? engine : "";
                        status.Text = "Found your model in this folder. Prepare its engine if needed, then save.";
                    } else status.Text = "Storage folder changed. Install or select a model in this folder, then save.";
                }
            });
            Button(models, "Use existing / custom GGUF model…", () => {
                using (var picker = new OpenFileDialog { Filter = "llama.cpp GGUF model|*.gguf", InitialDirectory = downloadDirectory.Text }) {
                    if (picker.ShowDialog(FindForm()) != DialogResult.OK) return;
                    try {
                        var details = LocalModelMetadata.Read(picker.FileName);
                        model.Text = picker.FileName; working.Experience.ModelPath = picker.FileName;
                        string executable = details.Kind == "madlad" ? "llama-completion.exe" : "llama-server.exe";
                        string selectedEngine = Path.Combine(downloadDirectory.Text, working.Experience.EngineBackend == "vulkan" ? "engine-vulkan" : "engine", executable);
                        runtime.Text = File.Exists(selectedEngine) ? selectedEngine : "";
                        status.Text = "Selected " + details.Name + " (" + details.Architecture + "). Prepare its engine below, then save.";
                    } catch (Exception ex) { status.Text = ex.Message; }
                }
            });
            Note(models, "Custom GGUF support: Hy-MT/instruction chat models, MADLAD-400 T5, and PLaMo Translate. The model must be supported by the installed llama.cpp engine. Other formats require an external Chat API server.");
            Choice(models, "Local model preset", new[] { "Fast · Hy-MT2 1.8B Q4", "Quality · Hy-MT2 7B Q4 (larger download)", "MADLAD-400 · 3B Q4", "PLaMo Translate · 2B Q4" }, new[] { "fast", "quality", "madlad", "plamo" }, working.Experience.ModelVariant, v => working.Experience.ModelVariant = v);
            Choice(models, "Local engine", new[] { "CPU", "Vulkan GPU · AMD / Intel / NVIDIA" }, new[] { "cpu", "vulkan" }, working.Experience.EngineBackend, v => working.Experience.EngineBackend = v);
            async void InstallModel(bool engineOnly) {
                if (downloading) return; working.Experience.DownloadDirectory = downloadDirectory.Text.Trim(); downloading = true;
                working.Experience.ModelPath = model.Text.Trim();
                try {
                    var progress = new Progress<string>(s => { if (!IsDisposed) status.Text = s; });
                    var options = working.Experience.Copy();
                    var installed = await Task.Run(() => LocalModelManager.Install(options, s => ((IProgress<string>)progress).Report(s), stop.Token, engineOnly));
                    if (IsDisposed || Disposing) return;
                    runtime.Text = installed.RuntimePath; model.Text = installed.ModelPath;
                    working.Experience.GpuLayers = installed.GpuLayers;
                    gpuLayers.Value = installed.GpuLayers;
                    status.Text = "Installed. Select Local AI on the Translation tab, then save.";
                } catch (Exception ex) { if (!IsDisposed) status.Text = ex is OperationCanceledException ? "Download cancelled." : ex.Message; }
                finally { downloading = false; }
            }
            Button(models, "Install recommended local engine + model", () => InstallModel(false));
            Button(models, "Prepare engine for selected custom model", () => InstallModel(true));
            Heading(models, "Advanced model options");
            runtime = Input(models, "Engine file", working.Experience.RuntimePath); save.Add(() => working.Experience.RuntimePath = runtime.Text.Trim());
            Button(models, "Browse engine…", () => Pick(runtime, "Local engine|llama-server.exe;llama-completion.exe"));
            model = Input(models, "Model file (.gguf)", working.Experience.ModelPath); save.Add(() => working.Experience.ModelPath = model.Text.Trim());
            Button(models, "Browse model…", () => Pick(model, "GGUF model|*.gguf"));
            Number(models, "CPU threads", 1, 32, working.Experience.CpuThreads, v => working.Experience.CpuThreads = v);
            gpuLayers = Number(models, "GPU layers (0 = CPU; requires a compatible engine)", 0, 100, working.Experience.GpuLayers, v => working.Experience.GpuLayers = v);
            Note(models, "Downloads: Fast 1.1 GB · Quality 4.6 GB · MADLAD 1.9 GB · PLaMo 1.8 GB. Allow 1.5 / 6.5 / 2.5 / 2.5 GB free respectively. Vulkan installation enables GPU offload. MADLAD translates individual messages without context or glossary and reloads per request. PLaMo uses the PLaMo Community License. Slang and jokes can still be mistranslated.");
            var installedModels = new Label { ForeColor = Palette.Muted, AutoSize = true, Font = UiFont.Small, MaximumSize = new Size(650, 0), Margin = new Padding(0, 12, 0, 18) };
            models.Controls.Add(installedModels);
            Action refreshModels = async () => {
                string directory = downloadDirectory.Text.Trim(); int generation = ++modelStatsGeneration;
                installedModels.Text = "Checking installed models…";
                try {
                    var read = ModelStatsReader;
                    string details = await Task.Run(() => read(directory));
                    if (!IsDisposed && !Disposing && generation == modelStatsGeneration) installedModels.Text = details;
                } catch (Exception ex) { if (!IsDisposed && !Disposing && generation == modelStatsGeneration) installedModels.Text = ex.Message; }
            };
            refreshModels(); Button(models, "Refresh downloaded models", refreshModels);
            var pairFrom = new SettingsPicker { Width = 640 }; pairFrom.Items.AddRange(ChatRules.LanguageNames); pairFrom.SelectedIndex = 0; AddRow(models, "Download from", "Source language for the lightweight model.", pairFrom);
            var pairTo = new SettingsPicker { Width = 640 }; pairTo.Items.AddRange(ChatRules.LanguageNames); pairTo.SelectedIndex = 1; AddRow(models, "Download to", "Target language. English pivot pairs are included.", pairTo);
            Note(models, "Pre-download a lightweight language pair before playing. Non-English pairs also prepare the English pivot models.");
            Button(models, "Pre-download lightweight language pair", async () => {
                if (downloading) return;
                string source = ChatRules.Languages[pairFrom.SelectedIndex], target = ChatRules.Languages[pairTo.SelectedIndex]; downloading = true;
                using (var local = new LocalTranslator()) using (stop.Token.Register(local.Dispose)) {
                    var progress = new Progress<string>(s => { if (!IsDisposed) status.Text = s; });
                    local.Status += s => ((IProgress<string>)progress).Report(s);
                    try { await Task.Run(() => local.Preload(source, target)); if (!IsDisposed) { status.Text = "Language pair ready for offline use."; refreshModels(); } }
                    catch (Exception ex) { if (!IsDisposed) status.Text = stop.IsCancellationRequested ? "Download cancelled." : ex.Message; }
                    finally { downloading = false; }
                }
            });
            Button(models, "Clear lightweight download cache…", () => {
                if (downloading) return;
                if (MessageBox.Show(FindForm(), "Remove temporary lightweight translator downloads? Installed language pairs are kept.", "Clear download cache", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                try { LocalPackageManager.ClearDownloadCache(); status.Text = "Download cache cleared."; refreshModels(); } catch (Exception ex) { status.Text = ex.Message; }
            });
            });
            Page("About", about => {
            Heading(about, "osu-chatter");
            Note(about, "Version " + (typeof(MainForm).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? typeof(MainForm).Assembly.GetName().Version.ToString()) + "\nUnofficial osu!stable companion. Local chat changes never transmit translated incoming messages.\n\nTranslation text is kept in memory. Cloud engines receive the current message and, for chat-completions engines, the context selected above.\n\nSettings: " + PreferencesStore.DefaultPath);
            bool inspecting = false;
            Button(about, "Inspect current osu! compatibility", async () => {
                if (inspecting) return;
                inspecting = true; status.Text = "Inspecting osu! version and integration…";
                try {
                    string report = await Task.Run(() => StableClientInfo.Describe());
                    if (IsDisposed || Disposing) return;
                    Clipboard.SetText(report); status.Text = "Compatibility snapshot copied to clipboard (no chat text).";
                } catch (Exception ex) { if (!IsDisposed && !Disposing) status.Text = ex.Message; }
                finally { inspecting = false; }
            });
            Button(about, "Check for app updates", () => Process.Start(new ProcessStartInfo("https://github.com/osu-chatter/osu-chatter/releases") { UseShellExecute = true }));
            Button(about, "Open compatibility guide", () => Process.Start(new ProcessStartInfo("https://github.com/osu-chatter/osu-chatter/blob/main/docs/stable-compatibility.md") { UseShellExecute = true }));
            Note(about, "Modern transcript uses Microsoft Edge WebView2. If its runtime is unavailable, Chatter uses the built-in transcript.\nLocal AI: Hy-MT2 and MADLAD-400 (Apache-2.0), llama.cpp (MIT). See THIRD-PARTY-NOTICES.txt in the release archive.");

            });
            SelectPage(0); ResumeLayout(true); initializing = false;
        }
        void MarkChanged() { if (!initializing && !IsDisposed) { status.ForeColor = Palette.Muted; status.Text = "Unsaved changes · Save to apply."; } }
        void RememberConversation(ChannelPreference row) {
            var before = working.FindChannel(row.Name) ?? new ChannelPreference { Name = row.Name };
            if (before.IncomingLanguage == row.IncomingLanguage && before.OutgoingLanguage == row.OutgoingLanguage && before.OutgoingSource == row.OutgoingSource && before.IncomingSource == row.IncomingSource && before.AutoTranslate == row.AutoTranslate) return;
            working.Remember(row);
        }
        internal bool SaveChanges() {
            if (downloading) { status.Text = "Finish the download, or return to chat to cancel it."; return false; }
            try {
                foreach (var action in save) action(); working.Validate();
                var result = working.Copy(); Saved?.Invoke(result); Result = result;
                status.ForeColor = Palette.Live; status.Text = "Changes saved."; return true;
            } catch (Exception ex) { status.ForeColor = Color.FromArgb(254, 205, 211); status.Text = ex.Message; return false; }
        }
        internal void SelectPage(int index) {
            index = Math.Max(0, Math.Min(index, pages.Count - 1));
            SuspendLayout();
            try {
                var build = pageBuilders[index];
                if (build != null) {
                    pageBuilders[index] = null;
                    pages[index].Content.SuspendLayout();
                    try { build(); } finally { pages[index].Content.ResumeLayout(true); }
                }
                navigation.SelectSection(index);
                // Prepare and reveal the new section before hiding the previous one.
                pages[index].BringToFront(); pages[index].Visible = true;
                for (int i = 0; i < pages.Count; i++) if (i != index) pages[i].Visible = false;
                pageTitle.Text = navigation.Sections[index];
                pageDetail.Text = new[] { "Choose how messages are translated and which languages you use.", "Read translations and send commands in osu!’s own chat.", "Make the overlay comfortable to read and interact with.", "Appearance, notifications and everyday shortcuts.", "Download and manage engines that run on your PC.", "App version, updates and compatibility information." }[index];
            } finally { ResumeLayout(true); }
            PaintKit.Redraw(this);
        }
        internal void SelectPage(string name) { int at = Array.IndexOf(navigation.Sections, name); if (at >= 0) SelectPage(at); }
        protected override void OnLayout(LayoutEventArgs e) {
            base.OnLayout(e); if (navigation == null) return;
            int pad = Px(26), navW = Px(152), headerH = Px(88), footerH = Px(78);
            title.SetBounds(pad, Px(21), Math.Max(1, Width - pad * 2 - Px(160)), Px(30));
            subtitle.SetBounds(pad, Px(55), Math.Max(1, Width - pad * 2 - Px(160)), Px(18));
            back.SetBounds(Math.Max(pad, Width - pad - Px(118)), Px(29), Px(118), Px(34));
            navigation.SetBounds(Px(12), headerH + Px(20), navW, Math.Max(1, Height - headerH - footerH - Px(20)));
            int x = navW + Px(34), width = Math.Max(1, Math.Min(Px(820), Width - x - pad));
            pageTitle.SetBounds(x, headerH + Px(17), width, Px(30));
            pageDetail.SetBounds(x, headerH + Px(52), width, Px(36));
            foreach (var page in pages) LayoutPage(page);
            apply.SetBounds(Math.Max(x, Width - pad - Px(136)), Height - Px(56), Px(136), Px(34));
            status.SetBounds(x, Height - Px(58), Math.Max(1, apply.Left - x - Px(18)), Px(46));
        }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e); using (var pen = new Pen(Palette.Line)) { e.Graphics.DrawLine(pen, 0, Px(88), Width, Px(88)); e.Graphics.DrawLine(pen, Px(26), Height - Px(78), Width - Px(26), Height - Px(78)); }
        }
        protected override void Dispose(bool disposing) { if (disposing && !IsDisposed) { stop.Cancel(); stop.Dispose(); } base.Dispose(disposing); }
        static string[] IncomingChoiceNames() { var result = new string[ChatRules.LanguageNames.Length + 1]; result[0] = "Follow default incoming language"; Array.Copy(ChatRules.LanguageNames, 0, result, 1, ChatRules.LanguageNames.Length); return result; }
        void Page(string title, Action<FlowLayoutPanel> build) {
            var viewport = new SettingsScroller { Name = title, Visible = false }; pages.Add(viewport); Controls.Add(viewport);
            LayoutPage(viewport);
            pageBuilders.Add(() => build(viewport.Content));
        }
        void LayoutPage(SettingsScroller page) {
            int x = Px(186), width = Math.Max(1, Math.Min(Px(820), Width - x - Px(26)));
            page.SetBounds(x, Px(188), width, Math.Max(1, Height - Px(266)));
        }
        SettingsRow AddRow(FlowLayoutPanel page, string title, string description, Control editor, bool wide = false) {
            var row = new SettingsRow(title, description, editor, wide, Math.Max(1, page.Width)); page.Controls.Add(row);
            if (editor is SettingsPicker picker) picker.ScrollRequested = delta => ((SettingsScroller)page.Parent).ScrollBy(delta / 3);
            return row;
        }
        void Heading(FlowLayoutPanel page, string title) => page.Controls.Add(new Label { Text = title.ToUpperInvariant(), Font = UiFont.Section, ForeColor = Palette.Muted, Height = Px(26), Margin = new Padding(0, Px(15), 0, Px(6)) });
        void Note(FlowLayoutPanel page, string text) => page.Controls.Add(new Label { Text = text, Font = UiFont.Small, ForeColor = Palette.Muted, AutoSize = true, MaximumSize = new Size(650, 0), Margin = new Padding(0, Px(7), 0, Px(15)) });
        TextBox Input(FlowLayoutPanel page, string caption, string value, string description = "") {
            var field = new SettingsText(value); field.Name = caption; field.Input.Name = caption;
            AddRow(page, caption, description, field); field.Input.TextChanged += delegate { MarkChanged(); }; return field.Input;
        }
        void Check(FlowLayoutPanel page, string caption, bool value, Action<bool> setter, string description = "") {
            var input = new SwitchBox { On = value, TabStop = true, AccessibleName = caption }; AddRow(page, caption, description, input); input.Toggled += delegate { MarkChanged(); }; save.Add(() => setter(input.On));
        }
        SettingsNumber Number(FlowLayoutPanel page, string caption, int min, int max, int value, Action<int> setter, string description = "") {
            var input = new SettingsNumber(min, max, value); AddRow(page, caption, description, input); input.Changed += MarkChanged; save.Add(() => setter(input.Value));
            return input;
        }
        SettingsPicker Choice(FlowLayoutPanel page, string caption, string[] labels, string[] values, string current, Action<string> setter) {
            var combo = new SettingsPicker { Name = caption }; combo.Items.AddRange(labels); combo.SelectedIndex = Math.Max(0, Array.IndexOf(values, current));
            AddRow(page, caption, "", combo); combo.SelectedIndexChanged += delegate { setter(values[combo.SelectedIndex]); MarkChanged(); }; save.Add(() => setter(values[combo.SelectedIndex])); return combo;
        }
        void Source(FlowLayoutPanel page, string label, string current, Action<string> setter) {
            var names = new string[ChatRules.Languages.Length + 1]; var values = new string[names.Length]; names[0] = "Automatic"; values[0] = "auto";
            Array.Copy(ChatRules.LanguageNames, 0, names, 1, ChatRules.Languages.Length); Array.Copy(ChatRules.Languages, 0, values, 1, ChatRules.Languages.Length); Choice(page, label, names, values, current, setter);
        }
        void Button(FlowLayoutPanel page, string label, Action action) {
            if ((label.StartsWith("Choose ", StringComparison.Ordinal) || label.StartsWith("Browse ", StringComparison.Ordinal)) && page.Controls.Count > 0 && page.Controls[page.Controls.Count - 1] is SettingsRow row && row.Editor is SettingsText field) { field.AddBrowse(action); return; }
            var button = new PillButton { Caption = label, Height = Px(34), Margin = new Padding(0, 0, 0, Px(12)) }; button.Click += delegate { action(); }; page.Controls.Add(button);
        }
        void Pick(TextBox field, string filter) { using (var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true }) if (dialog.ShowDialog(FindForm()) == DialogResult.OK) field.Text = dialog.FileName; }
        static string ValidateShortcut(string text) {
            try { Keys key = (Keys)new KeysConverter().ConvertFromInvariantString(text); if ((key & Keys.KeyCode) == Keys.None || (key & Keys.Modifiers) == Keys.None) throw new InvalidOperationException(); return new KeysConverter().ConvertToInvariantString(key); }
            catch { throw new InvalidOperationException("Choose a shortcut with Control, Alt or Shift and one key."); }
        }
    }
}
