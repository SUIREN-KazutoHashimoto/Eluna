using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.Checks;

internal static class UpdateChecks
{
    private static void Check(bool ok, string message) { if (!ok) { throw new InvalidOperationException(message); } }
    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
    private static byte[] Binary(byte marker) { var bytes = new byte[100000]; bytes[0] = (byte)'M'; bytes[1] = (byte)'Z'; Array.Fill(bytes, marker, 64, bytes.Length - 64); return bytes; }
    private sealed class Replies(Dictionary<string, byte[]> replies) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(replies.TryGetValue(request.RequestUri!.AbsoluteUri, out byte[]? bytes) ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes), RequestMessage = request } : new HttpResponseMessage(HttpStatusCode.NotFound)); }
    internal static async Task RunAsync(string root)
    {
        Directory.CreateDirectory(root); byte[] old = Binary(1), next = Binary(2); string source = Path.Combine(root, "source.exe"); await File.WriteAllBytesAsync(source, old);
        string install = Path.Combine(root, "Programs", "Eluna"); InstalledApp marker = await InstallerService.InstallAsync(source, install, false, false, CancellationToken.None);
        Check(marker.Id != Guid.Empty && File.ReadAllBytes(Path.Combine(install, "Eluna.exe")).SequenceEqual(old), "Per-user install lost its executable or ownership marker.");
        string data = Path.Combine(root, "data"); var store = new AppStore(data); Directory.CreateDirectory(Path.Combine(data, "Updates")); string stage = Path.Combine(data, "Updates", "fixture.exe"); await File.WriteAllBytesAsync(stage, next);
        string state = Path.Combine(data, "state-social.json"); await File.WriteAllTextAsync(state, "ORIGINAL_DATA"); string model = Path.Combine(data, "Models", "keep.gguf"); await File.WriteAllTextAsync(model, "MODEL_SENTINEL");
        var staged = new StagedUpdate("99.0.0", "stable", stage, Hash(next), next.Length, 5); var plan = new UpdatePlan(Guid.NewGuid(), install, marker.Id, data, 0, 0, staged);
        bool restored = false, failed = false;
        try { await UpdateWorker.ApplyAsync(plan, async (target, _) => { Check(File.ReadAllBytes(target).SequenceEqual(next), "Update did not replace the binary before health check."); await File.WriteAllTextAsync(state, "STARTUP_PARTIAL_WRITE"); return false; }, () => restored = true); } catch (IOException) { failed = true; }
        Check(failed && restored && File.ReadAllBytes(Path.Combine(install, "Eluna.exe")).SequenceEqual(old) && File.ReadAllText(state) == "ORIGINAL_DATA" && InstallerService.Read(install)!.Version == marker.Version, "A failed start did not restore the previous executable/data marker.");
        await UpdateWorker.ApplyAsync(plan with { Token = Guid.NewGuid() }, (_, _) => Task.FromResult(true), () => throw new InvalidOperationException("Unexpected rollback."));
        Check(InstallerService.Read(install)!.Version == "99.0.0" && File.ReadAllBytes(Path.Combine(install, "Eluna.previous.exe")).SequenceEqual(old) && File.ReadAllText(model) == "MODEL_SENTINEL", "Successful update lost rollback executable or user model.");
        bool badHash = false; try { await UpdateWorker.ApplyAsync(plan with { Update = staged with { Sha256 = new string('0', 64) } }, (_, _) => Task.FromResult(true), () => { }); } catch (System.IO.InvalidDataException) { badHash = true; }
        Check(badHash && File.ReadAllBytes(Path.Combine(install, "Eluna.exe")).SequenceEqual(next), "A damaged update changed the installed application.");
        string unrelated = Path.Combine(install, "my-notes.txt"); await File.WriteAllTextAsync(unrelated, "KEEP"); InstallerService.RemoveOwnedFiles(install, marker.Id, false); Check(File.Exists(unrelated) && File.Exists(model) && !File.Exists(Path.Combine(install, "Eluna.exe")), "Uninstall removed user data/unowned files.");
        const string prefix = "https://github.com/SUIREN-KazutoHashimoto/Eluna/releases/";
        byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(new ReleaseManifest(1, "99.0.0", "stable", 5, "Eluna.exe", next.Length, Hash(next)), AppStore.JsonOptions);
        string releaseJson = JsonSerializer.Serialize(new { tag_name = "v99.0.0", html_url = prefix + "tag/v99.0.0", prerelease = false, assets = new[] { new { name = "Eluna.exe", browser_download_url = prefix + "download/v99.0.0/Eluna.exe", size = next.Length, digest = "sha256:" + Hash(next) }, new { name = "update-manifest.json", browser_download_url = prefix + "download/v99.0.0/update-manifest.json", size = manifest.Length, digest = "sha256:" + Hash(manifest) } } });
        using var releaseDocument = JsonDocument.Parse(releaseJson); ReleaseInfo release = UpdateService.Parse(releaseDocument.RootElement, "1.0.0");
        using var http = new HttpClient(new Replies(new() { [prefix + "download/v99.0.0/Eluna.exe"] = next, [prefix + "download/v99.0.0/update-manifest.json"] = manifest })); using var service = new UpdateService(http);
        StagedUpdate downloaded = await service.StageAsync(store, release, "stable", null, CancellationToken.None); Check(File.ReadAllBytes(downloaded.File).SequenceEqual(next), "Verified download did not preserve bytes.");
        bool oldSchema = false; try { UpdateService.ValidateManifest(new(1, "99.0.0", "stable", 4, "Eluna.exe", next.Length, Hash(next)), release, "stable", release.Assets![0]); } catch (System.IO.InvalidDataException) { oldSchema = true; }
        Check(oldSchema, "Automatic update accepted a pre-social data schema.");
        using var trains = JsonDocument.Parse("[{\"tag_name\":\"v9.0.0\",\"html_url\":\"" + prefix + "tag/v9.0.0\",\"prerelease\":false},{\"tag_name\":\"v1.0.0-develop.12\",\"html_url\":\"" + prefix + "tag/v1.0.0-develop.12\",\"prerelease\":true},{\"tag_name\":\"v1.0.0-develop.2\",\"html_url\":\"" + prefix + "tag/v1.0.0-develop.2\",\"prerelease\":true}]");
        Check(UpdateService.SelectDevelopment(trains.RootElement, "1.0.0-develop.1")?.Tag == "v1.0.0-develop.12", "Release trains mixed stable/previews or sorted numbers lexically.");
        using var previews = JsonDocument.Parse(JsonSerializer.Serialize(new[] {
            new { tag_name = "v0.1.0-beta", html_url = prefix + "tag/v0.1.0-beta", prerelease = true, draft = false },
            new { tag_name = "v0.1.0-beta.2", html_url = prefix + "tag/v0.1.0-beta.2", prerelease = true, draft = false },
            new { tag_name = "v0.1.0-beta.12", html_url = prefix + "tag/v0.1.0-beta.12", prerelease = true, draft = false },
            new { tag_name = "v0.1.0-rc.1", html_url = prefix + "tag/v0.1.0-rc.1", prerelease = true, draft = true },
            new { tag_name = "v0.2.0-nightly.1", html_url = prefix + "tag/v0.2.0-nightly.1", prerelease = true, draft = false },
            new { tag_name = "v0.2.0", html_url = prefix + "tag/v0.2.0", prerelease = false, draft = false } }));
        Check(UpdateService.SelectDevelopment(previews.RootElement, "0.1.0-beta")?.Tag == "v0.1.0-beta.12", "Development selection lost beta previews or accepted drafts, nightly or stable releases.");
        using var beta = JsonDocument.Parse("[" + previews.RootElement[0].GetRawText() + "]");
        Check(UpdateService.SelectDevelopment(beta.RootElement, "0.1.0-alpha") is { Tag: "v0.1.0-beta", IsNewer: true } && UpdateService.SelectDevelopment(beta.RootElement, "0.1.0-beta")?.IsNewer == false, "The initial beta was not selectable or tried to update to itself.");
        using var candidate = JsonDocument.Parse("[" + previews.RootElement[3].GetRawText().Replace("\"draft\":true", "\"draft\":false", StringComparison.Ordinal) + "]");
        Check(UpdateService.SelectDevelopment(candidate.RootElement, "0.1.0-beta") is { Tag: "v0.1.0-rc.1", IsNewer: true }, "A published release candidate was not selectable.");
        bool foreign = false; try { using var doc = JsonDocument.Parse(releaseJson.Replace("/download/v99.0.0/Eluna.exe", "/download/foreign/Eluna.exe", StringComparison.Ordinal)); UpdateService.Parse(doc.RootElement, "1.0.0"); } catch (JsonException) { foreign = true; }
        Check(foreign, "Asset from a different tag was trusted.");
        var legacyStore = new AppStore(Path.Combine(root, "migration")); AppState legacy = new(4, new(), [], Agents: AccountDefaults.Cast()); await legacyStore.SaveAsync(legacy); byte[] before = await File.ReadAllBytesAsync(Path.Combine(legacyStore.Root, "state-workspace.json")); await legacyStore.SaveAsync(WorkspaceService.Upgrade(legacy));
        Check(File.ReadAllBytes(Path.Combine(legacyStore.Root, "state-workspace.json")).SequenceEqual(before) && (await legacyStore.LoadAsync()).SchemaVersion == 5, "Social migration overwrote the prior release's workspace data.");
        Console.WriteLine("Installer/update: isolated install/uninstall ownership, data/model preservation, verified download, train selection, asset trust, schema compatibility, startup rollback and previous executable: OK");
    }
}
