using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Eluna.App.Models;
using Eluna.App.Services;
using Eluna.App.ViewModels;
using Microsoft.Win32;

namespace Eluna.App;

public partial class SkillsWindow : Window
{
    private readonly ChatViewModel _chat;
    private readonly Guid? _projectId;
    private readonly CancellationTokenSource _lifetime = new();
    private SkillRegistration? _selected;
    private SkillPackage? _package;
    private Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);
    private bool _loading = true, _dirty, _busy, _closed;
    public SkillsWindow(ChatViewModel chat)
    {
        InitializeComponent(); _chat = chat; _projectId = chat.SelectedConversation?.ProjectId ?? chat.SelectedProject?.Id; Appearance.Attach(this);
        ScopeText.Text = (_projectId is Guid id ? chat.Projects.First(p => p.Id == id).Name + " · プロジェクト限定" : "共通スキル · 通常チャットで使用") + "\n手順・参考資料・補助コードをまとめ、必要な依頼に適用します。";
        CommonBox.Visibility = _projectId is null ? Visibility.Collapsed : Visibility.Visible;
        CommonBox.IsChecked = _projectId is Guid projectId && chat.Projects.First(p => p.Id == projectId).UseCommonSkills;
        PolicyBox.ItemsSource = new[] { new KeyValuePair<string, string>("ask", "実行ごとに内容を確認（初期値）"), new("never", "コードを実行しない"), new("trusted", "確認済みコードの実行を許可") };
        InstructionLayout.SizeChanged += (_, _) => MetadataScroll.MaxHeight = Math.Clamp(InstructionLayout.ActualHeight - 140 * chat.Settings.UiScale, 24, 200);
        _loading = false; Refresh(); NewDraft();
        Closing += OnClosing;
        Closed += (_, _) => { _closed = true; _lifetime.Cancel(); };
    }
    private void Refresh()
    {
        _loading = true;
        string search = SearchBox.Text;
        SkillList.ItemsSource = _chat.Skills.Where(s => s.ProjectId == _projectId && (s.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || s.Description.Contains(search, StringComparison.OrdinalIgnoreCase))).OrderBy(s => s.Name).ToList();
        if (_selected is not null) { SkillList.SelectedItem = _chat.Skills.FirstOrDefault(s => s.Id == _selected.Id); }
        _loading = false;
    }
    private bool DiscardDraft() => !_dirty || MessageBox.Show(this, "保存していない編集を破棄しますか？", "スキルの編集", MessageBoxButton.YesNo) == MessageBoxResult.Yes;
    private void NewDraft()
    {
        MetadataExpander.IsExpanded = false;
        _loading = true; _selected = null; _package = null; SkillList.SelectedItem = null; _files = new(StringComparer.OrdinalIgnoreCase);
        NameBox.Text = "my-skill"; DescriptionBox.Text = ""; InstructionBox.Text = "# 手順\n\n1. 依頼の目的と条件を確認する。\n2. 作業を行い、根拠と結果を整理する。";
        EnabledBox.IsChecked = true; PolicyBox.SelectedValue = "ask"; VersionText.Text = "新しいスキル"; LicenseText.Text = ""; CompatibilityText.Text = "Windows / Eluna";
        FileList.ItemsSource = null; FileText.Clear(); _dirty = false; UpdateUseState(); _loading = false; StatusText.Text = "名前・説明・手順を入力して保存してください。";
    }
    private async Task Run(Func<CancellationToken, Task> action)
    {
        if (_busy) { return; }
        _busy = true; UpdateUseState(); EditorArea.IsEnabled = SaveButton.IsEnabled = CommonBox.IsEnabled = false;
        try { await action(_lifetime.Token); }
        catch (OperationCanceledException) { if (!_closed) { StatusText.Text = "操作を停止しました。"; } }
        catch (Exception ex) { if (!_closed) { StatusText.Text = ex.Message; } }
        finally { _busy = false; UpdateUseState(); if (!_closed) { EditorArea.IsEnabled = SaveButton.IsEnabled = CommonBox.IsEnabled = true; } }
    }
    private void Edited(object sender, RoutedEventArgs e) { if (!_loading && StatusText is not null) { _dirty = true; UpdateUseState(); StatusText.Text = "未保存の変更があります。"; } }
    private void New_Click(object sender, RoutedEventArgs e) { if (!_busy && DiscardDraft()) { NewDraft(); } }
    private void Search_Changed(object sender, TextChangedEventArgs e) { if (!_loading && _chat is not null) { Refresh(); } }
    private async void Skill_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _busy || SkillList.SelectedItem is not SkillRegistration skill || skill.Id == _selected?.Id) { return; }
        if (!DiscardDraft()) { Refresh(); return; }
        await Run(async token => await LoadAsync(skill, await _chat.SkillFiles.PackageAsync(skill, token)));
    }
    private Task LoadAsync(SkillRegistration? selected, SkillPackage package)
    {
        _loading = true; _selected = selected; _package = package; _files = new(package.Files, StringComparer.OrdinalIgnoreCase);
        NameBox.Text = package.Name; DescriptionBox.Text = package.Description; InstructionBox.Text = package.Instructions;
        EnabledBox.IsChecked = selected?.Enabled ?? true; PolicyBox.SelectedValue = selected?.ExecutionPolicy ?? "ask";
        VersionText.Text = selected is null ? "取り込み内容を確認して保存してください" : "保存した版 · " + selected.Revision[..12];
        LicenseText.Text = package.License;
        CompatibilityText.Text = package.Compatibility;
        RefreshFiles(); _dirty = selected is null; _loading = false;
        StatusText.Text = selected is null ? "まだ登録していません。内容と同梱ファイルを確認して保存してください。" : "保存したスキルを表示しています。";
        return Task.CompletedTask;
    }
    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !DiscardDraft()) { return; }
        var picker = new OpenFileDialog { Filter = "スキル|*.zip;*.skill;SKILL.md|ZIP|*.zip;*.skill|SKILL.md|SKILL.md" };
        if (picker.ShowDialog(this) == true) { await Run(async token => await LoadAsync(null, await _chat.SkillFiles.ReadPackageAsync(picker.FileName, token))); }
    }
    private async void Folder_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !DiscardDraft()) { return; }
        var picker = new OpenFolderDialog { Title = "SKILL.mdを含むスキルフォルダー" };
        if (picker.ShowDialog(this) == true) { await Run(async token => await LoadAsync(null, await _chat.SkillFiles.ReadPackageAsync(picker.FolderName, token))); }
    }
    private void FlushFile()
    {
        if (FileList.SelectedItem is string path && !FileText.IsReadOnly) { _files[path] = Encoding.UTF8.GetBytes(FileText.Text); }
    }
    private void RefreshFiles() { FileList.ItemsSource = _files.Keys.Where(p => !p.Equals("SKILL.md", StringComparison.OrdinalIgnoreCase)).Order().ToArray(); FileText.Clear(); }
    private void File_Edited(object sender, TextChangedEventArgs e) { if (!_loading && FileList.SelectedItem is string path && !FileText.IsReadOnly) { _files[path] = Encoding.UTF8.GetBytes(FileText.Text); Edited(sender, e); } }
    private void File_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (FileText is null) { return; }
        _loading = true; FileText.Clear();
        if (FileList.SelectedItem is string path)
        {
            byte[] bytes = _files[path];
            try
            {
                FileText.Text = bytes.Length <= 128 * 1024 ? SkillFormat.Utf8.GetString(bytes) : "［大きなファイルのためプレビューを省略］";
                FileText.IsReadOnly = bytes.Length > 128 * 1024 || FileText.Text.Contains('\0');
                FileNotice.Text = path + " · " + FileSize.Format(bytes.Length) + (FileText.IsReadOnly ? " · 表示のみ" : " · 編集は保存ボタンで反映");
            }
            catch (DecoderFallbackException) { FileText.Text = "［バイナリファイル］"; FileText.IsReadOnly = true; FileNotice.Text = path + " · " + FileSize.Format(bytes.Length); }
        }
        _loading = false;
    }
    private async void AddFile_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Multiselect = true };
        if (picker.ShowDialog(this) != true) { return; }
        await Run(async token =>
        {
            foreach (string path in picker.FileNames)
            {
                if (new FileInfo(path).Length > 16 * 1024 * 1024 || _files.Count >= SkillStore.MaxFiles) { throw new InvalidDataException("ファイル数・サイズの上限を超えています。"); }
                string prefix = Path.GetExtension(path).Equals(".ps1", StringComparison.OrdinalIgnoreCase) ? "scripts/" : "references/";
                string relative = SkillStore.SafePath(prefix + Path.GetFileName(path));
                if (_files.ContainsKey(relative) && MessageBox.Show(this, relative + "を置き換えますか？", "同梱ファイル", MessageBoxButton.YesNo) != MessageBoxResult.Yes) { continue; }
                _files[relative] = await File.ReadAllBytesAsync(path, token);
            }
            _loading = true; RefreshFiles(); _loading = false; _dirty = true; UpdateUseState(); StatusText.Text = "ファイルを追加しました。保存すると反映します。";
        });
    }
    private void RenameFile_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || FileList.SelectedItem is not string original) { return; }
        var prompt = new TextPromptWindow("同梱ファイルのパス", original) { Owner = this }; if (prompt.ShowDialog() != true) { return; }
        try
        {
            string path = SkillStore.SafePath(prompt.Result.Replace('\\', '/')); if (path.Equals("SKILL.md", StringComparison.OrdinalIgnoreCase) || _files.ContainsKey(path)) { throw new InvalidDataException("既存のファイルと重複しないパスにしてください。"); }
            FlushFile(); byte[] bytes = _files[original]; _files.Remove(original); _files[path] = bytes;
            InstructionBox.Text = InstructionBox.Text.Replace(original, path, StringComparison.Ordinal); RefreshFiles(); FileList.SelectedItem = path; _dirty = true; UpdateUseState(); StatusText.Text = "パスと手順内の参照を変更しました。保存すると反映します。";
        }
        catch (Exception ex) when (ex is IOException or ArgumentException) { StatusText.Text = ex.Message; }
    }
    private void UpdateUseState() { if (UseButton is not null) { UseButton.IsEnabled = _selected is not null && !_dirty && !_busy && _selected.Enabled; } }
    private void RemoveFile_Click(object sender, RoutedEventArgs e)
    { if (FileList.SelectedItem is string path) { _files.Remove(path); _loading = true; RefreshFiles(); _loading = false; _dirty = true; UpdateUseState(); } }
    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveDraftAsync();
    private void Diff_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) { return; }
        FlushFile();
        string markdown = _package is null ? SkillFormat.Create(NameBox.Text.Trim(), DescriptionBox.Text.Trim(), InstructionBox.Text) : SkillFormat.Update(_package, NameBox.Text.Trim(), DescriptionBox.Text.Trim(), InstructionBox.Text);
        var current = new Dictionary<string, byte[]>(_files, StringComparer.OrdinalIgnoreCase) { ["SKILL.md"] = Encoding.UTF8.GetBytes(SkillFormat.WithLicenseAndCompatibility(markdown, LicenseText.Text, CompatibilityText.Text)) };
        var before = new Dictionary<string, byte[]>(_package?.Files ?? new Dictionary<string, byte[]>(), StringComparer.OrdinalIgnoreCase);
        string Options(bool enabled, string policy) => System.Text.Json.JsonSerializer.Serialize(new { Enabled = enabled, ExecutionPolicy = policy }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        before["［Elunaの適用・実行設定］"] = Encoding.UTF8.GetBytes(Options(_selected?.Enabled ?? true, _selected?.ExecutionPolicy ?? "ask"));
        current["［Elunaの適用・実行設定］"] = Encoding.UTF8.GetBytes(Options(EnabledBox.IsChecked == true, PolicyBox.SelectedValue as string ?? "ask"));
        new SkillDiffWindow(before, current) { Owner = this }.Show();
    }
    internal Task SaveDraftAsync() => Run(async token =>
    {
        FlushFile();
        string markdown = _package is null ? SkillFormat.Create(NameBox.Text.Trim(), DescriptionBox.Text.Trim(), InstructionBox.Text) :
            SkillFormat.Update(_package, NameBox.Text.Trim(), DescriptionBox.Text.Trim(), InstructionBox.Text);
        markdown = SkillFormat.WithLicenseAndCompatibility(markdown, LicenseText.Text, CompatibilityText.Text);
        _files["SKILL.md"] = Encoding.UTF8.GetBytes(markdown); SkillPackage package = SkillFormat.Parse(new(_files, StringComparer.OrdinalIgnoreCase));
        SkillRegistration? existing = _selected ?? _chat.Skills.FirstOrDefault(s => s.ProjectId == _projectId && s.Name == package.Name);
        if (_selected is null && existing is not null && MessageBox.Show(this, "同じ名前のスキルを新しい版に更新しますか？予約済みの版は保持します。", "スキルの更新", MessageBoxButton.YesNo) != MessageBoxResult.Yes) { return; }
        string policy = PolicyBox.SelectedValue as string ?? "ask";
        if (policy == "trusted" && (_selected?.ExecutionPolicy != "trusted" || _dirty) && MessageBox.Show(this, "同梱コードはPCのユーザー権限で動き、ファイル・ネットワークにアクセスできます。コードを確認し、この版の自動実行を許可しますか？", "補助コードの実行", MessageBoxButton.YesNo) != MessageBoxResult.Yes) { policy = "ask"; }
        SkillRegistration saved = await _chat.SkillFiles.SaveAsync(package, _projectId, existing, token);
        saved = saved with { Enabled = EnabledBox.IsChecked == true, ExecutionPolicy = policy };
        await _chat.SaveSkillAsync(saved); _selected = saved; _dirty = false; UpdateUseState(); Refresh(); await LoadAsync(saved, package); StatusText.Text = "保存しました。新しい依頼からこの版を使います。";
    });
    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) { StatusText.Text = "先にスキルを保存してください。"; return; }
        var picker = new SaveFileDialog { Filter = "スキルZIP|*.zip|スキルファイル|*.skill", FileName = _selected.Name + ".zip" };
        if (picker.ShowDialog(this) == true) { await Run(async token => { await _chat.SkillFiles.ExportAsync(_selected, picker.FileName, token); StatusText.Text = "書き出しました。"; }); }
    }
    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is not SkillRegistration skill || MessageBox.Show(this, "登録を解除しますか？予約済み・過去の回答用の版ファイルは保持します。", "スキルの登録解除", MessageBoxButton.YesNo) != MessageBoxResult.Yes) { return; }
        await Run(async _ => { await _chat.RemoveSkillAsync(skill); NewDraft(); Refresh(); });
    }
    private async void Use_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _dirty) { StatusText.Text = "スキルを保存してから指定してください。"; return; }
        await Run(async _ =>
        {
            if (_chat.SelectedConversation is not Conversation conversation || conversation.ProjectId != _projectId) { throw new InvalidDataException("この画面と同じ範囲のチャットを選んでください。"); }
            List<Guid> ids = [.. conversation.SkillIds]; if (!ids.Contains(_selected.Id)) { ids.Add(_selected.Id); }
            await _chat.SetConversationSkillsAsync(ids); if (_chat.SkillMode == "off") { _chat.SkillMode = "manual"; }
            StatusText.Text = "この会話で指定しました。入力欄のスキルから変更できます。";
        });
    }
    private async void Common_Click(object sender, RoutedEventArgs e)
    { if (_projectId is Guid id) { await Run(async _ => { await _chat.SetCommonSkillsAsync(id, CommonBox.IsChecked == true); StatusText.Text = "共通スキルの利用設定を保存しました。"; }); } }
    private async void Samples_Click(object sender, RoutedEventArgs e) => await Run(async token =>
    {
        foreach (SkillPackage sample in SkillSamples.All())
        {
            if (_chat.Skills.Any(s => s.ProjectId == _projectId && s.Name == sample.Name)) { continue; }
            await _chat.SaveSkillAsync(await _chat.SkillFiles.SaveAsync(sample, _projectId, null, token));
        }
        Refresh(); StatusText.Text = "標準スキルを追加しました。一覧から内容を確認できます。";
    });
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void OnClosing(object? sender, CancelEventArgs e)
    { if (!_busy && !DiscardDraft()) { e.Cancel = true; } else { _lifetime.Cancel(); } }
}
