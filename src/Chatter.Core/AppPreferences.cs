using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace Chatter {
    public sealed class AppPreferences {
        public string Endpoint { get; set; } = ChatRules.LocalTranslator;
        public string ApiKey { get; set; } = "";
        public string IncomingLanguage { get; set; } = "en";
        public string OutgoingLanguage { get; set; } = "channel";
        public bool AutoTranslateIncoming { get; set; } = true;
        public bool StartReading { get; set; } = true;
        public AppPreferences Copy() => (AppPreferences)MemberwiseClone();
        public void Validate() {
            if (Array.IndexOf(ChatRules.Languages, IncomingLanguage) < 0 ||
                (OutgoingLanguage != "channel" && Array.IndexOf(ChatRules.Languages, OutgoingLanguage) < 0))
                throw new InvalidOperationException("Choose an incoming and outgoing language.");
            new TranslationRequest { Endpoint = Endpoint, ApiKey = ApiKey, Text = "test", Source = "en", Target = IncomingLanguage }.Validate();
        }
    }

    public static class PreferencesStore {
        public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "osu-chatter", "settings.json");
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("osu-chatter/preferences/v1");
        public static bool TryLoad(out AppPreferences preferences, string path = null) {
            preferences = new AppPreferences();
            try {
                path = path ?? DefaultPath;
                if (!File.Exists(path) || new FileInfo(path).Length > 16384) return false;
                var values = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
                if (Convert.ToInt32(values["version"]) != 1) return false;
                string encrypted = (string)values["encryptedKey"];
                var loaded = new AppPreferences {
                    Endpoint = (string)values["endpoint"],
                    ApiKey = encrypted.Length == 0 ? "" : Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(encrypted), Entropy, DataProtectionScope.CurrentUser)),
                    IncomingLanguage = (string)values["incomingLanguage"], OutgoingLanguage = (string)values["outgoingLanguage"],
                    AutoTranslateIncoming = (bool)values["autoTranslateIncoming"], StartReading = (bool)values["startReading"]
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
            var values = new Dictionary<string, object> {
                ["version"] = 1, ["endpoint"] = preferences.Endpoint,
                ["encryptedKey"] = string.IsNullOrEmpty(key) ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key), Entropy, DataProtectionScope.CurrentUser)),
                ["incomingLanguage"] = preferences.IncomingLanguage, ["outgoingLanguage"] = preferences.OutgoingLanguage,
                ["autoTranslateIncoming"] = preferences.AutoTranslateIncoming, ["startReading"] = preferences.StartReading
            };
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(values), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
