using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Chatter;

namespace Chatter.App {
    sealed class GameChatOverlay : Form {
        internal const int RecentCount = 200;
        const int ReplyHotKey = 1, LockHotKey = 2, WmHotKey = 0x312, NoActivate = 0x08000000, Transparent = 0x20;
        readonly Timer watch = new Timer { Interval = 150 };
        readonly Label heading = new Label { Dock = DockStyle.Fill, Font = UiFont.BodyStrong, ForeColor = Palette.Ink, Padding = new Padding(12, 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft };
        readonly PillButton lockButton = new PillButton { Dock = DockStyle.Right, Width = 88, Caption = "Unlock" };
        readonly PillButton replyButton = new PillButton { Dock = DockStyle.Right, Width = 74, Caption = "Reply" };
        readonly TranscriptView transcript = new TranscriptView { Dock = DockStyle.Fill, ShowTranslate = false };
        ExperienceOptions options = new ExperienceOptions();
        int processId;
        bool canReply, newConversation, interactive, placing, moving;
        string hotKeySignature = "";
        IntPtr gameWindow, hotKeyWindow;
        Dictionary<string, string> translations = new Dictionary<string, string>(), errors = new Dictionary<string, string>();
        public ChatChannel? Channel { get; private set; }
        public IntPtr ReplyWindow { get; set; }
        public event Action<Rectangle>? ReplyRequested;
        public event Action<ExperienceOptions>? OptionsChanged;
        public event Action? VisibleMessagesChanged;
        internal Func<Rectangle>? PreviewBounds;
        public GameChatOverlay() {
            Text = "osu-chatter overlay"; BackColor = Palette.Sidebar; FormBorderStyle = FormBorderStyle.None; AutoScaleMode = AutoScaleMode.Dpi;
            ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual; Padding = new Padding(1);
            MinimumSize = new Size(320, 220);
            var header = new Panel { Dock = DockStyle.Top, Height = 38, BackColor = Palette.Elevated };
            header.Controls.Add(heading); header.Controls.Add(replyButton); header.Controls.Add(lockButton);
            Controls.Add(transcript); Controls.Add(header);
            header.MouseDown += DragHeader; heading.MouseDown += DragHeader;
            lockButton.Click += delegate { SetInteractive(!interactive); SaveOptions(); };
            replyButton.Click += delegate { if (canReply && TryGetGameBounds(out var bounds, out _)) ReplyRequested?.Invoke(bounds); };
            transcript.VisibleMessagesChanged += delegate { VisibleMessagesChanged?.Invoke(); };
            watch.Tick += delegate { Place(); };
        }
        public void Configure(ExperienceOptions value) {
            options = value.Copy(); Opacity = options.OverlayOpacity / 100.0; SetInteractive(options.OverlayInteractive);
            transcript.UseModern(options.ModernTranscript, options.MessageFontSize); UpdateHotKey(false); Place();
        }
        public HashSet<string> VisibleMessageIds() => transcript.VisibleMessageIds();
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams { get { var p = base.CreateParams; if (!interactive) p.ExStyle |= NoActivate | Transparent; return p; } }
        internal void SetInteractive(bool enabled) {
            interactive = enabled; options.OverlayInteractive = enabled;
            lockButton.Caption = enabled ? "Lock" : "Unlock";
            heading.Text = (Channel?.Name ?? "Chatter") + (enabled ? " · drag to move" : " · locked");
            transcript.Enabled = enabled;
            if (IsHandleCreated) {
                int style = GetWindowLong(Handle, -20);
                SetWindowLong(Handle, -20, enabled ? style & ~(NoActivate | Transparent) : style | NoActivate | Transparent);
            }
        }
        void DragHeader(object sender, MouseEventArgs e) {
            if (!interactive || e.Button != MouseButtons.Left) return;
            ReleaseCapture(); SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero);
        }
        internal void SetChat(int pid, ChatChannel? channel, bool reply, Dictionary<string, string> translated, Dictionary<string, string> failed) {
            newConversation |= processId != pid || Channel?.Name != channel?.Name;
            processId = pid; Channel = channel; canReply = reply; translations = translated; errors = failed;
            watch.Enabled = channel != null; Place(); if (Visible) RenderChat();
        }
        void Place() {
            if (moving) return;
            if (Channel == null || !TryGetGameBounds(out var bounds, out _)) { UpdateHotKey(false); Hide(); return; }
            int width = Math.Min(options.OverlayWidth, bounds.Width - 24), height = Math.Min(options.OverlayHeight, bounds.Height - 24);
            int x = options.OverlayX < 0 ? bounds.Width - width - 12 : options.OverlayX;
            int y = options.OverlayY < 0 ? 12 : options.OverlayY;
            var rectangle = new Rectangle(bounds.Left + Math.Max(0, Math.Min(x, bounds.Width - width)), bounds.Top + Math.Max(0, Math.Min(y, bounds.Height - height)), width, height);
            if (!Capture && Bounds != rectangle) { placing = true; Bounds = rectangle; placing = false; }
            if (!Visible) { RenderChat(); Show(); }
            UpdateHotKey(true); replyButton.Enabled = canReply;
        }
        void RenderChat() {
            if (Channel == null) return;
            heading.Text = Channel.Name + (interactive ? " · drag to move" : " · locked");
            if (Channel.Messages.Count == 0) transcript.SetEmpty("No messages yet", "");
            else transcript.SetMessages(Channel.Messages, translations, errors, newConversation);
            newConversation = false;
        }
        bool TryGetGameBounds(out Rectangle bounds, out IntPtr window) {
            bounds = Rectangle.Empty; window = GetForegroundWindow();
            if (PreviewBounds != null) { bounds = PreviewBounds(); return true; }
            if (processId <= 0) return false;
            GetWindowThreadProcessId(window, out uint foregroundPid);
            if (foregroundPid == processId) gameWindow = window;
            else if (window != Handle && window != ReplyWindow) return false;
            if (gameWindow == IntPtr.Zero || IsIconic(gameWindow) || !GetClientRect(gameWindow, out Rect rect)) return false;
            GetWindowThreadProcessId(gameWindow, out uint owner); if (owner != processId) return false;
            Point origin = Point.Empty; if (!ClientToScreen(gameWindow, ref origin)) return false;
            bounds = Rectangle.Intersect(new Rectangle(origin.X, origin.Y, rect.Right, rect.Bottom), Screen.FromHandle(gameWindow).WorkingArea); window = gameWindow;
            return bounds.Width >= 340 && bounds.Height >= 240;
        }
        void SaveOptions() {
            if (placing) return;
            if (TryGetGameBounds(out var bounds, out _)) {
                options.OverlayX = Math.Max(0, Left - bounds.Left); options.OverlayY = Math.Max(0, Top - bounds.Top);
                options.OverlayWidth = Math.Max(320, Math.Min(2400, Width)); options.OverlayHeight = Math.Max(220, Math.Min(1600, Height));
            }
            OptionsChanged?.Invoke(options.Copy());
        }
        void UpdateHotKey(bool enabled) {
            string signature = enabled ? options.ReplyShortcut + "|" + options.OverlayShortcut + "|" + canReply : "";
            if (signature == hotKeySignature) return; hotKeySignature = signature;
            if (hotKeyWindow != IntPtr.Zero) { UnregisterHotKey(hotKeyWindow, ReplyHotKey); UnregisterHotKey(hotKeyWindow, LockHotKey); hotKeyWindow = IntPtr.Zero; }
            if (!enabled) return;
            hotKeyWindow = Handle;
            if (canReply) Register(options.ReplyShortcut, ReplyHotKey);
            Register(options.OverlayShortcut, LockHotKey);
        }
        void Register(string text, int id) {
            try { Keys key = (Keys)new KeysConverter().ConvertFromInvariantString(text); uint modifiers = 0x4000;
                if ((key & Keys.Control) != 0) modifiers |= 2; if ((key & Keys.Shift) != 0) modifiers |= 4; if ((key & Keys.Alt) != 0) modifiers |= 1;
                RegisterHotKey(Handle, id, modifiers, (uint)(key & Keys.KeyCode));
            } catch (ArgumentException) { }
        }
        protected override void WndProc(ref Message m) {
            if (m.Msg == WmHotKey && Visible) {
                if (m.WParam.ToInt32() == LockHotKey) { SetInteractive(!interactive); SaveOptions(); return; }
                if (m.WParam.ToInt32() == ReplyHotKey && canReply && TryGetGameBounds(out var bounds, out _)) { ReplyRequested?.Invoke(bounds); return; }
            }
            if (m.Msg == 0x84 && interactive) {
                Point p = PointToClient(new Point(unchecked((short)m.LParam.ToInt64()), unchecked((short)(m.LParam.ToInt64() >> 16))));
                int hit = p.Y >= Height - 8 ? (p.X < 8 ? 16 : p.X >= Width - 8 ? 17 : 15) : p.X < 6 ? 10 : p.X >= Width - 6 ? 11 : 0;
                if (hit != 0) { m.Result = new IntPtr(hit); return; }
            }
            if (m.Msg == 0x231) moving = true;
            if (m.Msg == 0x232) { moving = false; SaveOptions(); }
            base.WndProc(ref m);
        }
        protected override void OnHandleDestroyed(EventArgs e) { UpdateHotKey(false); base.OnHandleDestroyed(e); }
        protected override void Dispose(bool disposing) { if (disposing) watch.Dispose(); base.Dispose(disposing); }
        [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr window, ref Point point);
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr window, int id);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr window, int index, int value);
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
    }
}
