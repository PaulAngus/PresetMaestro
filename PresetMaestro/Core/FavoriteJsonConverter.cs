using System.Text.Json;
using System.Text.Json.Serialization;

namespace PresetMaestro.Core;

// Every serialization route (including profile ZIPs) uses this compatibility boundary.
public sealed class FavoriteJsonConverter : JsonConverter<Favorite>
{
    public override Favorite Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var legacy = document.RootElement.Deserialize<LegacyFavorite>(options)
            ?? throw new JsonException("Invalid favorite.");
        if (legacy.Name is null || legacy.Tags is null || legacy.Tags.Any(t => t is null))
        {
            throw new JsonException("Invalid favorite name or tags.");
        }

        var tags = legacy.Tags.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        string category = legacy.Category?.Trim() ?? "";
        if (category.Length > 0 && !tags.Contains(category, StringComparer.OrdinalIgnoreCase))
        {
            tags.Add(category);
        }

        return new Favorite
        {
            Id = legacy.Id,
            Slot = legacy.Slot,
            Name = legacy.Name,
            Tags = tags,
            Preset = legacy.Preset,
            Scene = legacy.Scene
        };
    }

    public override void Write(Utf8JsonWriter writer, Favorite value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber(nameof(Favorite.Id), value.Id);
        writer.WriteNumber(nameof(Favorite.Slot), value.Slot);
        writer.WriteString(nameof(Favorite.Name), value.Name);
        writer.WritePropertyName(nameof(Favorite.Tags));
        JsonSerializer.Serialize(writer, value.Tags, options);
        writer.WriteNumber(nameof(Favorite.Preset), value.Preset);
        writer.WriteNumber(nameof(Favorite.Scene), value.Scene);
        writer.WriteEndObject();
    }

    private sealed class LegacyFavorite
    {
        public int Id { get; set; }
        public int Slot { get; set; }
        public string Name { get; set; } = "";
        public string? Category { get; set; }
        public List<string> Tags { get; set; } = [];
        public int Preset { get; set; }
        public int Scene { get; set; } = 1;
    }
}
