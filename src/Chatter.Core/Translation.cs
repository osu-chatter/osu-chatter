using System;
using System.IO;
using System.Net;
using System.Text;

namespace Chatter {
    public sealed class TranslationRequest {
        public string Endpoint;
        public string ApiKey;
        public string Text;
        public string Source;
        public string Target;

        public void Validate() {
            ApiKey = ApiKey ?? "";
            Source = string.IsNullOrEmpty(Source) ? "auto" : Source;
            int textBytes = Text == null ? 0 : Encoding.UTF8.GetByteCount(Text);
            if (string.IsNullOrEmpty(Text) || textBytes > 8192 || ApiKey.Length > 512
                || Array.IndexOf(ChatRules.Languages, Target) < 0
                || (Source != "auto" && Array.IndexOf(ChatRules.Languages, Source) < 0))
                throw new InvalidOperationException("Invalid translation request");
            Uri url;
            if (Translator.IsDeepL(Endpoint)) {
                if (string.IsNullOrWhiteSpace(ApiKey) || ChatRules.HasControl(ApiKey) || ApiKey.IndexOf(' ') >= 0)
                    throw new InvalidOperationException("Enter your DeepL API key and choose its Free or Pro plan.");
                return;
            }
            if (Endpoint == ChatRules.LocalTranslator) return;
            if (string.IsNullOrEmpty(Endpoint) || Endpoint.Length > 2048 || !Uri.TryCreate(Endpoint, UriKind.Absolute, out url))
                throw new InvalidOperationException("Invalid translation server");
            if (!string.IsNullOrEmpty(url.UserInfo) || !string.IsNullOrEmpty(url.Fragment))
                throw new InvalidOperationException("Use HTTPS, or HTTP on localhost");
            string host = url.Host;
            bool local = host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || host == "127.0.0.1" || host == "::1";
            if (!(url.Scheme == "https" || (url.Scheme == "http" && local)))
                throw new InvalidOperationException("Use HTTPS, or HTTP on localhost");
        }
    }

    public sealed class Translator : IDisposable {
        public const string DeepLFree = "deepl-free";
        public const string DeepLPro = "deepl-pro";
        public static bool IsDeepL(string endpoint) => endpoint == DeepLFree || endpoint == DeepLPro;
        readonly Func<string, HttpWebRequest> createRequest;
        public Translator() : this(url => (HttpWebRequest)WebRequest.Create(url)) { }
        internal Translator(Func<string, HttpWebRequest> factory) { createRequest = factory; }
        readonly LocalTranslator local = new LocalTranslator();
        public event Action<string> Status { add { local.Status += value; } remove { local.Status -= value; } }
        public void PrepareLocal() { local.Prepare(); }
        public void Dispose() { local.Dispose(); }
        public string Translate(TranslationRequest request) {
            if (request == null) throw new InvalidOperationException("Invalid translation request");
            request.Validate();
            if (request.Source == request.Target) return request.Text;
            if (request.Endpoint == ChatRules.LocalTranslator) return local.Translate(request);
            bool deepL = IsDeepL(request.Endpoint);
            var payload = new StringBuilder();
            if (deepL) {
                payload.Append("{\"text\":[").Append(Json(request.Text)).Append("],\"target_lang\":").Append(Json(DeepLLanguage(request.Target, true)));
                if (request.Source != "auto") payload.Append(",\"source_lang\":").Append(Json(DeepLLanguage(request.Source, false)));
                payload.Append('}');
            } else {
                payload.Append("{\"q\":").Append(Json(request.Text));
                payload.Append(",\"source\":").Append(Json(request.Source));
                payload.Append(",\"target\":").Append(Json(request.Target));
                payload.Append(",\"format\":\"text\",\"api_key\":").Append(Json(request.ApiKey)).Append('}');
            }
            var http = createRequest(deepL ? (request.Endpoint == DeepLFree ? "https://api-free.deepl.com/v2/translate" : "https://api.deepl.com/v2/translate") : request.Endpoint);
            if (deepL) http.Headers[HttpRequestHeader.Authorization] = "DeepL-Auth-Key " + request.ApiKey;
            http.Method = "POST";
            http.ContentType = "application/json; charset=utf-8";
            http.Accept = "application/json";
            http.UserAgent = "chatter";
            http.Timeout = 30000;
            http.ReadWriteTimeout = 30000;
            http.AllowAutoRedirect = false;
            http.ServicePoint.Expect100Continue = false;
            byte[] body = Encoding.UTF8.GetBytes(payload.ToString());
            http.ContentLength = body.Length;
            try {
                using (Stream stream = http.GetRequestStream()) stream.Write(body, 0, body.Length);
                using (var response = (HttpWebResponse)http.GetResponse())
                    return ReadTranslation(response, deepL);
            } catch (WebException ex) {
                var response = ex.Response as HttpWebResponse;
                if (response == null) throw new InvalidOperationException(deepL ? "Cannot reach DeepL. Check your internet connection and try again." : OfflineMessage);
                using (response) {
                    if (response.StatusCode == HttpStatusCode.OK) return ReadTranslation(response, deepL);
                    if (deepL) throw new InvalidOperationException(DeepLError((int)response.StatusCode));
                    string detail = ReadError(response);
                    if (!string.IsNullOrEmpty(detail)) throw new InvalidOperationException(detail);
                    throw new InvalidOperationException("Translation failed. Check the language and the server.");
                }
            }
        }

        public const string OfflineMessage = "The translator is not running.";

        internal static string DeepLLanguage(string id, bool target) {
            if (target && id == "en") return "EN-US";
            if (target && id == "pt") return "PT-PT";
            return id.ToUpperInvariant();
        }

        internal static string DeepLError(int status) {
            switch (status) {
                case 401: case 403: return "DeepL rejected the API key. Check the key and Free/Pro plan in translation preferences.";
                case 456: return "DeepL's character limit has been reached. Check your API account usage.";
                case 429: return "DeepL is receiving too many requests. Wait a moment, then retry.";
                case 400: return "DeepL could not translate this language pair or message.";
                default: return "DeepL could not complete the translation. Try again later.";
            }
        }

        public static bool Available(string endpoint) {
            if (endpoint == ChatRules.LocalTranslator) return true;
            Uri url;
            if (string.IsNullOrEmpty(endpoint) || !Uri.TryCreate(endpoint, UriKind.Absolute, out url)) return false;
            try {
                var http = (HttpWebRequest)WebRequest.Create(url.GetLeftPart(UriPartial.Authority) + "/languages");
                http.Method = "GET";
                http.Timeout = 1500;
                http.AllowAutoRedirect = false;
                http.ServicePoint.Expect100Continue = false;
                using (var response = (HttpWebResponse)http.GetResponse()) {
                    if (response.StatusCode != HttpStatusCode.OK) return false;
                    using (var reader = new StreamReader(response.GetResponseStream())) {
                        char[] buffer = new char[32769];
                        int total = 0, read;
                        while (total < buffer.Length && (read = reader.Read(buffer, total, buffer.Length - total)) > 0) total += read;
                        if (total > 32768) return false;
                        var serializer = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = 32768 };
                        var languages = serializer.DeserializeObject(new string(buffer, 0, total)) as object[];
                        if (languages == null) return false;
                        foreach (var language in languages) {
                            var values = language as System.Collections.Generic.Dictionary<string, object>;
                            if (values != null && values.TryGetValue("code", out var code) && code is string) return true;
                        }
                        return false;
                    }
                }
            } catch (WebException ex) {
                var response = ex.Response as HttpWebResponse;
                response?.Dispose();
                return false;
            } catch {
                return false;
            }
        }

        static string ReadError(HttpWebResponse response) {
            try {
                var buffer = new byte[2048];
                int total = 0;
                using (Stream stream = response.GetResponseStream()) {
                    if (stream == null) return null;
                    while (total < buffer.Length) {
                        int n = stream.Read(buffer, total, buffer.Length - total);
                        if (n == 0) break;
                        total += n;
                    }
                }
                string json = Encoding.UTF8.GetString(buffer, 0, total);
                string error = JsonString(json, "error");
                if (string.IsNullOrEmpty(error) || error.Length > 180) return null;
                return error;
            } catch {
                return null;
            }
        }

        static string ReadTranslation(HttpWebResponse response, bool deepL) {
            if (response.StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException("Translation failed; check the server, language and API key");
            var buffer = new byte[32769];
            int total = 0;
            using (Stream stream = response.GetResponseStream()) {
                if (stream == null) throw new InvalidOperationException("Cannot read translation");
                while (total < buffer.Length) {
                    int n = stream.Read(buffer, total, buffer.Length - total);
                    if (n == 0) break;
                    total += n;
                }
            }
            if (total > 32768) throw new InvalidOperationException("Translation response too large");
            string json = Encoding.UTF8.GetString(buffer, 0, total);
            string text = deepL ? DeepLText(json) : JsonString(json, "translatedText");
            if (text == null || Encoding.UTF8.GetByteCount(text) > 16384)
                throw new InvalidOperationException("Invalid translation response");
            return text;
        }

        internal static string DeepLText(string json) {
            try {
                var values = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = 32768, RecursionLimit = 16 }
                    .Deserialize<System.Collections.Generic.Dictionary<string, object>>(json);
                if (values == null || !values.TryGetValue("translations", out var items) || !(items is System.Collections.IList rows) || rows.Count != 1) return null;
                var row = rows[0] as System.Collections.Generic.Dictionary<string, object>;
                return row != null && row.TryGetValue("text", out var text) ? text as string : null;
            } catch (ArgumentException) { return null; } catch (InvalidOperationException) { return null; }
        }

        public static string Json(string value) {
            var builder = new StringBuilder(value.Length + 2);
            builder.Append('"');
            for (int i = 0; i < value.Length; i++) {
                char c = value[i];
                switch (c) {
                    case '\\': builder.Append("\\\\"); break;
                    case '"': builder.Append("\\\""); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < 0x20) builder.Append("\\u").Append(((int)c).ToString("x4"));
                        else builder.Append(c);
                        break;
                }
            }
            builder.Append('"');
            return builder.ToString();
        }

        public static string JsonString(string json, string name) {
            if (string.IsNullOrEmpty(json) || json.Length > 65536) return null;
            try {
                var serializer = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = 65536, RecursionLimit = 16 };
                var values = serializer.DeserializeObject(json) as System.Collections.Generic.Dictionary<string, object>;
                return values != null && values.TryGetValue(name, out var value) ? value as string : null;
            } catch (ArgumentException) { return null; }
              catch (InvalidOperationException) { return null; }
        }
    }
}
