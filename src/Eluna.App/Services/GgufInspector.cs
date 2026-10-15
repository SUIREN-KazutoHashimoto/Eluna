using System.IO;
using System.Text;
using Eluna.App.Models;

namespace Eluna.App.Services;

public static class GgufInspector
{
    public static Task<ModelProfile> ReadAsync(LocalModel model, CancellationToken token) => Task.Run(() => Read(model, token), token);
    private static ModelProfile Read(LocalModel model, CancellationToken token)
    {
        Dictionary<string, object> metadata = new(StringComparer.Ordinal); long parameters = 0;
        foreach (LocalModelPart part in model.Files)
        {
            using var stream = File.OpenRead(part.FilePath); using var reader = new BinaryReader(stream, Encoding.UTF8, true);
            if (reader.ReadUInt32() != 0x46554747 || reader.ReadUInt32() is not (2 or 3)) { throw new InvalidDataException("GGUFのバージョンを読み取れません。"); }
            ulong tensors = reader.ReadUInt64(), count = reader.ReadUInt64();
            if (tensors > 1000000 || count > 100000) { throw new InvalidDataException("GGUFのヘッダーが大きすぎます。"); }
            string ReadString(bool keep)
            {
                ulong length = reader.ReadUInt64();
                if (length > (ulong)(stream.Length - stream.Position) || keep && length > 1024 * 1024) { throw new InvalidDataException("GGUFの文字列サイズが不正です。"); }
                if (!keep) { stream.Seek((long)length, SeekOrigin.Current); return ""; }
                byte[] bytes = reader.ReadBytes((int)length); return new UTF8Encoding(false, true).GetString(bytes);
            }
            object? Value(uint type, bool keep, int depth = 0)
            {
                token.ThrowIfCancellationRequested();
                if (depth > 4 || stream.Position > 512L * 1024 * 1024) { throw new InvalidDataException("GGUFのヘッダーに対応していません。"); }
                switch (type)
                {
                    case 0: return reader.ReadByte();
                    case 1: return reader.ReadSByte();
                    case 2: return reader.ReadUInt16();
                    case 3: return reader.ReadInt16();
                    case 4: return reader.ReadUInt32();
                    case 5: return reader.ReadInt32();
                    case 6: return reader.ReadSingle();
                    case 7: return reader.ReadBoolean();
                    case 8: return ReadString(keep);
                    case 10: return reader.ReadUInt64();
                    case 11: return reader.ReadInt64();
                    case 12: return reader.ReadDouble();
                    case 9:
                        uint elementType = reader.ReadUInt32(); ulong length = reader.ReadUInt64(); if (length > 2000000) { throw new InvalidDataException("GGUFの配列が大きすぎます。"); }
                        var strings = new List<string>();
                        for (ulong i = 0; i < length; i++) { object? value = Value(elementType, keep && length <= 128, depth + 1); if (keep && value is string text && length <= 128) { strings.Add(text); } }
                        return strings;
                    default: throw new InvalidDataException("GGUFの値の型に対応していません。");
                }
            }
            for (ulong i = 0; i < count; i++)
            {
                token.ThrowIfCancellationRequested(); string key = ReadString(true); uint type = reader.ReadUInt32();
                bool keep = key.StartsWith("general.", StringComparison.Ordinal) || key == "tokenizer.chat_template" || key.EndsWith("context_length", StringComparison.Ordinal) ||
                    key.EndsWith("block_count", StringComparison.Ordinal) || key.EndsWith("embedding_length", StringComparison.Ordinal) || key.Contains("attention.head_count", StringComparison.Ordinal) || key.EndsWith("attention.key_length", StringComparison.Ordinal) || key.EndsWith("attention.value_length", StringComparison.Ordinal);
                object? value = Value(type, keep); if (keep && value is not null) { metadata.TryAdd(key, value); }
            }
            for (ulong i = 0; i < tensors; i++)
            {
                token.ThrowIfCancellationRequested(); ReadString(false); uint dimensions = reader.ReadUInt32(); if (dimensions is < 1 or > 8) { throw new InvalidDataException("GGUFのテンソル形状が不正です。"); }
                long elements = 1; for (int d = 0; d < dimensions; d++) { ulong dimension = reader.ReadUInt64(); if (dimension > long.MaxValue) { throw new InvalidDataException("GGUFの形状が大きすぎます。"); } elements = checked(elements * (long)dimension); }
                parameters = checked(parameters + elements); reader.ReadUInt32(); reader.ReadUInt64();
            }
        }
        string Text(string key) => metadata.GetValueOrDefault(key) as string ?? "";
        int Number(string key) { try { return Convert.ToInt32(metadata.GetValueOrDefault(key) ?? 0, System.Globalization.CultureInfo.InvariantCulture); } catch (Exception ex) when (ex is OverflowException or InvalidCastException or FormatException) { return 0; } }
        string architecture = Text("general.architecture"); string template = Text("tokenizer.chat_template");
        int embedding = Number(architecture + ".embedding_length"), heads = Number(architecture + ".attention.head_count"), kv = Number(architecture + ".attention.head_count_kv");
        int keyDimension = Number(architecture + ".attention.key_length"), valueDimension = Number(architecture + ".attention.value_length");
        if (keyDimension == 0 && heads > 0) { keyDimension = embedding / heads; }
        if (valueDimension == 0) { valueDimension = keyDimension; }
        if (kv == 0) { kv = heads; }
        string[] types = ["F32", "F16", "Q4_0", "Q4_1", "未対応", "未対応", "未対応", "Q8_0", "Q5_0", "Q5_1", "Q2_K", "Q3_K_S", "Q3_K_M", "Q3_K_L", "Q4_K_S", "Q4_K_M", "Q5_K_S", "Q5_K_M", "Q6_K", "IQ2_XXS", "IQ2_XS", "Q2_K_S", "IQ3_XS", "IQ3_XXS", "IQ1_S", "IQ4_NL", "IQ3_S", "IQ3_M", "IQ2_S", "IQ2_M", "IQ4_XS", "IQ1_M", "BF16", "未対応", "未対応", "未対応", "TQ1_0", "TQ2_0", "MXFP4_MOE", "NVFP4", "Q1_0", "Q2_0"];
        int fileType = Number("general.file_type"); string quant = metadata.ContainsKey("general.file_type") && fileType >= 0 && fileType < types.Length ? types[fileType] : "不明";
        int context = Number(architecture + ".context_length");
        return new(Text("general.name"), architecture, parameters, context > 0 ? context : null, quant,
            template.Length == 0 ? null : template.Contains("enable_thinking", StringComparison.Ordinal) || template.Contains("<think>", StringComparison.Ordinal),
            Number(architecture + ".block_count"), embedding, kv, keyDimension, valueDimension, Text("general.license"), metadata.GetValueOrDefault("general.languages") as List<string> ?? []);
    }
}
