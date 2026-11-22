using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using Chatter.Shared;
using Stable = Chatter.Shared.StableCompatibilityProfile;
using Members = Chatter.Shared.StableCompatibilityProfile.Members;

[assembly: InternalsVisibleTo("Chatter.Tests")]

namespace Chatter.Bridge {
    internal interface IGame {
        void Validate(int account, string target);
        void Schedule(Action action);
        void Send(string text, string target);
    }

    internal sealed class StableGame : IGame, INativeChatGame, INativeCommandGame {
        const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        readonly MethodInfo send, schedule;
        readonly FieldInfo user, userId, mode, connected, ready, rooms, roomName;
        readonly object scheduler;
        readonly Type callback;
        StableNativeChat? display;
        NativeCommands? commands;

        public StableGame() {
            Assembly assembly = Assembly.GetEntryAssembly();
            if (assembly == null || !string.Equals(Path.GetFileName(assembly.Location), Stable.ProcessName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unsupported game");
            Type chat = assembly.GetType(Members.ChatType, true);
            Type game = assembly.GetType(Members.GameType, true);
            Type bancho = assembly.GetType(Members.BanchoType, true);
            send = chat.GetMethod(Members.Send, Static, null, new[] { typeof(string), typeof(string), typeof(bool) }, null);
            if (send == null || send.ReturnType != typeof(void) || send.GetMethodBody() == null)
                throw new InvalidOperationException("Unsupported sender");
            user = Required(game, Members.User, Static);
            userId = Required(user.FieldType, Members.UserId, Instance);
            mode = Required(game, Members.Mode, Static);
            connected = Required(bancho, Members.Connected, Static);
            ready = Required(bancho, Members.Ready, Static);
            rooms = Required(chat, Members.Rooms, Static);
            Type room = assembly.GetType(Members.RoomType, true);
            roomName = Required(room, Members.RoomName, Instance);
            scheduler = Required(game, Members.Scheduler, Static).GetValue(null);
            if (scheduler == null) throw new InvalidOperationException("Game not ready");
            schedule = scheduler.GetType().GetMethods(Instance).Single(method =>
                method.Name == Members.Schedule && method.GetParameters().Length == 2 && method.GetParameters()[1].ParameterType == typeof(bool));
            callback = schedule.GetParameters()[0].ParameterType;
            MethodInfo invoke = callback.GetMethod("Invoke");
            if (schedule.ReturnType != typeof(bool) || connected.FieldType != typeof(bool) || ready.FieldType != typeof(bool) || roomName.FieldType != typeof(string) || !typeof(IList).IsAssignableFrom(rooms.FieldType)) throw new InvalidOperationException("Unsupported member contracts");
            if (!typeof(Delegate).IsAssignableFrom(callback) || invoke == null || invoke.ReturnType != typeof(void) || invoke.GetParameters().Length != 0 || userId.FieldType != typeof(int))
                throw new InvalidOperationException("Unsupported scheduler");
        }

        static FieldInfo Required(Type type, string name, BindingFlags flags) {
            FieldInfo field = type.GetField(name, flags);
            if (field == null) throw new InvalidOperationException("Unsupported layout");
            return field;
        }

        public void Validate(int account, string target) {
            SharedChatRules.ValidateTarget(target);
            object own = user.GetValue(null);
            if (account <= 0 || own == null || (int)userId.GetValue(own) != account
                || !(bool)connected.GetValue(null) || !(bool)ready.GetValue(null))
                throw new InvalidOperationException("Account changed or disconnected");
            int state = Convert.ToInt32(mode.GetValue(null));
            if (!Stable.MenuOrLobby(state))
                throw new InvalidOperationException("Sending is available outside gameplay");
            IList? list = rooms.GetValue(null) as IList;
            if (list == null || list.Count > 512 || !list.Cast<object>().Any(room => (string)roomName.GetValue(room) == target))
                throw new InvalidOperationException("Conversation closed");
        }

        public void Schedule(Action action) {
            Delegate call = Delegate.CreateDelegate(callback, action.Target, action.Method);
            schedule.Invoke(scheduler, new object[] { call, Members.ScheduleFlag });
        }

        public void Send(string text, string target) {
            send.Invoke(null, new object[] { text, target, Members.SendFlag });
        }

        public int Display(int account, string target, System.Collections.Generic.IDictionary<string, string> translations) {
            try {
                Validate(account, target);
                if (display == null) display = new StableNativeChat(Assembly.GetEntryAssembly());
                return display.Sync(account, target, translations);
            } catch { RestoreDisplay(); throw; }
        }

        public void RestoreDisplay() { display?.Restore(); }
        int CurrentAccount() { object own = user.GetValue(null); return own == null ? 0 : (int)userId.GetValue(own); }
        public byte[] PollCommands(int account, bool take, string completed, bool success) {
            if (commands == null) commands = new NativeCommands(this, send, CurrentAccount, RestoreDraft);
            return commands.Poll(account, take, completed, success);
        }
        public void StopCommands() { commands?.Dispose(); commands = null; }
        void RestoreDraft(CapturedTranslationCommand command) {
            try {
                if (CurrentAccount() != command.Account) return;
                var assembly = Assembly.GetEntryAssembly();
                object room = assembly.GetType(Members.ChatType, true).GetField(Stable.NativeChat.SelectedRoom, Static).GetValue(null);
                if (room == null || (string)roomName.GetValue(room) != command.Target) return;
                object editor = assembly.GetType(Stable.NativeCommands.InputType, true).GetField(Stable.NativeCommands.InputField, Static).GetValue(null);
                var text = editor.GetType().GetProperty("Text", Instance);
                if (text != null && (string)text.GetValue(editor, null) == "") text.SetValue(editor, command.Original, null);
            } catch { } // Never overwrite a different/new draft or break osu!'s input loop.
        }

    }

    public static class Entry {
        static readonly object sync = new object();
        static Session? active;

        public static int Start(string options) {
            try {
                string[] args = options.Split('\n');
                if (args.Length != 4 || !args[0].StartsWith("chatter-", StringComparison.Ordinal) || args[0].Length > 100
                    || args[0].Any(c => !(char.IsLetterOrDigit(c) || c == '-')))
                    return 10;
                byte[] key = Hex(args[1]);
                int pid = int.Parse(args[2]);
                long started = long.Parse(args[3]);
                if (key.Length != 32 || pid <= 0) return 10;
                IGame game = new StableGame();
                lock (sync) {
                    if (active != null) active.Stop();
                    active = new Session(args[0], key, pid, started, game);
                    active.Start();
                }
                return 0;
            } catch (TypeLoadException) { return 12; } catch (MissingMemberException) { return 12; } catch (InvalidOperationException) { return 13; } catch { return 11; }
        }

        static byte[] Hex(string text) {
            if (text.Length != 64) throw new FormatException();
            return Enumerable.Range(0, 32).Select(i => Convert.ToByte(text.Substring(i * 2, 2), 16)).ToArray();
        }
    }

    internal sealed class Session {
        readonly string name;
        readonly byte[] key;
        readonly int hostPid;
        readonly long hostStarted;
        readonly IGame game;
        readonly NativeDisplayLease displayLease;
        volatile bool stopped;
        NamedPipeServerStream? waiting;

        internal Session(string name, byte[] key, int hostPid, long hostStarted, IGame game) {
            this.name = name;
            this.key = key;
            this.hostPid = hostPid;
            this.hostStarted = hostStarted;
            this.game = game;
            displayLease = new NativeDisplayLease(game);
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetNamedPipeClientProcessId(IntPtr pipe, out uint pid);

        [DllImport("kernel32.dll")]
        internal static extern ulong GetTickCount64();

        internal void Start() { new Thread(Run) { IsBackground = true, Name = "chatter bridge" }.Start(); }

        internal void Stop() {
            stopped = true;
            displayLease.Dispose();
            try { game.Schedule(() => (game as INativeCommandGame)?.StopCommands()); } catch { }
            NamedPipeServerStream? pipe = waiting;
            if (pipe != null) try { pipe.Dispose(); } catch { }
        }

        bool HostAlive() {
            try {
                using (Process process = Process.GetProcessById(hostPid))
                    return !process.HasExited && process.StartTime.ToUniversalTime().ToFileTimeUtc() == hostStarted;
            } catch { return false; }
        }

        void Run() {
            try {
                while (!stopped && HostAlive()) {
                    var acl = new PipeSecurity();
                    acl.SetAccessRuleProtection(true, false);
                    acl.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
                    using (var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 8192, 8192, acl)) {
                        waiting = pipe;
                        IAsyncResult accept = pipe.BeginWaitForConnection(null, null);
                        if (!accept.AsyncWaitHandle.WaitOne(60000) || stopped) break;
                        pipe.EndWaitForConnection(accept);
                        uint pid;
                        if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out pid) || pid != (uint)hostPid || !HostAlive()) continue;
                        Serve(pipe);
                    }
                }
            } catch { } finally {
                displayLease.Dispose();
                try { game.Schedule(() => (game as INativeCommandGame)?.StopCommands()); } catch { }
                waiting = null;
                Array.Clear(key, 0, key.Length);
            }
        }

        internal void Serve(NamedPipeServerStream pipe) {
            var done = new ManualResetEvent(false);
            int cancelled = 0;
            try {
                byte[] header = Read(pipe, 45, 2000);
                if (BitConverter.ToUInt32(header, 0) != 0x52424331 || !Equal(header, 4, key)) return;
                byte operation = header[36];
                int account = BitConverter.ToInt32(header, 37);
                int targetLength = BitConverter.ToInt32(header, 41);
                if (operation > 5) return;
                string target = Utf8(Read(pipe, Bound(targetLength, (operation == 3 || operation == 5) ? 0 : 1, 640), 2000));
                int textLength = BitConverter.ToInt32(Read(pipe, 4, 2000), 0);
                byte[] payload = Read(pipe, Bound(textLength, 0, operation == 2 ? NativeChatProtocol.MaxPayload : 1600), 2000);
                string text = operation == 2 || operation == 4 ? "" : Utf8(payload);
                ulong nonce = BitConverter.ToUInt64(Read(pipe, 8, 2000), 0);
                if (operation != 3 && operation != 5) SharedChatRules.ValidateTarget(target);
                else if (account != 0 || target.Length != 0 || payload.Length != 0) return;
                var translations = operation == 2 ? NativeChatProtocol.Decode(payload) : null;
                if (operation == 1) SharedChatRules.ValidateText(text);
                else if (operation == 0 && text.Length != 0) return;
                ulong expires = GetTickCount64() + 2000;
                game.Schedule(() => {
                    try {
                        if (stopped || Interlocked.CompareExchange(ref cancelled, 0, 0) != 0 || GetTickCount64() > expires || !HostAlive()) return;
                        if (operation == 5) { (game as INativeCommandGame)?.StopCommands(); Write(pipe, new byte[] { 3 }); return; }
                        if (operation == 4) {
                            game.Validate(account, target);
                            byte[] result;
                            using (var input = new MemoryStream(payload)) using (var reader = new BinaryReader(input, new UTF8Encoding(false, true))) {
                                bool take = reader.ReadBoolean(); string completed = reader.ReadString(); bool success = reader.ReadBoolean();
                                if (completed.Length > 32 || input.Position != input.Length) throw new InvalidDataException();
                                var native = game as INativeCommandGame; if (native == null) throw new InvalidOperationException("Native commands are unavailable");
                                result = native.PollCommands(account, take, completed, success);
                            }
                            Write(pipe, new byte[] { 3 }); Write(pipe, BitConverter.GetBytes(result.Length)); Write(pipe, result); return;
                        }
                        if (operation == 3) { displayLease.Clear(); Write(pipe, new byte[] { 3 }); return; }
                        if (operation == 2) { int count = displayLease.Update(account, target, translations ?? throw new InvalidDataException()); Write(pipe, new byte[] { 3 }); Write(pipe, BitConverter.GetBytes(count)); return; }
                        game.Validate(account, target);
                        if (operation == 0) { Write(pipe, new byte[] { 3 }); return; }
                        Write(pipe, new byte[] { 1 });
                        byte[] commit = Read(pipe, 16, 100);
                        ulong deadline = BitConverter.ToUInt64(commit, 8);
                        if (BitConverter.ToUInt64(commit, 0) != nonce || GetTickCount64() > deadline || deadline > GetTickCount64() + 100 || stopped || !HostAlive()) return;
                        game.Validate(account, target);
                        if (Interlocked.CompareExchange(ref cancelled, 0, 0) != 0) return;
                        game.Send(text, target);
                        Write(pipe, new byte[] { 2 });
                    } catch (Exception ex) {
                        try {
                            Write(pipe, new byte[] { 4 });
                            if (operation == 4) {
                                string detail = ex.GetBaseException().Message;
                                if (detail.Length > 400) detail = detail.Substring(0, 400);
                                byte[] error = Encoding.UTF8.GetBytes(detail); Write(pipe, BitConverter.GetBytes(error.Length)); Write(pipe, error);
                            }
                        } catch { }
                    }
                    finally { done.Set(); }
                });
                done.WaitOne(2300);
            } catch { try { Write(pipe, new byte[] { 4 }); } catch { } }
            finally { Interlocked.Exchange(ref cancelled, 1); }
        }

        static int Bound(int value, int min, int max) {
            if (value < min || value > max) throw new InvalidDataException();
            return value;
        }

        static bool Equal(byte[] payload, int offset, byte[] expected) {
            int diff = 0;
            for (int i = 0; i < expected.Length; i++) diff |= payload[offset + i] ^ expected[i];
            return diff == 0;
        }

        static string Utf8(byte[] bytes) { return new UTF8Encoding(false, true).GetString(bytes); }

        internal static byte[] Read(Stream stream, int count, int timeout) {
            var data = new byte[count];
            int offset = 0;
            var watch = Stopwatch.StartNew();
            while (offset < count) {
                IAsyncResult read = stream.BeginRead(data, offset, count - offset, null, null);
                int remaining = timeout - (int)watch.ElapsedMilliseconds;
                if (remaining <= 0 || !read.AsyncWaitHandle.WaitOne(remaining)) throw new TimeoutException();
                int n = stream.EndRead(read);
                read.AsyncWaitHandle.Close();
                if (n == 0) throw new EndOfStreamException();
                offset += n;
            }
            return data;
        }

        static void Write(Stream stream, byte[] bytes) {
            IAsyncResult write = stream.BeginWrite(bytes, 0, bytes.Length, null, null);
            if (!write.AsyncWaitHandle.WaitOne(100)) throw new TimeoutException();
            stream.EndWrite(write);
            write.AsyncWaitHandle.Close();
        }
    }
}
