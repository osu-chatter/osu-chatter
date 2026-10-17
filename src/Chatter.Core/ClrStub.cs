using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Chatter {
    public sealed class StubRequest {
        public string ClrModule = "clr.dll";
        public string RuntimeModule = "mscoree.dll";
        public string CreateExport = "CLRCreateInstance";
        public string RuntimeVersion = "v4.0.30319";
        public string AssemblyPath;
        public string TypeName;
        public string MethodName;
        public string Argument;
        public int GetModuleHandleW;
        public int GetProcAddress;
    }

    /// <summary>
    /// x86 stub that runs inside osu! and calls the CLR already loaded there.
    /// Vtable slots: GetRuntime 3, IsLoaded 5, GetInterface 9,
    /// ExecuteInDefaultAppDomain 11, Release 2.
    /// </summary>
    public static class ClrStub {
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int Entry(IntPtr args);

        public static readonly byte[] Code = Build();

        public static byte[] BuildData(uint baseAddress, StubRequest request) {
            if (request == null) throw new ArgumentNullException("request");
            var blobs = new List<byte[]>();
            int cursor = 60;
            var pointers = new uint[15];
            pointers[0] = unchecked((uint)request.GetModuleHandleW);
            pointers[1] = unchecked((uint)request.GetProcAddress);
            pointers[2] = Add(ref cursor, blobs, Wide(request.ClrModule));
            pointers[3] = Add(ref cursor, blobs, Wide(request.RuntimeModule));
            pointers[4] = Add(ref cursor, blobs, Encoding.ASCII.GetBytes(request.CreateExport + "\0"));
            pointers[5] = Add(ref cursor, blobs, Wide(request.RuntimeVersion));
            pointers[6] = Add(ref cursor, blobs, new Guid("BD39D1D2-BA2F-486a-89B0-B4B0CB466891").ToByteArray());
            pointers[7] = Add(ref cursor, blobs, new Guid("9280188d-0e8e-4867-b30c-7fa83884e8de").ToByteArray());
            pointers[8] = Add(ref cursor, blobs, new Guid("D332DB9E-B9B3-4125-8207-A14884F53216").ToByteArray());
            pointers[9] = Add(ref cursor, blobs, new Guid("90F1A06E-7712-4762-86B5-7A5EBA6BDB02").ToByteArray());
            pointers[10] = Add(ref cursor, blobs, new Guid("90F1A06C-7712-4762-86B5-7A5EBA6BDB02").ToByteArray());
            pointers[11] = Add(ref cursor, blobs, Wide(request.AssemblyPath));
            pointers[12] = Add(ref cursor, blobs, Wide(request.TypeName));
            pointers[13] = Add(ref cursor, blobs, Wide(request.MethodName));
            pointers[14] = Add(ref cursor, blobs, Wide(request.Argument));
            for (int i = 2; i < pointers.Length; i++) pointers[i] += baseAddress;

            var data = new byte[cursor];
            for (int i = 0; i < pointers.Length; i++) Array.Copy(BitConverter.GetBytes(pointers[i]), 0, data, i * 4, 4);
            int write = 60;
            for (int i = 0; i < blobs.Count; i++) {
                if (NeedsAlign(blobs[i]) && (write & 1) != 0) write++;
                Array.Copy(blobs[i], 0, data, write, blobs[i].Length);
                write += blobs[i].Length;
            }
            if (write != cursor) throw new InvalidOperationException("Cannot allocate bridge arguments");
            return data;
        }

        static bool NeedsAlign(byte[] blob) {
            return blob.Length != Encoding.ASCII.GetByteCount("CLRCreateInstance") + 1;
        }

        static uint Add(ref int cursor, List<byte[]> blobs, byte[] blob) {
            if (NeedsAlign(blob) && (cursor & 1) != 0) cursor++;
            uint relative = (uint)cursor;
            blobs.Add(blob);
            cursor += blob.Length;
            return relative;
        }

        static byte[] Wide(string text) {
            if (text == null) text = "";
            return Encoding.Unicode.GetBytes(text + "\0");
        }

        public static int RunLocal(StubRequest request) {
            IntPtr data = NativeMethods.VirtualAlloc(IntPtr.Zero, 8192, NativeMethods.MemCommit | NativeMethods.MemReserve, NativeMethods.PageReadWrite);
            IntPtr code = NativeMethods.VirtualAlloc(IntPtr.Zero, (uint)Code.Length, NativeMethods.MemCommit | NativeMethods.MemReserve, NativeMethods.PageReadWrite);
            if (data == IntPtr.Zero || code == IntPtr.Zero) throw new InvalidOperationException("Cannot allocate bridge arguments");
            try {
                byte[] image = BuildData(unchecked((uint)data.ToInt32()), request);
                Marshal.Copy(image, 0, data, image.Length);
                Marshal.Copy(Code, 0, code, Code.Length);
                uint old;
                if (!NativeMethods.VirtualProtect(code, (uint)Code.Length, NativeMethods.PageExecuteRead, out old))
                    throw new InvalidOperationException("Cannot start same-session bridge");
                var entry = (Entry)Marshal.GetDelegateForFunctionPointer(code, typeof(Entry));
                return entry(data);
            } finally {
                NativeMethods.VirtualFree(code, 0, NativeMethods.MemRelease);
                NativeMethods.VirtualFree(data, 0, NativeMethods.MemRelease);
            }
        }

        static byte[] Build() {
            var asm = new Asm();
            asm.Emit(0x55);                   // push ebp
            asm.Emit(0x8B, 0xEC);             // mov ebp, esp
            asm.Emit(0x83, 0xEC, 0x28);       // sub esp, 0x28
            asm.Emit(0x53);                   // push ebx
            asm.Emit(0x56);                   // push esi
            asm.Emit(0x57);                   // push edi
            asm.Emit(0x8B, 0x5D, 0x08);       // mov ebx, [ebp+8]
            asm.Emit(0x33, 0xC0);             // xor eax, eax
            asm.Emit(0x89, 0x45, 0xFC);       // mov [ebp-4], eax
            asm.Emit(0x89, 0x45, 0xF8);       // mov [ebp-8], eax
            asm.Emit(0x89, 0x45, 0xF4);       // mov [ebp-12], eax
            asm.Emit(0x89, 0x45, 0xF0);       // mov [ebp-16], eax
            asm.Emit(0x89, 0x45, 0xEC);       // mov [ebp-20], eax
            asm.Dword(0xC7, 0x45, 0xE8, 1);   // mov dword [ebp-24], 1

            asm.Emit(0xFF, 0x73, 0x08);       // push dword [ebx+8]
            asm.Emit(0xFF, 0x53, 0x00);       // call dword [ebx]
            asm.Emit(0x85, 0xC0);             // test eax, eax
            asm.Jz("cleanup");

            asm.Dword(0xC7, 0x45, 0xE8, 4);
            asm.Emit(0xFF, 0x73, 0x0C);
            asm.Emit(0xFF, 0x53, 0x00);
            asm.Emit(0x85, 0xC0);
            asm.Jz("cleanup");
            asm.Emit(0x8B, 0xF0);             // mov esi, eax

            asm.Dword(0xC7, 0x45, 0xE8, 5);
            asm.Emit(0xFF, 0x73, 0x10);
            asm.Emit(0x56);
            asm.Emit(0xFF, 0x53, 0x04);
            asm.Emit(0x85, 0xC0);
            asm.Jz("cleanup");
            asm.Emit(0x8B, 0xF8);             // mov edi, eax

            asm.Dword(0xC7, 0x45, 0xE8, 7);
            asm.Emit(0x8D, 0x45, 0xFC);       // lea eax, [ebp-4]
            asm.Emit(0x50);
            asm.Emit(0xFF, 0x73, 0x20);
            asm.Emit(0xFF, 0x73, 0x1C);
            asm.Emit(0xFF, 0xD7);             // call edi
            asm.Emit(0x85, 0xC0);
            asm.Js("cleanup");
            asm.Emit(0x8B, 0x45, 0xFC);       // mov eax, [ebp-4]
            asm.Emit(0x85, 0xC0);
            asm.Jz("cleanup");

            asm.Emit(0x8B, 0x08);             // mov ecx, [eax]
            asm.Emit(0x8B, 0x49, 12);         // mov ecx, [ecx+12]
            asm.Emit(0x8D, 0x55, 0xF8);       // lea edx, [ebp-8]
            asm.Emit(0x52);
            asm.Emit(0xFF, 0x73, 0x18);
            asm.Emit(0xFF, 0x73, 0x14);
            asm.Emit(0x50);
            asm.Emit(0xFF, 0xD1);             // call ecx
            asm.Emit(0x85, 0xC0);
            asm.Js("cleanup");
            asm.Emit(0x8B, 0x45, 0xF8);
            asm.Emit(0x85, 0xC0);
            asm.Jz("cleanup");

            asm.Emit(0x8B, 0x08);
            asm.Emit(0x8B, 0x49, 20);         // IsLoaded
            asm.Emit(0x8D, 0x55, 0xF0);       // lea edx, [ebp-16]
            asm.Emit(0x52);
            asm.Emit(0x6A, 0xFF);             // push -1
            asm.Emit(0x50);
            asm.Emit(0xFF, 0xD1);
            asm.Emit(0x85, 0xC0);
            asm.Js("cleanup");
            asm.Emit(0x83, 0x7D, 0xF0, 0x00); // cmp dword [ebp-16], 0
            asm.Jz("cleanup");

            asm.Emit(0x8B, 0x45, 0xF8);       // mov eax, [ebp-8]
            asm.Emit(0x8B, 0x08);
            asm.Emit(0x8B, 0x49, 36);         // GetInterface
            asm.Emit(0x8D, 0x55, 0xF4);       // lea edx, [ebp-12]
            asm.Emit(0x52);
            asm.Emit(0xFF, 0x73, 0x28);
            asm.Emit(0xFF, 0x73, 0x24);
            asm.Emit(0x50);
            asm.Emit(0xFF, 0xD1);
            asm.Emit(0x85, 0xC0);
            asm.Js("cleanup");
            asm.Emit(0x8B, 0x45, 0xF4);
            asm.Emit(0x85, 0xC0);
            asm.Jz("cleanup");

            asm.Emit(0x8B, 0x08);
            asm.Emit(0x8B, 0x49, 44);         // ExecuteInDefaultAppDomain
            asm.Emit(0x8D, 0x55, 0xEC);       // lea edx, [ebp-20]
            asm.Emit(0x52);
            asm.Emit(0xFF, 0x73, 0x38);
            asm.Emit(0xFF, 0x73, 0x34);
            asm.Emit(0xFF, 0x73, 0x30);
            asm.Emit(0xFF, 0x73, 0x2C);
            asm.Emit(0x50);
            asm.Emit(0xFF, 0xD1);
            asm.Emit(0x85, 0xC0);
            asm.Js("cleanup");
            asm.Emit(0x8B, 0x45, 0xEC);       // mov eax, [ebp-20]
            asm.Emit(0x89, 0x45, 0xE8);       // mov [ebp-24], eax

            asm.Label("cleanup");
            Release(asm, 0xF4);              // host [ebp-12]
            asm.Label("skip_host");
            Release(asm, 0xF8);              // runtime [ebp-8]
            asm.Label("skip_runtime");
            Release(asm, 0xFC);              // meta [ebp-4]
            asm.Label("skip_meta");
            asm.Emit(0x8B, 0x45, 0xE8);       // mov eax, [ebp-24]
            asm.Emit(0x5F);                   // pop edi
            asm.Emit(0x5E);                   // pop esi
            asm.Emit(0x5B);                   // pop ebx
            asm.Emit(0x8B, 0xE5);             // mov esp, ebp
            asm.Emit(0x5D);                   // pop ebp
            asm.Emit(0xC2, 0x04, 0x00);       // ret 4
            return asm.Finish();
        }

        static void Release(Asm asm, byte disp) {
            string skip = disp == 0xF4 ? "skip_host" : disp == 0xF8 ? "skip_runtime" : "skip_meta";
            asm.Emit(0x8B, 0x45, disp);       // mov eax, [ebp+disp]
            asm.Emit(0x85, 0xC0);
            asm.Jz(skip);
            asm.Emit(0x8B, 0x08);             // mov ecx, [eax]
            asm.Emit(0x50);                   // push eax
            asm.Emit(0xFF, 0x51, 0x08);       // call dword [ecx+8]
        }
    }

    sealed class Asm {
        readonly List<byte> code = new List<byte>();
        readonly Dictionary<string, int> labels = new Dictionary<string, int>();
        readonly List<int> sites = new List<int>();
        readonly List<string> targets = new List<string>();

        public void Emit(params byte[] bytes) { code.AddRange(bytes); }

        public void Dword(byte op, byte modrm, byte disp, int value) {
            code.Add(op);
            code.Add(modrm);
            code.Add(disp);
            code.AddRange(BitConverter.GetBytes(value));
        }

        public void Label(string name) { labels[name] = code.Count; }

        public void Jz(string label) { code.Add(0x0F); code.Add(0x84); Site(label); }
        public void Js(string label) { code.Add(0x0F); code.Add(0x88); Site(label); }

        void Site(string label) {
            sites.Add(code.Count);
            targets.Add(label);
            code.AddRange(new byte[4]);
        }

        public byte[] Finish() {
            byte[] raw = code.ToArray();
            for (int i = 0; i < sites.Count; i++) {
                int at = sites[i];
                int target;
                if (!labels.TryGetValue(targets[i], out target)) throw new InvalidOperationException("Missing stub label " + targets[i]);
                int relative = target - (at + 4);
                Array.Copy(BitConverter.GetBytes(relative), 0, raw, at, 4);
            }
            return raw;
        }
    }
}
