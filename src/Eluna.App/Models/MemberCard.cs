namespace Eluna.App.Models;

public sealed class MemberCard : ObservableObject
{
    public AgentAccount Account { get; private set; }
    public string Name => Account.Name;
    public string Icon => Account.Icon;
    private string _status = "";
    public string Status => _status;
    public string Detail => Status.Length > 0 ? Status : Account.Specialty;
    public string Hint => Name + (Status.Length > 0 ? "\n" + Status : "") + (Account.Specialty.Length > 0 ? "\n" + Account.Specialty : "");
    public bool HasActivity => !Account.IsLocal && Status.Length > 0;
    internal MemberCard(AgentAccount account) { Account = account; }
    internal void Update(AgentAccount account, string status)
    {
        if (account == Account && status == _status) { return; }
        Account = account; _status = status; Notify(nameof(Name)); Notify(nameof(Icon)); Notify(nameof(Status)); Notify(nameof(Detail)); Notify(nameof(Hint)); Notify(nameof(HasActivity));
    }
}
