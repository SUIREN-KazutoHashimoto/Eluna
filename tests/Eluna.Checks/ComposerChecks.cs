using System.IO;
using System.Text.Json;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.Checks;

internal static class ComposerChecks
{
    private static void Check(bool ok, string message) { if (!ok) { throw new InvalidOperationException(message); } }
    private static void Reject(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new InvalidOperationException("Invalid emoji settings were accepted."); }
    internal static async Task RunAsync(string root)
    {
        Check(EmojiCatalog.All.Count >= 90 && EmojiCatalog.All.DistinctBy(e => e.Glyph).Count() == EmojiCatalog.All.Count && EmojiCatalog.All.All(e => e.Name.Length > 0 && e.Glyph.Length <= 16 && EmojiCatalog.Categories.Any(c => c.Key == e.Category)), "Emoji catalog is duplicated, incomplete or has invalid categories.");
        Check(EmojiCatalog.Find("moon", "all", []).Single().Glyph == "🌙" && EmojiCatalog.Find("ありがとう", "all", []).Single().Glyph == "🙏" && EmojiCatalog.Find("お祝い", "all", []).Count >= 2, "Emoji search did not cover Japanese/English names and keywords.");
        Check(EmojiCatalog.Find("", "food", []).All(e => e.Category == "food") && EmojiCatalog.Find("存在しない", "all", []).Count == 0 && EmojiCatalog.Find("", "recent", ["👍", "🎉", "👍"]).Select(e => e.Glyph).SequenceEqual(["👍", "🎉"]), "Category/recent/empty emoji search was wrong.");
        List<string> recent = []; foreach (EmojiEntry entry in EmojiCatalog.All.Take(40)) { recent = EmojiCatalog.Remember(entry.Glyph, recent); }
        Check(recent.Count == 24 && recent.Distinct().Count() == 24 && EmojiCatalog.Remember("👍", recent)[0] == "👍" && !EmojiCatalog.Contains("<script>"), "Recent emoji ordering, bounds or whitelist failed.");
        ComposerSnapshot Snap(string text, int start, int length) => new(Guid.NewGuid(), Guid.NewGuid(), text, start, length);
        var selected = Snap("前😀後", 1, 2); Check(ComposerEdits.Insert(selected, "☕").Apply(selected.Text) == "前☕後", "Emoji insertion lost surrounding text or a surrogate pair.");
        var original = Snap("前の案と次の案", 1, 2); ComposerEdit bold = ComposerEdits.Format(original, "bold"); Check(bold.Apply(original.Text) == "前**の案**と次の案" && bold.SelectionLength == 2, "Selected Markdown did not retain the surrounding draft/selection.");
        var wrapped = original with { Text = bold.Apply(original.Text), Start = bold.SelectionStart, Length = bold.SelectionLength }; Check(ComposerEdits.Format(wrapped, "bold").Apply(wrapped.Text) == original.Text, "Repeated formatting did not remove its own wrappers.");
        foreach (string kind in new[] { "italic", "strike", "code", "block" }) { ComposerEdit formatted = ComposerEdits.Format(original, kind); Check(formatted.Apply(original.Text).Contains("の案", StringComparison.Ordinal), "Formatting lost selected Japanese text."); }
        var line = Snap("一行\r\n二行\r\n三行", 4, 4); Check(ComposerEdits.Format(line, "quote").Apply(line.Text) == "一行\r\n> 二行\r\n三行", "Line formatting changed an unselected next line or split CRLF.");
        var mixed = Snap("> 一行\n二行", 0, 7); Check(ComposerEdits.Format(mixed, "quote").Apply(mixed.Text) == "> 一行\n> 二行", "Mixed quotation duplicated an existing prefix.");
        var empty = Snap("", 0, 0); ComposerEdit quote = ComposerEdits.Format(empty, "quote"); Check(quote.Value == "> " && quote.SelectionStart == 2 && quote.SelectionLength == 0, "Empty quote did not put the caret after its marker.");
        var room = new Conversation(); room.Draft = "  "; Check(!room.HasDraft, "Whitespace counted as a draft."); room.Draft = "保存する"; Check(room.HasDraft && room.NavigationStatus.Contains("下書き", StringComparison.Ordinal), "Draft status was not observable."); room.ActivityState = "queued"; Check(room.NavigationStatus.Contains("順番待ち", StringComparison.Ordinal), "Queue status was not visible.");
        var card = new MemberCard(new(Guid.NewGuid(), "星乃", Specialty: "天文学")); card.Update(card.Account, "入力中…"); Check(card.HasActivity && card.Detail == "入力中…", "Member activity did not override idle specialty."); card.Update(card.Account with { Name = "星乃さん" }, ""); Check(!card.HasActivity && card.Detail == "天文学" && card.Hint.Contains("星乃さん", StringComparison.Ordinal), "Member display retained stale status/name.");
        AppSettings legacy = JsonSerializer.Deserialize<AppSettings>("{}", AppStore.JsonOptions)!; Check(legacy.RecentEmoji is null, "Legacy emoji preferences did not migrate.");
        AppState state = new(4, new(RecentEmoji: recent, ReduceMotion: true), []); var store = new AppStore(root); await store.SaveAsync(state); Check((await store.LoadAsync()).Settings.RecentEmoji!.SequenceEqual(recent), "Recent emoji did not persist.");
        Directory.CreateDirectory(store.BackupsDirectory); string backup = Path.Combine(store.BackupsDirectory, "composer.zip"); await BackupArchive.CreateAsync(store, backup, false); var restored = new AppStore(Path.Combine(root, "restored")); PreparedBackup prepared = await BackupArchive.PrepareAsync(restored, backup); await BackupArchive.ApplyAsync(restored, prepared); AppSettings recovered = (await restored.LoadAsync()).Settings; Check(recovered.ReduceMotion && recovered.RecentEmoji!.SequenceEqual(recent), "Backup dropped composer or motion preferences.");
        Reject(() => AppStore.Validate(state with { Settings = state.Settings with { RecentEmoji = ["👍", "👍"] } })); Reject(() => AppStore.Validate(state with { Settings = state.Settings with { RecentEmoji = ["not an emoji"] } }));
        Console.WriteLine("Composer: bilingual emoji/categories/recents/bounds, UTF-16 replacement/format toggle/CRLF/empty selection, draft/member state, legacy/persistence/validation: OK");
    }
}
