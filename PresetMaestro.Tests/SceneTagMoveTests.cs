using System.Buffers.Binary;
using PresetMaestro.Core;
using PresetMaestro.FractalIndex;
using PresetNameSync.Core;

namespace PresetMaestro.Tests;

public sealed class SceneTagMoveTests
{
    internal static StoredPresetImage Image(int slot = 0, bool swapped = false, bool edited = false)
    {
        byte[] body = new byte[4096], raw = new byte[16384];
        void Word(int offset, int value) => BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(offset, 2), (ushort)value);
        Word(0x104, 58); Word(0x108, 118);
        Word(0x21e, 25); Word(0x220, 1); Word(0x27e, 25); Word(0x280, 1);
        int amp = 0x2c0, drive = amp + (23 + 147 * 4) * 2;
        Word(amp + 30, 147); Word(amp + 32, 4); Word(drive + 30, 12); Word(drive + 32, 4);
        for (int channel = 0; channel < 4; channel++) { Word(amp + 46 + (channel * 147 + 4) * 2, 141 + channel); }
        for (int scene = 0; scene < 8; scene++)
        {
            int source = swapped ? scene == 0 ? 3 : scene == 3 ? 0 : scene : scene;
            foreach (int pos in new[] { amp, drive })
            {
                Word(scene == 0 ? pos - 2 : pos + (scene - 1) * 2, source % 4);
                Word(pos + (7 + scene) * 2, source / 4);
            }
        }
        string[] names = ["Mode 1", "Mode 2", "#34", "Greasy Mode 1", "Clean FX", "Lead FX", "Rhythm", "Solo"];
        if (swapped) { (names[0], names[3]) = (names[3], names[0]); }
        for (int i = 0; i < 8; i++) { System.Text.Encoding.ASCII.GetBytes(names[i]).CopyTo(body, 4 + i * 32); }
        if (edited) { body[amp + 48] = 1; }
        body.CopyTo(raw, 0x100);
        return new(new(slot, "AFD100 Marshall", names), raw, body);
    }

    internal static async Task<IndexedPreset> Read(StoredPresetImage image) => await new PresetIndexReader(
        new Source((_, _, _) => Task.FromResult(image)), AmpModelCatalogRegistry.CreateStarter()).ReadAsync(
            FractalDeviceDefinition.For(FractalDeviceVariant.FM9), new Version(12, 0), image.Scenes.Slot, CancellationToken.None);

    internal sealed class Source(Func<int, FractalDeviceDefinition, CancellationToken, Task<StoredPresetImage>> read) : IStoredPresetImageSource
    {
        internal List<int> Reads { get; } = [];
        public Task<StoredPresetImage> ReadStoredImageAsync(int slot, FractalDeviceDefinition device, CancellationToken token)
        { Reads.Add(slot); return read(slot, device, token); }
    }

    [Fact]
    public async Task FingerprintsProveWholeSceneSwapsAndKeepOpaqueAndNonAmpSettings()
    {
        var before = await Read(Image());
        var swapped = await Read(Image(swapped: true));
        Assert.NotNull(before.SceneContentSha256);
        Assert.Equal(before.SceneContentSha256[0], swapped.SceneContentSha256![3]);
        Assert.Equal(before.SceneContentSha256[3], swapped.SceneContentSha256[0]);
        Assert.True(SceneTagMoves.Compare(before, swapped, null).Exact);
        var edited = await Read(Image(swapped: true, edited: true));
        Assert.False(SceneTagMoves.Compare(before, edited, null).Exact);
        var opaque = Image(swapped: true); opaque.Body[^1] = 42;
        Assert.False(SceneTagMoves.Compare(before, await Read(opaque), null).Exact);
        var effect = Image(swapped: true);
        int drive = 0x2c0 + (23 + 147 * 4) * 2;
        effect.Body[drive + 48] = 42;
        Assert.False(SceneTagMoves.Compare(before, await Read(effect), null).Exact);
        var invalid = Image(); BinaryPrimitives.WriteUInt16LittleEndian(invalid.Body.AsSpan(drive + 14, 2), 2);
        Assert.Null((await Read(invalid)).SceneContentSha256);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SwapsAreAppliedSimultaneouslyAndNameOnlyMatchesRequireConfirmation(bool legacy)
    {
        var before = await Read(Image());
        var after = await Read(Image(swapped: true));
        if (legacy) { before = before with { SceneContentSha256 = null }; }
        var annotation = new PresetAnnotation { Tags = ["Preset tag"], SceneTags = new() { [0] = ["Rhythm"], [3] = ["Lead"], [2] = ["Metal"] } };
        var plan = SceneTagMoves.Compare(before, after, annotation);
        Assert.Equal(!legacy, plan.Exact);
        Assert.True(plan.HasTaggedMoves);
        Assert.Equal([(0, 3), (3, 0)], plan.Moves.Select(move => (move.From, move.To)).ToArray());
        plan.Apply(annotation);
        Assert.Equal(["Lead"], annotation.SceneTags[0]);
        Assert.Equal(["Rhythm"], annotation.SceneTags[3]);
        Assert.Equal(["Metal"], annotation.SceneTags[2]);
        Assert.Equal(["Preset tag"], annotation.Tags);
    }

    [Fact]
    public async Task DuplicateAndBlankNamesNeverGuessNameOnlyTagMoves()
    {
        var before = (await Read(Image())) with { SceneContentSha256 = null, SceneNames = ["Clean", "Clean", "", "", "A", "B", "C", "D"] };
        var after = before with { SceneNames = ["", "Clean", "Clean", "", "A", "B", "C", "D"] };
        var annotation = new PresetAnnotation { SceneTags = new() { [0] = ["Lead"] } };
        Assert.Empty(SceneTagMoves.Compare(before, after, annotation).Moves);
        Assert.False(SceneTagMoves.Compare(before, after, annotation).HasTaggedMoves);
    }

    [Fact]
    public async Task SceneEvidenceSurvivesLibrarySerialization()
    {
        var preset = await Read(Image());
        var copy = IndexJson.Clone(preset);
        Assert.Equal(preset.SceneContentSha256, copy.SceneContentSha256);
    }
}
