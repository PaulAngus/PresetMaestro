using System.Text.Json.Serialization;

namespace PresetMaestro.Core;

// A named shortcut to a specific scene within a specific preset.
[JsonConverter(typeof(FavoriteJsonConverter))]
public sealed class Favorite
{
    public int Id { get; set; } // stable identity, never reused
    public int Slot { get; set; } // number typed on the keypad to trigger this favorite
    public string Name { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    // Identifies the index annotation whose scene tags this shortcut displays.
    // Null means legacy/standalone tags that have not yet been linked.
    public Guid? SceneTagSourceId { get; set; }
    public int Preset { get; set; } // displayed preset number
    public int Scene { get; set; } = 1; // 1-8

    [JsonIgnore]
    public string TagsDisplay => string.Join(", ", Tags);

    [JsonIgnore]
    public bool IsEmpty => string.IsNullOrWhiteSpace(Name) &&
                           Tags.Count == 0 &&
                           Preset == 0 &&
                           Scene == 0;
}
