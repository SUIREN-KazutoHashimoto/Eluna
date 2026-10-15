namespace Eluna.App.Models;

public sealed record ModelPreferences(int? ContextSize = null, double Temperature = 0.7, double TopP = 0.95, int TopK = 40,
    double RepeatPenalty = 1.0, int MaxOutputTokens = 2048, string DefaultMode = "standard")
{
    public static void Validate(ModelPreferences value)
    {
        if (value.ContextSize is not (null or 2048 or 4096 or 8192 or 16384 or 32768) || !double.IsFinite(value.Temperature) || value.Temperature is < 0 or > 2 ||
            !double.IsFinite(value.TopP) || value.TopP is <= 0 or > 1 || value.TopK is < 0 or > 1000 || !double.IsFinite(value.RepeatPenalty) || value.RepeatPenalty is < 0.5 or > 2 ||
            value.MaxOutputTokens is < 128 or > 8192 || !InferenceModes.IsValid(value.DefaultMode))
        { throw new System.IO.InvalidDataException("モデル設定の範囲を確認してください。"); }
    }
}
public sealed record ModelProfile(string Name, string Architecture, long Parameters, int? TrainingContext, string Quantization,
    bool? SupportsThinking, int Layers, int Embedding, int KvHeads, int KeyDimension, int ValueDimension, string License, List<string> Languages)
{
    public string ParameterLabel => Parameters > 0 ? $"{Parameters / 1e9:F2}B" : "不明";
    public string ThinkingLabel => SupportsThinking switch { true => "テンプレートに思考形式あり（切替可否はモデル依存）", false => "思考切替は確認できません", _ => "思考対応は不明" };
    public string JapaneseLabel => Languages.Contains("ja", StringComparer.OrdinalIgnoreCase) ? "配布元メタデータに日本語あり（品質は未評価）" : "日本語能力の評価情報なし";
    public long EstimatedKvBytes(int context)
    {
        if (Layers <= 0 || KvHeads <= 0 || KeyDimension <= 0 || ValueDimension <= 0) { return 0; }
        try { return checked((long)context * Layers * KvHeads * (KeyDimension + ValueDimension) * 2); } catch (OverflowException) { return 0; }
    }
}
