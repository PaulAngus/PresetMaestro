namespace PresetMaestro.FractalIndex;

public sealed record LibraryValueDifference(string Setting, string Saved, string Connected);

/// <summary>Decoded evidence, not an inference about edits or physical device identity.</summary>
public sealed record LibraryPresetDifference(int Slot, string? SavedName, string? ConnectedName,
    bool ContentChanged, LibraryValueDifference[] Changes, string? ReadError = null)
{
    public string? SavedFingerprint { get; init; }
    public string? ConnectedFingerprint { get; init; }
    public string? SavedComparisonFingerprint { get; init; }
    public string? ConnectedComparisonFingerprint { get; init; }
    public string Explanation => ReadError is not null ? ReadError : ContentChanged
        ? "The saved preset image differs. This library keeps a fingerprint rather than the full image, so other effect parameters and image data cannot be compared individually."
        : "The decoded saved settings differ.";

    internal static LibraryPresetDifference Between(int slot, IndexedPreset? saved, IndexedPreset current)
    {
        var changes = new List<LibraryValueDifference>();
        if (saved is null || !LibraryMatch.IsPopulated(saved))
        { changes.Add(new("Preset slot", "Empty", "Populated")); }
        else if (!LibraryMatch.IsPopulated(current))
        { changes.Add(new("Preset slot", "Populated", "Empty")); }
        else
        {
            if (saved.Variant != current.Variant) { changes.Add(new("Device model", saved.Variant.ToString(), current.Variant.ToString())); }
            if (saved.Name != current.Name) { changes.Add(new("Preset name", saved.Name, current.Name)); }
            for (int scene = 0; scene < Math.Max(saved.SceneNames.Length, current.SceneNames.Length); scene++)
            {
                string before = saved.SceneNames.ElementAtOrDefault(scene) ?? "Unavailable";
                string after = current.SceneNames.ElementAtOrDefault(scene) ?? "Unavailable";
                if (before != after) { changes.Add(new($"Scene {scene + 1} name", before, after)); }
            }
            foreach (int block in saved.Amps.Select(amp => amp.BlockNumber).Union(current.Amps.Select(amp => amp.BlockNumber)).Order())
            {
                var before = saved.Amps.FirstOrDefault(amp => amp.BlockNumber == block);
                var after = current.Amps.FirstOrDefault(amp => amp.BlockNumber == block);
                if (before is null || after is null)
                {
                    changes.Add(new($"Amp {block}", before is null ? "Absent" : "Present", after is null ? "Absent" : "Present"));
                    continue;
                }
                foreach (int channel in before.Channels.Select(c => c.Channel).Union(after.Channels.Select(c => c.Channel)).Order())
                {
                    var oldModel = before.Channels.FirstOrDefault(c => c.Channel == channel)?.Model;
                    var newModel = after.Channels.FirstOrDefault(c => c.Channel == channel)?.Model;
                    if (oldModel?.Id != newModel?.Id)
                    { changes.Add(new($"Amp {block}, channel {(char)('A' + channel)} model", oldModel?.DisplayName ?? "Absent", newModel?.DisplayName ?? "Absent")); }
                }
                for (int scene = 0; scene < Math.Max(before.Scenes.Length, after.Scenes.Length); scene++)
                {
                    var oldState = before.Scenes.ElementAtOrDefault(scene);
                    var newState = after.Scenes.ElementAtOrDefault(scene);
                    if (oldState?.Channel != newState?.Channel)
                    { changes.Add(new($"Scene {scene + 1}, Amp {block} channel", Describe(oldState), Describe(newState))); }
                }
            }
        }
        bool comparable = saved?.BypassIgnoredSha256?.StartsWith(BypassIgnoredFingerprint.Prefix, StringComparison.Ordinal) == true &&
            current.BypassIgnoredSha256?.StartsWith(BypassIgnoredFingerprint.Prefix, StringComparison.Ordinal) == true;
        bool contentChanged = comparable ? saved!.BypassIgnoredSha256 != current.BypassIgnoredSha256
            : saved?.ContentSha256 != current.ContentSha256;
        return new(slot, saved?.Name, current.Name, contentChanged, [.. changes])
        {
            SavedFingerprint = saved?.ContentSha256,
            ConnectedFingerprint = current.ContentSha256,
            SavedComparisonFingerprint = saved?.BypassIgnoredSha256,
            ConnectedComparisonFingerprint = current.BypassIgnoredSha256,
        };
    }

    private static string Describe(AmpSceneState? state) => state is null ? "Absent"
        : $"Channel {(char)('A' + state.Channel)}";
}
