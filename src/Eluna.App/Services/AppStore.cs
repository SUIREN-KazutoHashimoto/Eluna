using System.IO;
using System.Text.Json;
using Eluna.App.Models;

namespace Eluna.App.Services;

public sealed class AppStore
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    public static JsonSerializerOptions JsonOptions { get; } = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public string Root { get; }
    private string? _modelsDirectory;
    public string ModelsDirectory => _modelsDirectory ?? Path.Combine(Root, "Models");
    public SemaphoreSlim ModelsGate { get; } = new(1, 1);
    public string BackupsDirectory => Path.Combine(Root, "Backups");
    internal string DocumentDraftPath(Guid? projectId, Guid? conversationId = null) => Path.Combine(Root, "Generated", conversationId is Guid room ? $"room-{room:N}-document-draft.md" : projectId is Guid id ? $"project-{id:N}-document-draft.md" : "document-draft.md");
    internal bool UsesKnowledgeState => File.Exists(Path.Combine(Root, "state-knowledge.json"));
    internal bool UsesSkillsState => File.Exists(Path.Combine(Root, "state-skills.json"));
    internal bool UsesWorkspaceState => File.Exists(Path.Combine(Root, "state-workspace.json"));
    private static int Version(string path) => Path.GetFileName(path) switch { "state-workspace.json" => 4, "state-skills.json" => 3, "state-knowledge.json" => 2, _ => 1 };
    private string StatePath(AppState? state = null) => Path.Combine(Root, UsesWorkspaceState || state?.SchemaVersion >= 4 || state?.Agents is not null ? "state-workspace.json" : UsesSkillsState || state?.SchemaVersion >= 3 || state?.Skills is { Count: > 0 } ? "state-skills.json" : UsesKnowledgeState || state?.SchemaVersion >= 2 || state?.Projects is { Count: > 0 } || state?.CommonMemories is { Count: > 0 } ? "state-knowledge.json" : "state.json");
    private DateTimeOffset _lastBackup;
    public string? RecoveryNotice { get; private set; }

    public AppStore(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Eluna");
        Directory.CreateDirectory(Root);
        // Roll back an interrupted multi-file restore before reading any application state.
        BackupArchive.Recover(Root);
        string location = Path.Combine(Root, "model-location.json");
        if (File.Exists(location))
        {
            try { string? saved = JsonSerializer.Deserialize<string>(File.ReadAllText(location)); if (!string.IsNullOrWhiteSpace(saved) && Path.IsPathFullyQualified(saved)) { _modelsDirectory = Path.GetFullPath(saved); } }
            catch (Exception ex) when (ex is JsonException or IOException or ArgumentException) { RecoveryNotice = "モデルの保存先設定を読み取れませんでした。標準の場所を使用します。"; }
        }
        try { Directory.CreateDirectory(ModelsDirectory); }
        catch (Exception ex) when (_modelsDirectory is not null && ex is IOException or UnauthorizedAccessException)
        { RecoveryNotice = "モデルの保存先が利用できません。ドライブを接続するか、モデル管理で保存先を変更してください。会話は通常どおり保存できます。"; }
    }

    public async Task<AppState> LoadAsync()
    {
        await _writeLock.WaitAsync();
        try { return await Task.Run(ReadStateAsync); } finally { _writeLock.Release(); }
    }
    private async Task<AppState> ReadStateAsync()
    {
        string path = StatePath();
        if (!File.Exists(path))
        {
            return new(UsesSkillsState ? 3 : UsesKnowledgeState ? 2 : 1, new(), []);
        }
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 16384, true);
            AppState state = await JsonSerializer.DeserializeAsync<AppState>(stream, JsonOptions)
                ?? throw new InvalidDataException("履歴が空です。");
            Validate(state);
            if (!InferenceModes.IsValid(state.Settings.InferenceMode))
            {
                state = state with { Settings = state.Settings with { InferenceMode = InferenceModes.DefaultId } };
            }
            return state;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            string backup = Path.Combine(Root, $"state.broken-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
            File.Copy(path, backup);
            RecoveryNotice = $"履歴を読み込めませんでした。元のデータは {Path.GetFileName(backup)} に保管しています。";
            if (Path.GetFileName(path) == "state-workspace.json")
            {
                foreach (string previous in new[] { "state-skills.json", "state-knowledge.json", "state.json" })
                {
                    string old = Path.Combine(Root, previous); if (!File.Exists(old)) { continue; }
                    try
                    {
                        AppState recovered = JsonSerializer.Deserialize<AppState>(await File.ReadAllTextAsync(old), JsonOptions) ?? throw new InvalidDataException();
                        Validate(recovered); RecoveryNotice += " 以前の形式の履歴を読み込みました。新形式の破損データは退避済みです。"; return WorkspaceService.Upgrade(recovered);
                    }
                    catch (Exception failure) when (failure is JsonException or InvalidDataException) { }
                }
            }
            return new(UsesSkillsState ? 3 : UsesKnowledgeState ? 2 : 1, new(), []);
        }
    }

    public async Task SaveAsync(AppState state)
    {
        await _writeLock.WaitAsync();
        try
        {
            string path = StatePath(state);
            AppState stored = state with { SchemaVersion = Version(path) };
            byte[] contents = await Task.Run(() => JsonSerializer.SerializeToUtf8Bytes(stored, JsonOptions));
            if (File.Exists(path) && DateTimeOffset.UtcNow - _lastBackup > TimeSpan.FromMinutes(5))
            {
                await Task.Run(() => CreateBackup(path));
                _lastBackup = DateTimeOffset.UtcNow;
            }
            string temporary = path + ".tmp";
            await File.WriteAllBytesAsync(temporary, contents);
            await ReplaceStateAsync(temporary, path);
        }
        finally
        {
            _writeLock.Release();
        }
    }
    private static async Task ReplaceStateAsync(string temporary, string path)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(temporary, path, true);
                return;
            }
            catch (Exception ex) when (attempt < 7 && ex is IOException or UnauthorizedAccessException && (ex.HResult & 0xffff) is 5 or 32 or 33 or 1175 or 1176 or 1224)
            { await Task.Delay(25 << attempt); }
        }
    }

    internal static void Validate(AppState state)
    {
        static bool ValidAttachments(List<ChatAttachment>? items) => items is null || items.Count <= 12 && items.All(a => a is not null &&
            !string.IsNullOrWhiteSpace(a.Name) && a.Name.Length <= 512 && a.Kind is "image" or "document" && a.Text is not null && a.Text.Length <= 61000 &&
            (a.Kind == "image" ? a.ImageBase64 is { Length: <= 2800000 } data && data.StartsWith("/9j/", StringComparison.Ordinal) && System.Buffers.Text.Base64.IsValid(data, out int bytes) && bytes <= 2 * 1024 * 1024 : a.ImageBase64 is null));
        static bool ValidMessages(List<SavedMessage>? messages) => messages is not null && messages.All(m => m is not null &&
            m.Content is not null && m.Role is "user" or "assistant" &&
            (m.Artifact is null || m.Artifact.JobId != Guid.Empty && m.Artifact.Title is { Length: <= 1000 } && m.Artifact.Kind is "image" or "document" or "translation" && m.Artifact.ResultPath is { Length: <= 32767 } && Path.IsPathFullyQualified(m.Artifact.ResultPath)) &&
            ValidAttachments(m.Attachments) &&
            (m.Knowledge is null || m.Knowledge.Count <= 8 && m.Knowledge.All(k => k is not null && k.Number is >= 1 and <= 8 && k.Kind is not null && k.Title is not null && k.Text is not null && k.Title.Length <= 1024 && k.Text.Length <= 700) && m.Knowledge.Select(k => k.Number).Distinct().Count() == m.Knowledge.Count) &&
            SkillValidation.Uses(m.Skills) && ComputerActionPolicy.ValidSaved(m.Computer) && (m.Work is null || ValidWork(m.Work)) &&
            (m.ThinkingSeconds is null || double.IsFinite(m.ThinkingSeconds.Value) && m.ThinkingSeconds >= 0));
        static bool ValidWork(SavedWork work)
        {
            if (work.Options is null || work.Mode is not ("search" or "research" or "debate") || work.Question is null || work.ModelId is null || work.Stage is null ||
                work.Plan is null || work.Verification is null || work.Report is null || work.Queries is null || work.SearchedQueries is null || work.AttemptedPages is null ||
                work.Hits is null || work.Sources is null || work.Activities is null || work.Turns is null || !double.IsFinite(work.SecondsUsed) || work.SecondsUsed < 0 ||
                work.SearchesUsed < 0 || work.PagesUsed < 0 || work.TokensUsed < 0 || work.SearchesUsed > work.Options.SearchLimit || work.PagesUsed > work.Options.PageLimit || work.TokensUsed > work.Options.TokenLimit ||
                work.Queries.Any(q => q is null) || work.SearchedQueries.Any(q => q is null) || work.AttemptedPages.Any(q => q is null) ||
                work.Sources.Any(s => s is null || s.Number < 1 || s.Title is null || s.Text is null || !PublicUrl(s.Url)) ||
                work.Sources.Select(s => s.Number).Distinct().Count() != work.Sources.Count || work.Sources.Count > work.Options.PageLimit ||
                work.Hits.Any(h => h is null || h.Title is null || h.Snippet is null || h.Provider is null || !PublicUrl(h.Url)) ||
                work.Activities.Any(a => a is null || a.Kind is null || a.Title is null || a.Detail is null) ||
                work.Turns.Any(t => t is null || t.Role is null || t.Content is null || t.Round < 1 || (work.Options.DebateStyle == "rounds" && t.Round > work.Options.Rounds)) ||
                work.Claims?.Any(c => c is null || c.Quote is null || c.Claim is null || !work.Sources.Any(s => s.Number == c.SourceNumber)) == true) { return false; }
            try { ResearchRunner.ValidateOptions(work.Options); return true; } catch (InvalidDataException) { return false; }
        }
        static bool PublicUrl(string? url) => Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0;
        if (state.SchemaVersion is not (1 or 2 or 3 or 4) || state.Settings is null || state.Settings.ContextSize is not (2048 or 4096 or 8192 or 16384) || state.Settings.MaxConcurrentInferences is < 1 or > 4 || state.Settings.MaxConcurrentDownloads is < 1 or > 4 || state.Settings.MaxConcurrentMedia is < 1 or > 4 ||
            !double.IsFinite(state.Settings.UiScale) || state.Settings.UiScale is < 1 or > 1.5 ||
            !double.IsFinite(state.Settings.ChatFontSize) || state.Settings.ChatFontSize is < 10 or > 28 ||
            !double.IsFinite(state.Settings.LineHeight) || state.Settings.LineHeight is < 15 or > 60 ||
            state.Settings.Theme is not ("dark" or "light") || state.Settings.SystemInstruction is null || state.Conversations is null ||
            !WorkModes.All.Any(m => m.Id == state.Settings.WorkMode) ||
            state.Conversations.Any(c => c is null || c.Title is null || !ValidMessages(c.Messages) || !ValidAttachments(c.DraftAttachments) ||
                c.Branches?.Any(b => b is null || b.Name is null || !ValidMessages(b.Messages) || b.SummaryThrough < 0 || b.SummaryThrough > b.Messages.Count) == true ||
                c.Branches is not null && c.Branches.Select(b => b.Id).Distinct().Count() != c.Branches.Count) ||
            state.Conversations.Select(c => c.Id).Distinct().Count() != state.Conversations.Count)
        {
            throw new InvalidDataException("履歴または設定の形式に対応していません。");
        }
        if (state.Settings.WorkOptions is not null) { ResearchRunner.ValidateOptions(state.Settings.WorkOptions); }
        KnowledgeService.Validate(state.Projects, state.CommonMemories);
        SkillValidation.State(state);
        WorkspaceService.Validate(state);
        if (state.Settings.RecentEmoji is { } recent && (recent.Count > 24 || recent.Any(g => g is null || !EmojiCatalog.Contains(g)) || recent.Distinct(StringComparer.Ordinal).Count() != recent.Count)) { throw new InvalidDataException("最近使った絵文字の形式または件数が不正です。"); }
        foreach (SavedConversation conversation in state.Conversations) { MessageLocations.Validate(conversation); }
        HashSet<Guid> projectIds = (state.Projects ?? []).Select(p => p.Id).ToHashSet();
        if (state.Settings.SelectedProjectId is Guid selected && !projectIds.Contains(selected) || state.Conversations.Any(c => c.ProjectId is Guid id && !projectIds.Contains(id)))
        { throw new InvalidDataException("会話または選択範囲のプロジェクトが見つかりません。"); }
        if (state.InferenceJobs is not null)
        {
            if (state.InferenceJobs.Count > 100 || state.InferenceJobs.Any(j => j is null) || state.InferenceJobs.Select(j => j.Id).Distinct().Count() != state.InferenceJobs.Count ||
                state.InferenceJobs.Select(j => j.AnswerId).Distinct().Count() != state.InferenceJobs.Count) { throw new InvalidDataException("推論予約の件数またはIDが不正です。"); }
            foreach (SavedInferenceJob job in state.InferenceJobs)
            {
                SavedConversation? conversation = state.Conversations.FirstOrDefault(c => c.Id == job.ConversationId);
                SavedBranch? branch = conversation?.Branches?.FirstOrDefault(b => b.Id == job.BranchId);
                if (job.Model is null || job.Models is null || job.Models.Count > 1000 || job.Models.Any(m => m is null) || job.Settings is null ||
                    job.Title is null || job.Instruction is null || job.Instruction.Length > 100000 || job.References is null ||
                    !ValidMessages([new("assistant", "", Knowledge: job.References)]) || job.PreviousWork is not null && !ValidWork(job.PreviousWork) ||
                    conversation is null || branch is null || conversation.ProjectId != job.Settings.SelectedProjectId ||
                    branch.Messages.Count(m => m.Id == job.AnswerId && m.Role == "assistant") != 1)
                { throw new InvalidDataException("推論予約の参照先または保存内容が不正です。"); }
                if (job.Social is { } social && (social.SessionId == Guid.Empty || social.RemainingTurns is < 1 or > 100 || social.RemainingTokens is < 64 or > 65536 || job.Agent is null || state.Agents?.Any(a => a.Id == job.Agent.Id) != true ||
                    social.Called is { Count: > 3 } || social.Called?.Distinct().Count() != social.Called?.Count || social.Called?.Any(id => id != AgentAccount.LocalId && state.Agents?.Any(a => a.Id == id) != true) == true ||
                    social.TriggerId is Guid trigger && !branch.Messages.Any(m => m.Id == trigger))) { throw new InvalidDataException("自律会話の予約と会話上限の形式が不正です。"); }
                ModelPreferences.Validate(job.Model.Options with { ContextSize = job.Settings.ContextSize });
                Validate(new(1, job.Settings with { SelectedProjectId = null, ContextSize = 4096 }, []));
                ModelPreferences.Validate(job.Model.Options);
            }
        }
    }

    private void CreateBackup(string path)
    {
        Directory.CreateDirectory(BackupsDirectory);
        string destination = Path.Combine(BackupsDirectory, $"state-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
        File.Copy(path, destination);
        foreach (FileInfo old in new DirectoryInfo(BackupsDirectory).GetFiles("state-*.json").OrderByDescending(f => f.LastWriteTimeUtc).Skip(20))
        {
            old.Delete();
        }
    }

    public IReadOnlyList<FileInfo> GetBackups() => Directory.Exists(BackupsDirectory)
        ? new DirectoryInfo(BackupsDirectory).GetFiles("state-*.json").OrderByDescending(f => f.LastWriteTimeUtc).ToList() : [];

    public Task<AppState> ReadExportAsync(string path) => Task.Run(async () =>
    {
        if (new FileInfo(path).Length > 100 * 1024 * 1024) { throw new InvalidDataException("履歴ファイルが大きすぎます（上限100 MB）。"); }
        AppState state = JsonSerializer.Deserialize<AppState>(await File.ReadAllTextAsync(path), JsonOptions)
            ?? throw new InvalidDataException("履歴が空です。");
        Validate(state);
        return state;
    });

    public Task ExportAsync(AppState state, string path) => Task.Run(async () =>
    {
        Validate(state);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(state, JsonOptions));
    });

    public async Task RestoreBackupAsync(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string prefix = Path.GetFullPath(BackupsDirectory) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { throw new InvalidDataException("バックアップ一覧から選択してください。"); }
        AppState state = await ReadExportAsync(fullPath);
        await _writeLock.WaitAsync();
        try
        {
            string current = StatePath(state);
            if (File.Exists(current)) { CreateBackup(current); }
            string temporary = current + ".restore";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(state with { SchemaVersion = Version(current) }, JsonOptions));
            await ReplaceStateAsync(temporary, current);
        }
        finally { _writeLock.Release(); }
    }

    public async Task<List<LocalModel>> ListModelsAsync()
    {
        var models = new List<LocalModel>();
        if (!Directory.Exists(ModelsDirectory)) { return models; }
        foreach (string path in Directory.EnumerateFiles(ModelsDirectory, "model.json", SearchOption.AllDirectories))
        {
            try
            {
                LocalModel? model = JsonSerializer.Deserialize<LocalModel>(await File.ReadAllTextAsync(path), JsonOptions);
                if (model is not null && model.Files.Sum(f => f.Size) == model.Size && model.Files.All(f => File.Exists(f.FilePath) && new FileInfo(f.FilePath).Length == f.Size))
                {
                    models.Add(model);
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // Keep damaged metadata available for recovery, without listing it as a usable model.
            }
        }
        return models.OrderByDescending(m => m.IsFavorite).ThenBy(m => m.DisplayName).ToList();
    }
    internal async Task SetModelLocationAsync(string path)
    {
        string setting = Path.Combine(Root, "model-location.json");
        await File.WriteAllTextAsync(setting + ".tmp", JsonSerializer.Serialize(path)); File.Move(setting + ".tmp", setting, true); _modelsDirectory = path;
    }
}
