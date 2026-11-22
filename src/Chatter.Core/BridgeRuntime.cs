using System;
using System.Linq;
using System.Reflection;

namespace Chatter {
    internal static class BridgeRuntime {
        internal static string AssemblyName {
            get {
                string name = typeof(BridgeRuntime).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "Chatter.BridgeAssemblyName").Value;
                if (!name.StartsWith("Chatter.Bridge.Runtime_", StringComparison.Ordinal) || name.Any(c => !char.IsLetterOrDigit(c) && c != '.' && c != '_')) throw new InvalidOperationException("Invalid bundled bridge identity");
                return name;
            }
        }
        internal static string FileName => AssemblyName + ".dll";
    }
}
