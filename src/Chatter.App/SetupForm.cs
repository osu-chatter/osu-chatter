using System;
using System.Drawing;
using System.Windows.Forms;
using Chatter;

namespace Chatter.App {
    sealed class DarkPicker : ComboBox {
        public DarkPicker() {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            DropDownStyle = ComboBoxStyle.DropDownList;
            FlatStyle = FlatStyle.Flat;
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = 24;
            BackColor = Palette.Field;
            ForeColor = Palette.Ink;
            Font = UiFont.Body;
        }
        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.Clear(Palette.Bg);
            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            PaintKit.FillRound(e.Graphics, bounds, 7, Palette.Field);
            PaintKit.StrokeRound(e.Graphics, bounds, 7, Focused ? Palette.Accent : Palette.Line);
            string label = SelectedIndex >= 0 ? GetItemText(Items[SelectedIndex]) : "Choose…";
            TextRenderer.DrawText(e.Graphics, label, Font, new Rectangle(12, 0, Width - 38, Height), Palette.Ink, PaintKit.Single);
            using (var pen = new Pen(Palette.Muted, 1.5f)) {
                int x = Width - 20, y = Height / 2;
                e.Graphics.DrawLines(pen, new[] { new Point(x - 4, y - 2), new Point(x, y + 2), new Point(x + 4, y - 2) });
            }
        }
        protected override void OnSelectedIndexChanged(EventArgs e) { base.OnSelectedIndexChanged(e); Invalidate(); }
        protected override void OnDrawItem(DrawItemEventArgs e) {
            using (var brush = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? Palette.Elevated : Palette.Field))
                e.Graphics.FillRectangle(brush, e.Bounds);
            string label = e.Index >= 0 && e.Index < Items.Count ? GetItemText(Items[e.Index]) : Text;
            var bounds = new Rectangle(e.Bounds.X + 10, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 14), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, label, Font, bounds, Palette.Ink, PaintKit.Single);
        }
    }

    public sealed class SetupForm : Form {
        readonly ComboBox provider = Picker();
        readonly ComboBox incoming = Picker();
        readonly ComboBox outgoing = Picker();
        readonly LineField key = new LineField();
        readonly LineField server = new LineField();
        readonly Label detail = Copy();
        readonly Label keyLabel = Copy();
        readonly Label serverLabel = Copy();
        readonly Label error = Copy();
        readonly Label incomingLabel = Copy("Translate incoming chat to");
        readonly Label outgoingLabel = Copy("Default outgoing language");
        readonly Label autoHint = Copy("Includes existing messages in the selected chat. Cloud translations count toward your API usage.");
        readonly CheckBox automatic = new CheckBox();
        readonly CheckBox startReading = new CheckBox();
        readonly PillButton finish = new PillButton { Filled = true };
        readonly PillButton cancel = new PillButton { Caption = "Cancel" };
        readonly bool persist;
        readonly Panel content = new Panel { Dock = DockStyle.Fill, AutoScroll = true, AutoScrollMinSize = new Size(0, 690) };
        bool loading;
        int previousProvider = -1;
        public AppPreferences Result { get; private set; }
        public SetupForm(AppPreferences current, bool firstRun = true, bool persist = true) {
            this.persist = persist;
            Text = firstRun ? "Welcome to osu-chatter" : "Translation preferences";
            Icon = AppIcon.Create();
            Font = UiFont.Body;
            ForeColor = Palette.Ink;
            BackColor = Palette.Bg;
            ClientSize = new Size(620, Math.Min(690, Screen.PrimaryScreen.WorkingArea.Height - 100));
            Controls.Add(content);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            var title = Copy(firstRun ? "Make chatter yours" : "Translation preferences");
            title.Font = UiFont.Title;
            Put(title, 28, 24, 560, 36);
            Put(Copy("Choose how messages are translated. You can change these choices in Settings."), 28, 67, 560, 40);
            Put(Copy("TRANSLATION PROVIDER"), 28, 114, 560, 20);
            provider.Items.AddRange(new object[] { "Local · private, on this PC", "DeepL API Free", "DeepL API Pro", "Custom LibreTranslate server" });
            Put(provider, 28, 140, 560, 32);
            Put(detail, 28, 180, 560, 48);
            Put(keyLabel, 28, 235, 560, 20);
            key.Input.UseSystemPasswordChar = true;
            key.Input.MaxLength = 512;
            Put(key, 28, 259, 560, 40);
            Put(serverLabel, 28, 309, 560, 20);
            Put(server, 28, 333, 560, 40);
            Put(incomingLabel, 28, 391, 270, 20);
            Put(outgoingLabel, 318, 391, 270, 20);
            incoming.Items.AddRange(ChatRules.LanguageNames);
            outgoing.Items.Add("Match the channel name");
            outgoing.Items.AddRange(ChatRules.LanguageNames);
            Put(incoming, 28, 417, 270, 32);
            Put(outgoing, 318, 417, 270, 32);
            automatic.Text = "Automatically translate incoming chat";
            automatic.ForeColor = Palette.Ink;
            automatic.FlatStyle = FlatStyle.Flat;
            Put(automatic, 28, 466, 560, 24);
            Put(autoHint, 48, 494, 540, 38);
            startReading.Text = "Start reading open osu! chats when chatter launches";
            startReading.ForeColor = Palette.Ink;
            startReading.FlatStyle = FlatStyle.Flat;
            Put(startReading, 28, 540, 560, 24);
            error.ForeColor = Color.FromArgb(254, 205, 211);
            Put(error, 28, 575, 560, 40);
            finish.Caption = firstRun ? "Start chatter" : "Save choices";
            Put(cancel, 340, 632, 108, 36);
            Put(finish, 460, 632, 128, 36);
            finish.Click += delegate { SaveChoices(); };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            provider.SelectedIndexChanged += delegate { UpdateProvider(); };
            loading = true;
            provider.SelectedIndex = current.Endpoint == ChatRules.LocalTranslator ? 0 : current.Endpoint == Translator.DeepLFree ? 1 : current.Endpoint == Translator.DeepLPro ? 2 : 3;
            key.Input.Text = current.ApiKey;
            server.Input.Text = provider.SelectedIndex == 3 ? current.Endpoint : "https://";
            incoming.SelectedIndex = Math.Max(0, Array.IndexOf(ChatRules.Languages, current.IncomingLanguage));
            outgoing.SelectedIndex = current.OutgoingLanguage == "channel" ? 0 : Array.IndexOf(ChatRules.Languages, current.OutgoingLanguage) + 1;
            automatic.Checked = current.AutoTranslateIncoming;
            startReading.Checked = current.StartReading;
            loading = false;
            UpdateProvider(false);
            KeyPreview = true;
            KeyDown += (sender, e) => { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
        }

        static ComboBox Picker() => new DarkPicker();
        static Label Copy(string text = "") => new Label { Text = text, AutoSize = false, ForeColor = Palette.Muted, BackColor = Palette.Bg, Font = UiFont.Body };
        void Put(Control control, int x, int y, int w, int h) { control.SetBounds(x, y, w, h); content.Controls.Add(control); }
        void UpdateProvider(bool resetAuto = true) {
            bool local = provider.SelectedIndex == 0;
            bool custom = provider.SelectedIndex == 3;
            bool previousDeepL = previousProvider == 1 || previousProvider == 2;
            bool currentDeepL = provider.SelectedIndex == 1 || provider.SelectedIndex == 2;
            if (!loading && resetAuto && !(previousDeepL && currentDeepL)) key.Input.Clear();
            previousProvider = provider.SelectedIndex;
            key.Visible = keyLabel.Visible = !local;
            server.Visible = serverLabel.Visible = custom;
            keyLabel.Text = custom ? "API key (optional; encrypted for your Windows account)" : "DeepL API key (encrypted for your Windows account)";
            serverLabel.Text = "LibreTranslate /translate URL";
            detail.Text = local ? "Messages stay on this PC. First setup downloads the runtime; new languages download their models automatically."
                : custom ? "Messages are sent to your chosen LibreTranslate server. Use HTTPS, or HTTP on localhost."
                : "Uses your DeepL API subscription and sends message text to DeepL. Choose the Free or Pro plan matching your API key.";
            autoHint.Text = local ? "Translate existing and new messages in the selected conversation."
                : "Includes existing messages in the selected chat. Cloud translations count toward your API usage.";
            if (!loading && resetAuto) automatic.Checked = local;
            error.Text = "";
            int y = local ? 245 : custom ? 391 : 318;
            float scale = DeviceDpi / 96f;
            Func<int, int> px = value => (int)Math.Round(value * scale);
            content.AutoScrollPosition = Point.Empty;
            incomingLabel.Top = outgoingLabel.Top = px(y);
            incoming.Top = outgoing.Top = px(y + 26);
            automatic.Top = px(y + 75);
            autoHint.Top = px(y + 103);
            startReading.Top = px(y + 149);
            error.Top = px(y + 184);
            finish.Top = cancel.Top = px(y + 241);
            content.AutoScrollMinSize = new Size(0, px(y + 299));
            ClientSize = new Size(ClientSize.Width, Math.Min(px(y + 299), Screen.FromControl(this).WorkingArea.Height - px(100)));
        }

        protected override void OnHandleCreated(EventArgs e) {
            base.OnHandleCreated(e);
            int dark = 1;
            DwmSetWindowAttribute(Handle, 20, ref dark, 4);
            int caption = ColorTranslator.ToWin32(Palette.Bg);
            DwmSetWindowAttribute(Handle, 35, ref caption, 4);
            int text = ColorTranslator.ToWin32(Palette.Ink);
            DwmSetWindowAttribute(Handle, 36, ref text, 4);
        }
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);

        void SaveChoices() {
            try {
                var choices = new AppPreferences {
                    Endpoint = provider.SelectedIndex == 0 ? ChatRules.LocalTranslator : provider.SelectedIndex == 1 ? Translator.DeepLFree : provider.SelectedIndex == 2 ? Translator.DeepLPro : server.Input.Text.Trim(),
                    ApiKey = provider.SelectedIndex == 0 ? "" : key.Input.Text.Trim(),
                    IncomingLanguage = incoming.SelectedIndex >= 0 ? ChatRules.Languages[incoming.SelectedIndex] : "",
                    OutgoingLanguage = outgoing.SelectedIndex == 0 ? "channel" : outgoing.SelectedIndex > 0 ? ChatRules.Languages[outgoing.SelectedIndex - 1] : "",
                    AutoTranslateIncoming = automatic.Checked, StartReading = startReading.Checked
                };
                if (provider.SelectedIndex == 3 && (choices.Endpoint == ChatRules.LocalTranslator || Translator.IsDeepL(choices.Endpoint)))
                    throw new InvalidOperationException("Enter a LibreTranslate server URL.");
                choices.Validate();
                if (persist) PreferencesStore.Save(choices);
                Result = choices;
                DialogResult = DialogResult.OK;
                Close();
            } catch (Exception ex) when (ex is InvalidOperationException || ex is System.IO.IOException || ex is UnauthorizedAccessException || ex is System.Security.Cryptography.CryptographicException) {
                error.Text = ex is InvalidOperationException ? ex.Message : "Could not save these choices. Check access to your local app data folder.";
            }
        }
    }
}
