using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using Eluna.App.Models;

namespace Eluna.App.Services;

internal static class ComputerApplications
{
    private static IEnumerable<string> Roots => new[] { Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu) }.Where(Directory.Exists);
    internal static List<ComputerInstalledApp> List()
    {
        var items = new List<ComputerInstalledApp>();
        foreach (string root in Roots)
        {
            foreach (string path in Directory.EnumerateFiles(root, "*.lnk", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            {
                if (items.Count >= 300) { break; }
                string name = Path.GetFileNameWithoutExtension(path);
                if (System.Text.RegularExpressions.Regex.IsMatch(name, @"PowerShell|Terminal|Command Prompt|コマンド|ターミナル|セキュリティ|Security|Defender|Uninstall|アンインストール", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant)) { continue; }
                try { using var file = File.OpenRead(path); items.Add(new(name, Path.GetFullPath(path), Convert.ToHexString(SHA256.HashData(file)))); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        return items.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
    internal static void Launch(ComputerInstalledApp app)
    {
        string path = Path.GetFullPath(app.Shortcut);
        if (!Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase) || !Roots.Any(root => path.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
        { throw new InvalidOperationException("Windowsのスタートメニューに登録されたアプリだけを起動できます。"); }
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) { throw new InvalidOperationException("アプリの登録先が変わりました。一覧を更新してください。"); }
        using (var file = File.OpenRead(path)) { if (!Convert.ToHexString(SHA256.HashData(file)).Equals(app.Sha256, StringComparison.OrdinalIgnoreCase)) { throw new InvalidOperationException("アプリの登録内容が変わりました。更新して選び直してください。"); } }
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path) })?.Dispose();
    }
}
