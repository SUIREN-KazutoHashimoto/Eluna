namespace Eluna.App.Models;

public sealed record LogOptions(string Level = "normal", bool CaptureContents = false, int RetentionDays = 14, int MaxMegabytes = 64);
public sealed record DiagnosticEvent(Guid Session, long Sequence, DateTimeOffset Time, string Level, string Area, string Event,
    string Detail, Guid? Correlation = null, Guid? Room = null, Guid? Agent = null, int Part = 0, int Parts = 1)
{
    public string Label => Time.ToLocalTime().ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture) + " · " + Level + " · " + Area + "." + Event + (Parts > 1 ? " (" + (Part + 1) + "/" + Parts + ")" : "");
}
