using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Eluna.App.Models;

namespace Eluna.App.Controls;

public partial class MemoryEditor : UserControl
{
    private readonly ObservableCollection<MemoryNote> _notes = [];
    public event EventHandler? Changed;
    public MemoryEditor() { InitializeComponent(); NotesList.ItemsSource = _notes; }
    internal void LoadNotes(IEnumerable<MemoryNote> notes) { _notes.Clear(); foreach (MemoryNote note in notes) { _notes.Add(note); } NotesList.SelectedIndex = _notes.Count > 0 ? 0 : -1; }
    internal List<MemoryNote> GetNotes() => [.. _notes];
    private void Selection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (MemoryText is null) { return; }
        MemoryText.Text = (NotesList.SelectedItem as MemoryNote)?.Text ?? "";
        MemoryText.ScrollToHome(); EditButton.IsEnabled = RemoveButton.IsEnabled = NotesList.SelectedItem is MemoryNote;
    }
    private void Add_Click(object sender, RoutedEventArgs e) => Edit(null);
    private void Edit_Click(object sender, RoutedEventArgs e) { if (NotesList.SelectedItem is MemoryNote note) { Edit(note); } }
    private void Remove_Click(object sender, RoutedEventArgs e) { if (NotesList.SelectedItem is MemoryNote note) { _notes.Remove(note); Changed?.Invoke(this, EventArgs.Empty); } }
    private void Edit(MemoryNote? current)
    {
        if (current is null && _notes.Count >= 100) { StatusText.Text = "保存メモリは100件までです。不要な項目を削除してください。"; return; }
        var editor = new TextPromptWindow("保存メモリ（4,000文字まで）", current?.Text ?? "", true, 4000) { Owner = Window.GetWindow(this) };
        if (editor.ShowDialog() != true || string.IsNullOrWhiteSpace(editor.Result)) { return; }
        var note = new MemoryNote(current?.Id ?? Guid.NewGuid(), editor.Result.Trim(), DateTimeOffset.UtcNow);
        if (current is not null) { _notes[_notes.IndexOf(current)] = note; } else { _notes.Add(note); }
        NotesList.SelectedItem = note;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
