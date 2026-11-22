using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Chatter;

namespace Chatter.Tests {
    // Diagnostic only: reads hook metadata, never invokes a game sender or changes an input draft.
    public static class NativeBridgeInspection {
        [DllImport("kernel32.dll")] static extern ulong GetTickCount64();
        public static int Inspect(string file) {
            try {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
                var lines = new List<string>(); var game = Assembly.GetEntryAssembly();
                lines.Add("game mode=" + game.GetType("#=zUp$mEEXw9tnX4wTw9g==", true).GetField("#=zPlM2ZxE=", flags).GetValue(null));
                var chat = game.GetType("#=zfZ67dsuuO$fV485iSEUJubF$6dHYUCshjA==", true);
                var sender = chat.GetMethod("#=zk23iJUA=", flags, null, new[] { typeof(string), typeof(string), typeof(bool) }, null);
                lines.Add("sender IL bytes=" + sender.GetMethodBody().GetILAsByteArray().Length);
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name.StartsWith("Chatter.Bridge") || a.GetName().Name == "0Harmony")) {
                    lines.Add(assembly.FullName + " · " + assembly.Location);
                    var commands = assembly.GetType("Chatter.Bridge.NativeCommands");
                    if (commands == null) continue;
                    var owner = commands.GetField("owner", flags).GetValue(null);
                    lines.Add("command owner=" + (owner != null));
                    if (owner != null) {
                        ulong expires = (ulong)commands.GetField("expires", flags).GetValue(owner);
                        lines.Add("lease remaining ms=" + ((long)expires - (long)GetTickCount64()));
                        foreach (var name in new[] { "queue", "pending" }) { object collection = commands.GetField(name, flags).GetValue(owner); lines.Add(name + "=" + collection.GetType().GetProperty("Count").GetValue(collection, null)); }
                    }
                }
                var harmony = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "0Harmony");
                if (harmony != null) {
                    object patch = harmony.GetType("HarmonyLib.Harmony").GetMethod("GetPatchInfo", flags).Invoke(null, new object[] { sender });
                    lines.Add("sender patched=" + (patch != null));
                    if (patch != null) foreach (object prefix in Prefixes(patch)) lines.Add("prefix owner=" + prefix.GetType().GetField("owner").GetValue(prefix));
                }
                File.WriteAllLines(file, lines); return 0;
            } catch (Exception ex) { File.WriteAllText(file, ex.ToString()); return 1; }
        }
        internal static IEnumerable Prefixes(object patch) {
            // Harmony exposes this collection as a field, not a property.
            var field = patch.GetType().GetField("Prefixes", BindingFlags.Public | BindingFlags.Instance);
            if (field == null) throw new MissingFieldException(patch.GetType().FullName, "Prefixes");
            return (IEnumerable)field.GetValue(patch);
        }
        internal static void Run(string report) {
            int pid = ProcessMemory.FindOsu(); if (pid == 0) throw new InvalidOperationException("osu! is not running");
            IntPtr process = NativeMethods.OpenProcess(NativeMethods.ProcessQueryInformation | NativeMethods.ProcessVmRead | NativeMethods.ProcessVmWrite | NativeMethods.ProcessVmOperation | NativeMethods.ProcessCreateThread, false, pid);
            if (process == IntPtr.Zero) throw new InvalidOperationException("Cannot inspect process");
            IntPtr data = IntPtr.Zero, code = IntPtr.Zero, thread = IntPtr.Zero; bool running = false;
            try {
                var export = typeof(BridgeClient).GetMethod("RemoteExport", BindingFlags.NonPublic | BindingFlags.Static);
                data = NativeMethods.VirtualAllocEx(process, IntPtr.Zero, 8192, NativeMethods.MemCommit | NativeMethods.MemReserve, NativeMethods.PageReadWrite);
                if (data == IntPtr.Zero) throw new InvalidOperationException("Cannot allocate diagnostic arguments");
                byte[] bytes = ClrStub.BuildData(unchecked((uint)data.ToInt32()), new StubRequest { AssemblyPath = typeof(NativeBridgeInspection).Assembly.Location, TypeName = typeof(NativeBridgeInspection).FullName, MethodName = nameof(Inspect), Argument = Path.GetFullPath(report), GetModuleHandleW = (int)export.Invoke(null, new object[] { pid, "GetModuleHandleW" }), GetProcAddress = (int)export.Invoke(null, new object[] { pid, "GetProcAddress" }) });
                if (!NativeMethods.WriteProcessMemory(process, data, bytes, bytes.Length, out int written) || written != bytes.Length) throw new InvalidOperationException("Cannot prepare diagnostic arguments");
                code = NativeMethods.VirtualAllocEx(process, IntPtr.Zero, (uint)ClrStub.Code.Length, NativeMethods.MemCommit | NativeMethods.MemReserve, NativeMethods.PageReadWrite);
                if (code == IntPtr.Zero || !NativeMethods.WriteProcessMemory(process, code, ClrStub.Code, ClrStub.Code.Length, out written) || written != ClrStub.Code.Length || !NativeMethods.VirtualProtectEx(process, code, (uint)ClrStub.Code.Length, NativeMethods.PageExecuteRead, out _)) throw new InvalidOperationException("Cannot prepare diagnostic code");
                thread = NativeMethods.CreateRemoteThread(process, IntPtr.Zero, 0, code, data, 0, out _); if (thread == IntPtr.Zero) throw new InvalidOperationException("Cannot run diagnostics"); running = true;
                if (NativeMethods.WaitForSingleObject(thread, 8000) != 0) throw new TimeoutException("Diagnostic probe timed out");
                running = false;
                if (!NativeMethods.GetExitCodeThread(thread, out uint exit) || exit != 0) throw new InvalidOperationException("Diagnostic probe returned " + exit);
                Console.WriteLine(File.ReadAllText(report));
            } finally { if (!running) { if (data != IntPtr.Zero) NativeMethods.VirtualFreeEx(process, data, 0, NativeMethods.MemRelease); if (code != IntPtr.Zero) NativeMethods.VirtualFreeEx(process, code, 0, NativeMethods.MemRelease); } if (thread != IntPtr.Zero) NativeMethods.CloseHandle(thread); NativeMethods.CloseHandle(process); }
        }
    }
}
