using System.IO;
using System.Text;
using System.Text.Json;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.Checks;

internal static class KnowledgeChecks
{
    internal static async Task RunAsync(string root)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var a = new KnowledgeProject(Guid.NewGuid(), "A", "A_SCOPE_INSTRUCTION", "project-only", true,
            [new(Guid.NewGuid(), "secret-handbook.txt", "secret harbor approval code = A_DOCUMENT_4826", now),
             new(Guid.NewGuid(), "secret-disabled.txt", "secret harbor DISABLED_DOCUMENT", now, false)],
            [new(Guid.NewGuid(), "secret harbor A_MEMORY_1379", now)]);
        var b = new KnowledgeProject(Guid.NewGuid(), "B", "B_INSTRUCTION_SECRET", "project-only", true,
            [new(Guid.NewGuid(), "secret-other.txt", "secret harbor B_DOCUMENT_SECRET", now)], [new(Guid.NewGuid(), "secret harbor B_MEMORY_SECRET", now)]);
        var current = new Conversation { ProjectId = a.Id };
        var other = new Conversation { ProjectId = a.Id, Title = "secret harbor previous chat" };
        other.Messages.Add(new() { Role = "user", Content = "secret harbor OLD_BRANCH_SECRET" });
        other.Fork(0, "active"); other.Messages.Add(new() { Role = "user", Content = "secret harbor A_CHAT_5531" });
        other.Messages.Add(new() { Role = "assistant", Content = "secret harbor INCOMPLETE_SECRET", IsGenerating = true });
        other.Draft = "secret harbor DRAFT_SECRET";
        var archived = new Conversation { ProjectId = a.Id, IsArchived = true }; archived.Messages.Add(new() { Content = "secret harbor ARCHIVED_SECRET" });
        var outside = new Conversation(); outside.Messages.Add(new() { Content = "secret harbor OUTSIDE_CHAT_SECRET" });
        var foreign = new Conversation { ProjectId = b.Id }; foreign.Messages.Add(new() { Content = "secret harbor OTHER_PROJECT_CHAT_SECRET" });
        var state = new AppState(1, new(SystemInstruction: "GLOBAL_INSTRUCTION_SECRET", SelectedProjectId: a.Id),
            [current.Save(), other.Save(), archived.Save(), outside.Save(), foreign.Save()], [a, b], [new(Guid.NewGuid(), "secret harbor COMMON_MEMORY_SECRET", now)]);
        KnowledgeContext knowledge = KnowledgeService.Build(state, current, "secret harbor approval code", 16384, default);
        string sent = knowledge.Instruction + "\n" + knowledge.ReferenceText;
        Require(sent.Contains("A_DOCUMENT_4826", StringComparison.Ordinal) && sent.Contains("A_MEMORY_1379", StringComparison.Ordinal) && sent.Contains("A_CHAT_5531", StringComparison.Ordinal), "A project could not use its documents, saved memory and active sibling chat.");
        foreach (string excluded in new[] { "B_INSTRUCTION_SECRET", "B_DOCUMENT_SECRET", "B_MEMORY_SECRET", "GLOBAL_INSTRUCTION_SECRET", "COMMON_MEMORY_SECRET", "OUTSIDE_CHAT_SECRET", "OTHER_PROJECT_CHAT_SECRET", "OLD_BRANCH_SECRET", "INCOMPLETE_SECRET", "DRAFT_SECRET", "ARCHIVED_SECRET", "DISABLED_DOCUMENT" })
        { Require(!sent.Contains(excluded, StringComparison.Ordinal), "Project-only isolation failed: " + excluded); }
        Require(sent.Contains("A_SCOPE_INSTRUCTION", StringComparison.Ordinal) && !knowledge.Prepend([new("user", "question")]).Any(m => m.Content.Contains("COMMON_MEMORY_SECRET", StringComparison.Ordinal)), "The model input could include out-of-scope data.");
        var noChats = a with { ReferenceChats = false };
        Require(!KnowledgeService.Build(state with { Projects = [noChats, b] }, current, "secret harbor", 16384, default).ReferenceText.Contains("A_CHAT_5531", StringComparison.Ordinal), "Turning off sibling-chat references had no effect.");
        var shared = a with { MemoryMode = "shared" };
        KnowledgeContext sharedContext = KnowledgeService.Build(state with { Projects = [shared, b] }, current, "secret harbor", 16384, default);
        Require(sharedContext.Instruction.Contains("GLOBAL_INSTRUCTION_SECRET", StringComparison.Ordinal) && sharedContext.ReferenceText.Contains("COMMON_MEMORY_SECRET", StringComparison.Ordinal) &&
            !sharedContext.ReferenceText.Contains("OUTSIDE_CHAT_SECRET", StringComparison.Ordinal) && !sharedContext.ReferenceText.Contains("B_DOCUMENT_SECRET", StringComparison.Ordinal), "Shared-memory opt-in leaked unrelated chat/project history or ignored common memories.");
        KnowledgeContext general = KnowledgeService.Build(state, outside, "secret harbor", 16384, default);
        Require(general.ReferenceText.Contains("COMMON_MEMORY_SECRET", StringComparison.Ordinal) && !general.ReferenceText.Contains("A_", StringComparison.Ordinal) && !general.ReferenceText.Contains("B_", StringComparison.Ordinal), "A normal chat read project knowledge.");
        KnowledgeContext removed = KnowledgeService.Build(state with { Projects = [a with { Documents = [], Memories = [], ReferenceChats = false }, b] }, current, "secret harbor", 16384, default);
        Require(removed.References.Count == 0, "Removed knowledge was reused from a previous retrieval.");
        Require(KnowledgeService.Build(state, current, "ZXQW-unrelated-word", 4096, default).References.All(r => r.Kind.Contains("メモリ", StringComparison.Ordinal)), "Unmatched documents appeared as relevant evidence.");
        bool orphan = false; try { KnowledgeService.Build(state, new() { ProjectId = Guid.NewGuid() }, "secret", 4096, default); } catch (InvalidDataException) { orphan = true; }
        Require(orphan, "An unknown project fell back to common memory.");
        using var stopped = new CancellationTokenSource(); stopped.Cancel();
        bool canceled = false; try { KnowledgeService.Build(state, current, "secret", 4096, stopped.Token); } catch (OperationCanceledException) { canceled = true; }
        Require(canceled, "Knowledge retrieval ignored cancellation.");

        var unicode = a with { Documents = [new(Guid.NewGuid(), new string('x', 1500), string.Concat(Enumerable.Repeat("港の承認コード😀 HARBOR-4826。", 2000)), now)], Memories = [], ReferenceChats = false };
        KnowledgeContext bounded = KnowledgeService.Build(state with { Projects = [unicode, b] }, current, "港の承認コード HARBOR-4826", 4096, default);
        Require(bounded.References.Count is > 0 and <= 8 && bounded.References.All(r => r.Text.Length <= 700 && r.Title.Length <= 512) && bounded.References.Sum(r => r.Text.Length + 100) <= 2048, "Knowledge retrieval exceeded its input or source-title bounds.");
        foreach (KnowledgeReference reference in bounded.References) { new UTF8Encoding(false, true).GetBytes(reference.Text); }
        var answer = new ChatMessage { Role = "assistant", Content = "A_DOCUMENT_4826 [K1]", Knowledge = knowledge.References }; current.Messages.Add(answer);
        AppState saved = state with { Conversations = [current.Save(), other.Save(), archived.Save(), outside.Save(), foreign.Save()] };
        AppStore.Validate(saved);
        Directory.CreateDirectory(root); var store = new AppStore(root);
        await store.SaveAsync(new(1, new(), [outside.Save()]));
        string legacyText = await File.ReadAllTextAsync(Path.Combine(root, "state.json"));
        await store.SaveAsync(saved);
        Require(File.Exists(Path.Combine(root, "state-knowledge.json")) && await File.ReadAllTextAsync(Path.Combine(root, "state.json")) == legacyText, "Knowledge migration overwrote the history used by older releases.");
        AppState loaded = await store.LoadAsync(); Conversation restored = Conversation.Restore(loaded.Conversations[0]);
        Require(loaded.SchemaVersion == 2 && restored.ProjectId == a.Id && restored.Messages[0].Knowledge.SequenceEqual(knowledge.References) && loaded.Projects![0].Documents[0].Text == a.Documents[0].Text && loaded.CommonMemories!.Count == 1, "Project membership, documents, shared memories or generation-time references did not survive saving.");
        await File.WriteAllTextAsync(Path.Combine(root, "state.json"), "{older application updated its separate history}");
        Require((await store.LoadAsync()).Projects!.Count == 2, "An older release's separate file replaced project state.");
        await store.SaveAsync(loaded with { Projects = [a with { Instruction = "changed" }, b] });
        FileInfo backup = store.GetBackups().First(); await store.RestoreBackupAsync(backup.FullName);
        Require((await store.LoadAsync()).Projects![0].Instruction == a.Instruction && (await store.LoadAsync()).SchemaVersion == 2, "Knowledge-aware backup restoration lost the original scope or format.");
        Require(store.DocumentDraftPath(a.Id) != store.DocumentDraftPath(b.Id) && store.DocumentDraftPath(null) != store.DocumentDraftPath(a.Id), "Generated document drafts share a path across knowledge scopes.");
        string export = Path.Combine(root, "projects-export.json"); await store.ExportAsync(loaded, export);
        Require((await store.ReadExportAsync(export)).Projects!.Count == 2, "Export lost project knowledge.");
        foreach (AppState invalid in new[]
        {
            saved with { Projects = [a, a] }, saved with { Projects = [a with { MemoryMode = "unknown" }, b] },
            saved with { CommonMemories = [new(Guid.NewGuid(), new string('x', 4001), now)] }, saved with { Projects = [] }
        })
        { bool rejected = false; try { AppStore.Validate(invalid); } catch (InvalidDataException) { rejected = true; } Require(rejected, "Malformed or orphaned project data was accepted."); }
        AppState legacy = JsonSerializer.Deserialize<AppState>("{\"schemaVersion\":1,\"settings\":{},\"conversations\":[]}", AppStore.JsonOptions)!;
        AppStore.Validate(legacy); Require(legacy.Projects is null && legacy.CommonMemories is null, "Old history incorrectly acquired knowledge scopes.");
        Console.WriteLine("Projects: project-only/shared/general isolation, active sibling branches, archived/draft/incomplete exclusion, removal, unknown scopes, cancellation, Unicode budgets, persistence/export and legacy data: OK");
    }
    private static void Require(bool condition, string message) { if (!condition) { throw new InvalidOperationException(message); } }
}
