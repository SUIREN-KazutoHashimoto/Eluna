using System.Text;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.App.ViewModels;

public sealed partial class ChatViewModel
{
    internal async Task<CharacterDraft> GenerateCharacterAsync(string brief, string? modelId, CancellationToken token, Action<string>? progress = null)
    {
        if (brief.Length > 2000) { throw new InvalidOperationException("キャラクターの希望は2,000文字以内で入力してください。"); }
        LocalModel chosen = (modelId is null ? SelectedModel : Models.FirstOrDefault(m => m.Id == modelId)) ?? throw new InvalidOperationException("キャラクターの生成にはモデルが必要です。設定からモデルを追加してください。");
        LocalModel model = chosen with { Preferences = chosen.Options with { MaxOutputTokens = 2048 } };
        AppSettings settings = EffectiveSettings with { ContextSize = Math.Max(4096, model.Options.ContextSize ?? EffectiveContext) }; string names = string.Join("、", Agents.Select(a => a.Name).Take(100)); CharacterDraft? result = null;
        await QueueMediaOperationAsync("キャラクターの設定を生成", async cancel =>
        {
            LlamaService engine = _mediaContext.Value!.Value.Engine; MediaJob job = _mediaContext.Value.Value.Job;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel); deadline.CancelAfter(TimeSpan.FromSeconds(120)); CancellationToken work = deadline.Token;
            await engine.EnsureModelAsync(model, settings, message => { job.Detail = message; progress?.Invoke(message); }, work); job.Detail = "名前・性格・背景・話し方を考えています…"; progress?.Invoke(job.Detail);
            var json = new StringBuilder();
            await foreach (GenerationUpdate piece in engine.GenerateAsync([new("user", "希望: " + (string.IsNullOrWhiteSpace(brief) ? "おまかせ" : brief) + "\n既存の名前: " + names)], InferenceModes.Find("fast"), work, CharacterGenerator.Instruction, 1536, CharacterGenerator.Schema))
            { if (piece.FinishReason == "length") { throw new InvalidOperationException("キャラクター案が出力上限に達しました。短い希望で再試行してください。"); } json.Append(piece.Content); }
            result = CharacterGenerator.Parse(json.ToString());
        }, token);
        return result ?? throw new InvalidOperationException("キャラクター案がありません。");
    }
}
