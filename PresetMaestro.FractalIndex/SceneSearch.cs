namespace PresetMaestro.FractalIndex;

// A search result refers to the saved preset; it is never a second scene store.
public sealed record IndexedScene(IndexedPreset Preset, int Index, string Name, string[] Tags);

public static class SceneSearch
{
    public static IndexedScene[] Find(IEnumerable<IndexedPreset> presets, Func<IndexedPreset, PresetAnnotation?> annotation,
        IEnumerable<string> textTerms, IEnumerable<string> tagTerms, bool allTags)
    {
        var text = textTerms.ToArray();
        var tags = tagTerms.ToArray();
        var results = new List<IndexedScene>();
        foreach (var preset in presets.OrderBy(p => p.Slot))
        {
            if (preset.NameOnlyEmpty || preset.Name.Trim() == "<EMPTY>" || preset.ContentSha256.Length == 0) { continue; }
            var attached = annotation(preset);
            for (int scene = 0; scene < Math.Min(8, preset.SceneNames.Length); scene++)
            {
                string name = preset.SceneNames[scene];
                var sceneTags = attached?.SceneTags.GetValueOrDefault(scene)?.ToArray() ?? [];
                if (!text.All(term => name.Contains(term, StringComparison.OrdinalIgnoreCase))) { continue; }
                bool HasTag(string tag) => sceneTags.Contains(tag, StringComparer.OrdinalIgnoreCase);
                if (tags.Length > 0 && !(allTags ? tags.All(HasTag) : tags.Any(HasTag))) { continue; }
                results.Add(new(preset, scene, name, sceneTags));
            }
        }
        return results.ToArray();
    }
}
