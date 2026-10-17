using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Chatter {
    public sealed class SendPlan {
        public SendPlan(PlayerIdentity identity, string target) {
            Identity = identity;
            Target = target;
        }

        public PlayerIdentity Identity { get; }
        public string Target { get; }
    }

    public sealed class BridgeClient {
        readonly object gate = new object();
        readonly string bridgePath;
        Connection cached;

        public BridgeClient(string bridgePath) {
            if (string.IsNullOrEmpty(bridgePath)) throw new InvalidOperationException("Cannot prepare chat bridge");
            this.bridgePath = bridgePath;
        }

        public void Request(SendPlan plan, string text) {
            if (plan == null) throw new InvalidOperationException("Open this conversation in osu! first");
            if (text != null) ChatRules.ValidateText(text);
            lock (gate) {
                PipeLink link = null;
                if (cached != null && cached.Pid == plan.Identity.Pid) link = PipeLink.TryConnect(cached.Pipe, cached.Pid);
                if (link == null) {
                    cached = Attach(plan.Identity.Pid);
                    var until = DateTime.UtcNow.AddSeconds(3);
                    while (link == null && DateTime.UtcNow < until) {
                        link = PipeLink.TryConnect(cached.Pipe, cached.Pid);
                        if (link == null) System.Threading.Thread.Sleep(20);
                    }
                }
                if (link == null) throw new InvalidOperationException("Chat bridge did not connect; nothing was sent");
                using (link) {
                    byte[] nonce = new byte[8];
                    using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(nonce);
                    byte[] targetBytes = Encoding.UTF8.GetBytes(plan.Target);
                    byte[] textBytes = Encoding.UTF8.GetBytes(text ?? "");
                    using (var payload = new MemoryStream()) {
                        var writer = new BinaryWriter(payload);
                        writer.Write(0x52424331);
                        writer.Write(cached.Key);
                        writer.Write((byte)(text != null ? 1 : 0));
                        writer.Write(plan.Identity.Account);
                        writer.Write(targetBytes.Length);
                        writer.Write(targetBytes);
                        writer.Write(textBytes.Length);
                        writer.Write(textBytes);
                        writer.Write(nonce);
                        writer.Flush();
                        try { link.WriteAll(payload.ToArray(), 2300); }
                        catch (InvalidOperationException) { throw new InvalidOperationException("Chat request failed; nothing was committed"); }
                    }
                    byte status = link.ReadExact(1, 2300)[0];
                    if (text == null && status == 3) return;
                    if (status != 1 || text == null) throw new InvalidOperationException("Direct chat validation failed; nothing was sent");
                    byte[] commit = new byte[16];
                    Buffer.BlockCopy(nonce, 0, commit, 0, 8);
                    Buffer.BlockCopy(BitConverter.GetBytes(NativeMethods.GetTickCount64() + 75), 0, commit, 8, 8);
                    try { link.WriteAll(commit, 200); }
                    catch (InvalidOperationException) { throw new InvalidOperationException("Chat commit failed; check the conversation"); }
                    byte result;
                    try { result = link.ReadExact(1, 200)[0]; }
                    catch (InvalidOperationException) { throw new InvalidOperationException("osu! rejected the direct send; check the conversation before retrying"); }
                    if (result != 2) throw new InvalidOperationException("osu! rejected the direct send; check the conversation before retrying");
                }
            }
        }

        Connection Attach(int pid) {
            string managed = PrepareLibrary();
            byte[] key = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(key);
            string pipe = "chatter-" + pid.ToString() + "-" + Hex(key, 0, 16);
            IntPtr process = NativeMethods.OpenProcess(
                NativeMethods.ProcessQueryInformation | NativeMethods.ProcessVmRead | NativeMethods.ProcessVmWrite
                | NativeMethods.ProcessVmOperation | NativeMethods.ProcessCreateThread, false, pid);
            if (process == IntPtr.Zero) {
                int error = Marshal.GetLastWin32Error();
                if (error == 5) throw new InvalidOperationException("Cannot open same-session chat bridge. Run chatter with the same administrator rights as osu!.");
                throw new InvalidOperationException("Cannot open same-session chat bridge");
            }
            IntPtr codeRemote = IntPtr.Zero;
            IntPtr dataRemote = IntPtr.Zero;
            bool running = false;
            try {
                int getModule = RemoteExport(pid, "GetModuleHandleW");
                int getProc = RemoteExport(pid, "GetProcAddress");
                var host = Process.GetCurrentProcess();
                string options = pipe + "\n" + Hex(key, 0, 32) + "\n" + host.Id.ToString() + "\n"
                    + host.StartTime.ToUniversalTime().ToFileTimeUtc().ToString();
                dataRemote = NativeMethods.VirtualAllocEx(process, IntPtr.Zero, 8192, NativeMethods.MemCommit | NativeMethods.MemReserve, NativeMethods.PageReadWrite);
                if (dataRemote == IntPtr.Zero) throw new InvalidOperationException("Cannot allocate bridge arguments");
                var request = new StubRequest {
                    AssemblyPath = managed,
                    TypeName = "Chatter.Bridge.Entry",
                    MethodName = "Start",
                    Argument = options,
                    GetModuleHandleW = getModule,
                    GetProcAddress = getProc
                };
                byte[] data = ClrStub.BuildData(unchecked((uint)dataRemote.ToInt32()), request);
                int written;
                if (!NativeMethods.WriteProcessMemory(process, dataRemote, data, data.Length, out written) || written != data.Length)
                    throw new InvalidOperationException("Cannot initialize bridge arguments");
                codeRemote = NativeMethods.VirtualAllocEx(process, IntPtr.Zero, (uint)ClrStub.Code.Length, NativeMethods.MemCommit | NativeMethods.MemReserve, NativeMethods.PageReadWrite);
                if (codeRemote == IntPtr.Zero) throw new InvalidOperationException("Cannot allocate bridge arguments");
                if (!NativeMethods.WriteProcessMemory(process, codeRemote, ClrStub.Code, ClrStub.Code.Length, out written) || written != ClrStub.Code.Length)
                    throw new InvalidOperationException("Cannot initialize bridge arguments");
                uint old;
                if (!NativeMethods.VirtualProtectEx(process, codeRemote, (uint)ClrStub.Code.Length, NativeMethods.PageExecuteRead, out old))
                    throw new InvalidOperationException("Cannot start same-session bridge");
                int threadId;
                IntPtr thread = NativeMethods.CreateRemoteThread(process, IntPtr.Zero, 0, codeRemote, dataRemote, 0, out threadId);
                if (thread == IntPtr.Zero) throw new InvalidOperationException("Cannot start same-session bridge");
                running = true;
                using (new Handle(thread)) {
                    if (NativeMethods.WaitForSingleObject(thread, 8000) != 0) {
                        running = true;
                        throw new InvalidOperationException("Chat bridge startup timed out; no message was requested");
                    }
                    running = false;
                    uint code;
                    if (!NativeMethods.GetExitCodeThread(thread, out code))
                        throw new InvalidOperationException("Cannot inspect bridge startup");
                    if (code != 0) throw new InvalidOperationException("This stable build does not support the direct chat bridge (startup " + code.ToString() + ")");
                }
            } finally {
                if (!running) {
                    if (codeRemote != IntPtr.Zero) NativeMethods.VirtualFreeEx(process, codeRemote, 0, NativeMethods.MemRelease);
                    if (dataRemote != IntPtr.Zero) NativeMethods.VirtualFreeEx(process, dataRemote, 0, NativeMethods.MemRelease);
                }
                NativeMethods.CloseHandle(process);
            }
            return new Connection { Pid = pid, Pipe = pipe, Key = key };
        }

        public string PrepareLibrary() {
            byte[] bytes;
            // Prefer the matching embedded bridge in the standalone app.
            using (var source = System.Reflection.Assembly.GetEntryAssembly()?.GetManifestResourceStream("Chatter.Bundle.Chatter.Bridge.dll")) {
                if (source != null) {
                    using (var buffer = new MemoryStream()) { source.CopyTo(buffer); bytes = buffer.ToArray(); }
                } else {
                    if (!File.Exists(bridgePath)) throw new InvalidOperationException("Cannot prepare chat bridge");
                    bytes = File.ReadAllBytes(bridgePath);
                }
            }
            string hash;
            using (var sha = SHA256.Create()) hash = Hex(sha.ComputeHash(bytes), 0, 8);
            string dir = Path.Combine(Path.GetTempPath(), "chatter-bridge", hash);
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "Chatter.Bridge.dll");
            if (!File.Exists(path)) {
                try { File.WriteAllBytes(path, bytes); }
                catch (IOException) { throw new InvalidOperationException("Cannot write chat bridge"); }
            }
            byte[] disk = File.ReadAllBytes(path);
            if (disk.Length != bytes.Length) throw new InvalidOperationException("Chat bridge file verification failed");
            for (int i = 0; i < bytes.Length; i++) if (disk[i] != bytes[i]) throw new InvalidOperationException("Chat bridge file verification failed");
            return path;
        }

        static int RemoteExport(int pid, string name) {
            IntPtr localModule = NativeMethods.GetModuleHandleA("kernel32.dll");
            IntPtr local = NativeMethods.GetProcAddress(localModule, name);
            if (local == IntPtr.Zero) throw new InvalidOperationException("Required bridge export missing");
            ProcessModule owner = null;
            foreach (ProcessModule module in Process.GetCurrentProcess().Modules) {
                long start = module.BaseAddress.ToInt64();
                long end = start + module.ModuleMemorySize;
                long at = local.ToInt64();
                if (at >= start && at < end) { owner = module; break; }
            }
            if (owner == null) throw new InvalidOperationException("Bridge module missing");
            uint remote = ModuleBase(pid, owner.ModuleName);
            int delta = local.ToInt32() - owner.BaseAddress.ToInt32();
            return unchecked((int)(remote + (uint)delta));
        }

        static uint ModuleBase(int pid, string name) {
            IntPtr snap = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.SnapModule | NativeMethods.SnapModule32, pid);
            if (snap == IntPtr.Zero || snap == new IntPtr(-1)) throw new InvalidOperationException("Cannot locate stable modules");
            try {
                var entry = new NativeMethods.ModuleEntry { Size = (uint)Marshal.SizeOf(typeof(NativeMethods.ModuleEntry)) };
                if (!NativeMethods.Module32FirstW(snap, ref entry)) throw new InvalidOperationException("Bridge module missing");
                do {
                    if (string.Equals(entry.ModuleName, name, StringComparison.OrdinalIgnoreCase))
                        return unchecked((uint)entry.BaseAddress.ToInt32());
                } while (NativeMethods.Module32NextW(snap, ref entry));
            } finally { NativeMethods.CloseHandle(snap); }
            throw new InvalidOperationException("Bridge module missing");
        }

        static string Hex(byte[] bytes, int offset, int count) {
            var builder = new StringBuilder(count * 2);
            for (int i = 0; i < count; i++) builder.Append(bytes[offset + i].ToString("x2"));
            return builder.ToString();
        }

        sealed class Connection {
            public int Pid;
            public string Pipe;
            public byte[] Key;
        }

        sealed class Handle : IDisposable {
            readonly IntPtr value;
            public Handle(IntPtr value) { this.value = value; }
            public void Dispose() { if (value != IntPtr.Zero) NativeMethods.CloseHandle(value); }
        }

        sealed class PipeLink : IDisposable {
            readonly IntPtr handle;
            PipeLink(IntPtr handle) { this.handle = handle; }

            public static PipeLink TryConnect(string name, int expectPid) {
                IntPtr handle = NativeMethods.CreateFileW(@"\\.\pipe\" + name, NativeMethods.GenericRead | NativeMethods.GenericWrite,
                    0, IntPtr.Zero, NativeMethods.OpenExisting, NativeMethods.SecuritySqosPresent | NativeMethods.SecurityIdentification, IntPtr.Zero);
                if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return null;
                int pid;
                if (!NativeMethods.GetNamedPipeServerProcessId(handle, out pid) || pid != expectPid) {
                    NativeMethods.CloseHandle(handle);
                    throw new InvalidOperationException(pid == 0 ? "Cannot verify chat bridge" : "Chat bridge process changed");
                }
                uint mode = NativeMethods.PipeNowait;
                if (!NativeMethods.SetNamedPipeHandleState(handle, ref mode, IntPtr.Zero, IntPtr.Zero)) {
                    NativeMethods.CloseHandle(handle);
                    throw new InvalidOperationException("Cannot bound chat bridge IO");
                }
                return new PipeLink(handle);
            }

            public void WriteAll(byte[] bytes, int timeoutMs) {
                int offset = 0;
                var start = DateTime.UtcNow;
                while (offset < bytes.Length) {
                    int written;
                    bool ok = NativeMethods.WriteFile(handle, Slice(bytes, offset), bytes.Length - offset, out written, IntPtr.Zero);
                    if (ok && written > 0) { offset += written; continue; }
                    int error = Marshal.GetLastWin32Error();
                    if ((DateTime.UtcNow - start).TotalMilliseconds >= timeoutMs)
                        throw new InvalidOperationException("Chat bridge timed out; check the conversation before retrying");
                    if (ok || error == 232) { System.Threading.Thread.Sleep(1); continue; }
                    throw new InvalidOperationException("Chat bridge disconnected; check the conversation before retrying");
                }
            }

            public byte[] ReadExact(int count, int timeoutMs) {
                var bytes = new byte[count];
                int offset = 0;
                var start = DateTime.UtcNow;
                while (offset < count) {
                    var chunk = new byte[count - offset];
                    int read;
                    bool ok = NativeMethods.ReadFile(handle, chunk, chunk.Length, out read, IntPtr.Zero);
                    if (ok && read > 0) {
                        Buffer.BlockCopy(chunk, 0, bytes, offset, read);
                        offset += read;
                        continue;
                    }
                    int error = Marshal.GetLastWin32Error();
                    if ((DateTime.UtcNow - start).TotalMilliseconds >= timeoutMs)
                        throw new InvalidOperationException("Chat bridge timed out; check the conversation before retrying");
                    if ((ok && read == 0) || error == 232) { System.Threading.Thread.Sleep(1); continue; }
                    throw new InvalidOperationException("Chat bridge disconnected; check the conversation before retrying");
                }
                return bytes;
            }

            static byte[] Slice(byte[] bytes, int offset) {
                if (offset == 0) return bytes;
                var slice = new byte[bytes.Length - offset];
                Buffer.BlockCopy(bytes, offset, slice, 0, slice.Length);
                return slice;
            }

            public void Dispose() { if (handle != IntPtr.Zero) NativeMethods.CloseHandle(handle); }
        }
    }
}
