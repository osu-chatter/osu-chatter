using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
namespace Chatter {
    public enum CompatibilityCode { NotRunning, AccessDenied, ChatChanged, WaitingForChat, UnsupportedLayout, UnsupportedArchitecture, AmbiguousSignature }
    public sealed class CompatibilityException : InvalidOperationException {
        public CompatibilityCode Code { get; }
        public CompatibilityException(CompatibilityCode code, string message) : base(message) { Code = code; }
    }
    public static class StableClientInfo {
        public static string Describe() {
            int pid = ProcessMemory.FindOsu(); if (pid == 0) return "osu!stable is not running.";
            using (var process = Process.GetProcessById(pid)) {
                string path = process.MainModule.FileName;
                using (var stream = File.OpenRead(path)) using (var hash = SHA256.Create()) {
                    string fingerprint = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
                    bool tested = fingerprint == Shared.StableCompatibilityProfile.TestedSha256;
                    var version = StableVersionReader.Inspect(pid, path);
                    return "Executable: " + path + "\nFile version: " + FileVersionInfo.GetVersionInfo(path).FileVersion + "\nSHA-256: " + fingerprint + "\nFingerprint: " + (tested ? "matches the locally tested executable" : "unverified executable")
                        + "\nIntegration profile: " + Shared.StableCompatibilityProfile.Id + "\nArchitecture: x86 required\nGame build: " + (version.Build == null ? "unknown" : "b " + version.Build)
                        + "\nBuild source: " + version.Source + "\nConfigured release stream: " + (version.Stream ?? "unknown") + "\nVersion identification is not functional compatibility certification.\nReader validates the chat graph; sender and commands also validate reflected members.";
                }
            }
        }
    }
}
