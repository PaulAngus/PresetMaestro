using System.Text.Json;

namespace PresetMaestro.FractalIndex;

public sealed record AmpReference(string CatalogId, string Url);

/// <summary>Personal reading links. Never a source of model IDs, mappings or firmware availability.</summary>
public sealed class AmpReferenceStore(string path)
{
    public static string NormalizeUrl(string value)
    {
        if (value.Length > 2048 || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != "https" || !uri.Host.Equals("wiki.fractalaudio.com", StringComparison.OrdinalIgnoreCase) ||
            uri.UserInfo.Length != 0 || !uri.IsDefaultPort || !uri.AbsolutePath.EndsWith("/index.php", StringComparison.Ordinal))
        { throw new ArgumentException("Use an HTTPS page link on wiki.fractalaudio.com."); }
        return uri.AbsoluteUri;
    }

    public static List<AmpReference> Validate(IEnumerable<AmpReference> references) => references.Select(r =>
    {
        if (r is null || string.IsNullOrWhiteSpace(r.CatalogId) || r.CatalogId.Length > 180 || r.CatalogId.Any(char.IsControl))
        { throw new InvalidDataException("Invalid amp reference identity."); }
        return r with { Url = NormalizeUrl(r.Url) };
    }).Distinct().ToList();

    public List<AmpReference> Load() => File.Exists(path)
        ? Validate(JsonSerializer.Deserialize<List<AmpReference>>(File.ReadAllText(path)) ?? throw new InvalidDataException("Invalid amp references.")) : [];

    public void Merge(IEnumerable<AmpReference> references)
    {
        var current = Load();
        var all = Validate(current.Concat(references));
        if (all.SequenceEqual(current)) { return; }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(all, IndexJson.Options));
        if (File.Exists(path)) { File.Replace(temp, path, path + ".bak"); }
        else { File.Move(temp, path); }
    }
}
