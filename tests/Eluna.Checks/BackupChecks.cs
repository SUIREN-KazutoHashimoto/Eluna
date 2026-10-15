using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.Checks;

internal static class BackupChecks
{
    private static void Check(bool value, string message) { if (!value) { throw new InvalidOperationException(message); } }
    internal static async Task RunAsync(string root)
    {
        var source = new AppStore(Path.Combine(root, "source"));
        string external = Path.Combine(root, "external-models"); Directory.CreateDirectory(external); await source.SetModelLocationAsync(external);
        string modelPath = Path.Combine(external, "fixture.gguf"); await File.WriteAllBytesAsync(modelPath, Enumerable.Range(0, 65536).Select(i => (byte)i).ToArray());
        string modelHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(modelPath)));
        var model = new LocalModel("fixture", "保存モデル", modelPath, "fixture/repository", new string('a', 40), modelHash, 65536, IsFavorite: true);
        Directory.CreateDirectory(Path.Combine(external, model.Id)); await File.WriteAllTextAsync(Path.Combine(external, model.Id, "model.json"), JsonSerializer.Serialize(model, AppStore.JsonOptions));
        var package = new SkillPackage("backup-skill", "保存対象", "手順", "---\nname: backup-skill\ndescription: 保存対象\n---\n手順", "MIT", "Windows", new() { ["references/state.json"] = Encoding.UTF8.GetBytes("plain resource, not application JSON") });
        package.Files["SKILL.md"] = Encoding.UTF8.GetBytes(package.Markdown);
        SkillRegistration skill = await new SkillStore(source).SaveAsync(package, null, null, CancellationToken.None);
        string skillOutput = Path.Combine(source.Root, "Generated", "Skills", "fixture", "output.txt"); Directory.CreateDirectory(Path.GetDirectoryName(skillOutput)!); await File.WriteAllTextAsync(skillOutput, "スキルの生成物");
        Guid conversationId = Guid.NewGuid(), branchId = Guid.NewGuid(), answerId = Guid.NewGuid();
        var messages = new List<SavedMessage> { new("user", "本文内のパスは変更しない: " + source.Root), new("assistant", "途中の回答", Incomplete: true, Id: answerId, Skills: [new(skill.Id, skill.Name, skill.Revision, "指定", "手順", [], [new("scripts/example.ps1", "{}", "完了", "結果", [skillOutput])])]) };
        var conversation = new SavedConversation(conversationId, "保存する会話", messages, Branches: [new(branchId, "元の会話", messages)], ActiveBranchId: branchId, Draft: "未送信の下書き");
        var job = new SavedInferenceJob(Guid.NewGuid(), conversationId, branchId, answerId, "保存する予約", DateTimeOffset.UtcNow, model, [model], new(), "指示", []);
        var state = new AppState(3, new(), [conversation], CommonMemories: [new(Guid.NewGuid(), "共通の記憶", DateTimeOffset.UtcNow)], InferenceJobs: [job], Skills: [skill]);
        await source.SaveAsync(state);
        Directory.CreateDirectory(Path.Combine(source.Root, "Generated", "Jobs"));
        string output = Path.Combine(source.Root, "Generated", "Jobs", "image.png"); await File.WriteAllBytesAsync(output, [1, 2, 3, 4]);
        await File.WriteAllTextAsync(Path.Combine(source.Root, "Generated", "state.json"), "user document: this is not a JSON object");
        var media = new SavedMediaJob(Guid.NewGuid(), "画像の予約", DateTimeOffset.UtcNow, "active", new("image", "描いて", "", null, output, Image: new(modelPath, "描いて")), "途中");
        await File.WriteAllTextAsync(Path.Combine(source.Root, "media-jobs.json"), JsonSerializer.Serialize(new[] { media }, AppStore.JsonOptions));
        await File.WriteAllTextAsync(Path.Combine(source.Root, "image-model.json"), JsonSerializer.Serialize(modelPath));
        var downloaded = new SavedDownload(Guid.NewGuid(), new("fixture/repository", new string('a', 40), "fixture.gguf", modelHash, 65536, "MIT"), null, "fixture.gguf", "complete", 65536, 65536, ModelId: model.Id);
        await File.WriteAllTextAsync(Path.Combine(source.Root, "downloads-v2.json"), JsonSerializer.Serialize(new[] { downloaded }, AppStore.JsonOptions));
        var reading = new ReadingPositions(source.Root); reading.Set(conversationId + "/" + branchId, 123.5, false); await reading.SaveAsync();
        Directory.CreateDirectory(Path.Combine(source.Root, "Runtime")); await File.WriteAllTextAsync(Path.Combine(source.Root, "Runtime", "engine.exe"), "not a real executable");
        Directory.CreateDirectory(Path.Combine(source.Root, "Sessions")); await File.WriteAllTextAsync(Path.Combine(source.Root, "Sessions", "test.key"), "temporary session key");
        await File.WriteAllTextAsync(Path.Combine(source.Root, "Generated", "unfinished.tmp"), "incomplete write");
        string archive = Path.Combine(root, "all.zip");
        BackupPreview preview = await BackupArchive.PreviewAsync(source, false); await BackupArchive.CreateAsync(source, archive, false);
        BackupManifest manifest = await BackupArchive.InspectAsync(archive);
        Check(preview.Files == manifest.Files.Count && preview.Bytes == manifest.Files.Sum(f => f.Length), "Backup preview differs from the archive.");
        Check(manifest.Files.Any(f => f.Path.StartsWith("Skills/", StringComparison.Ordinal)) && manifest.Files.Any(f => f.Path == "media-jobs.json") && manifest.Files.Any(f => f.Path == "reading-positions.json"), "Whole backup omitted user data.");
        Check(!manifest.Files.Any(f => f.Path.StartsWith("Runtime/", StringComparison.Ordinal) || f.Path.StartsWith("Sessions/", StringComparison.Ordinal) || f.Path.EndsWith(".tmp", StringComparison.Ordinal) || f.Path.EndsWith(".gguf", StringComparison.Ordinal)), "Backup included temporary keys, engines or unselected model weights.");
        byte[] originalArchive = await File.ReadAllBytesAsync(archive);
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel(); try { await BackupArchive.CreateAsync(source, archive, true, token: canceled.Token); throw new InvalidOperationException("Canceled backup completed."); } catch (OperationCanceledException) { }
            byte[] canceledArchive = await File.ReadAllBytesAsync(archive); Check(originalArchive.SequenceEqual(canceledArchive), "Cancellation replaced an existing backup.");
        }
        using (var midway = new CancellationTokenSource())
        {
            var stop = new ProgressAction(p => { if (p.Completed > 0) { midway.Cancel(); } });
            try { await BackupArchive.CreateAsync(source, archive, false, stop, midway.Token); throw new InvalidOperationException("Mid-stream cancellation completed."); } catch (OperationCanceledException) { }
            byte[] afterCancel = await File.ReadAllBytesAsync(archive); Check(originalArchive.SequenceEqual(afterCancel) && !Directory.GetFiles(root, "all.zip.*.tmp").Any(), "Mid-stream cancellation replaced the archive or left temporary files.");
        }
        var target = new AppStore(Path.Combine(root, "target")); await target.SaveAsync(new(1, new(), [new(Guid.NewGuid(), "復元前の会話", [])]));
        Directory.CreateDirectory(Path.Combine(target.Root, "Generated")); await File.WriteAllTextAsync(Path.Combine(target.Root, "Generated", "old.txt"), "old generated data");
        PreparedBackup prepared = await BackupArchive.PrepareAsync(target, archive);
        SavedMediaJob migrated = JsonSerializer.Deserialize<List<SavedMediaJob>>(await File.ReadAllTextAsync(Path.Combine(prepared.Directory, "media-jobs.json")), AppStore.JsonOptions)!.Single();
        Check(migrated.Request.OutputPath == Path.Combine(target.Root, "Generated", "Jobs", "image.png") && migrated.Request.Image!.ModelPath == modelPath, "Restore did not relocate outputs or preserve external model references.");
        string safety = await BackupArchive.ApplyAsync(target, prepared);
        var reopened = new AppStore(target.Root); AppState restored = await reopened.LoadAsync();
        Check(restored.Conversations.Single().Title == "保存する会話" && restored.Conversations[0].Draft == "未送信の下書き" && restored.Conversations[0].Messages[0].Content.EndsWith(source.Root, StringComparison.Ordinal), "Restore lost state or rewrote user-authored content.");
        Check(restored.InferenceJobs?.Single().Model.FilePath == modelPath && restored.Skills?.Single().Revision == skill.Revision && restored.CommonMemories?.Single().Text == "共通の記憶", "Restore lost captured requests, skills or memories.");
        Check(restored.Conversations[0].Messages[1].Skills!.Single().Executions.Single().Artifacts.Single() == Path.Combine(target.Root, "Generated", "Skills", "fixture", "output.txt"), "Skill artifact links still point to the previous data directory.");
        Check((await new SkillStore(reopened).PackageAsync(skill, CancellationToken.None)).Files.ContainsKey("references/state.json"), "Skill files were not restored.");
        Check(await File.ReadAllTextAsync(Path.Combine(target.Root, "Generated", "state.json")) == "user document: this is not a JSON object" && !File.Exists(Path.Combine(target.Root, "Generated", "old.txt")), "Generated documents were parsed as app state or replacement retained unrelated current data.");
        var restoredReading = new ReadingPositions(target.Root); await restoredReading.LoadAsync(); Check(restoredReading.Find(conversationId + "/" + branchId)?.Offset == 123.5, "Reading positions were not restored.");
        Check(File.Exists(safety) && (await BackupArchive.InspectAsync(safety)).Files.Any(f => f.Path == "Generated/old.txt"), "Pre-restore safety backup omitted existing data.");
        File.Move(modelPath, modelPath + ".held");
        try { using var missingQueue = new DownloadQueue(reopened); await missingQueue.LoadAsync(); Check(missingQueue.Items.Single().CanResume && missingQueue.Items[0].Received == 0, "A restored completed download with missing model weights could not be downloaded again."); }
        finally { File.Move(modelPath + ".held", modelPath); }
        string withModels = Path.Combine(root, "with-models.zip"); await BackupArchive.CreateAsync(source, withModels, true);
        var portable = new AppStore(Path.Combine(root, "portable")); PreparedBackup bundled = await BackupArchive.PrepareAsync(portable, withModels); await BackupArchive.ApplyAsync(portable, bundled);
        LocalModel moved = (await new AppStore(portable.Root).ListModelsAsync()).Single();
        byte[] movedBytes = await File.ReadAllBytesAsync(moved.FilePath), originalBytes = await File.ReadAllBytesAsync(modelPath);
        Check(moved.FilePath == Path.Combine(portable.ModelsDirectory, "fixture.gguf") && movedBytes.SequenceEqual(originalBytes), "Included external model weights were not relocated with their metadata.");
        await RejectCorruptAsync(target, archive, Path.Combine(root, "corrupt.zip"));
        await CheckFailedCommitAsync(root, archive);
        await CheckRecoveryAsync(Path.Combine(root, "recovery"));
        var unc = new AppStore(Path.Combine(root, "unc-source")); await unc.SaveAsync(new(1, new(), []));
        await File.WriteAllTextAsync(Path.Combine(unc.Root, "image-model.json"), JsonSerializer.Serialize(@"\\unavailable.invalid\models\fixture.gguf"));
        string uncArchive = Path.Combine(root, "unc.zip"); await BackupArchive.CreateAsync(unc, uncArchive, false);
        var uncTarget = new AppStore(Path.Combine(root, "unc-target")); PreparedBackup uncPrepared = await BackupArchive.PrepareAsync(uncTarget, uncArchive);
        string uncPath = JsonSerializer.Deserialize<string>(await File.ReadAllTextAsync(Path.Combine(uncPrepared.Directory, "image-model.json")))!;
        Check(uncPath.StartsWith(uncTarget.Root, StringComparison.OrdinalIgnoreCase), "Restore retained an arbitrary UNC path instead of an unavailable local model reference.");
        BackupArchive.DeleteStage(uncPrepared.Directory, uncTarget.BackupsDirectory);
        Console.WriteLine("Whole backup: data/skills/artifacts/jobs/positions, optional external models, portable path rebasing, safety backup, cancellation, corrupt/traversal archives, partial-commit rollback and startup recovery: OK");
    }
    private sealed class ProgressAction(Action<BackupProgress> action) : IProgress<BackupProgress> { public void Report(BackupProgress value) => action(value); }
    private static async Task RejectCorruptAsync(AppStore target, string archive, string corrupt)
    {
        string statePath = Path.Combine(target.Root, "state-skills.json"); byte[] before = await File.ReadAllBytesAsync(statePath);
        File.Copy(archive, corrupt);
        using (var zip = ZipFile.Open(corrupt, ZipArchiveMode.Update))
        { ZipArchiveEntry entry = zip.GetEntry("files/Generated/Jobs/image.png")!; entry.Delete(); await using Stream output = zip.CreateEntry("files/Generated/Jobs/image.png").Open(); await output.WriteAsync(new byte[] { 9, 8, 7, 6 }); }
        try { await BackupArchive.PrepareAsync(target, corrupt); throw new InvalidOperationException("Corrupted backup was accepted."); } catch (InvalidDataException) { }
        byte[] after = await File.ReadAllBytesAsync(statePath); Check(before.SequenceEqual(after), "Corruption changed current data.");
        File.Copy(archive, corrupt, true);
        using (var zip = ZipFile.Open(corrupt, ZipArchiveMode.Update)) { await using Stream entry = zip.CreateEntry("files/../outside.txt").Open(); await entry.WriteAsync(new byte[] { 1 }); }
        try { await BackupArchive.PrepareAsync(target, corrupt); throw new InvalidOperationException("Traversal backup was accepted."); } catch (InvalidDataException) { }
        foreach (string path in new[] { "../state.json", "Generated/../state.json", "Generated/a:stream", "Generated/CON.txt", "Generated/a.", "Generated/dir /x", "Runtime/engine.exe" })
        { try { BackupArchive.CheckPath(path); throw new InvalidOperationException("Unsafe archive path was accepted: " + path); } catch (InvalidDataException) { } }
    }
    private static async Task CheckFailedCommitAsync(string root, string archive)
    {
        var target = new AppStore(Path.Combine(root, "locked-target")); await target.SaveAsync(new(1, new(), []));
        Directory.CreateDirectory(Path.Combine(target.Root, "Generated")); string existing = Path.Combine(target.Root, "Generated", "old.txt"); await File.WriteAllTextAsync(existing, "must survive rollback");
        PreparedBackup prepared = await BackupArchive.PrepareAsync(target, archive);
        using (var locked = new FileStream(Path.Combine(target.Root, "state.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
        { try { await BackupArchive.ApplyAsync(target, prepared); throw new InvalidOperationException("Locked restore completed."); } catch (IOException) { } }
        Check(await File.ReadAllTextAsync(existing) == "must survive rollback" && !File.Exists(Path.Combine(target.Root, "state-skills.json")), "A failed commit left partially restored user data.");
        Check(!File.Exists(Path.Combine(target.Root, "restore-current.json")), "Completed rollback left a pending transaction.");
    }
    private static async Task CheckRecoveryAsync(string root)
    {
        Directory.CreateDirectory(root); string backups = Path.Combine(root, "Backups"), previous = Path.Combine(backups, "restore-previous-check"), stage = Path.Combine(backups, ".restore-stage-check");
        Directory.CreateDirectory(previous); Directory.CreateDirectory(stage); await File.WriteAllTextAsync(Path.Combine(previous, "state.json"), JsonSerializer.Serialize(new AppState(1, new(), []), AppStore.JsonOptions));
        await File.WriteAllTextAsync(Path.Combine(root, "state.json"), "partial replacement"); Directory.CreateDirectory(Path.Combine(root, "Generated")); await File.WriteAllTextAsync(Path.Combine(root, "Generated", "new.txt"), "partial new file");
        var journal = new RestoreJournal(previous, stage, [new("state.json", true, true), new("Generated/new.txt", false, true)]);
        await File.WriteAllTextAsync(Path.Combine(root, "restore-current.json"), JsonSerializer.Serialize(journal, AppStore.JsonOptions));
        var recovered = new AppStore(root); Check((await recovered.LoadAsync()).SchemaVersion == 1 && !File.Exists(Path.Combine(root, "Generated", "new.txt")) && !File.Exists(Path.Combine(root, "restore-current.json")), "Startup did not undo an interrupted multi-file restore.");
    }
}
