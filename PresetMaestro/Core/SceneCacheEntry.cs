namespace PresetMaestro.Core;

public sealed class SceneCacheEntry
{
    public string[] Names { get; set; } = [];
    public DateTimeOffset RetrievedAt { get; set; }
    public string Source { get; set; } = "stored";
}
