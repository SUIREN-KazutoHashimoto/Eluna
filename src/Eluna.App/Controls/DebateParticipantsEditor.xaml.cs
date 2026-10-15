using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Eluna.App.Models;

namespace Eluna.App.Controls;

public sealed record DebateModelChoice(string Id, string Name, string Detail, bool Available = true);
public sealed class DebateParticipantDraft : ObservableObject
{
    private string _name, _instruction; private DebateModelChoice _model; private int _order;
    public string Id { get; }
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Instruction { get => _instruction; set => Set(ref _instruction, value); }
    public DebateModelChoice SelectedModel { get => _model; set { if (value is not null) { Set(ref _model, value); } } }
    public int Order { get => _order; set { if (Set(ref _order, value)) { Notify(nameof(OrderLabel)); } } }
    public string OrderLabel => Order.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
    public DebateParticipantDraft(DebateRole role, DebateModelChoice model) { Id = role.Id; _name = role.Name; _instruction = role.Instruction; _model = model; }
    public DebateRole Save() => new(Id, Name.Trim(), Instruction.Trim(), SelectedModel.Id.Length == 0 ? null : SelectedModel.Id);
}

public partial class DebateParticipantsEditor : UserControl, INotifyPropertyChanged
{
    private readonly string _dragFormat = "Eluna.Debate." + Guid.NewGuid().ToString("N");
    private string? _pressed; private Point _start; private long _lastScroll; private string _status = "";
    private UIElement? _pressedSource;
    private readonly ObservableCollection<DebateParticipantDraft> _participants = [];
    private ListCollectionView? _modelView;
    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<DebateModelChoice> ModelChoices { get; } = [];
    public bool CanAdd => _participants.Count < 6;
    public bool CanRemove => _participants.Count > 2;
    public bool CanMoveUp => ParticipantList.SelectedIndex > 0;
    public bool CanMoveDown => ParticipantList.SelectedIndex >= 0 && ParticipantList.SelectedIndex < _participants.Count - 1;
    public string Status => _status;
    internal IReadOnlyList<DebateParticipantDraft> Participants => _participants;

    public DebateParticipantsEditor(IEnumerable<DebateRole> roles, IEnumerable<LocalModel> models)
    {
        InitializeComponent();
        ModelChoices.Add(new("", "選択中のモデル", "チャット左上で選択したモデルを使用"));
        foreach (LocalModel model in models) { ModelChoices.Add(new(model.Id, model.DisplayName, model.SizeLabel)); }
        foreach (DebateRole role in roles)
        {
            DebateModelChoice? choice = ModelChoices.FirstOrDefault(m => m.Id == (role.ModelId ?? ""));
            if (choice is null) { choice = new(role.ModelId!, "見つからないモデル · " + role.Name, "モデルを再取得するか、割り当てを変更してください", false); ModelChoices.Add(choice); }
            _participants.Add(new(role, choice));
        }
        ParticipantList.ItemsSource = _participants; ParticipantList.SelectedIndex = 0;
        _modelView = new ListCollectionView(ModelChoices.Where(m => m.Available).ToList());
        _modelView.Filter = item => item is DebateModelChoice m && m.Name.Contains(ModelSearch.Text, StringComparison.OrdinalIgnoreCase); ModelList.ItemsSource = _modelView;
        Updated(); Notice("モデルの追加・変更と発言順は「保存」で反映します。司会はチャットで選択したモデルです。");
        PreviewMouseLeftButtonUp += (_, _) => ReleasePointer();
        LostMouseCapture += (_, _) => { if (_pressedSource is not null && Mouse.Captured != _pressedSource) { ReleasePointer(); } };
        Unloaded += (_, _) => { ReleasePointer(); ClearHints(); };
        QueryContinueDrag += (_, e) => { if (!IsVisible || !IsEnabled) { e.Action = DragAction.Cancel; } };
    }
    internal List<DebateRole> GetRoles() => _participants.Select(p => p.Save()).ToList();
    internal DataObject ModelData(string id) => DragData("model:" + id);
    internal DataObject ParticipantData(string id) => DragData("participant:" + id);
    private DataObject DragData(string value) { var data = new DataObject(); data.SetData(_dragFormat, value, false); return data; }
    private string? ReadData(IDataObject data)
    {
        if (!data.GetDataPresent(_dragFormat, false) || data.GetData(_dragFormat, false) is not string text) { return null; }
        return text.StartsWith("model:", StringComparison.Ordinal) && ModelChoices.Any(m => m.Available && m.Id == text[6..]) ||
            text.StartsWith("participant:", StringComparison.Ordinal) && _participants.Any(p => p.Id == text[12..]) ? text : null;
    }
    internal bool ApplyDrop(IDataObject data, DebateParticipantDraft? target = null, bool after = false, bool remove = false)
    {
        string? payload = ReadData(data); if (payload is null) { return false; }
        if (target is not null && !_participants.Contains(target)) { return false; }
        if (payload.StartsWith("model:", StringComparison.Ordinal))
        {
            if (remove) { return false; }
            DebateModelChoice model = ModelChoices.First(m => m.Id == payload[6..]);
            if (target is not null) { target.SelectedModel = model; ParticipantList.SelectedItem = target; Notice("モデルを変更しました。「保存」で反映します。"); return true; }
            if (!CanAdd) { Notice("参加者は6名までです。既存の参加者へドロップするとモデルを変更できます。"); return false; }
            var draft = new DebateParticipantDraft(new(Guid.NewGuid().ToString("N"), "参加者 " + (_participants.Count + 1), "別の観点から問いを検討し、根拠と改善案を示す。"), model);
            _participants.Add(draft); ParticipantList.SelectedItem = draft; ParticipantList.ScrollIntoView(draft); Updated(); Notice("参加者を追加しました。下の欄で名前・役割を編集できます。"); return true;
        }
        DebateParticipantDraft source = _participants.First(p => p.Id == payload[12..]);
        if (remove) { return Remove(source); }
        int from = _participants.IndexOf(source), to = target is null ? _participants.Count : _participants.IndexOf(target) + (after ? 1 : 0);
        if (from < to) { to--; }
        to = Math.Clamp(to, 0, _participants.Count - 1);
        _participants.Move(from, to); ParticipantList.SelectedItem = source; ParticipantList.ScrollIntoView(source); Updated(); Notice("発言順を変更しました。「保存」で反映します。"); return true;
    }
    private bool Remove(DebateParticipantDraft draft)
    {
        if (!CanRemove) { Notice("ディベートには2名以上の参加者が必要です。"); return false; }
        int index = _participants.IndexOf(draft); _participants.Remove(draft); ParticipantList.SelectedIndex = Math.Min(index, _participants.Count - 1); Updated(); Notice("参加者を外しました。「保存」で反映します。"); return true;
    }
    private void Updated()
    {
        for (int i = 0; i < _participants.Count; i++) { _participants[i].Order = i + 1; }
        foreach (string name in new[] { nameof(CanAdd), nameof(CanRemove), nameof(CanMoveUp), nameof(CanMoveDown) }) { PropertyChanged?.Invoke(this, new(name)); }
    }
    private void Notice(string value) { _status = $"{_participants.Count}/6名 · " + value; PropertyChanged?.Invoke(this, new(nameof(Status))); }
    private void Search_Changed(object sender, TextChangedEventArgs e) => _modelView?.Refresh();
    private void Participant_Selected(object sender, SelectionChangedEventArgs e) => Updated();
    private void Add_Click(object sender, RoutedEventArgs e) { if (sender is FrameworkElement { DataContext: DebateModelChoice model }) { ApplyDrop(ModelData(model.Id)); } }
    private void Remove_Click(object sender, RoutedEventArgs e) { if (ParticipantList.SelectedItem is DebateParticipantDraft draft) { Remove(draft); } }
    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DebateParticipantDraft draft }) { ParticipantList.SelectedItem = draft; DetailsExpander.IsExpanded = true; ParticipantNameBox.Focus(); ParticipantNameBox.SelectAll(); }
    }
    private void Up_Click(object sender, RoutedEventArgs e) => MoveSelected(-1);
    private void Down_Click(object sender, RoutedEventArgs e) => MoveSelected(1);
    private void MoveSelected(int delta)
    {
        if (ParticipantList.SelectedItem is not DebateParticipantDraft draft) { return; }
        int from = _participants.IndexOf(draft), to = from + delta; if (to < 0 || to >= _participants.Count) { return; }
        _participants.Move(from, to); ParticipantList.SelectedItem = draft; ParticipantList.ScrollIntoView(draft); Updated(); Notice("発言順を変更しました。「保存」で反映します。");
    }
    private void Model_Down(object sender, MouseButtonEventArgs e)
    {
        ReleasePointer();
        // Let the plus button keep its normal click/capture behavior.
        for (DependencyObject? node = e.OriginalSource as DependencyObject; node is not null; node = node is Visual ? VisualTreeHelper.GetParent(node) : (node as FrameworkContentElement)?.Parent)
        { if (node is System.Windows.Controls.Primitives.ButtonBase) { return; } }
        if (sender is FrameworkElement { DataContext: DebateModelChoice model } source)
        { _pressed = "model:" + model.Id; _start = e.GetPosition(this); _pressedSource = source; if (!source.CaptureMouse()) { ReleasePointer(); } }
    }
    private void Participant_Down(object sender, MouseButtonEventArgs e)
    {
        ReleasePointer();
        if (sender is FrameworkElement { DataContext: DebateParticipantDraft draft } source)
        { _pressed = "participant:" + draft.Id; _start = e.GetPosition(this); _pressedSource = source; ParticipantList.SelectedItem = draft; }
    }
    private void ReleasePointer()
    {
        UIElement? source = _pressedSource; _pressed = null; _pressedSource = null;
        if (source is not null && Mouse.Captured == source) { source.ReleaseMouseCapture(); }
    }
    private void Source_Up(object sender, MouseButtonEventArgs e) => ReleasePointer();
    private void Source_Move(object sender, MouseEventArgs e)
    {
        if (_pressed is null || sender is not DependencyObject source) { return; }
        if (e.LeftButton != MouseButtonState.Pressed || Mouse.Captured != _pressedSource) { ReleasePointer(); return; }
        Point point = e.GetPosition(this); if (Math.Abs(point.X - _start.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - _start.Y) < SystemParameters.MinimumVerticalDragDistance) { return; }
        string payload = _pressed; ReleasePointer();
        try { DragDrop.DoDragDrop(source, DragData(payload), payload.StartsWith("model:", StringComparison.Ordinal) ? DragDropEffects.Copy : DragDropEffects.Move); }
        finally { ClearHints(); }
    }
    private void Card_Over(object sender, DragEventArgs e)
    {
        e.Handled = true; e.Effects = DragDropEffects.None; ClearHints();
        string? payload = ReadData(e.Data); if (sender is not Border card || payload is null) { return; }
        bool model = payload.StartsWith("model:", StringComparison.Ordinal); e.Effects = model ? DragDropEffects.Copy : DragDropEffects.Move;
        if (model) { card.SetResourceReference(Border.BorderBrushProperty, "AccentBrush"); }
        else { bool after = e.GetPosition(card).Y >= card.ActualHeight / 2; if (card.FindName(after ? "DropAfter" : "DropBefore") is Border line) { line.Visibility = Visibility.Visible; } }
        AutoScroll(e);
    }
    private void Card_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true; e.Effects = DragDropEffects.None;
        if (sender is Border { DataContext: DebateParticipantDraft target } card && ApplyDrop(e.Data, target, e.GetPosition(card).Y >= card.ActualHeight / 2))
        { e.Effects = ReadData(e.Data)?.StartsWith("model:", StringComparison.Ordinal) == true ? DragDropEffects.Copy : DragDropEffects.Move; }
        ClearHints();
    }
    private void List_Over(object sender, DragEventArgs e)
    {
        e.Handled = true; e.Effects = DragDropEffects.None; ClearHints(); string? payload = ReadData(e.Data);
        if (payload is null || payload.StartsWith("model:", StringComparison.Ordinal) && !CanAdd) { return; }
        e.Effects = payload.StartsWith("model:", StringComparison.Ordinal) ? DragDropEffects.Copy : DragDropEffects.Move;
        AddZoneLabel.Text = payload.StartsWith("model:", StringComparison.Ordinal) ? "＋ このモデルで参加者を追加" : "最後の発言順へ移動";
        AddZone.SetResourceReference(Border.BorderBrushProperty, "AccentBrush"); AutoScroll(e);
    }
    private void List_Drop(object sender, DragEventArgs e) { e.Handled = true; e.Effects = ApplyDrop(e.Data) ? ReadData(e.Data)?.StartsWith("model:", StringComparison.Ordinal) == true ? DragDropEffects.Copy : DragDropEffects.Move : DragDropEffects.None; ClearHints(); }
    private void Remove_Over(object sender, DragEventArgs e)
    {
        e.Handled = true; ClearHints(); e.Effects = CanRemove && ReadData(e.Data)?.StartsWith("participant:", StringComparison.Ordinal) == true ? DragDropEffects.Move : DragDropEffects.None;
        if (e.Effects != DragDropEffects.None) { RemoveZone.SetResourceReference(Border.BorderBrushProperty, "AccentBrush"); }
    }
    private void Remove_Drop(object sender, DragEventArgs e) { e.Handled = true; e.Effects = ApplyDrop(e.Data, remove: true) ? DragDropEffects.Move : DragDropEffects.None; ClearHints(); }
    private void Target_Leave(object sender, DragEventArgs e) => ClearHints();
    private void ClearHints()
    {
        AddZone?.SetResourceReference(Border.BorderBrushProperty, "StrokeBrush"); RemoveZone?.SetResourceReference(Border.BorderBrushProperty, "StrokeBrush");
        if (AddZoneLabel is not null) { AddZoneLabel.Text = "＋ ここへモデルをドロップして参加者を追加"; }
        if (ParticipantList is null) { return; }
        foreach (Border border in Descendants<Border>(ParticipantList))
        {
            if (border.Name is "DropBefore" or "DropAfter") { border.Visibility = Visibility.Collapsed; }
            if (border.Name == "ParticipantCard") { border.BorderBrush = Brushes.Transparent; }
        }
    }
    private void AutoScroll(DragEventArgs e)
    {
        if (Environment.TickCount64 - _lastScroll < 100) { return; }
        _lastScroll = Environment.TickCount64;
        ScrollViewer? scroll = Descendants<ScrollViewer>(ParticipantList).FirstOrDefault(); if (scroll is null) { return; }
        double y = e.GetPosition(ParticipantList).Y; if (y < 24) { scroll.LineUp(); } else if (y > ParticipantList.ActualHeight - 24) { scroll.LineDown(); }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { DependencyObject child = VisualTreeHelper.GetChild(parent, i); if (child is T value) { yield return value; } foreach (T descendant in Descendants<T>(child)) { yield return descendant; } }
    }
}
