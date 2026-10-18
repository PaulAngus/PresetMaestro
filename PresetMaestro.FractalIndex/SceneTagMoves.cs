namespace PresetMaestro.FractalIndex;

public sealed record SceneTagMove(int From, int To, string Name, string[] Tags);

public sealed record SceneTagMovePlan(bool Exact, SceneTagMove[] Moves)
{
    public bool HasTaggedMoves => Moves.Any(move => move.Tags.Length > 0);

    public void Apply(PresetAnnotation annotation)
    {
        // Read the complete old assignment before writing any destination of a swap.
        var mapping = Moves.ToDictionary(move => move.From, move => move.To);
        var original = annotation.SceneTags.ToArray();
        annotation.SceneTags = [];
        foreach (var (scene, tags) in original)
        {
            int destination = mapping.GetValueOrDefault(scene, scene);
            annotation.SceneTags[destination] = IndexProfile.NormalizeTags(
                annotation.SceneTags.GetValueOrDefault(destination, []).Concat(tags));
        }
    }
}

public static class SceneTagMoves
{
    public static SceneTagMovePlan Compare(IndexedPreset before, IndexedPreset after, PresetAnnotation? annotation)
    {
        if (before.Slot != after.Slot || before.Variant != after.Variant || before.NameOnlyEmpty || after.NameOnlyEmpty)
        { return new(false, []); }
        int[]? exact = null;
        if (before.SceneContentSha256 is { Length: 8 } oldHashes && after.SceneContentSha256 is { Length: 8 } newHashes &&
            oldHashes.Concat(newHashes).All(hash => hash.StartsWith(SceneContentFingerprint.Prefix, StringComparison.Ordinal)))
        {
            var mapping = new int[8];
            for (int source = 0; source < 8; source++)
            {
                var matches = Enumerable.Range(0, 8).Where(target => newHashes[target] == oldHashes[source]).ToArray();
                if (matches.Length > 1)
                {
                    string name = before.SceneNames[source];
                    matches = UniqueName(before.SceneNames, name)
                        ? matches.Where(target => after.SceneNames[target] == name && UniqueName(after.SceneNames, name)).ToArray() : [];
                    if (matches.Length == 0 && oldHashes[source] == newHashes[source] && before.SceneNames[source] == after.SceneNames[source])
                    { matches = [source]; }
                }
                mapping[source] = matches.Length == 1 ? matches[0] : -1;
            }
            // A complete bijection proves a permutation, rather than a partial/copy match.
            if (mapping.All(i => i >= 0) && mapping.Distinct().Count() == 8) { exact = mapping; }
        }
        var moves = new List<SceneTagMove>();
        for (int source = 0; source < 8; source++)
        {
            string name = before.SceneNames[source];
            int target = exact is not null ? exact[source] : UniqueName(before.SceneNames, name) && UniqueName(after.SceneNames, name)
                ? Array.IndexOf(after.SceneNames, name) : source;
            if (target == source) { continue; }
            moves.Add(new(source, target, name, annotation?.SceneTags.GetValueOrDefault(source)?.ToArray() ?? []));
        }
        return new(exact is not null, moves.ToArray());
    }

    private static bool UniqueName(string[] names, string name) =>
        !string.IsNullOrWhiteSpace(name) && names.Count(candidate => candidate == name) == 1;
}
