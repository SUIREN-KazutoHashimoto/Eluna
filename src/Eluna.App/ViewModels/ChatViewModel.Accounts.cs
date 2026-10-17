using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.App.ViewModels;

public sealed partial class ChatViewModel
{
    public async Task AddStarterAccountsAsync()
    {
        List<AgentAccount> cast = AccountDefaults.Cast();
        List<AgentAccount> missing = cast.Where(a => Agents.All(existing => existing.Id != a.Id)).ToList();
        var names = Agents.Select(a => a.Name).Append(Self.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < missing.Count; i++)
        {
            AgentAccount account = missing[i]; string name = account.Name; int suffix = 2;
            while (!names.Add(name)) { name = account.Name + suffix++; }
            missing[i] = account with { Name = name };
        }
        WorkspaceService.Validate(CaptureState() with { Agents = [.. Agents, .. missing] });
        foreach (AgentAccount account in missing) { Agents.Add(account); }
        RefreshRoom(); await PersistSafelyAsync();
    }
    public async Task RetireAccountAsync(Guid id)
    {
        AgentAccount account = Agents.First(a => a.Id == id);
        await Task.WhenAll(CancelWorkspaceRequests(agent: id));
        foreach (Conversation room in Conversations.Where(c => c.Room?.Members.Contains(id) == true))
        {
            CancelSocialCycle(room.Id); RoomProfile profile = room.Room!;
            List<Guid> members = profile.Members.Where(member => member != id).ToList();
            room.Room = profile with
            {
                Members = members,
                DefaultAgentId = profile.DefaultAgentId == id ? members.Select(m => (Guid?)m).FirstOrDefault() : profile.DefaultAgentId,
                PastMembers = [.. (profile.PastMembers ?? []).Append(id).Distinct()]
            };
        }
        Agents[Agents.IndexOf(account)] = account with { Enabled = false, Retired = true };
        RefreshRoom(); await PersistSafelyAsync();
    }
}
