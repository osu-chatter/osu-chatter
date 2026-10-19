using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Chatter {
    /// <summary>
    /// Reads osu!stable's presence.db (user id, name, and permission bits).
    /// A short or unfamiliar file throws so the caller can keep the previous index.
    /// </summary>
    public sealed class PresenceIndex {
        const int MaxPlayers = 200000;
        const int MaxNameBytes = 64;
        const int Supporter = 4;

        readonly Dictionary<string, Player> byName;
        readonly Dictionary<int, Player> byId;

        PresenceIndex(Dictionary<string, Player> byName, Dictionary<int, Player> byId) {
            this.byName = byName;
            this.byId = byId;
        }

        public static PresenceIndex Read(string path) {
            try {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    return Parse(stream);
            } catch (InvalidDataException) {
                throw;
            } catch (Exception) {
                throw new InvalidDataException("Presence list is incomplete");
            }
        }

        public bool TryFind(string? name, out int id, out bool supporter) {
            id = 0;
            supporter = false;
            string key = Key(name);
            if (key.Length == 0) return false;
            Player player;
            if (!byName.TryGetValue(key, out player)) return false;
            id = player.Id;
            supporter = player.Supporter;
            return true;
        }

        public bool IsSupporter(int id) {
            Player player;
            return id > 0 && byId.TryGetValue(id, out player) && player.Supporter;
        }

        static PresenceIndex Parse(Stream stream) {
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true)) {
                reader.ReadInt32();
                int count = reader.ReadInt32();
                if (count < 0 || count > MaxPlayers) throw new InvalidDataException("Presence list is incomplete");
                var byName = new Dictionary<string, Player>(StringComparer.OrdinalIgnoreCase);
                var byId = new Dictionary<int, Player>();
                for (int i = 0; i < count; i++) {
                    int id = reader.ReadInt32();
                    string name = ReadName(reader);
                    reader.ReadByte();
                    reader.ReadByte();
                    bool supporter = (reader.ReadByte() & ~0xe0 & Supporter) != 0;
                    reader.ReadSingle();
                    reader.ReadSingle();
                    reader.ReadInt32();
                    reader.ReadInt64();
                    string key = Key(name);
                    if (key.Length == 0) continue;
                    var player = new Player(id, supporter);
                    byName[key] = player;
                    if (id > 0) byId[id] = player;
                }
                if (stream.Position != stream.Length) throw new InvalidDataException("Presence list is incomplete");
                return new PresenceIndex(byName, byId);
            }
        }

        static string ReadName(BinaryReader reader) {
            int indicator = reader.ReadByte();
            if (indicator == 0) return "";
            if (indicator != 0x0b) throw new InvalidDataException("Presence list is incomplete");
            int length = ReadLength(reader);
            if (length < 0 || length > MaxNameBytes) throw new InvalidDataException("Presence list is incomplete");
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new InvalidDataException("Presence list is incomplete");
            return Encoding.UTF8.GetString(bytes);
        }

        static int ReadLength(BinaryReader reader) {
            int value = 0;
            int shift = 0;
            for (int i = 0; i < 5; i++) {
                int part = reader.ReadByte();
                value |= (part & 0x7f) << shift;
                if ((part & 0x80) == 0) return value;
                shift += 7;
            }
            throw new InvalidDataException("Presence list is incomplete");
        }

        static string Key(string? name) {
            return ChatRules.NormalizeName(name ?? "").Trim();
        }

        sealed class Player {
            public Player(int id, bool supporter) {
                Id = id;
                Supporter = supporter;
            }

            public int Id { get; }
            public bool Supporter { get; }
        }
    }
}
