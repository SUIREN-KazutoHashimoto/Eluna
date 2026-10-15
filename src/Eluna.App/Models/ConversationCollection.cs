using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Eluna.App.Models;

public sealed class ConversationCollection : ObservableCollection<Conversation>
{
    public void AddRange(IEnumerable<Conversation> conversations)
    {
        CheckReentrancy(); foreach (Conversation conversation in conversations) { Items.Add(conversation); }
        ResetView();
    }
    public void ReplaceAll(IEnumerable<Conversation> conversations)
    {
        Conversation[] next = conversations.ToArray(); CheckReentrancy(); Items.Clear(); foreach (Conversation conversation in next) { Items.Add(conversation); }
        ResetView();
    }
    public void RemoveRange(IEnumerable<Conversation> conversations)
    { HashSet<Conversation> removed = conversations.ToHashSet(); ReplaceAll(Items.Where(c => !removed.Contains(c))); }
    private void ResetView()
    { OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count))); OnPropertyChanged(new PropertyChangedEventArgs("Item[]")); OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset)); }
}
