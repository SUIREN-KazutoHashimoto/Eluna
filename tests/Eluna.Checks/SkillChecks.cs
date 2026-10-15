using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.Checks;

internal static class SkillChecks
{
    private static void Check(bool value, string message) { if (!value) { throw new InvalidOperationException(message); } }
    private static SkillPackage Package(string name, string body, Dictionary<string, string>? files = null)
    {
        var data = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase) { ["SKILL.md"] = Encoding.UTF8.GetBytes(SkillFormat.Create(name, "集計・文章のテストに使う。", body)) };
        foreach ((string path, string content) in files ?? []) { data.Add(path, Encoding.UTF8.GetBytes(content)); }
        return SkillFormat.Parse(data);
    }
    internal static async Task RunAsync(string root)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2)); CancellationToken token = timeout.Token;
        var store = new AppStore(root); var files = new SkillStore(store);
        string sampleYaml = "---\nname: metadata-test\ndescription: >-\n  複数行の説明。\n  日本語の依頼に使う。\nlicense: 'Apache-2.0' # notice\ncompatibility: \"Windows\" # \"comment\"\nmetadata:\n  author: local\nallowed-tools: Read Bash\n---\n本文の手順。";
        SkillPackage metadata = SkillFormat.Parse(new() { ["SKILL.md"] = Encoding.UTF8.GetBytes(sampleYaml) });
        Check(metadata.Description.Contains("日本語", StringComparison.Ordinal) && metadata.Compatibility == "Windows" && metadata.Markdown.Contains("allowed-tools", StringComparison.Ordinal), "Common YAML scalar forms or optional metadata were lost.");
        string edited = SkillFormat.Update(metadata, "renamed-test", "新しい説明", "本文\n---\n\n区切りを保持");
        SkillPackage editedPackage = SkillFormat.Parse(new() { ["SKILL.md"] = Encoding.UTF8.GetBytes(edited) });
        Check(editedPackage.License == "Apache-2.0" && editedPackage.Markdown.Contains("allowed-tools: Read Bash", StringComparison.Ordinal) && editedPackage.Instructions == "本文\n---\n\n区切りを保持", "Editing metadata lost optional fields or modified a Markdown separator.");
        foreach (string bad in new[] { "Uppercase", "-leading", "trailing-", "two--hyphens", "../outside" })
        { bool rejected = false; try { Package(bad, "body"); } catch (InvalidDataException) { rejected = true; } Check(rejected, "An invalid skill name was accepted: " + bad); }
        foreach (string bad in new[] { "../secret.txt", "/absolute", "C:/secret", "scripts/CON.ps1", "a/../b", "a:file", ".eluna-manifest.json" })
        { bool rejected = false; try { SkillStore.SafePath(bad); } catch (InvalidDataException) { rejected = true; } Check(rejected, "An unsafe skill path was accepted."); }
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        SkillRegistration common = await files.SaveAsync(Package("shared-skill", "COMMON-ONLY"), null, null, token);
        SkillRegistration privateA = await files.SaveAsync(Package("project-skill", "A-PRIVATE"), a, null, token);
        SkillRegistration privateB = await files.SaveAsync(Package("project-skill", "B-PRIVATE"), b, null, token);
        SkillRegistration[] all = [common, privateA, privateB];
        Check(SkillRunner.Available(all, a, false).SequenceEqual([privateA]) && SkillRunner.Available(all, null, false).SequenceEqual([common]) &&
            SkillRunner.Available(all, a, true).Count == 2, "Project/common skill discovery leaked another project's metadata.");
        SkillRequest explicitRequest = SkillRunner.Capture(all, a, false, "manual", [], "/project-skill 作業してください");
        Check(explicitRequest.Forced.SequenceEqual([privateA.Id]), "Slash invocation did not select the scoped skill.");
        Check(SkillRunner.Capture(all, a, false, "off", [privateA.Id], "通常の質問").Forced.Count == 0 &&
            SkillRunner.Capture(all, a, false, "auto", [], "https://example.com/project-skill を見る").Forced.Count == 0, "Off mode or URL command detection was incorrect.");
        bool unknownRejected = false;
        try { SkillRunner.Capture(all, a, false, "auto", [], "/shared-skill 不可"); } catch (InvalidDataException) { unknownRejected = true; }
        Check(unknownRejected, "A common skill was explicitly invoked inside a project-only scope.");

        string export = Path.Combine(root, "export.skill"); await files.ExportAsync(privateA, export, token);
        SkillPackage imported = await files.ReadPackageAsync(export, token);
        Check(imported.Name == privateA.Name && imported.Instructions == "A-PRIVATE", "Skill ZIP round-trip lost instructions.");
        SkillRegistration newA = await files.SaveAsync(Package("project-skill", "A-NEW-VERSION"), a, privateA, token);
        Check(privateA.Revision != newA.Revision && SkillFormat.Utf8.GetString(await files.ReadFileAsync(privateA, "SKILL.md", token)).Contains("A-PRIVATE", StringComparison.Ordinal), "Updating a skill changed a queued version.");
        string zipPath = Path.Combine(root, "unsafe.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        { await using Stream output = zip.CreateEntry("../escape/SKILL.md").Open(); await output.WriteAsync(Encoding.UTF8.GetBytes(metadata.Markdown), token); }
        bool traversalRejected = false; try { await files.ReadPackageAsync(zipPath, token); } catch (InvalidDataException) { traversalRejected = true; }
        Check(traversalRejected, "A traversal ZIP was accepted.");

        SkillPackage numeric = SkillSamples.All().Single(p => p.Name == "number-summary");
        SkillRegistration scriptSkill = await files.SaveAsync(numeric, null, null, token);
        var scriptRunner = new SkillScriptRunner(store, files);
        SkillExecution denied = await scriptRunner.RunAsync(scriptSkill, "scripts/summarize.ps1", "{\"values\":[1,2,3]}", (_, _) => Task.FromResult(false), false, token);
        Check(denied.Status == "拒否" && denied.Artifacts.Count == 0, "Declining execution still ran code.");
        SkillExecution success = await scriptRunner.RunAsync(scriptSkill, "scripts/summarize.ps1", "{\"values\":[4,8,12]}", (_, _) => Task.FromResult(true), false, token);
        Check(success.Status == "成功" && success.Output.Contains("24", StringComparison.Ordinal) && success.Artifacts.Count == 1 && File.Exists(success.Artifacts[0]), "Standard Windows PowerShell helper failed: " + success.Output);
        SkillRegistration japanese = await files.SaveAsync(Package("japanese-script", "日本語の入出力", new() { ["scripts/echo.ps1"] = "param([string]$InputJson,[string]$OutputDirectory)\n$data=Get-Content -LiteralPath $InputJson -Raw | ConvertFrom-Json\n[Console]::WriteLine('日本語の出力：'+$data.name)\n" }), null, null, token);
        SkillExecution japaneseResult = await scriptRunner.RunAsync(japanese, "scripts/echo.ps1", "{\"name\":\"青森\"}", (_, _) => Task.FromResult(true), false, token);
        Check(japaneseResult.Status == "成功" && japaneseResult.Output.Contains("日本語の出力：青森", StringComparison.Ordinal), "UTF-8 script or Japanese JSON input was corrupted by Windows PowerShell.");
        bool asked = false;
        SkillExecution offlineDenied = await scriptRunner.RunAsync(scriptSkill with { ExecutionPolicy = "trusted" }, "scripts/summarize.ps1", "{}", (_, _) => { asked = true; return Task.FromResult(false); }, true, token);
        Check(asked && offlineDenied.Status == "拒否", "Offline use bypassed per-execution review.");
        SkillExecution never = await scriptRunner.RunAsync(scriptSkill with { ExecutionPolicy = "never" }, "scripts/summarize.ps1", "{}", (_, _) => throw new InvalidOperationException("Must not ask"), false, token);
        Check(never.Status == "拒否", "Never-execute policy was ignored.");

        SkillPackage progressive = Package("progressive-test", "PROCEDURE-SECRET: 必要なときreferences/details.mdを読んでください。", new() { ["references/details.md"] = "REFERENCE-SECRET: 発送元は青森です。" });
        SkillRegistration progressiveSkill = await files.SaveAsync(progressive, null, null, token); int calls = 0;
        var runner = new SkillRunner(store);
        List<AppliedSkill> applied = await runner.PrepareAsync(new("auto", [progressiveSkill], []), "発送元について調べて",
            (input, _, _, _) =>
            {
                calls++;
                if (calls == 1) { Check(!input.Contains("PROCEDURE-SECRET", StringComparison.Ordinal) && !input.Contains("REFERENCE-SECRET", StringComparison.Ordinal), "Discovery eagerly loaded instructions or resources."); return Task.FromResult("{\"skills\":[\"progressive-test\"]}"); }
                if (calls == 2) { Check(input.Contains("PROCEDURE-SECRET", StringComparison.Ordinal) && !input.Contains("REFERENCE-SECRET", StringComparison.Ordinal), "Progressive instruction loading failed."); return Task.FromResult("{\"actions\":[{\"skill\":\"progressive-test\",\"action\":\"read\",\"path\":\"references/details.md\",\"input\":\"{}\"}]}"); }
                return Task.FromResult("{\"actions\":[]}");
            }, null, true, _ => { }, _ => { }, token);
        Check(applied.Single().Resources.Single().Text.Contains("青森", StringComparison.Ordinal) && calls == 3, "Reference loading or its usage record failed.");
        KnowledgeContext combined = SkillRunner.WithKnowledge(new("instructions", Enumerable.Range(1, 8).Select(n => new KnowledgeReference(n, "project", "project-source", "project data")).ToList()), applied);
        Check(combined.References.Count == 8 && combined.References.Select(r => r.Number).Distinct().Count() == 8 && combined.References.Last().Title.Contains("references/details.md", StringComparison.Ordinal) && combined.References.Last().Text.Contains("青森", StringComparison.Ordinal), "Skill evidence was missing from citation snapshots or exceeded the reference budget.");
        bool revoked = false; int planCalls = 0;
        List<AppliedSkill> revokedUses = await new SkillRunner(store, _ => "never").PrepareAsync(new("manual", [scriptSkill], [scriptSkill.Id]), "集計",
            (_, _, _, _) => Task.FromResult(++planCalls == 1 ? "{\"actions\":[{\"skill\":\"number-summary\",\"action\":\"run\",\"path\":\"scripts/summarize.ps1\",\"input\":\"{\\\"values\\\":[1,2]}\"}]}" : "{\"actions\":[]}"),
            (_, _) => { revoked = true; return Task.FromResult(true); }, false, _ => { }, _ => { }, token);
        Check(!revoked && revokedUses.Single().Executions.Single().Status == "拒否", "A queued job ignored a revoked execution policy.");

        SkillPackage sleeping = Package("cancel-test", "test", new() { ["scripts/sleep.ps1"] = "param([string]$InputJson,[string]$OutputDirectory)\n[IO.File]::WriteAllText((Join-Path $OutputDirectory 'started.tmp'),[string]$PID)\n[IO.File]::Move((Join-Path $OutputDirectory 'started.tmp'),(Join-Path $OutputDirectory 'started.txt'))\nStart-Sleep -Seconds 60\n" });
        SkillRegistration sleepingSkill = await files.SaveAsync(sleeping, null, null, token);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token); string? marker = null;
        Task<SkillExecution> running = scriptRunner.RunAsync(sleepingSkill, "scripts/sleep.ps1", "{}", (request, _) => { marker = Path.Combine(request.OutputDirectory, "started.txt"); return Task.FromResult(true); }, false, cancel.Token);
        while (marker is null || !File.Exists(marker)) { await Task.Delay(20, token); }
        int pid = int.Parse(await File.ReadAllTextAsync(marker, token), System.Globalization.CultureInfo.InvariantCulture); cancel.Cancel(); bool canceled = false;
        try { await running; } catch (OperationCanceledException) { canceled = true; }
        bool exited; try { using var process = System.Diagnostics.Process.GetProcessById(pid); exited = process.HasExited; } catch (ArgumentException) { exited = true; }
        Check(canceled && exited, "Canceling a skill left its helper running.");

        string oldState = JsonSerializer.Serialize(new AppState(2, new(), []), AppStore.JsonOptions);
        await File.WriteAllTextAsync(Path.Combine(root, "state-knowledge.json"), oldState, token);
        var projects = new List<KnowledgeProject> { new(a, "A", "", "project-only", false, [], []), new(b, "B", "", "project-only", false, [], []) };
        var state = new AppState(3, new(), [], projects, Skills: [common, privateA, privateB]);
        AppStore.Validate(state); await store.SaveAsync(state);
        Check((await store.LoadAsync()).Skills?.Count == 3 && await File.ReadAllTextAsync(Path.Combine(root, "state-knowledge.json"), token) == oldState, "Skills migration lost registrations or overwrote the older app's state.");
        string tampered = Path.Combine(root, "Skills", progressiveSkill.Id.ToString("N"), progressiveSkill.Revision, "references", "details.md");
        await File.WriteAllTextAsync(tampered, "MODIFIED", token); bool changedRejected = false;
        try { await files.ReadFileAsync(progressiveSkill, "references/details.md", token); } catch (InvalidDataException) { changedRejected = true; }
        Check(changedRejected, "A modified stored skill file bypassed integrity verification.");
        Console.WriteLine("Skills: YAML/ZIP, path containment, project scopes, immutable versions, staged discovery, explicit/auto/off selection, PowerShell approval/output/cancel, permission revocation and migration: OK");
    }
}
