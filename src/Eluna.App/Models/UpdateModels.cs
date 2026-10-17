namespace Eluna.App.Models;

public sealed record UpdateOptions(string Train = "stable", bool Automatic = true);
internal sealed record InstalledApp(int Schema, Guid Id, string Directory, string Version, bool Shortcuts, bool Registered = false);
internal sealed record ReleaseManifest(int Schema, string Version, string Train, int DataSchema, string File, long Size, string Sha256);
internal sealed record StagedUpdate(string Version, string Train, string File, string Sha256, long Size, int DataSchema);
internal sealed record UpdatePlan(Guid Token, string Directory, Guid InstallId, string DataRoot, int PreviousPid, long PreviousStart, StagedUpdate Update, bool Background = false);
