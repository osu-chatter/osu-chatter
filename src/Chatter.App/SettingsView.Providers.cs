using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Chatter;

namespace Chatter.App {
    sealed partial class SettingsView {
        readonly List<Func<bool>> usesChatApi = new List<Func<bool>>();
        Action? refreshChatFields;
        void AddProvider(FlowLayoutPanel page, bool manual) {
            string[] names = { "Local · lightweight", "Local AI", "DeepL API Free", "DeepL API Pro", "LibreTranslate", "Chat API", "No translation" };
            string[] codes = { ChatRules.LocalTranslator, Translator.LocalModel, Translator.DeepLFree, Translator.DeepLPro, "custom", Translator.ChatApi, Translator.NoTranslation };
            if (manual) { names = new[] { "Same as automatic" }.Concat(names).ToArray(); codes = new[] { "" }.Concat(codes).ToArray(); }
            string original = manual ? working.ManualEndpoint : working.Endpoint;
            string selected = original.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? "custom" : original;
            var picker = new SettingsPicker { Name = manual ? "Manual provider" : "Automatic provider" }; picker.Items.AddRange(names); picker.SelectedIndex = Math.Max(0, Array.IndexOf(codes, selected));
            AddRow(page, manual ? "Translate button" : "Automatic engine", manual ? "Optional engine for messages you translate manually." : "Used for incoming chat and your outgoing drafts.", picker);
            var help = new Label { AutoSize = true, Font = UiFont.Small, ForeColor = Palette.Muted, MaximumSize = new System.Drawing.Size(650, 0), Margin = new Padding(3, 0, 3, 14) };
            page.Controls.Add(help);
            var url = Input(page, manual ? "Manual server URL" : "Server URL", selected == "custom" ? original : "https://");
            var key = Input(page, manual ? "Manual API key" : "API key", manual ? working.ManualApiKey : working.ApiKey); key.UseSystemPasswordChar = true;
            var modelButton = new PillButton { Caption = "Manage local models" }; modelButton.Click += delegate { SelectPage("Models"); };
            var modelRow = AddRow(page, "Local model", !string.IsNullOrWhiteSpace(working.Experience.ModelPath) ? Path.GetFileName(working.Experience.ModelPath) : "Install or select a model on this PC.", modelButton);
            string previous = codes[picker.SelectedIndex];
            string Family(string provider) => Translator.IsDeepL(provider) ? "deepl" : provider;
            var keys = new Dictionary<string, string> { [Family(previous)] = key.Text };
            void Refresh() {
                string engine = codes[picker.SelectedIndex];
                if (engine != previous) { keys[Family(previous)] = key.Text; key.Text = keys.TryGetValue(Family(engine), out string savedKey) ? savedKey : ""; previous = engine; }
                url.Parent!.Parent!.Visible = engine == "custom";
                key.Parent!.Parent!.Visible = Translator.IsDeepL(engine) || engine == "custom" || engine == Translator.ChatApi;
                modelRow.Visible = engine == Translator.LocalModel;
                help.Text = engine == "" ? "Uses the automatic engine and its credentials."
                    : engine == ChatRules.LocalTranslator ? "Free on this PC. Downloads language pairs as needed."
                    : engine == Translator.LocalModel ? "Runs on this PC without an API quota. Choose speed or quality in Models."
                    : Translator.IsDeepL(engine) ? "Requires a DeepL API key. Translated text counts towards your plan’s usage."
                    : engine == "custom" ? "Use your own LibreTranslate server’s /translate URL."
                    : engine == Translator.ChatApi ? "Connect to a local or hosted chat-completions server."
                    : "Read and send messages in their original language.";
                refreshChatFields?.Invoke(); page.PerformLayout(); page.Parent?.PerformLayout();
            }
            picker.SelectedIndexChanged += delegate { Refresh(); MarkChanged(); };
            usesChatApi.Add(() => codes[picker.SelectedIndex] == Translator.ChatApi);
            save.Add(() => {
                string engine = codes[picker.SelectedIndex];
                string endpoint = engine == "custom" ? url.Text.Trim() : engine;
                string credential = Translator.IsDeepL(engine) || engine == "custom" || engine == Translator.ChatApi ? key.Text.Trim() : "";
                if (manual) { working.ManualEndpoint = endpoint; working.ManualApiKey = credential; }
                else { working.Endpoint = endpoint; working.ApiKey = credential; }
            });
            Refresh();
        }
        void AddChatConfiguration(FlowLayoutPanel page) {
            var endpoint = Input(page, "Chat API endpoint", working.Experience.ChatEndpoint);
            var modelId = Input(page, "Chat API model", working.Experience.ChatModel);
            refreshChatFields = () => {
                bool active = usesChatApi.Any(p => p());
                endpoint.Parent!.Parent!.Visible = active; modelId.Parent!.Parent!.Visible = active;
            };
            refreshChatFields();
            save.Add(() => { if (usesChatApi.Any(p => p())) { working.Experience.ChatEndpoint = endpoint.Text.Trim(); working.Experience.ChatModel = modelId.Text.Trim(); } });
        }
    }
}
