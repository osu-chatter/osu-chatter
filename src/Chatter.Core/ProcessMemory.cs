using System;
using System.Collections.Generic;
using Chatter.Shared;

namespace Chatter {
    public sealed class ProcessMemory : IProcessMemory, IDisposable {
        IntPtr handle;
        public int Pid { get; private set; }

        public bool Alive {
            get {
                if (handle == IntPtr.Zero) return false;
                uint code;
                if (!NativeMethods.GetExitCodeProcess(handle, out code)) return false;
                return code == NativeMethods.StillActive;
            }
        }

        public static int FindOsu() {
            IntPtr snap = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.SnapProcess, 0);
            if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return 0;
            try {
                var entry = new NativeMethods.ProcessEntry { Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.ProcessEntry)) };
                if (!NativeMethods.Process32FirstW(snap, ref entry)) return 0;
                do {
                    if (string.Equals(entry.ExeFile, StableCompatibilityProfile.ProcessName, StringComparison.OrdinalIgnoreCase)) return (int)entry.ProcessId;
                } while (NativeMethods.Process32NextW(snap, ref entry));
                return 0;
            } finally {
                NativeMethods.CloseHandle(snap);
            }
        }

        public void Open(int pid, uint access) {
            Close();
            IntPtr opened = NativeMethods.OpenProcess(access, false, pid);
            if (opened == IntPtr.Zero) {
                int error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                if (error == 5) throw new InvalidOperationException("Cannot read osu!stable. Run chatter with the same administrator rights as osu!.");
                throw new InvalidOperationException("Cannot read osu!stable");
            }
            handle = opened;
            Pid = pid;
        }

        public void Close() {
            if (handle != IntPtr.Zero) {
                NativeMethods.CloseHandle(handle);
                handle = IntPtr.Zero;
            }
            Pid = 0;
        }

        public byte[] ReadBytes(uint address, int size) {
            if (handle == IntPtr.Zero || address == 0 || size <= 0 || size > 8 * 1024 * 1024) return null;
            var buffer = new byte[size];
            int read;
            bool ok = NativeMethods.ReadProcessMemory(handle, new IntPtr(unchecked((int)address)), buffer, size, out read);
            if (!ok || read != size) return null;
            return buffer;
        }

        public void VisitExecutable(Action<byte[]> visit) {
            if (handle == IntPtr.Zero) return;
            uint address = 0;
            while (true) {
                NativeMethods.MemoryBasicInformation info;
                int got = NativeMethods.VirtualQueryEx(handle, new IntPtr(unchecked((int)address)), out info, System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.MemoryBasicInformation)));
                if (got == 0) break;
                uint regionBase = unchecked((uint)info.BaseAddress.ToInt32());
                uint region = unchecked((uint)info.RegionSize.ToInt32());
                if (region == 0) break;
                uint next = regionBase + region;
                if (next <= regionBase) break;
                address = next;
                if (info.State != NativeMethods.MemCommit) continue;
                if (info.Protect != NativeMethods.PageExecuteRead && info.Protect != NativeMethods.PageExecuteReadWrite) continue;
                if (region > 32 * 1024 * 1024) continue;
                uint off = 0;
                while (off < region) {
                    uint start = off > 128 ? off - 128 : 0;
                    int count = (int)Math.Min(65536u + 128u, region - start);
                    byte[] buf = ReadBytes(regionBase + start, count);
                    if (buf != null) visit(buf);
                    off += 65536;
                }
            }
        }

        public Dictionary<string, uint> Scan(Pattern[] patterns) {
            var found = new Dictionary<string, uint>();
            if (handle == IntPtr.Zero || patterns == null || patterns.Length == 0) return found;
            uint address = 0;
            while (found.Count < patterns.Length) {
                NativeMethods.MemoryBasicInformation info;
                int got = NativeMethods.VirtualQueryEx(handle, new IntPtr(unchecked((int)address)), out info, System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.MemoryBasicInformation)));
                if (got == 0) break;
                uint regionBase = unchecked((uint)info.BaseAddress.ToInt32());
                uint region = unchecked((uint)info.RegionSize.ToInt32());
                if (region == 0) break;
                uint next = regionBase + region;
                if (next <= regionBase) break;
                address = next;
                if (info.State != NativeMethods.MemCommit) continue;
                if (!Readable(info.Protect)) continue;
                if (region > 100 * 1024 * 1024) continue;
                bool exec = info.Protect == NativeMethods.PageExecuteRead || info.Protect == NativeMethods.PageExecuteReadWrite;
                if (info.Type != NativeMethods.MemImage && !exec && region > 1024 * 1024) continue;
                int longest = 0;
                for (int p = 0; p < patterns.Length; p++) if (patterns[p].Bytes.Length > longest) longest = patterns[p].Bytes.Length;
                uint off = 0;
                while (off < region && found.Count < patterns.Length) {
                    int count = (int)Math.Min((uint)longest + 65536u, region - off);
                    byte[] buf = ReadBytes(regionBase + off, count);
                    if (buf != null) {
                        for (int p = 0; p < patterns.Length; p++) {
                            if (found.ContainsKey(patterns[p].Name)) continue;
                            int at = IndexOf(buf, patterns[p]);
                            if (at < 0) continue;
                            long abs = (long)regionBase + off + at + patterns[p].Offset;
                            if (abs > 0 && abs < uint.MaxValue) found[patterns[p].Name] = (uint)abs;
                        }
                    }
                    off += 65536;
                }
            }
            return found;
        }

        static bool Readable(uint protect) {
            return protect == NativeMethods.PageExecuteRead
                || protect == NativeMethods.PageExecuteReadWrite
                || protect == NativeMethods.PageReadWrite
                || protect == NativeMethods.PageReadonly;
        }

        static int IndexOf(byte[] buffer, Pattern pattern) {
            int first = -1;
            byte firstByte = 0;
            for (int i = 0; i < pattern.Mask.Length; i++) if (pattern.Mask[i] == 1) { first = i; firstByte = pattern.Bytes[i]; break; }
            if (first < 0) return -1;
            int search = 0;
            while (search + pattern.Bytes.Length <= buffer.Length) {
                int rel = Array.IndexOf(buffer, firstByte, search);
                if (rel < 0) return -1;
                int start = rel - first;
                if (start < 0) { search = rel + 1; continue; }
                if (start + pattern.Bytes.Length > buffer.Length) return -1;
                if (pattern.Matches(buffer, start)) return start;
                search = rel + 1;
            }
            return -1;
        }

        public void Dispose() { Close(); }
    }

    public sealed class Pattern {
        public Pattern(string name, string text, int offset) {
            Name = name;
            Offset = offset;
            var bytes = new List<byte>();
            var mask = new List<byte>();
            string[] parts = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++) {
                if (parts[i] == "??") { bytes.Add(0); mask.Add(0); }
                else { bytes.Add(Convert.ToByte(parts[i], 16)); mask.Add(1); }
            }
            Bytes = bytes.ToArray();
            Mask = mask.ToArray();
        }

        public string Name { get; }
        public byte[] Bytes { get; }
        public byte[] Mask { get; }
        public int Offset { get; }

        internal bool Matches(byte[] buffer, int start) {
            if (buffer == null || start < 0 || start > buffer.Length - Bytes.Length) return false;
            for (int i = 0; i < Bytes.Length; i++) {
                if (Mask[i] == 1 && buffer[start + i] != Bytes[i]) return false;
            }
            return true;
        }
    }
}
