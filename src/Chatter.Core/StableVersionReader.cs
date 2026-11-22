using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using V = Chatter.Shared.StableCompatibilityProfile.ClientVersion;

namespace Chatter {
    internal sealed class StableVersionInfo {
        internal string? Build, Stream;
        internal string Source = "unavailable";
    }

    // Read-only diagnostics. Version strings/configuration never grant send authority.
    internal static class StableVersionReader {
        internal static string? Normalize(string? text) {
            var match = Regex.Match((text ?? "").Trim().Trim('"').Trim(), @"^(?:b\s*)?(20\d{6})(?:\.(\d{1,9}))?$", RegexOptions.CultureInvariant);
            if (!match.Success || !DateTime.TryParseExact(match.Groups[1].Value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) return null;
            return match.Groups[1].Value + (match.Groups[2].Success ? "." + match.Groups[2].Value : "");
        }
        internal static string? ConfigValue(string text, string key) {
            using (var lines = new StringReader(text)) {
                string? line;
                while ((line = lines.ReadLine()) != null) {
                    line = line.Trim();
                    if (line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith("//", StringComparison.Ordinal)) continue;
                    int equals = line.IndexOf('=');
                    if (equals >= 0 && line.Substring(0, equals).Trim() == key) return line.Substring(equals + 1).Trim();
                }
            }
            return null;
        }
        static uint? Pointer(IProcessMemory memory, uint address) {
            if (address < Clr32Layout.MinimumAddress || address >= Clr32Layout.AddressLimit) return null;
            uint? value = ChatGraph.Word(memory, address);
            return value.HasValue && value.Value >= Clr32Layout.MinimumAddress && value.Value < Clr32Layout.AddressLimit ? value : null;
        }
        internal static string? FromMemory(IProcessMemory memory, uint operandAddress) {
            uint? slot = Pointer(memory, operandAddress);
            if (!slot.HasValue) return null;
            uint? dictionary = Pointer(memory, slot.Value);
            if (!dictionary.HasValue) return null;
            uint? entries = Pointer(memory, dictionary.Value + V.Entries);
            uint? count = ChatGraph.Word(memory, dictionary.Value + V.Count);
            if (!entries.HasValue || !count.HasValue || count == 0 || count > 4096) return null;
            uint? capacity = ChatGraph.Word(memory, entries.Value + Clr32Layout.ArrayLength);
            if (!capacity.HasValue || count > capacity || capacity > 65536) return null;
            for (uint i = 0; i < count.Value; i++) {
                uint entry = entries.Value + Clr32Layout.ArrayData + V.EntrySize * i;
                uint? key = Pointer(memory, entry + V.EntryKey);
                if (!key.HasValue || ChatGraph.String(memory, key.Value) != V.Key) continue;
                uint? bindable = Pointer(memory, entry + V.EntryBindable);
                uint? value = bindable.HasValue ? Pointer(memory, bindable.Value + V.BindableValue) : null;
                string? build = value.HasValue ? Normalize(ChatGraph.String(memory, value.Value)) : null;
                if (Pointer(memory, operandAddress) != slot || Pointer(memory, slot.Value) != dictionary
                    || Pointer(memory, dictionary.Value + V.Entries) != entries || ChatGraph.Word(memory, dictionary.Value + V.Count) != count
                    || Pointer(memory, entry + V.EntryKey) != key || Pointer(memory, entry + V.EntryBindable) != bindable
                    || (bindable.HasValue && Pointer(memory, bindable.Value + V.BindableValue) != value)) return null;
                return build;
            }
            return null;
        }
        internal static StableVersionInfo FromConfiguration(string directory) {
            var info = new StableVersionInfo();
            try {
                var userBuilds = new HashSet<string>(StringComparer.Ordinal);
                string? commonBuild = null;
                foreach (string path in Directory.EnumerateFiles(directory, "osu!*.cfg").Take(64)) {
                    try {
                        if (new FileInfo(path).Length > 4 * 1024 * 1024) continue;
                        string text = File.ReadAllText(path);
                        string? build = Normalize(ConfigValue(text, V.Key));
                        if (Path.GetFileName(path).Equals("osu!.cfg", StringComparison.OrdinalIgnoreCase)) {
                            commonBuild = build;
                            string? stream = ConfigValue(text, "_ReleaseStream")?.Trim('"').ToLowerInvariant();
                            if (stream == "stable40" || stream == "beta" || stream == "cuttingedge") info.Stream = stream;
                        } else if (build != null) userBuilds.Add(build);
                    } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
                info.Build = userBuilds.Count == 1 ? userBuilds.Single() : userBuilds.Count == 0 ? commonBuild : null;
                info.Source = userBuilds.Count > 1 ? "conflicting configuration files" : info.Build != null ? "configuration file (last launched version)" : "unavailable";
            } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return info;
        }
        internal static StableVersionInfo Inspect(int pid, string executable) {
            var info = FromConfiguration(Path.GetDirectoryName(executable));
            if (pid <= 0) return info;
            try {
                using (var memory = new ProcessMemory()) {
                    memory.Open(pid, NativeMethods.ProcessVmRead | NativeMethods.ProcessQueryInformation);
                    var addresses = memory.Scan(new[] { new Pattern("configurationAddr", V.ConfigurationSignature, V.OperandOffset) });
                    if (addresses.TryGetValue("configurationAddr", out uint address)) {
                        string? build = FromMemory(memory, address);
                        if (build != null) { info.Build = build; info.Source = "live configuration memory"; }
                    }
                }
            } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
            return info;
        }
    }
}
