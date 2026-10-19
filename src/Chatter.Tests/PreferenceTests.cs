using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using Chatter;
using Chatter.App;

namespace Chatter.Tests {
    static class PreferenceTests {
        const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        static object Get(object target, string name) => target.GetType().GetField(name, Members).GetValue(target);
        static void Call(object target, string name) => target.GetType().GetMethod(name, Members).Invoke(target, null);
        static void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); }
        public static void DeepLProtocol() {
            foreach (string endpoint in new[] { Translator.DeepLFree, Translator.DeepLPro }) {
                foreach (var example in new[] { (200, "auto"), (200, "en"), (403, "auto"), (429, "auto"), (456, "auto") }) {
                    int status = example.Item1;
                    string source = example.Item2;
                    using (var listener = new HttpListener()) {
                        listener.Prefixes.Add("http://127.0.0.1:17322/");
                        listener.Start();
                        Exception serverFailure = null;
                        string requestBody = "", authorization = "", method = "";
                        var server = new Thread(() => {
                            try {
                                var context = listener.GetContext();
                                using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8)) {
                                    requestBody = reader.ReadToEnd();
                                    authorization = context.Request.Headers["Authorization"];
                                    method = context.Request.HttpMethod;
                                }
                                context.Response.StatusCode = status;
                                byte[] response = Encoding.UTF8.GetBytes(status == 200 ? "{\"translations\":[{\"detected_source_language\":\"RU\",\"text\":\"Hello 👋\"}]}" : "{\"message\":\"fixture-test-key should never appear in the error\"}");
                                context.Response.ContentType = "application/json";
                                context.Response.ContentLength64 = response.Length;
                                context.Response.OutputStream.Write(response, 0, response.Length);
                                context.Response.Close();
                            } catch (Exception ex) { serverFailure = ex; }
                        }) { IsBackground = true };
                        server.Start();
                        int calls = 0;
                        using (var translator = new Translator(url => {
                            calls++;
                            Check(url == (endpoint == Translator.DeepLFree ? "https://api-free.deepl.com/v2/translate" : "https://api.deepl.com/v2/translate"), "plan endpoint");
                            return (HttpWebRequest)WebRequest.Create("http://127.0.0.1:17322/v2/translate");
                        })) {
                            try {
                                string result = translator.Translate(new TranslationRequest { Endpoint = endpoint, ApiKey = "fixture-test-key", Text = "Привет 👋", Source = source, Target = source == "auto" ? "en" : "ru" });
                                Check(status == 200 && result == "Hello 👋", "DeepL response");
                            } catch (InvalidOperationException ex) {
                                Check(status != 200 && ex.Message == Translator.DeepLError(status), "DeepL error mapping");
                                Check(!ex.Message.Contains("fixture-test-key"), "error leaked credentials");
                            }
                        }
                        Check(server.Join(5000), "server did not finish");
                        if (serverFailure != null) throw serverFailure;
                        Check(calls == 1, "translation retried");
                        var json = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(requestBody);
                        Check(authorization == "DeepL-Auth-Key fixture-test-key", "authorization header");
                        Check(!requestBody.Contains("fixture-test-key"), "key leaked in body");
                        Check(method == "POST", "request method");
                        Check(((System.Collections.IList)json["text"])[0].Equals("Привет 👋"), "UTF-8 input");
                        Check(json["target_lang"].Equals(source == "auto" ? "EN-US" : "RU"), "target language mapping");
                        Check(source == "auto" ? !json.ContainsKey("source_lang") : json["source_lang"].Equals("EN"), "source language mapping");
                    }
                }
            }
            Check(Translator.DeepLLanguage("pt", true) == "PT-PT" && Translator.DeepLLanguage("en", false) == "EN" && Translator.DeepLLanguage("ko", true) == "KO", "language codes");
            foreach (var invalid in new[] { "null", "{}", "{\"translations\":[]}", "{\"translations\":[{\"text\":null}]}" })
                Check(Translator.DeepLText(invalid) == null, "invalid response accepted");
            foreach (string key in new[] { "", "  ", "bad\r\nkey" }) {
                try { new TranslationRequest { Endpoint = Translator.DeepLFree, ApiKey = key, Source = "en", Target = "pl", Text = "Hello" }.Validate(); throw new Exception("missing key accepted"); }
                catch (InvalidOperationException) { }
            }
        }
        public static void Persistence() {
            string directory = Path.Combine(Path.GetTempPath(), "chatter-preferences-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "settings.json");
            try {
                Check(!PreferencesStore.TryLoad(out _, path), "missing preferences");
                var source = new AppPreferences { Endpoint = Translator.DeepLPro, ApiKey = "fixture-test-key", IncomingLanguage = "pl", OutgoingLanguage = "ko", AutoTranslateIncoming = false, StartReading = false };
                PreferencesStore.Save(source, path);
                Check(!File.ReadAllText(path).Contains(source.ApiKey), "plaintext key on disk");
                Check(PreferencesStore.TryLoad(out var loaded, path), "preferences read");
                Check(loaded.ApiKey == source.ApiKey && loaded.Endpoint == source.Endpoint && loaded.IncomingLanguage == "pl" && loaded.OutgoingLanguage == "ko" && !loaded.AutoTranslateIncoming && !loaded.StartReading, "preferences roundtrip");
                source.Endpoint = ChatRules.LocalTranslator;
                PreferencesStore.Save(source, path);
                Check(PreferencesStore.TryLoad(out loaded, path) && loaded.ApiKey == "", "local retained cloud key");
                File.WriteAllText(path, "{broken");
                Check(!PreferencesStore.TryLoad(out _, path), "corrupt settings accepted");
                source.Endpoint = Translator.DeepLFree;
                PreferencesStore.Save(source, path);
                var values = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
                values["encryptedKey"] = "invalid";
                File.WriteAllText(path, new JavaScriptSerializer().Serialize(values));
                Check(!PreferencesStore.TryLoad(out _, path), "invalid key accepted");
                File.WriteAllText(path, "{\"version\":1,\"endpoint\":\"local\",\"encryptedKey\":\"\",\"incomingLanguage\":\"en\",\"outgoingLanguage\":\"channel\",\"autoTranslateIncoming\":true,\"startReading\":true}");
                Check(PreferencesStore.TryLoad(out loaded, path) && loaded.Endpoint == ChatRules.LocalTranslator && loaded.ManualEndpoint == "" && loaded.Channels.Count == 0, "version 1 settings");
                source = new AppPreferences {
                    Endpoint = "https://libre.example/translate", ApiKey = "libre-key", ManualEndpoint = Translator.DeepLFree, ManualApiKey = "manual-secret",
                    IncomingLanguage = "en", OutgoingLanguage = "channel", AutoTranslateIncoming = true
                };
                Check(source.Remember(new ChannelPreference { Name = "#osu", IncomingLanguage = "pl", OutgoingLanguage = "ja", AutoTranslate = false }), "channel saved");
                Check(source.Remember(new ChannelPreference { Name = "игрок", IncomingLanguage = "ru", AutoTranslate = true }), "unicode channel");
                PreferencesStore.Save(source, path);
                string disk = File.ReadAllText(path);
                Check(!disk.Contains("libre-key") && !disk.Contains("manual-secret"), "manual key on disk");
                Check(PreferencesStore.TryLoad(out loaded, path), "dual providers read");
                Check(loaded.Endpoint == source.Endpoint && loaded.ApiKey == "libre-key" && loaded.ManualEndpoint == Translator.DeepLFree && loaded.ManualApiKey == "manual-secret", "provider roundtrip");
                var osu = loaded.FindChannel("#osu");
                var player = loaded.FindChannel("игрок");
                Check(osu != null && osu.IncomingLanguage == "pl" && osu.OutgoingLanguage == "ja" && osu.AutoTranslate == false, "osu channel roundtrip");
                Check(player != null && player.IncomingLanguage == "ru" && player.AutoTranslate == true && player.OutgoingLanguage == "", "player channel roundtrip");
                var rows = (System.Collections.ArrayList)new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(disk)["channels"];
                rows.Add(new Dictionary<string, object> { { "name", "bad\nname" }, { "incoming", "nope" } });
                var document = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(disk);
                document["channels"] = rows;
                File.WriteAllText(path, new JavaScriptSerializer().Serialize(document));
                Check(PreferencesStore.TryLoad(out loaded, path) && loaded.FindChannel("#osu") != null && loaded.FindChannel("bad\nname") == null, "invalid channel rejected the file");
                var crowded = new AppPreferences();
                for (int i = 0; i < AppPreferences.MaxChannels + 1; i++)
                    Check(crowded.Remember(new ChannelPreference { Name = "#c" + i }), "remember channel");
                Check(crowded.Channels.Count == AppPreferences.MaxChannels && crowded.FindChannel("#c0") == null && crowded.FindChannel("#c" + AppPreferences.MaxChannels) != null, "oldest conversation dropped");
                Check(!crowded.Remember(new ChannelPreference { Name = "bad\rname" }), "invalid conversation name");
            } finally { File.Delete(path); Directory.Delete(directory); }
        }
        public static void Setup() {
            using (var setup = new SetupForm(new AppPreferences(), true, false)) {
                var provider = (ComboBox)Get(setup, "provider");
                var automatic = (CheckBox)Get(setup, "automatic");
                var key = (TextBox)Get(Get(setup, "key"), "Input");
                Check(provider.SelectedIndex == 0 && automatic.Checked, "initial defaults");
                provider.SelectedIndex = 1;
                Check(!automatic.Checked, "cloud started bulk translation by default");
                Call(setup, "SaveChoices");
                Check(setup.Result == null, "empty DeepL key saved");
                key.Text = "fixture-test-key";
                provider.SelectedIndex = 2;
                Check(key.Text.Length > 0, "Free/Pro switch cleared the key");
                provider.SelectedIndex = 3;
                Check(key.Text.Length == 0, "cloud key carried to another service");
                provider.SelectedIndex = 1;
                key.Text = "fixture-test-key";
                ((ComboBox)Get(setup, "incoming")).SelectedIndex = 1;
                Call(setup, "SaveChoices");
                Check(setup.DialogResult == DialogResult.OK && setup.Result.Endpoint == Translator.DeepLFree && setup.Result.IncomingLanguage == "pl" && !setup.Result.AutoTranslateIncoming, "setup choices");
            }
            using (var setup = new SetupForm(new AppPreferences { Endpoint = Translator.DeepLPro, ApiKey = "fixture-test-key", AutoTranslateIncoming = true }, false, false))
                Check(((CheckBox)Get(setup, "automatic")).Checked, "saved cloud preference was lost");
            var current = new AppPreferences {
                Endpoint = "https://libre.example/translate", ApiKey = "libre-key", ManualEndpoint = Translator.DeepLFree, ManualApiKey = "deepl-key",
                IncomingLanguage = "en", OutgoingLanguage = "channel", AutoTranslateIncoming = true
            };
            current.Remember(new ChannelPreference { Name = "#osu", IncomingLanguage = "pl", OutgoingLanguage = "ja", AutoTranslate = false });
            using (var setup = new SetupForm(current, false, false)) {
                Check(((ComboBox)Get(setup, "provider")).SelectedIndex == 3, "automatic libre");
                Check(((ComboBox)Get(setup, "manualProvider")).SelectedIndex == 2, "manual deepl");
                Check(((TextBox)Get(Get(setup, "manualKey"), "Input")).Text == "deepl-key", "manual key loaded");
                Check(((TextBox)Get(Get(setup, "key"), "Input")).Text == "libre-key", "automatic key loaded");
                ((ComboBox)Get(setup, "manualProvider")).SelectedIndex = 3;
                Check(((TextBox)Get(Get(setup, "manualKey"), "Input")).Text == "deepl-key", "Free/Pro switch cleared the manual key");
                ((ComboBox)Get(setup, "manualProvider")).SelectedIndex = 4;
                Check(((TextBox)Get(Get(setup, "manualKey"), "Input")).Text.Length == 0, "manual key carried to LibreTranslate");
                ((ComboBox)Get(setup, "manualProvider")).SelectedIndex = 2;
                ((TextBox)Get(Get(setup, "manualKey"), "Input")).Text = "deepl-key";
                Call(setup, "SaveChoices");
                Check(setup.Result != null && setup.Result.Endpoint == current.Endpoint && setup.Result.ApiKey == "libre-key" && setup.Result.ManualEndpoint == Translator.DeepLFree && setup.Result.ManualApiKey == "deepl-key", "both providers saved");
                var kept = setup.Result.FindChannel("#osu");
                Check(kept != null && kept.IncomingLanguage == "pl" && kept.OutgoingLanguage == "ja" && kept.AutoTranslate == false, "channel settings survived the dialog");
            }
            using (var main = new MainForm(new AppPreferences { Endpoint = Translator.DeepLFree, ApiKey = "fixture-test-key", AutoTranslateIncoming = false, StartReading = false, OutgoingLanguage = "pl" })) {
                var handle = main.Handle;
                main.GetType().GetMethod("OnShown", Members).Invoke(main, new object[] { EventArgs.Empty });
                Check(!(bool)Get(main, "preparingTranslator"), "cloud startup prepared local translator");
                Check(!(bool)Get(main, "reading"), "startup reading preference ignored");
                Check((string)main.GetType().GetMethod("OutgoingLanguage", Members).Invoke(main, null) == "pl", "outgoing preference ignored");
                var preferences = (AppPreferences)Get(main, "preferences");
                Check(preferences.Endpoint == Translator.DeepLFree && preferences.ApiKey == "fixture-test-key", "saved cloud preferences were not loaded");
            }
        }
        public static void Channels() {
            var prefs = new AppPreferences {
                Endpoint = "https://libre.example/translate", ApiKey = "libre-key", ManualEndpoint = Translator.DeepLFree, ManualApiKey = "deepl-key",
                IncomingLanguage = "en", OutgoingLanguage = "channel", AutoTranslateIncoming = true
            };
            var request = new TranslationRequest { Text = "hello", Source = "en", Target = "pl" };
            prefs.ApplyProvider(request, false);
            Check(request.Endpoint == prefs.Endpoint && request.ApiKey == "libre-key", "automatic provider");
            prefs.ApplyProvider(request, true);
            Check(request.Endpoint == Translator.DeepLFree && request.ApiKey == "deepl-key", "manual provider");
            prefs.ManualEndpoint = "";
            prefs.ApplyProvider(request, true);
            Check(request.Endpoint == prefs.Endpoint, "manual falls back to automatic");
            prefs.ManualEndpoint = Translator.DeepLFree;
            Check(prefs.HasDistinctManualProvider && prefs.ProviderSummary().Contains("Translate button") && prefs.ProviderSummary().Contains("DeepL API Free"), "provider summary");
            prefs.Remember(new ChannelPreference { Name = "#osu", IncomingLanguage = "pl", OutgoingLanguage = "ja", AutoTranslate = false });
            using (var main = new MainForm(prefs) { DemoMode = true }) {
                var handle = main.Handle;
                Call(main, "ApplyPreferences");
                var summary = (Label)Get(main, "providerSummary");
                Check(summary.Text.Contains("LibreTranslate") && summary.Text.Contains("Translate button"), "settings summary");
                Invoke(main, "Select", "#osu");
                Check(Caption(main, "incomingButton") == "Polish", "channel incoming");
                Check(Caption(main, "languageButton") == "Japanese", "channel outgoing");
                Check(On(main) == false, "channel auto");
                Invoke(main, "Select", "#russian");
                Check(Caption(main, "incomingButton") == "English", "default incoming");
                Check(Caption(main, "languageButton") == "Russian", "default outgoing");
                Check(On(main), "default auto");
                Invoke(main, "SetIncoming", "de");
                Invoke(main, "SetOutgoing", "ko");
                Click(Get(main, "auto"));
                Invoke(main, "Select", "#osu");
                Check(Caption(main, "incomingButton") == "Polish" && On(main) == false && Caption(main, "languageButton") == "Japanese", "first channel preserved");
                Invoke(main, "Select", "#russian");
                Check(Caption(main, "incomingButton") == "German" && Caption(main, "languageButton") == "Korean" && On(main) == false, "edits preserved");
                var stored = ((AppPreferences)Get(main, "preferences")).FindChannel("#russian");
                Check(stored != null && stored.IncomingLanguage == "de" && stored.OutgoingLanguage == "ko" && stored.AutoTranslate == false, "channel record");
                var original = ((AppPreferences)Get(main, "preferences")).FindChannel("#osu");
                Check(original != null && original.IncomingLanguage == "pl" && original.OutgoingLanguage == "ja" && original.AutoTranslate == false, "other channel record");
            }
        }
        static string Caption(object main, string field) {
            object button = Get(main, field);
            return (string)button.GetType().GetProperty("Caption").GetValue(button, null);
        }
        static bool On(object main) {
            object box = Get(main, "auto");
            return (bool)box.GetType().GetProperty("On").GetValue(box, null);
        }
        static void Click(object control) {
            control.GetType().GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(EventArgs) }, null).Invoke(control, new object[] { EventArgs.Empty });
        }
        static void Invoke(object target, string name, params object[] args) {
            var types = new Type[args.Length];
            for (int i = 0; i < args.Length; i++) types[i] = args[i] == null ? typeof(object) : args[i].GetType();
            var method = target.GetType().GetMethod(name, Members, null, types, null);
            if (method == null) throw new MissingMethodException(name);
            method.Invoke(target, args);
        }
    }
}
