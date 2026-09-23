using System.Text.Json;

namespace PresetMaestro.Core;

internal static class LegacyFavoritesStorage
{
    // Validate before replacing anything. The first pre-migration copy is never overwritten.
    internal static void PreserveOriginal(string path)
    {
        if (!File.Exists(path) || !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string original = File.ReadAllText(path);
        using var document = JsonDocument.Parse(original);
        bool legacy = ContainsLegacyProperty(document.RootElement);
        if (path.EndsWith("favorites.json", StringComparison.OrdinalIgnoreCase) && document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Invalid favorites document.");
        }
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            var favorites = JsonSerializer.Deserialize<List<Favorite>>(original) ?? throw new JsonException("Invalid favorites.");
            if (favorites.Any(favorite => favorite is null))
            {
                throw new JsonException("Invalid favorite entry.");
            }
        }

        if (legacy && !File.Exists(path + ".pre-tags.bak"))
        {
            File.Copy(path, path + ".pre-tags.bak", false);
        }
    }

    private static bool ContainsLegacyProperty(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => value.EnumerateObject().Any(p => p.Name is "Category" or "CategoryOrder" || ContainsLegacyProperty(p.Value)),
        JsonValueKind.Array => value.EnumerateArray().Any(ContainsLegacyProperty),
        _ => false,
    };

    internal static void Write(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, json);
            PreserveOriginal(path);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
