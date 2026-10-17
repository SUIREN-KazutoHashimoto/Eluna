using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Eluna.App.Models;
using Microsoft.Win32;

namespace Eluna.App.Services;

internal static class InstallerService
{
    internal static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Eluna");
    internal static string? Executable => Environment.ProcessPath;
    internal static InstalledApp? Current => Executable is { } path ? Read(Path.GetDirectoryName(path)!) : null;
    internal static InstalledApp? Read(string directory)
    {
        try
        {
            string full = Path.GetFullPath(directory); var marker = JsonSerializer.Deserialize<InstalledApp>(File.ReadAllText(Path.Combine(full, "install.json")), AppStore.JsonOptions);
            return marker is { Schema: 1 } && marker.Id != Guid.Empty && Path.GetFullPath(marker.Directory).Equals(full, StringComparison.OrdinalIgnoreCase) ? marker : null;
        }
        catch (Exception e) when (e is IOException or JsonException or ArgumentException or UnauthorizedAccessException) { return null; }
    }
    internal static void RejectLink(string directory)
    {
        for (DirectoryInfo? at = new(directory); at is not null; at = at.Parent)
        { if (at.Exists && (at.Attributes & FileAttributes.ReparsePoint) != 0) { throw new IOException("リンクを含むインストール先は使用できません。"); } }
    }
    internal static async Task<InstalledApp> InstallAsync(string source, string directory, bool shortcuts, bool register, CancellationToken token)
    {
        string target = Path.GetFullPath(directory); RejectLink(target);
        if (!Path.IsPathFullyQualified(source) || !File.Exists(source) || !Path.GetExtension(source).Equals(".exe", StringComparison.OrdinalIgnoreCase)) { throw new InvalidDataException("配布用exeを確認してください。"); }
        if (File.Exists(Path.Combine(target, "Eluna.exe")) && Read(target) is null) { throw new IOException("所有を確認できないEluna.exeがあります。別の場所へ移動してから実行してください。"); }
        Directory.CreateDirectory(target); string temporary = Path.Combine(target, "Eluna.install-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true)) { await input.CopyToAsync(output, token); await output.FlushAsync(token); }
            token.ThrowIfCancellationRequested(); File.Move(temporary, Path.Combine(target, "Eluna.exe"), true);
            InstalledApp install = new(1, Read(target)?.Id ?? Guid.NewGuid(), target, UpdateService.CurrentVersion, shortcuts, register);
            await WriteMarkerAsync(install, token);
            if (register) { Register(install); }
            return install;
        }
        finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
    }
    internal static async Task WriteMarkerAsync(InstalledApp marker, CancellationToken token = default)
    {
        string path = Path.Combine(marker.Directory, "install.json"); string temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(marker, AppStore.JsonOptions), token); File.Move(temp, path, true);
        if (marker.Registered) { using RegistryKey? registration = Registry.CurrentUser.OpenSubKey(UninstallKey, true); if (registration?.GetValue("InstallLocation") is string location && location.Equals(marker.Directory, StringComparison.OrdinalIgnoreCase)) { registration.SetValue("DisplayVersion", marker.Version); } }
    }
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Eluna";
    private static void Register(InstalledApp marker)
    {
        string executable = Path.Combine(marker.Directory, "Eluna.exe");
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            key.SetValue("DisplayName", "Eluna"); key.SetValue("DisplayVersion", marker.Version); key.SetValue("Publisher", "Eluna contributors"); key.SetValue("InstallLocation", marker.Directory); key.SetValue("DisplayIcon", executable + ",0");
            key.SetValue("UninstallString", "\"" + executable + "\" --uninstall"); key.SetValue("NoModify", 1); key.SetValue("NoRepair", 1);
        }
        if (marker.Shortcuts)
        {
            Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Eluna.lnk"), executable);
            Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Eluna.lnk"), executable);
        }
    }
    private static void Shortcut(string path, string target)
    {
        Type type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new IOException("Windowsのショートカット作成機能を利用できません。");
        object shell = Activator.CreateInstance(type)!; object? link = null;
        try { dynamic automation = shell; link = automation.CreateShortcut(path); dynamic shortcut = link; shortcut.TargetPath = target; shortcut.WorkingDirectory = Path.GetDirectoryName(target); shortcut.IconLocation = target + ",0"; shortcut.Description = "Eluna"; shortcut.Save(); }
        finally { if (link is not null) { Marshal.FinalReleaseComObject(link); } Marshal.FinalReleaseComObject(shell); }
    }
    internal static void RemoveOwnedFiles(string directory, Guid expectedId, bool unregister)
    {
        RejectLink(directory); InstalledApp marker = Read(directory) ?? throw new IOException("Elunaのインストール記録を確認できません。"); if (marker.Id != expectedId) { throw new IOException("インストール先が変更されています。"); }
        if (unregister)
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(UninstallKey);
            if (key?.GetValue("InstallLocation") is string location && location.Equals(directory, StringComparison.OrdinalIgnoreCase))
            { key.Close(); Registry.CurrentUser.DeleteSubKey(UninstallKey, false); }
            foreach (string folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Programs), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) })
            {
                string path = Path.Combine(folder, "Eluna.lnk"); if (!File.Exists(path)) { continue; }
                Type? type = Type.GetTypeFromProgID("WScript.Shell"); if (type is null) { continue; }
                object shell = Activator.CreateInstance(type)!; object? link = null;
                try { dynamic automation = shell; link = automation.CreateShortcut(path); dynamic shortcut = link; if (((string)shortcut.TargetPath).Equals(Path.Combine(directory, "Eluna.exe"), StringComparison.OrdinalIgnoreCase)) { File.Delete(path); } }
                finally { if (link is not null) { Marshal.FinalReleaseComObject(link); } Marshal.FinalReleaseComObject(shell); }
            }
        }
        // Deliberately enumerate only our files; user data and unrelated files are preserved.
        foreach (string name in new[] { "Eluna.exe", "Eluna.previous.exe", "install.json", "install.json.tmp" }) { string path = Path.Combine(directory, name); if (File.Exists(path)) { File.Delete(path); } }
        if (!Directory.EnumerateFileSystemEntries(directory).Any()) { Directory.Delete(directory); }
    }
    internal static Process Start(string executable, params string[] arguments)
    { var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true }; foreach (string argument in arguments) { start.ArgumentList.Add(argument); } return Process.Start(start) ?? throw new IOException("Elunaを起動できませんでした。"); }
}
