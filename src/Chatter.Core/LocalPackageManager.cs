using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace Chatter {
    public static class LocalPackageManager {
        public static string PackagesDirectory {
            get {
                string root = LocalTranslator.DataDirectory;
                string old = Path.Combine(Path.GetDirectoryName(root), "translator-v1", "data");
                return Path.Combine(Directory.Exists(old) ? old : Path.Combine(root, "data"), "argos-translate", "packages");
            }
        }
        public static string Describe() {
            var lines = new List<string>();
            if (Directory.Exists(PackagesDirectory)) foreach (var folder in Directory.EnumerateDirectories(PackagesDirectory)) {
                try {
                    string metadata = Path.Combine(folder, "metadata.json");
                    if (!File.Exists(metadata) || new FileInfo(metadata).Length > 32768) continue;
                    var values = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(metadata));
                    long size = new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
                    lines.Add(values["from_code"] + " → " + values["to_code"] + " · " + size / 1048576 + " MB");
                } catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is KeyNotFoundException) { }
            }
            return (lines.Count == 0 ? "No lightweight language pairs downloaded yet." : string.Join("\n", lines.OrderBy(s => s))) + "\n" + PackagesDirectory;
        }
        public static void ClearDownloadCache() {
            string root = Path.GetFullPath(LocalTranslator.DataDirectory);
            string cache = Path.GetFullPath(Path.Combine(root, "cache"));
            if (!cache.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid cache folder.");
            if (Directory.Exists(cache)) Directory.Delete(cache, true);
        }
    }
}
