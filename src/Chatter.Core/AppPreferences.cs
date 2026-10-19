using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace Chatter {
    public sealed class ChannelPreference {
        public string Name { get; set; } = "";
        public string IncomingLanguage { get; set; } = "";
        public string OutgoingLanguage { get; set; } = "";
        public bool? AutoTranslate { get; set; }

        public ChannelPreference Copy() => new ChannelPreference {
            Name = Name, IncomingLanguage = IncomingLanguage, OutgoingLanguage = OutgoingLanguage, AutoTranslate = AutoTranslate
        };

        public bool LanguagesValid() {
            IncomingLanguage = IncomingLanguage ?? "";
            OutgoingLanguage = OutgoingLanguage ?? "";
            if (IncomingLanguage.Length > 0 && Array.IndexOf(ChatRules.Languages, IncomingLanguage) < 0) return false;
            if (OutgoingLanguage.Length > 0 && Array.IndexOf(ChatRules.Languages, OutgoingLanguage) < 0) return false;
            return true;
        }
    }

    public sealed class AppPreferences {
        public const int MaxChannels = 48;
        public string Endpoint { get; set; } = ChatRules.LocalTranslator;
        public string ApiKey { get; set; } = "";
        public string ManualEndpoint { get; set; } = "";
        public string ManualApiKey { get; set; } = "";
        public string IncomingLanguage { get; set; } = "en";
        public string OutgoingLanguage { get; set; } = "channel";
        public bool AutoTranslateIncoming { get; set; } = true;
        public bool StartReading { get; set; } = true;
        public List<ChannelPreference> Channels { get; set; } = new List<ChannelPreference>();

        public bool HasDistinctManualProvider =>
            !string.IsNullOrEmpty(ManualEndpoint) && !string.Equals(ManualEndpoint, Endpoint, StringComparison.Ordinal);

        public bool UsesLocalTranslator =>
            Endpoint == ChatRules.LocalTranslator || ManualEndpoint == ChatRules.LocalTranslator;

        public static string ProviderName(string endpoint) {
            if (endpoint == Translator.DeepLFree) return "DeepL API Free";
            if (endpoint == Translator.DeepLPro) return "DeepL API Pro";
            if (string.IsNullOrEmpty(endpoint) || endpoint == ChatRules.LocalTranslator) return "Local translation";
            return "LibreTranslate";
        }

        public string ProviderSummary() {
            string summary = ProviderName(Endpoint);
            summary += Endpoint == ChatRules.LocalTranslator ? " · on this PC"
                : Translator.IsDeepL(Endpoint) ? " · cloud translation" : " · your server";
            if (!HasDistinctManualProvider) return summary;
            return summary + "\nTranslate button · " + ProviderName(ManualEndpoint);
        }

        public AppPreferences Copy() {
            var copy = (AppPreferences)MemberwiseClone();
            var source = Channels ?? new List<ChannelPreference>();
            copy.Channels = new List<ChannelPreference>(source.Count);
            for (int i = 0; i < source.Count; i++) if (source[i] != null) copy.Channels.Add(source[i].Copy());
            return copy;
        }

        public ChannelPreference FindChannel(string name) {
            if (string.IsNullOrEmpty(name) || Channels == null) return null;
            for (int i = 0; i < Channels.Count; i++)
                if (Channels[i] != null && string.Equals(Channels[i].Name, name, StringComparison.Ordinal)) return Channels[i];
            return null;
        }

        public bool Remember(ChannelPreference entry) {
            if (entry == null || !IsChannelName(entry.Name) || !entry.LanguagesValid()) return false;
            if (Channels == null) Channels = new List<ChannelPreference>();
            for (int i = Channels.Count - 1; i >= 0; i--)
                if (Channels[i] == null || string.Equals(Channels[i].Name, entry.Name, StringComparison.Ordinal)) Channels.RemoveAt(i);
            Channels.Add(entry);
            while (Channels.Count > MaxChannels) Channels.RemoveAt(0);
            return true;
        }

        public void ApplyProvider(TranslationRequest request, bool manual) {
            bool useManual = manual && !string.IsNullOrEmpty(ManualEndpoint);
            request.Endpoint = useManual ? ManualEndpoint : Endpoint;
            request.ApiKey = useManual ? ManualApiKey ?? "" : ApiKey ?? "";
        }

        public static bool IsChannelName(string name) {
            if (string.IsNullOrEmpty(name) || name.Length > 80) return false;
            for (int i = 0; i < name.Length; i++) if (name[i] < 32 || name[i] == 127) return false;
            return true;
        }

        public void Validate() {
            if (Array.IndexOf(ChatRules.Languages, IncomingLanguage) < 0 ||
                (OutgoingLanguage != "channel" && Array.IndexOf(ChatRules.Languages, OutgoingLanguage) < 0))
                throw new InvalidOperationException("Choose an incoming and outgoing language.");
            new TranslationRequest { Endpoint = Endpoint, ApiKey = ApiKey, Text = "test", Source = "en", Target = IncomingLanguage }.Validate();
            if (!string.IsNullOrEmpty(ManualEndpoint))
                new TranslationRequest { Endpoint = ManualEndpoint, ApiKey = ManualApiKey, Text = "test", Source = "en", Target = IncomingLanguage }.Validate();
            if (Channels == null) Channels = new List<ChannelPreference>();
            if (Channels.Count > MaxChannels) throw new InvalidOperationException("Too many conversation settings.");
            var seen = new HashSet<string>();
            for (int i = 0; i < Channels.Count; i++) {
                ChannelPreference channel = Channels[i];
                if (channel == null || !IsChannelName(channel.Name) || !channel.LanguagesValid() || !seen.Add(channel.Name))
                    throw new InvalidOperationException("Invalid conversation settings.");
            }
        }
    }

    public static class PreferencesStore {
        public const int MaxBytes = 65536;
        public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "osu-chatter", "settings.json");
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("osu-chatter/preferences/v1");
        public static bool TryLoad(out AppPreferences preferences, string path = null) {
            preferences = new AppPreferences();
            try {
                path = path ?? DefaultPath;
                if (!File.Exists(path) || new FileInfo(path).Length > MaxBytes) return false;
                var values = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
                int version = Convert.ToInt32(values["version"]);
                // Version 1 files predate the manual provider and per-conversation settings.
                if (version != 1 && version != 2) return false;
                string encrypted = (string)values["encryptedKey"];
                var loaded = new AppPreferences {
                    Endpoint = (string)values["endpoint"],
                    ApiKey = encrypted.Length == 0 ? "" : Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(encrypted), Entropy, DataProtectionScope.CurrentUser)),
                    ManualEndpoint = ReadString(values, "manualEndpoint"),
                    ManualApiKey = Unprotect(ReadString(values, "encryptedManualKey")),
                    IncomingLanguage = (string)values["incomingLanguage"], OutgoingLanguage = (string)values["outgoingLanguage"],
                    AutoTranslateIncoming = (bool)values["autoTranslateIncoming"], StartReading = (bool)values["startReading"],
                    Channels = ReadChannels(values)
                };
                loaded.Validate();
                preferences = loaded;
                return true;
            } catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException || ex is CryptographicException || ex is FormatException || ex is KeyNotFoundException || ex is InvalidCastException || ex is NullReferenceException || ex is OverflowException) {
                return false;
            }
        }
        public static void Save(AppPreferences preferences, string path = null) {
            preferences.Validate();
            path = path ?? DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string key = preferences.Endpoint == ChatRules.LocalTranslator ? "" : preferences.ApiKey;
            string manualKey = string.IsNullOrEmpty(preferences.ManualEndpoint) || preferences.ManualEndpoint == ChatRules.LocalTranslator ? "" : preferences.ManualApiKey;
            var values = new Dictionary<string, object> {
                ["version"] = 2, ["endpoint"] = preferences.Endpoint,
                ["encryptedKey"] = Protect(key),
                ["manualEndpoint"] = preferences.ManualEndpoint ?? "",
                ["encryptedManualKey"] = Protect(manualKey),
                ["incomingLanguage"] = preferences.IncomingLanguage, ["outgoingLanguage"] = preferences.OutgoingLanguage,
                ["autoTranslateIncoming"] = preferences.AutoTranslateIncoming, ["startReading"] = preferences.StartReading,
                ["channels"] = WriteChannels(preferences.Channels)
            };
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(values), new UTF8Encoding(false));
                if (new FileInfo(temporary).Length > MaxBytes) throw new IOException("Could not save your preferences. Check access to your local app data folder.");
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        static string Protect(string key) {
            if (string.IsNullOrEmpty(key)) return "";
            return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key), Entropy, DataProtectionScope.CurrentUser));
        }

        static string Unprotect(string encrypted) {
            if (string.IsNullOrEmpty(encrypted)) return "";
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(encrypted), Entropy, DataProtectionScope.CurrentUser));
        }

        static string ReadString(Dictionary<string, object> values, string key) {
            object value;
            if (!values.TryGetValue(key, out value) || value == null) return "";
            return value as string ?? "";
        }

        static List<ChannelPreference> ReadChannels(Dictionary<string, object> values) {
            var channels = new List<ChannelPreference>();
            object raw;
            if (!values.TryGetValue("channels", out raw)) return channels;
            var rows = raw as IList;
            if (rows == null) return channels;
            for (int i = 0; i < rows.Count; i++) {
                var row = rows[i] as Dictionary<string, object>;
                if (row == null) continue;
                var channel = new ChannelPreference { Name = ReadString(row, "name"), IncomingLanguage = ReadString(row, "incoming"), OutgoingLanguage = ReadString(row, "outgoing") };
                object auto;
                if (row.TryGetValue("auto", out auto) && auto is bool) channel.AutoTranslate = (bool)auto;
                if (!AppPreferences.IsChannelName(channel.Name) || !channel.LanguagesValid()) continue;
                bool duplicate = false;
                for (int j = 0; j < channels.Count; j++) if (channels[j].Name == channel.Name) { channels[j] = channel; duplicate = true; break; }
                if (!duplicate) channels.Add(channel);
            }
            while (channels.Count > AppPreferences.MaxChannels) channels.RemoveAt(0);
            return channels;
        }

        static List<Dictionary<string, object>> WriteChannels(List<ChannelPreference> channels) {
            var rows = new List<Dictionary<string, object>>();
            if (channels == null) return rows;
            for (int i = 0; i < channels.Count; i++) {
                ChannelPreference channel = channels[i];
                if (channel == null) continue;
                var row = new Dictionary<string, object> { ["name"] = channel.Name };
                if (!string.IsNullOrEmpty(channel.IncomingLanguage)) row["incoming"] = channel.IncomingLanguage;
                if (!string.IsNullOrEmpty(channel.OutgoingLanguage)) row["outgoing"] = channel.OutgoingLanguage;
                if (channel.AutoTranslate.HasValue) row["auto"] = channel.AutoTranslate.Value;
                rows.Add(row);
            }
            return rows;
        }
    }
}
