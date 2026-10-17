using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.Checks;

internal static class ExperienceChecks
{
    private static void Check(bool ok, string message) { if (!ok) { throw new InvalidOperationException(message); } }
    internal static async Task RunAsync(string root)
    {
        var journal = new NavigationJournal(); Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid(), d = Guid.NewGuid();
        journal.Visit(a); journal.Visit(a); Check(!journal.CanBack, "Repeated navigation polluted history."); journal.Visit(b); journal.Visit(c);
        Check(journal.Move(-1, _ => true) == b && journal.CanForward, "Back history was not reversible."); journal.Visit(d); Check(!journal.CanForward && journal.Move(-1, id => id != b) == a, "Branching or deleted destinations broke history.");
        Check(!journal.CanMove(-1, _ => false) && !journal.CanMove(1, _ => false), "Unavailable destinations left navigation enabled.");
        for (int i = 0; i < 200; i++) { journal.Visit(Guid.NewGuid()); }
        int count = 0; while (journal.Move(-1, _ => true) is not null) { count++; }
        Check(count == 99, "Navigation history grew without a bound.");
        Guid server = Guid.NewGuid(); var projects = new[] { new KnowledgeProject(server, "読書会", "", "project-only", false, [], []) }; var member = new AgentAccount(Guid.NewGuid(), "星乃");
        var channel = new Conversation { Title = "相談", ProjectId = server, Room = new("channel", [member.Id]) }; var voice = new Conversation { Title = "夜の会話", ProjectId = server, Room = new("voice", [member.Id]) }; var dm = new Conversation { Title = "星乃とのDM", Room = new("dm", [member.Id]) }; var archived = new Conversation { Title = "消した会話", IsArchived = true };
        Conversation[] rooms = [channel, voice, dm, archived];
        Check(QuickNavigation.Find(rooms, projects, [member], "#相談", server, dm.Id).Single().Room == channel.Id, "Channel prefix crossed navigation scopes.");
        Check(QuickNavigation.Find(rooms, projects, [member], "!", null, dm.Id).Single().Room == voice.Id && QuickNavigation.Find(rooms, projects, [member], "*読書", null, dm.Id).Single().Server == server, "Server/voice prefixes were ignored.");
        Check(QuickNavigation.Find(rooms, projects, [member], "@星乃", null, channel.Id).Count == 2 && QuickNavigation.Find(rooms, projects, [member], "相談 読書会", null, dm.Id).Single().Room == channel.Id, "Members or multi-term navigation failed.");
        Check(QuickNavigation.Find(rooms, projects, [member], "消した", null, null).Count == 0 && QuickNavigation.Find(rooms, projects, [member], "存在しない", null, null).Count == 0, "Archived or absent targets were offered.");
        var many = Enumerable.Range(0, 10000).Select(i => new Conversation { Title = "channel " + i, Room = new("channel", []) }).ToArray(); var watch = Stopwatch.StartNew(); Check(QuickNavigation.Find(many, projects, [], "#channel", null, null).Count == 40, "Quick switch results were unbounded."); watch.Stop(); Check(watch.Elapsed < TimeSpan.FromSeconds(2), "Navigation search blocked for too long with 10,000 rooms.");
        AppSettings old = JsonSerializer.Deserialize<AppSettings>("{}", AppStore.JsonOptions)!; Check(!old.ReduceMotion, "Legacy settings lost their animation default.");
        var store = new AppStore(root); await store.SaveAsync(new(4, old with { ReduceMotion = true }, [channel.Save()], Projects: projects.ToList(), Agents: [member])); Check((await store.LoadAsync()).Settings.ReduceMotion, "Reduced motion was not persisted.");
        Console.WriteLine("Experience: navigation history/branching/deleted targets/bounds, quick switch prefixes/scopes/members/10k search, reduced-motion migration/persistence: OK");
    }
}
