using System;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using Chatter;

namespace Chatter.Tests {
    public sealed class LegacyAssemblyEmitter : MarshalByRefObject {
        public void Write(string directory, string name, int version, int answer) {
            Directory.CreateDirectory(directory);
            var assembly = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName(name) { Version = new Version(version, 0, 0, 0) }, AssemblyBuilderAccess.Save, directory);
            var module = assembly.DefineDynamicModule(name, name + ".dll");
            var type = module.DefineType("Fixture.Entry", TypeAttributes.Public | TypeAttributes.Class);
            var method = type.DefineMethod("Start", MethodAttributes.Public | MethodAttributes.Static, typeof(int), new[] { typeof(string) });
            method.GetILGenerator().Emit(OpCodes.Ldc_I4, answer); method.GetILGenerator().Emit(OpCodes.Ret);
            type.CreateType(); assembly.Save(name + ".dll");
        }
    }
    static class BridgeUpgradeTests {
        internal static void Identity() {
            string root = Path.Combine(Path.GetTempPath(), "chatter-bridge-upgrade-" + Guid.NewGuid().ToString("N"));
            string legacy = "Chatter.LegacyFixture." + Guid.NewGuid().ToString("N");
            string revised = legacy + ".RuntimeV2";
            var domain = AppDomain.CreateDomain("bridge-emitter-" + Guid.NewGuid().ToString("N"), null, new AppDomainSetup { ApplicationBase = AppDomain.CurrentDomain.BaseDirectory });
            try {
                var emitter = (LegacyAssemblyEmitter)domain.CreateInstanceAndUnwrap(typeof(LegacyAssemblyEmitter).Assembly.FullName, typeof(LegacyAssemblyEmitter).FullName);
                emitter.Write(Path.Combine(root, "old"), legacy, 1, 41);
                emitter.Write(Path.Combine(root, "bumped"), legacy, 2, 42);
                emitter.Write(Path.Combine(root, "renamed"), revised, 2, 43);
            } finally { AppDomain.Unload(domain); }
            int Invoke(string folder, string name) {
                IntPtr kernel = NativeMethods.GetModuleHandleA("kernel32.dll");
                return ClrStub.RunLocal(new StubRequest { AssemblyPath = Path.Combine(root, folder, name + ".dll"), TypeName = "Fixture.Entry", MethodName = "Start", Argument = "", GetModuleHandleW = NativeMethods.GetProcAddress(kernel, "GetModuleHandleW").ToInt32(), GetProcAddress = NativeMethods.GetProcAddress(kernel, "GetProcAddress").ToInt32() });
            }
            try {
                if (Invoke("old", legacy) != 41) throw new InvalidOperationException("Legacy fixture did not load");
                int bumped = Invoke("bumped", legacy);
                if (bumped != 41) throw new InvalidOperationException("Expected CLR weak-name binding to reproduce stale code; got " + bumped);
                if (Invoke("renamed", revised) != 43) throw new InvalidOperationException("Changed runtime identity did not load new code");
                string file = new BridgeClient(ChatService.DefaultBridgePath()).PrepareLibrary();
                if (AssemblyName.GetAssemblyName(file).Name != typeof(Chatter.Bridge.Entry).Assembly.GetName().Name || !Path.GetFileName(file).StartsWith("Chatter.Bridge.Runtime_")) throw new InvalidOperationException("Bundled/runtime bridge identities differ");
            } finally {
                // The CLR can keep loaded fixture DLLs mapped until this test process exits.
                string parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
                if (Path.GetDirectoryName(Path.GetFullPath(root)) != parent) throw new InvalidOperationException("Unexpected fixture directory");
                try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }
}
