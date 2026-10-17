using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Chatter {
    /// <summary>One private worker, serialized requests, no chat text on disk.</summary>
    public sealed class LocalTranslator : IDisposable {
        readonly object gate = new object();
        readonly CancellationTokenSource stopping = new CancellationTokenSource();
        static readonly string[] RuntimeSettings = LoadRuntimeSettings();
        Process worker;
        Process active;
        public event Action<string> Status;
        public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "osu-chatter", "translator-v1");
        static string PythonDirectory => Path.Combine(DataDirectory, "python-" + RuntimeSettings[0]);
        static string PythonPath => Path.Combine(PythonDirectory, RuntimeSettings[1]);

        void Report(string value) { Status?.Invoke(value); }

        public void Prepare() {
            lock (gate) {
                try { EnsureWorker(); }
                catch {
                    Stop(active);
                    worker?.Dispose();
                    worker = null;
                    active = null;
                    throw;
                } finally { Report(""); }
            }
        }

        void EnsureWorker() {
            stopping.Token.ThrowIfCancellationRequested();
            if (worker != null && !worker.HasExited) return;
            worker?.Dispose();
            worker = null;
            Directory.CreateDirectory(DataDirectory);
            WriteResource("Chatter.Translation.setup.ps1", "setup.ps1");
            WriteResource("Chatter.Translation.worker.py", "worker.py");
            WriteResource("Chatter.Translation.requirements.txt", "requirements.txt");
            WriteResource("Chatter.Translation.runtime.txt", "runtime.txt");
            {
                Report("Checking local translation setup…");
                var info = Info(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
                    "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + Path.Combine(DataDirectory, "setup.ps1") + "\" -Root \"" + DataDirectory + "\"");
                using (var setup = Process.Start(info)) {
                    active = setup;
                    if (stopping.IsCancellationRequested) { Stop(setup); stopping.Token.ThrowIfCancellationRequested(); }
                    setup.ErrorDataReceived += delegate { }; // Never retain process diagnostics containing input.
                    setup.BeginErrorReadLine();
                    ReadUntil(setup, line => { Report(line); return false; }, TimeSpan.FromMinutes(20), false);
                    if (!setup.WaitForExit(5000) || setup.ExitCode != 0)
                        throw new InvalidOperationException("Translation setup failed. Check your connection and retry. Setup details are in " + Path.Combine(DataDirectory, "setup.log"));
                    active = null;
                }
            }
            Report("Starting local translation…");
            worker = Process.Start(Info(PythonPath, "-I -u \"" + Path.Combine(DataDirectory, "worker.py") + "\""));
            active = worker;
            if (stopping.IsCancellationRequested) { Stop(worker); stopping.Token.ThrowIfCancellationRequested(); }
            worker.ErrorDataReceived += delegate { };
            worker.BeginErrorReadLine();
            ReadUntil(worker, line => line.Trim() == "{\"ready\": true}", TimeSpan.FromMinutes(2), true);
            Report("");
        }

        public string Translate(TranslationRequest request) {
            lock (gate) {
                try {
                    EnsureWorker();
                    // Frameworks StandardInput writer follows the Windows console code page
                    // Write UTF8 bytes explicitly so Cyrillic and Korean survive
                    byte[] payload = Encoding.UTF8.GetBytes("{\"text\":" + Translator.Json(request.Text) + ",\"source\":" + Translator.Json(request.Source) + ",\"target\":" + Translator.Json(request.Target) + "}\n");
                    worker.StandardInput.BaseStream.Write(payload, 0, payload.Length);
                    worker.StandardInput.BaseStream.Flush();
                    string result = null;
                    ReadUntil(worker, line => {
                        string status = Translator.JsonString(line, "status");
                        if (status != null) { Report(status); return false; }
                        string error = Translator.JsonString(line, "error");
                        if (error != null) throw new InvalidOperationException(error);
                        result = Translator.JsonString(line, "translatedText");
                        return result != null;
                    }, TimeSpan.FromMinutes(20), true);
                    if (result == null || Encoding.UTF8.GetByteCount(result) > 16384)
                        throw new InvalidOperationException("Invalid local translation response");
                    return result;
                } catch {
                    Stop(active);
                    worker?.Dispose();
                    worker = null;
                    active = null;
                    throw;
                } finally { Report(""); }
            }
        }

        void ReadUntil(Process process, Func<string, bool> accept, TimeSpan timeout, bool requireResult) {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (true) {
                Task<string> read = process.StandardOutput.ReadLineAsync();
                while (!read.Wait(200)) {
                    if (stopping.IsCancellationRequested || DateTime.UtcNow > deadline) {
                        Stop(process);
                        stopping.Token.ThrowIfCancellationRequested();
                        throw new InvalidOperationException("Local translation timed out. Retry when your connection is ready.");
                    }
                }
                string line = read.Result;
                if (line == null) {
                    if (requireResult) throw new InvalidOperationException("The local translator stopped. Retry to restart it.");
                    return;
                }
                if (line.Length > 65536) throw new InvalidOperationException("Invalid local translation response");
                if (accept(line)) return;
            }
        }

        static ProcessStartInfo Info(string file, string arguments) {
            var info = new ProcessStartInfo(file, arguments) {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = DataDirectory,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            info.EnvironmentVariables["PYTHONUTF8"] = "1";
            info.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            info.EnvironmentVariables["CHATTER_LANGUAGES"] = string.Join(",", ChatRules.Languages);
            return info;
        }

        static void WriteResource(string name, string file) {
            using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)) {
                if (source == null) throw new InvalidOperationException("Translation runtime is missing from this build");
                using (var target = File.Create(Path.Combine(DataDirectory, file))) source.CopyTo(target);
            }
        }

        static string[] LoadRuntimeSettings() {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Chatter.Translation.runtime.txt")) {
                if (stream == null) throw new InvalidOperationException("Translation runtime settings are missing from this build");
                using (var reader = new StreamReader(stream, Encoding.UTF8)) {
                    string[] settings = reader.ReadToEnd().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    if (settings.Length != 5 || !System.Text.RegularExpressions.Regex.IsMatch(settings[0], @"^\d+\.\d+\.\d+$")
                        || settings[1] != "python.exe"
                        || !System.Text.RegularExpressions.Regex.IsMatch(settings[2], @"^ready-v\d+$")
                        || !System.Text.RegularExpressions.Regex.IsMatch(settings[3], @"^ready-v\d+$")
                        || !System.Text.RegularExpressions.Regex.IsMatch(settings[4], @"^setup-v\d+$"))
                        throw new InvalidOperationException("Translation runtime settings are invalid");
                    return settings;
                }
            }
        }

        static void Stop(Process process) {
            if (process == null) return;
            try {
                if (process.HasExited) return;
                // Kill only this owned process and its setup/worker descendants.
                using (var kill = Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "taskkill.exe"), "/PID " + process.Id + " /T /F") {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
                })) { kill.WaitForExit(5000); }
            } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
        }

        public void Dispose() {
            stopping.Cancel();
            Stop(active);
        }
    }
}
