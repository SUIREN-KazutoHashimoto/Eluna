using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using Eluna.App.Models;
using Eluna.App.Services;
using Eluna.App.ViewModels;

namespace Eluna.App;

public partial class InferenceQueueWindow : Window
{
    private readonly ChatViewModel _chat;
    public InferenceQueueWindow(ChatViewModel chat)
    {
        InitializeComponent(); _chat = chat; DataContext = chat; Appearance.Attach(this);
        var view = new ListCollectionView(chat.Inferences.Items);
        view.SortDescriptions.Add(new(nameof(InferenceJob.Priority), ListSortDirection.Ascending));
        view.LiveSortingProperties.Add(nameof(InferenceJob.Priority)); view.IsLiveSorting = true;
        QueueList.ItemsSource = view; Closed += (_, _) => view.IsLiveSorting = false;
    }
    private void Open_Click(object sender, RoutedEventArgs e)
    { if (sender is FrameworkElement { DataContext: InferenceJob job }) { _chat.OpenInference(job); Owner?.Activate(); } }
    private void Resume_Click(object sender, RoutedEventArgs e)
    { if (sender is FrameworkElement { DataContext: InferenceJob job }) { _chat.Inferences.Resume(job); } }
    private void Cancel_Click(object sender, RoutedEventArgs e)
    { if (sender is FrameworkElement { DataContext: InferenceJob job }) { _chat.Inferences.Cancel(job); } }
    private void Clear_Click(object sender, RoutedEventArgs e) => _chat.Inferences.ClearFinished();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
