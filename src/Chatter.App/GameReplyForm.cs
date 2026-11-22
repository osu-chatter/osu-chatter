using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Chatter;

namespace Chatter.App {
    sealed class GameReplyForm : Form {
        readonly LineField draft = new LineField();
        readonly PillButton preview = new PillButton { Caption = "Preview" };
        readonly PillButton send = new PillButton { Caption = "Send", Filled = true };
        readonly TextBox result = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Palette.Elevated, ForeColor = Palette.Ink, BorderStyle = BorderStyle.None };
        readonly DarkPicker from = new DarkPicker(), to = new DarkPicker();
        readonly Label count = new Label { ForeColor = Palette.Muted, Font = UiFont.Small };
        readonly Func<TranslationRequest, Task<string>> translate;
        readonly Func<TranslationRequest, Task<SendReceipt>> submit;
        readonly Func<string, TranslationRequest> requestFor;
        bool busy, tooLong;
        public GameReplyForm(string channel, string source, string target, bool rightToLeft,
            Func<string, TranslationRequest> requestFor, Func<TranslationRequest, Task<string>> translate, Func<TranslationRequest, Task<SendReceipt>> submit) {
            this.requestFor = requestFor; this.translate = translate; this.submit = submit;
            Text = "Reply · " + channel; Font = UiFont.Body; BackColor = Palette.Bg; ForeColor = Palette.Ink;
            ShowInTaskbar = false; FormBorderStyle = FormBorderStyle.None; AutoScaleMode = AutoScaleMode.Dpi; StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(520, 310); KeyPreview = true; TopMost = true; Padding = new Padding(1);
            var heading = new Label { Text = "Reply to " + channel, Font = UiFont.BodyStrong, ForeColor = Palette.Ink, Location = new Point(20, 14), Size = new Size(430, 28) };
            var close = new PillButton { Caption = "×", Location = new Point(472, 10), Size = new Size(32, 30) }; close.Click += delegate { if (!busy) Close(); };
            heading.MouseDown += (sender, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero); } };
            from.Items.Add("Automatic"); from.Items.AddRange(ChatRules.LanguageNames); to.Items.AddRange(ChatRules.LanguageNames);
            from.SelectedIndex = Math.Max(0, Array.IndexOf(ChatRules.LanguageNames, source) + 1); to.SelectedIndex = Math.Max(0, Array.IndexOf(ChatRules.LanguageNames, target));
            from.SetBounds(20, 55, 220, 32); to.SetBounds(274, 55, 226, 32);
            var arrow = new Label { Text = "→", ForeColor = Palette.Muted, Location = new Point(245, 62), Size = new Size(24, 24) };
            draft.SetBounds(20, 104, 480, 44); draft.Input.MaxLength = 400; draft.Input.RightToLeft = rightToLeft ? RightToLeft.Yes : RightToLeft.No; draft.Cue("Type a message…");
            preview.SetBounds(20, 160, 100, 34); send.SetBounds(400, 160, 100, 34); count.SetBounds(136, 168, 248, 24);
            result.SetBounds(20, 211, 480, 78); result.Text = "Preview checks the translation. Enter sends once. Esc closes.";
            Controls.AddRange(new Control[] { heading, close, from, to, arrow, draft, preview, send, count, result });
            draft.Input.TextChanged += delegate { tooLong = false; result.Text = ""; RefreshButtons(); };
            preview.Click += async delegate { await Translate(false); }; send.Click += async delegate { await Translate(true); };
            from.SelectedIndexChanged += delegate { draft.Input.RightToLeft = from.SelectedIndex > 0 && ChatRules.RightToLeft(ChatRules.Languages[from.SelectedIndex - 1]) ? RightToLeft.Yes : RightToLeft.No; };
            from.SelectedIndexChanged += delegate { tooLong = false; RefreshButtons(); };
            to.SelectedIndexChanged += delegate { tooLong = false; RefreshButtons(); };
            draft.Input.KeyDown += async (sender, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await Translate(true); } };
            KeyDown += (sender, e) => { if (e.KeyCode == Keys.Escape && !busy) { e.SuppressKeyPress = true; Close(); } };
            RefreshButtons();
        }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); using (var pen = new Pen(Palette.Line)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1); }
        protected override void OnShown(EventArgs e) { base.OnShown(e); draft.Input.Focus(); draft.Input.SelectionStart = draft.Input.TextLength; }
        static string MessageText(string input) => input.StartsWith("/t ", StringComparison.OrdinalIgnoreCase) ? input.Substring(3).Trim() : input.Trim();
        void RefreshButtons() {
            bool can = !busy && MessageText(draft.Input.Text).Length > 0; preview.Enabled = can; send.Enabled = can && !tooLong;
            draft.Input.ReadOnly = busy; from.Enabled = to.Enabled = !busy; count.Text = draft.Input.TextLength + "/400";
        }
        async Task Translate(bool sending) {
            if (busy || (sending && tooLong)) return; string text = MessageText(draft.Input.Text); if (text.Length == 0) return;
            busy = true; RefreshButtons(); result.Text = sending ? "Translating and sending…" : "Translating…";
            try {
                ChatRules.ValidateText(text); var request = requestFor(text);
                request.Source = from.SelectedIndex == 0 ? "auto" : ChatRules.Languages[from.SelectedIndex - 1]; request.Target = ChatRules.Languages[to.SelectedIndex];
                if (sending) {
                    var receipt = await submit(request); if (IsDisposed) return;
                    draft.Input.Text = ""; result.Text = (receipt.Echoed ? "Sent: " : "Submitted: ") + receipt.Translated;
                } else {
                    string translated = await translate(request); if (IsDisposed) return;
                    tooLong = translated.Length > 400; result.Text = translated + (tooLong ? "\r\n\r\nTranslation is " + translated.Length + "/400 characters. Shorten the draft before sending." : "");
                }
            } catch (Exception ex) { if (!IsDisposed) result.Text = ex.Message; }
            finally { busy = false; if (!IsDisposed) RefreshButtons(); }
        }
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
    }
}
