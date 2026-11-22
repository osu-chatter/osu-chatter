using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Web.Script.Serialization;

namespace Chatter {
    public static class LocalModelManager {
        public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "osu-chatter", "local-ai");
        static readonly SemaphoreSlim installing = new SemaphoreSlim(1, 1);
        public static ExperienceOptions Install(ExperienceOptions options, Action<string> status, CancellationToken cancellation = default) {
            if (!installing.Wait(0)) throw new InvalidOperationException("A model download is already running.");
            try {
                string destination = string.IsNullOrWhiteSpace(options.DownloadDirectory) ? DirectoryPath : Path.GetFullPath(options.DownloadDirectory);
                bool quality = options.ModelVariant == "quality";
                string modelFile = quality ? "hy-mt2-7b-q4.gguf" : "hy-mt2-1.8b-q4.gguf";
                var drive = new DriveInfo(Path.GetPathRoot(destination));
                if (!File.Exists(Path.Combine(destination, modelFile)) && drive.AvailableFreeSpace < (quality ? 6500L : 1500L) * 1024 * 1024)
                    throw new InvalidOperationException("The selected model needs " + (quality ? "6.5" : "1.5") + " GB of free space. Choose another download folder in Settings → Models.");
                Directory.CreateDirectory(destination);
                string runtimeDir = Path.Combine(destination, "engine"); Directory.CreateDirectory(runtimeDir);
                string executable = Path.Combine(runtimeDir, "llama-server.exe");
                if (!File.Exists(executable)) {
                    status("Finding the Windows CPU engine…");
                    var release = Json("https://api.github.com/repos/ggml-org/llama.cpp/releases/latest");
                    var firstAssets = ((IList)release["assets"]).Cast<Dictionary<string, object>>().ToArray();
                    if (!firstAssets.Any(a => ((string)a["name"]).EndsWith("bin-win-cpu-x64.zip", StringComparison.OrdinalIgnoreCase))) {
                        var marker = firstAssets.FirstOrDefault(a => (string)a["name"] == "nightly-tag.txt");
                        if (marker == null || !(marker["digest"] is string markerHash) || !markerHash.StartsWith("sha256:")) throw new InvalidOperationException("The official engine build marker is unavailable.");
                        string markerFile = Path.Combine(destination, "nightly-tag.txt");
                        if (File.Exists(markerFile)) File.Delete(markerFile);
                        Download((string)marker["browser_download_url"], markerFile, markerHash.Substring(7), status, cancellation);
                        string tag = File.ReadAllText(markerFile).Trim();
                        if (!System.Text.RegularExpressions.Regex.IsMatch(tag, @"^b[0-9]{3,8}$")) throw new InvalidOperationException("Invalid engine build marker.");
                        release = Json("https://api.github.com/repos/ggml-org/llama.cpp/releases/tags/" + tag);
                    }
                    var assets = ((IList)release["assets"]).Cast<Dictionary<string, object>>();
                    var asset = assets.FirstOrDefault(a => ((string)a["name"]).EndsWith("bin-win-cpu-x64.zip", StringComparison.OrdinalIgnoreCase));
                    if (asset == null || !asset.TryGetValue("digest", out object hash) || !(hash is string digest) || !digest.StartsWith("sha256:"))
                        throw new InvalidOperationException("The engine release has no verified Windows CPU download. Choose an installed llama-server.exe instead.");
                    string zip = Path.Combine(destination, "engine.zip");
                    Download((string)asset["browser_download_url"], zip, digest.Substring(7), status, cancellation);
                    using (var archive = ZipFile.OpenRead(zip)) foreach (var entry in archive.Entries) {
                        cancellation.ThrowIfCancellationRequested();
                        // Only executable/runtime files at a flattened, known destination; no zip paths are trusted.
                        if (entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || entry.Name.Equals("llama-server.exe", StringComparison.OrdinalIgnoreCase) || entry.Name.StartsWith("LICENSE", StringComparison.OrdinalIgnoreCase) || entry.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) {
                            string target = Path.Combine(runtimeDir, Path.GetFileName(entry.Name));
                            entry.ExtractToFile(target, true);
                        }
                    }
                    File.Delete(zip);
                    if (!File.Exists(executable)) throw new InvalidOperationException("The downloaded engine did not contain llama-server.exe.");
                    File.WriteAllText(Path.Combine(runtimeDir, "VERSION.txt"), (string)release["tag_name"]);
                }
                string model = Path.Combine(destination, modelFile);
                if (!File.Exists(model)) {
                    status("Finding the Hy-MT2 translation model…");
                    string repo = quality ? "tencent/Hy-MT2-7B-GGUF" : "tencent/Hy-MT2-1.8B-GGUF";
                    var manifest = Json("https://huggingface.co/api/models/" + repo + "?blobs=true");
                    var file = ((IList)manifest["siblings"]).Cast<Dictionary<string, object>>().FirstOrDefault(f =>
                        ((string)f["rfilename"]).EndsWith("Q4_K_M.gguf", StringComparison.OrdinalIgnoreCase));
                    if (file == null || !(file["lfs"] is Dictionary<string, object> lfs)) throw new InvalidOperationException("The model has no verified Q4 download.");
                    string revision = (string)manifest["sha"], filename = (string)file["rfilename"];
                    Download("https://huggingface.co/" + repo + "/resolve/" + revision + "/" + filename, model, (string)lfs["sha256"], status, cancellation);
                    File.WriteAllText(Path.Combine(destination, "MODEL.txt"), repo + "\n" + revision + "\nApache-2.0\n" + (string)lfs["sha256"]);
                }
                var result = options.Copy(); result.DownloadDirectory = destination; result.RuntimePath = executable; result.ModelPath = model;
                status("Local AI engine and model are ready."); return result;
            } finally { installing.Release(); }
        }
        static Dictionary<string, object> Json(string url) {
            var request = (HttpWebRequest)WebRequest.Create(url); request.UserAgent = "osu-chatter/1.0"; request.Timeout = 30000;
            using (var response = request.GetResponse()) using (var reader = new StreamReader(response.GetResponseStream()))
                return new JavaScriptSerializer { MaxJsonLength = 4000000 }.Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
        }
        static void Download(string url, string path, string expected, Action<string> status, CancellationToken cancellation) {
            if (expected == null || expected.Length != 64 || expected.Any(c => !Uri.IsHexDigit(c))) throw new InvalidOperationException("Missing download checksum.");
            if (File.Exists(path)) {
                using (var input = File.OpenRead(path)) using (var sha = SHA256.Create())
                    if (BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").Equals(expected, StringComparison.OrdinalIgnoreCase)) return;
                File.Delete(path);
            }
            string partial = path + ".download";
            try {
                var request = (HttpWebRequest)WebRequest.Create(url); request.UserAgent = "osu-chatter/1.0"; request.Timeout = 30000; request.ReadWriteTimeout = 30000;
                using (var response = request.GetResponse()) using (var input = response.GetResponseStream()) using (var output = File.Create(partial)) {
                    var buffer = new byte[1024 * 128]; int read; long total = 0; DateTime next = DateTime.MinValue;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0) {
                        cancellation.ThrowIfCancellationRequested(); output.Write(buffer, 0, read); total += read;
                        if (total > 7L * 1024 * 1024 * 1024) throw new InvalidOperationException("Download exceeded its size limit.");
                        if (DateTime.UtcNow > next) { status("Downloading · " + (total / 1048576) + " MB" + (response.ContentLength > 0 ? " / " + (response.ContentLength / 1048576) + " MB" : "")); next = DateTime.UtcNow.AddMilliseconds(500); }
                    }
                }
                status("Verifying download…");
                using (var input = File.OpenRead(partial)) using (var sha = SHA256.Create()) {
                    string actual = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "");
                    if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Download checksum failed. Retry the download.");
                }
                File.Move(partial, path);
            } finally { if (File.Exists(partial)) File.Delete(partial); }
        }
        public static long DiskBytes(string? directory = null) {
            string path = string.IsNullOrWhiteSpace(directory) ? DirectoryPath : directory!;
            if (!Directory.Exists(path)) return 0;
            return new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        }
    }
}
