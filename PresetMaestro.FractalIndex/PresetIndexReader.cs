using PresetNameSync.Core;

namespace PresetMaestro.FractalIndex;

public interface IStoredPresetImageSource
{
    Task<StoredPresetImage> ReadStoredImageAsync(int slot, FractalDeviceDefinition device, CancellationToken token);
}

/// <summary>Optional lightweight name read. Null means this device path does not support the optimization.</summary>
public interface IStoredPresetNameSource
{
    Task<string?> ReadStoredPresetNameAsync(int slot, FractalDeviceDefinition device, CancellationToken token);
}

public sealed record IndexedAmpChannel(int Channel, AmpModelResolution Model);

public sealed record IndexedAmpBlock(int BlockNumber, IndexedAmpChannel[] Channels, AmpSceneState[] Scenes);

public sealed record IndexedPreset(
    FractalDeviceVariant Variant,
    int Slot,
    string Name,
    string[] SceneNames,
    IndexedAmpBlock[] Amps,
    string ContentSha256)
{
    // An explicit device name marker, not a decoded empty body or a content fingerprint.
    public bool NameOnlyEmpty { get; init; }

    public bool Contains(int modelId) => Amps.Any(amp => amp.Channels.Any(channel => channel.Model.Id == modelId));

    public bool Selects(int scene, int modelId) => scene is >= 0 and < 8 &&
        Amps.Any(amp => amp.Channels[amp.Scenes[scene].Channel].Model.Id == modelId);

    public bool Uses(int scene, int modelId) => scene is >= 0 and < 8 &&
        Amps.Any(amp => !amp.Scenes[scene].Bypassed && amp.Channels[amp.Scenes[scene].Channel].Model.Id == modelId);
}

/// <summary>Reads saved programmed state through the host's serialized MIDI source.</summary>
public sealed class PresetIndexReader(IStoredPresetImageSource source, AmpModelCatalogRegistry catalogs)
{
    public async Task<IndexedPreset> ReadAsync(
        FractalDeviceDefinition device, Version? firmware, int slot, CancellationToken token)
    {
        if (slot < 0 || slot >= device.PresetSlots)
        {
            throw new ArgumentOutOfRangeException(nameof(slot));
        }
        if (source is IStoredPresetNameSource names)
        {
            string? name = null;
            try { name = await names.ReadStoredPresetNameAsync(slot, device, token).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or TimeoutException)
            { /* A failed name check is unknown; fall back to reading the saved content. */ }
            if (string.Equals(name?.Trim(), "<EMPTY>", StringComparison.Ordinal))
            {
                return new IndexedPreset(device.Variant, slot, name!, Enumerable.Repeat("", 8).ToArray(), [], "") { NameOnlyEmpty = true };
            }
        }
        var image = await source.ReadStoredImageAsync(slot, device, token).ConfigureAwait(false);
        if (image.Scenes.Slot != slot)
        {
            throw new InvalidDataException("Saved preset reply does not match the requested slot.");
        }
        var decoded = Gen3PresetBodyDecoder.Decode(image, device);
        var amps = decoded.Amps.Select(amp => new IndexedAmpBlock(
            amp.BlockNumber,
            [.. amp.ModelIds.Select((id, channel) => new IndexedAmpChannel(channel, catalogs.Resolve(device, firmware, id)))],
            amp.Scenes)).ToArray();
        return new IndexedPreset(device.Variant, decoded.Slot, decoded.Name, decoded.SceneNames, amps, decoded.ContentSha256);
    }
}
