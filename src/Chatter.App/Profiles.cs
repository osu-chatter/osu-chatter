using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading;
using System.Windows.Forms;
using Chatter;

namespace Chatter.App {
    public sealed class PlayerFace {
        public PlayerFace(int id, bool supporter, Image? image) {
            Id = id;
            Supporter = supporter;
            Image = image;
        }

        public int Id { get; }
        public bool Supporter { get; }
        public Image? Image { get; }
    }

    /// <summary>
    /// Resolves names to a user id, supporter flag, and picture.
    /// Pictures come from osu!'s Data\a cache, then from https://a.ppy.sh/{id}. Nothing is written to disk.
    /// </summary>
    sealed class PlayerDirectory : IDisposable {
        const int MaxBytes = 4 * 1024 * 1024;
        const int Batch = 24;
        const int CacheLimit = 64;

        readonly Control ui;
        readonly EventHandler onFrame;
        readonly object gate = new object();
        readonly Dictionary<string, PlayerFace> remembered = new Dictionary<string, PlayerFace>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, PlayerFace> shown = new Dictionary<string, PlayerFace>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<int, Slot> slots = new Dictionary<int, Slot>();
        readonly List<int> order = new List<int>();
        PresenceIndex? index;
        DateTime presenceWrite = DateTime.MinValue;
        DateTime presenceRetry = DateTime.MinValue;
        Work? pending;
        bool running;
        bool disposed;
        int generation;
        int applied;
        int cachedPid = -1;
        string? cachedDir;

        public event Action? Changed;

        public PlayerDirectory(Control ui) {
            this.ui = ui;
            onFrame = OnFrame;
        }

        public PlayerFace? Face(string? name) {
            string key = Key(name);
            if (key.Length == 0) return null;
            lock (gate) {
                PlayerFace face;
                if (remembered.TryGetValue(key, out face)) return face;
                if (shown.TryGetValue(key, out face)) return face;
            }
            return null;
        }

        public void Remember(string name, int id, bool supporter, Image? image) {
            string key = Key(name);
            if (key.Length == 0) return;
            lock (gate) remembered[key] = new PlayerFace(id, supporter, image);
            Raise();
        }

        public void Watch(IList<string>? names, string? selfName, int selfId) {
            if (disposed) return;
            var copy = new List<string>();
            if (names != null) {
                for (int i = 0; i < names.Count; i++) if (!string.IsNullOrEmpty(names[i])) copy.Add(names[i]);
            }
            string signature = Signature(copy, selfName, selfId);
            lock (gate) {
                if (signature == pendingSignature && (DateTime.UtcNow - queuedAt).TotalSeconds < 5) return;
                pendingSignature = signature;
                queuedAt = DateTime.UtcNow;
                pending = new Work { Names = copy, SelfName = selfName ?? "", SelfId = selfId, Ticket = Interlocked.Increment(ref generation) };
                if (running) return;
                running = true;
            }
            ThreadPool.QueueUserWorkItem(delegate { Pump(); });
        }

        string pendingSignature = "";
        DateTime queuedAt = DateTime.MinValue;

        public void Dispose() {
            if (disposed) return;
            disposed = true;
            int cutoff = Interlocked.Increment(ref generation);
            List<Slot> owned;
            lock (gate) {
                applied = cutoff;
                running = false;
                pending = null;
                owned = new List<Slot>(slots.Values);
                slots.Clear();
                order.Clear();
                shown.Clear();
            }
            for (int i = 0; i < owned.Count; i++) owned[i].Release(onFrame);
        }

        void Pump() {
            for (;;) {
                Work? work;
                lock (gate) {
                    work = pending;
                    pending = null;
                    if (work == null || disposed) { running = false; return; }
                }
                try { Load(work); }
                catch { }
            }
        }

        void Load(Work work) {
            if (Stale(work.Ticket)) return;
            string? dir = LocateOsu();
            if (dir != null && dir.Length > 0) RefreshPresence(dir);
            if (Stale(work.Ticket)) return;
            PresenceIndex? current;
            lock (gate) current = index;
            var people = new List<Person>();
            var wanted = new List<int>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Consider(work.SelfName, work.SelfName, work.SelfId, current, people, wanted, seen);
            for (int i = 0; i < work.Names.Count; i++) Consider(work.Names[i], work.SelfName, work.SelfId, current, people, wanted, seen);
            if (Stale(work.Ticket)) return;
            Post(delegate { Apply(work.Ticket, people, new List<Payload>()); });
            var payloads = new List<Payload>();
            for (int i = 0; i < wanted.Count && payloads.Count < Batch; i++) {
                if (Stale(work.Ticket)) return;
                int id = wanted[i];
                string? localPath = string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "Data", "a", id.ToString(CultureInfo.InvariantCulture));
                bool local = localPath != null && File.Exists(localPath);
                byte[]? bytes = local ? ReadLocal(localPath!) : Download(id);
                payloads.Add(new Payload { Id = id, Bytes = bytes, LocalMiss = local && bytes == null });
            }
            if (Stale(work.Ticket) || payloads.Count == 0) return;
            Post(delegate { Apply(work.Ticket, people, payloads); });
        }

        void Consider(string? name, string selfName, int selfId, PresenceIndex? current, List<Person> people, List<int> wanted, HashSet<string> seen) {
            string key = Key(name);
            if (key.Length == 0 || !seen.Add(key)) return;
            int id = 0;
            bool supporter = false;
            if (current != null) current.TryFind(key, out id, out supporter);
            bool self = selfId > 0 && key.Equals(Key(selfName), StringComparison.OrdinalIgnoreCase);
            if (self && id <= 0) id = selfId;
            if (id > 0 && !supporter && current != null && current.IsSupporter(id)) supporter = true;
            people.Add(new Person { Key = key, Id = id, Supporter = supporter });
            if (id > 0 && wanted.Count < Batch && !wanted.Contains(id) && Need(id)) wanted.Add(id);
        }

        bool Need(int id) {
            lock (gate) {
                Slot slot;
                if (!slots.TryGetValue(id, out slot)) return true;
                if (slot.Image != null || slot.Ready) return false;
                return slot.Retry <= DateTime.UtcNow;
            }
        }

        void Apply(int ticket, List<Person> people, List<Payload> payloads) {
            if (disposed || ticket < applied) return;
            applied = ticket;
            var retain = new HashSet<int>();
            for (int i = 0; i < people.Count; i++) if (people[i].Id > 0) retain.Add(people[i].Id);
            for (int i = 0; i < payloads.Count; i++) {
                Payload payload = payloads[i];
                if (payload.Bytes == null) {
                    Hold(payload.Id, payload.LocalMiss);
                    continue;
                }
                var stream = new MemoryStream(payload.Bytes);
                try {
                    Image image = Image.FromStream(stream);
                    Keep(payload.Id, image, stream, retain);
                } catch {
                    stream.Dispose();
                    Hold(payload.Id, payload.LocalMiss);
                }
            }
            bool changed = payloads.Count > 0;
            lock (gate) {
                for (int i = 0; i < people.Count; i++) {
                    Person person = people[i];
                    Image? image = null;
                    Slot slot;
                    if (person.Id > 0 && slots.TryGetValue(person.Id, out slot)) image = slot.Image;
                    var face = new PlayerFace(person.Id, person.Supporter, image);
                    PlayerFace previous;
                    if (!shown.TryGetValue(person.Key, out previous) || previous.Id != face.Id || previous.Supporter != face.Supporter || !ReferenceEquals(previous.Image, face.Image)) {
                        shown[person.Key] = face;
                        changed = true;
                    }
                }
            }
            if (changed) Raise();
        }

        void Hold(int id, bool permanent) {
            lock (gate) {
                Slot slot;
                if (!slots.TryGetValue(id, out slot)) {
                    slot = new Slot();
                    slots[id] = slot;
                    order.Add(id);
                }
                slot.Ready = permanent;
                slot.Retry = permanent ? DateTime.MaxValue : DateTime.UtcNow.AddMinutes(2);
            }
        }

        void Keep(int id, Image image, MemoryStream stream, HashSet<int> retain) {
            bool animate = false;
            try { animate = image.GetFrameCount(FrameDimension.Time) > 1; }
            catch { animate = false; }
            Slot? previous = null;
            lock (gate) {
                if (slots.TryGetValue(id, out previous)) order.Remove(id);
                while (slots.Count >= CacheLimit) {
                    int victim = -1;
                    for (int i = 0; i < order.Count; i++) {
                        if (order[i] != id && !retain.Contains(order[i])) { victim = order[i]; break; }
                    }
                    if (victim < 0) break;
                    order.Remove(victim);
                    Slot gone;
                    if (!slots.TryGetValue(victim, out gone)) continue;
                    slots.Remove(victim);
                    gone.Release(onFrame);
                }
                var slot = new Slot { Image = image, Stream = stream, Animated = animate, Ready = true };
                slots[id] = slot;
                order.Add(id);
                if (animate) ImageAnimator.Animate(image, onFrame);
            }
            if (previous != null) previous.Release(onFrame);
        }

        void OnFrame(object? sender, EventArgs e) {
            if (disposed) return;
            try {
                if (!ui.IsDisposed && ui.IsHandleCreated) ui.BeginInvoke(new Action(Raise));
            } catch (InvalidOperationException) { }
        }

        void Raise() {
            if (!disposed && Changed != null) Changed();
        }

        bool Stale(int ticket) {
            return disposed || ticket != Volatile.Read(ref generation);
        }

        void RefreshPresence(string dir) {
            string path = Path.Combine(dir, "presence.db");
            if (!File.Exists(path)) return;
            DateTime write;
            try { write = File.GetLastWriteTimeUtc(path); }
            catch { return; }
            lock (gate) {
                if (index != null && write == presenceWrite) return;
                if (DateTime.UtcNow < presenceRetry) return;
            }
            try {
                PresenceIndex next = PresenceIndex.Read(path);
                lock (gate) {
                    index = next;
                    presenceWrite = write;
                    presenceRetry = DateTime.MinValue;
                }
            } catch {
                lock (gate) presenceRetry = DateTime.UtcNow.AddSeconds(30);
            }
        }

        string? LocateOsu() {
            int pid = 0;
            try { pid = ProcessMemory.FindOsu(); }
            catch { pid = 0; }
            if (pid == cachedPid) return cachedDir;
            string? dir = pid == 0 ? null : ModuleDirectory(pid);
            if (string.IsNullOrEmpty(dir)) dir = LocalFallback();
            cachedPid = pid;
            cachedDir = dir;
            return dir;
        }

        static string? ModuleDirectory(int pid) {
            try {
                using (var process = Process.GetProcessById(pid)) {
                    ProcessModule? module = process.MainModule;
                    if (module == null || string.IsNullOrEmpty(module.FileName)) return null;
                    return Path.GetDirectoryName(module.FileName);
                }
            } catch {
                return null;
            }
        }

        static string? LocalFallback() {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "osu!");
            return Directory.Exists(dir) ? dir : null;
        }

        static byte[]? ReadLocal(string path) {
            try {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
                    if (stream.Length <= 0 || stream.Length > MaxBytes) return null;
                    var data = new byte[stream.Length];
                    int read = 0;
                    while (read < data.Length) {
                        int n = stream.Read(data, read, data.Length - read);
                        if (n <= 0) return null;
                        read += n;
                    }
                    return data;
                }
            } catch {
                return null;
            }
        }

        static byte[]? Download(int id) {
            if (id <= 0) return null;
            try {
                var request = (HttpWebRequest)WebRequest.Create("https://a.ppy.sh/" + id.ToString(CultureInfo.InvariantCulture));
                request.Method = "GET";
                request.Timeout = 8000;
                request.ReadWriteTimeout = 8000;
                request.AllowAutoRedirect = true;
                request.MaximumAutomaticRedirections = 3;
                request.UserAgent = "osu-chatter";
                request.Accept = "image/png,image/gif,image/jpeg,image/*";
                using (var response = (HttpWebResponse)request.GetResponse()) {
                    if (response.StatusCode != HttpStatusCode.OK) return null;
                    string type = response.ContentType ?? "";
                    int semi = type.IndexOf(';');
                    if (semi >= 0) type = type.Substring(0, semi);
                    if (type.Trim().StartsWith("text/", StringComparison.OrdinalIgnoreCase)) return null;
                    using (Stream? stream = response.GetResponseStream()) {
                        if (stream == null) return null;
                        var buffer = new MemoryStream();
                        var chunk = new byte[8192];
                        int read;
                        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0) {
                            if (buffer.Length + read > MaxBytes) return null;
                            buffer.Write(chunk, 0, read);
                        }
                        return buffer.Length == 0 ? null : buffer.ToArray();
                    }
                }
            } catch {
                return null;
            }
        }

        void Post(Action action) {
            try {
                if (disposed || ui.IsDisposed || !ui.IsHandleCreated) return;
                ui.BeginInvoke(action);
            } catch (InvalidOperationException) { }
        }

        static string Signature(List<string> names, string? selfName, int selfId) {
            var keys = new List<string>();
            for (int i = 0; i < names.Count; i++) {
                string key = Key(names[i]);
                if (key.Length > 0) keys.Add(key);
            }
            keys.Sort(StringComparer.OrdinalIgnoreCase);
            var builder = new System.Text.StringBuilder();
            builder.Append(selfId).Append('\n').Append(Key(selfName)).Append('\n');
            string previous = "";
            for (int i = 0; i < keys.Count; i++) {
                if (keys[i].Equals(previous, StringComparison.OrdinalIgnoreCase)) continue;
                builder.Append(keys[i]).Append('\n');
                previous = keys[i];
            }
            return builder.ToString();
        }

        static string Key(string? name) {
            return ChatRules.NormalizeName(name ?? "").Trim();
        }

        sealed class Work {
            public List<string> Names = new List<string>();
            public string SelfName = "";
            public int SelfId;
            public int Ticket;
        }

        sealed class Person {
            public string Key = "";
            public int Id;
            public bool Supporter;
        }

        sealed class Payload {
            public int Id;
            public byte[]? Bytes;
            public bool LocalMiss;
        }

        sealed class Slot {
            public Image? Image;
            public Stream? Stream;
            public bool Animated;
            public bool Ready;
            public DateTime Retry;

            public void Release(EventHandler handler) {
                Image? image = Image;
                Image = null;
                if (image != null) {
                    if (Animated) {
                        try { ImageAnimator.StopAnimate(image, handler); }
                        catch { }
                    }
                    image.Dispose();
                }
                Stream? stream = Stream;
                Stream = null;
                if (stream != null) stream.Dispose();
            }
        }
    }
}
