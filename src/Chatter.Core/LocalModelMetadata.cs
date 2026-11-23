using System;
using System.IO;
using System.Text;

namespace Chatter {
    public sealed class LocalModelMetadata {
        public string Name { get; private set; } = "";
        public string Architecture { get; private set; } = "";
        // MADLAD conversions commonly carry only "T5" as their general.name.
        // T5 translation models use the MADLAD adapter; other T5 tasks are not supported.
        public string Kind => Architecture == "t5" ? "madlad"
            : Architecture == "plamo3" && Name.IndexOf("translate", StringComparison.OrdinalIgnoreCase) >= 0 ? "plamo" : "chat";
        public static LocalModelMetadata Read(string path) {
            using (var file = File.OpenRead(path)) using (var reader = new BinaryReader(file, Encoding.UTF8)) {
                if (reader.ReadUInt32() != 0x46554747 || reader.ReadUInt32() != 3) throw new InvalidOperationException("Select a GGUF v3 model supported by llama.cpp.");
                reader.ReadUInt64(); ulong count = reader.ReadUInt64();
                if (count > 100000) throw new InvalidOperationException("Invalid GGUF metadata.");
                var result = new LocalModelMetadata();
                for (ulong i = 0; i < count; i++) {
                    string key = ReadString(reader); uint type = reader.ReadUInt32();
                    if ((key == "general.name" || key == "general.architecture") && type == 8) {
                        string value = ReadString(reader);
                        if (key == "general.name") result.Name = value; else result.Architecture = value;
                        if (result.Name.Length > 0 && result.Architecture.Length > 0) return result;
                    } else Skip(reader, type, false);
                }
                if (result.Architecture.Length == 0) throw new InvalidOperationException("The GGUF model has no architecture metadata.");
                return result;
            }
        }
        static string ReadString(BinaryReader reader) {
            ulong size = reader.ReadUInt64();
            if (size > 1048576 || size > (ulong)(reader.BaseStream.Length - reader.BaseStream.Position)) throw new InvalidOperationException("Invalid GGUF string.");
            return Encoding.UTF8.GetString(reader.ReadBytes((int)size));
        }
        static void Skip(BinaryReader reader, uint type, bool nested) {
            if (type == 8) { ReadString(reader); return; }
            if (type == 9 && !nested) {
                uint itemType = reader.ReadUInt32(); ulong count = reader.ReadUInt64();
                if (count > 10000000) throw new InvalidOperationException("Invalid GGUF array.");
                for (ulong i = 0; i < count; i++) Skip(reader, itemType, true);
                return;
            }
            int bytes = type == 0 || type == 1 || type == 7 ? 1 : type == 2 || type == 3 ? 2 : type == 4 || type == 5 || type == 6 ? 4 : type == 10 || type == 11 || type == 12 ? 8 : 0;
            if (bytes == 0 || reader.BaseStream.Position + bytes > reader.BaseStream.Length) throw new InvalidOperationException("Invalid GGUF value.");
            reader.BaseStream.Seek(bytes, SeekOrigin.Current);
        }
    }
}
