using System.Buffers.Binary;
using PresetMaestro.Core;
using PresetMaestro.FractalIndex;
using PresetMaestro.Midi;
using PresetNameSync.Core;

namespace PresetMaestro.Tests;

public sealed class FractalIndexCoreTests
{
    [Fact]
    public void DeviceDefinitionsKeepAxeRevisionCapacityAndCatalogFamilySeparate()
    {
        var fm9 = FractalDeviceDefinition.For(FractalDeviceVariant.FM9);
        var fm3 = FractalDeviceDefinition.For(FractalDeviceVariant.FM3);
        var original = FractalDeviceDefinition.For(FractalDeviceVariant.AxeFxIIIOriginal);
        var markII = FractalDeviceDefinition.For(FractalDeviceVariant.AxeFxIIIMarkII);
        var turbo = FractalDeviceDefinition.For(FractalDeviceVariant.AxeFxIIIMarkIITurbo);

        Assert.Equal((0x12, 512, 147, false, "FM9"),
            ((int)fm9.ModelByte, fm9.PresetSlots, fm9.AmpColumns, fm9.AmpTypeInHeader, fm9.CatalogFamily));
        Assert.Equal((0x10, 512, 142, true, "AxeFxIII"),
            ((int)original.ModelByte, original.PresetSlots, original.AmpColumns, original.AmpTypeInHeader, original.CatalogFamily));
        Assert.Equal(1024, markII.PresetSlots);
        Assert.Equal(1024, turbo.PresetSlots);
        Assert.Equal(original.CatalogFamily, turbo.CatalogFamily);
        Assert.Equal((0x11, 512, 144, false, "FM3"),
            ((int)fm3.ModelByte, fm3.PresetSlots, fm3.AmpColumns, fm3.AmpTypeInHeader, fm3.CatalogFamily));
        Assert.Equal((4, 12, 1, 0),
            (fm3.GridRows, fm3.GridColumns, fm3.MaxAmpBlocks, fm3.AmpTypeParameterWordOffset));
        Assert.Equal((6, 14, 2, 4),
            (fm9.GridRows, fm9.GridColumns, fm9.MaxAmpBlocks, fm9.AmpTypeParameterWordOffset));
        Assert.Equal((6, 14, 2), (original.GridRows, original.GridColumns, original.MaxAmpBlocks));
    }

    [Fact]
    public void AxeUpperSlotQueryUsesAxeModelByteAndTwoSeptetAddress()
    {
        var query = StoredPresetDecoder.BuildQuery(1023, 0x10, 1023);

        Assert.Equal(0x10, query[4]);
        Assert.Equal(0x03, query[5]);
        Assert.Equal(7, query[6]);
        Assert.Equal(127, query[7]);
        Assert.True(SysexProtocol.ValidFrame(query, 0x10, 0x03, query.Length));
        Assert.Throws<ArgumentOutOfRangeException>(() => StoredPresetDecoder.BuildQuery(1024, 0x10, 1023));
    }

    [Theory]
    [InlineData(0x10, 1023)]
    [InlineData(0x11, 511)]
    public void CandidateEnvelopeCanDecodeAnUpperSlotSyntheticDump(byte modelByte, int maxSlot)
    {
        var frames = DumpFixture(modelByte, maxSlot);
        var decoder = new StoredPresetDecoder(modelByte, maxSlot);
        StoredPresetImage? result = null;
        foreach (var frame in frames)
        {
            result = decoder.AcceptImage(frame, maxSlot) ?? result;
        }
        Assert.Equal(maxSlot, result!.Scenes.Slot);
        Assert.Equal("Test preset", result.Scenes.PresetName);
    }

    [Theory]
    [InlineData(FractalDeviceVariant.FM9, 147, false)]
    [InlineData(FractalDeviceVariant.AxeFxIIIOriginal, 142, true)]
    public void DecodesDistinctModelsAndProgrammedScenesForEachLayout(
        FractalDeviceVariant variant, int ampColumns, bool typeInHeader)
    {
        byte[] body = new byte[4096];
        static void Write(byte[] bytes, int offset, int value) =>
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset, 2), (ushort)value);
        Write(body, 0x104, 58);
        Write(body, 0x104 + 4, 59);
        Write(body, 0x200 + 30, 25); Write(body, 0x200 + 32, 1);
        Write(body, 0x260 + 30, 25); Write(body, 0x260 + 32, 1);

        int pos = 0x2c0;
        for (int amp = 0; amp < 2; amp++)
        {
            Write(body, pos + 30, ampColumns);
            Write(body, pos + 32, 4);
            int parameters = pos + 46;
            for (int channel = 0; channel < 4; channel++)
            {
                int model = amp * 100 + channel + 10;
                int offset = typeInHeader
                    ? channel == 0 ? pos + 34 : parameters + (channel - 1) * ampColumns * 2 + (ampColumns - 6) * 2
                    : parameters + channel * ampColumns * 2 + 8;
                Write(body, offset, model);
            }
            for (int scene = 0; scene < 8; scene++)
            {
                Write(body, scene == 0 ? pos - 2 : pos + (scene - 1) * 2, scene % 4);
                Write(body, pos + (7 + scene) * 2, scene % 2);
            }
            pos += (23 + ampColumns * 4) * 2;
        }

        var image = new StoredPresetImage(
            new PresetScenes(3, "Synthetic", ["One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight"]),
            new byte[16384], body);
        var snapshot = Gen3PresetBodyDecoder.Decode(image, FractalDeviceDefinition.For(variant));

        Assert.Equal(2, snapshot.Amps.Length);
        Assert.Equal([10, 11, 12, 13], snapshot.Amps[0].ModelIds);
        Assert.Equal([110, 111, 112, 113], snapshot.Amps[1].ModelIds);
        Assert.Equal(new AmpSceneState(0, false), snapshot.Amps[0].Scenes[0]);
        Assert.Equal(new AmpSceneState(3, true), snapshot.Amps[1].Scenes[7]);

        // Move both grid entries, reversing their physical order. Block values
        // and Amp 1/Amp 2 identity must continue to come from the same records.
        Write(body, 0x104, 0); Write(body, 0x108, 0);
        Write(body, 0x140, 59); Write(body, 0x180, 58);
        var moved = Gen3PresetBodyDecoder.Decode(image, FractalDeviceDefinition.For(variant));
        AssertSameAmpState(snapshot, moved);

        // A preset containing only Amp 2 must not relabel it as Amp 1.
        Write(body, 0x180, 0);
        int ampBytes = (23 + ampColumns * 4) * 2;
        Array.Clear(body, 0x2c0 + ampBytes, ampBytes);
        var onlySecond = Gen3PresetBodyDecoder.Decode(image, FractalDeviceDefinition.For(variant));
        Assert.Equal(2, Assert.Single(onlySecond.Amps).BlockNumber);
    }

    [Fact]
    public void CatalogueSelectionDoesNotLeakNamesBetweenDevices()
    {
        var registry = AmpModelCatalogRegistry.CreateStarter();
        var fm9 = FractalDeviceDefinition.For(FractalDeviceVariant.FM9);
        var axe = FractalDeviceDefinition.For(FractalDeviceVariant.AxeFxIIIMarkII);
        var fm3 = FractalDeviceDefinition.For(FractalDeviceVariant.FM3);

        Assert.Equal("Matchbox D-30 EF86", registry.Resolve(fm9, new Version(12, 0), 271).DisplayName);
        Assert.False(registry.Resolve(fm9, new Version(11, 0), 271).IsKnown);
        Assert.False(registry.Resolve(axe, new Version(12, 0), 271).IsKnown);
        Assert.False(registry.Resolve(fm3, new Version(12, 0), 271).IsKnown);
        Assert.False(registry.Resolve(fm9, null, 271).IsKnown);

        var variantCatalog = new AmpModelCatalog("AxeFxIII",
            new Dictionary<int, AmpModelEntry> { [271] = new(271, "Axe-specific name", null, "fixture") },
            FractalDeviceVariant.AxeFxIIIMarkII);
        var axeRegistry = new AmpModelCatalogRegistry([variantCatalog]);
        Assert.Equal("Axe-specific name", axeRegistry.Resolve(axe, new Version(1, 0), 271).DisplayName);
        Assert.False(axeRegistry.Resolve(FractalDeviceDefinition.For(FractalDeviceVariant.AxeFxIIIOriginal),
            new Version(1, 0), 271).IsKnown);
        var fm3Catalog = new AmpModelCatalog("FM3",
            new Dictionary<int, AmpModelEntry> { [271] = new(271, "FM3-specific name", null, "fixture") },
            minFirmware: new Version(10, 0), maxFirmware: new Version(10, 0));
        var combined = new AmpModelCatalogRegistry([variantCatalog, fm3Catalog]);
        Assert.Equal("FM3-specific name", combined.Resolve(fm3, new Version(10, 0), 271).DisplayName);
        Assert.Equal("Axe-specific name", combined.Resolve(axe, new Version(10, 0), 271).DisplayName);
        Assert.False(combined.Resolve(fm9, new Version(10, 0), 271).IsKnown);
        Assert.False(combined.Resolve(fm3, new Version(11, 0), 271).IsKnown);
    }

    [Fact]
    public async Task Fm3IndexReadUsesItsModelByteGridAndChannelOffsets()
    {
        var midi = new DeviceMidi();
        using var client = new PresetNameClient(midi);
        client.SetDeviceModel(DeviceModel.FM3);
        midi.Send = query =>
        {
            Assert.Equal(0x11, query[4]);
            Assert.Equal(0x03, query[5]);
            Assert.Equal(new byte[] { 3, 127, 0 }, query[6..9]);
            Assert.True(SysexProtocol.ValidFrame(query, 0x11, 0x03, query.Length));
            foreach (var frame in DumpFixture(0x11, 511, Fm3BodyFixture())) { midi.Reply(frame); }
        };
        var reader = new PresetIndexReader(client, AmpModelCatalogRegistry.CreateStarter());
        var device = FractalDeviceDefinition.For(FractalDeviceVariant.FM3);

        var preset = await reader.ReadAsync(device, new Version(12, 0), 511, CancellationToken.None);

        Assert.Equal(FractalDeviceVariant.FM3, preset.Variant);
        Assert.Equal(511, preset.Slot);
        Assert.Equal("Test preset", preset.Name);
        Assert.Equal("Lead", preset.SceneNames[2]);
        var amp = Assert.Single(preset.Amps);
        Assert.Equal(new[] { 141, 277, 261, 145 }, amp.Channels.Select(channel => channel.Model.Id));
        Assert.All(amp.Channels, channel => Assert.False(channel.Model.IsKnown));
        Assert.Equal(new[] { 0, 1, 2, 3, 0, 1, 2, 3 }, amp.Scenes.Select(scene => scene.Channel));
        Assert.Equal(new[] { false, true, false, true, false, true, false, true }, amp.Scenes.Select(scene => scene.Bypassed));
        Assert.True(preset.Contains(277));
        Assert.True(preset.Selects(1, 277));
        Assert.False(preset.Uses(1, 277));
        Assert.True(preset.Uses(2, 261));
        Assert.Single(midi.Sent);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            reader.ReadAsync(device, null, 512, CancellationToken.None));
        // Existing name-only APIs still use the independently verified FM9 path.
        await Assert.ThrowsAsync<NotSupportedException>(() => client.StoredScenesAsync(1, CancellationToken.None));
        Assert.Single(midi.Sent);
    }

    [Fact]
    public async Task Fm9EmptyIndexChecksFreshNameWithoutRequestingSavedDump()
    {
        var midi = new DeviceMidi();
        using var client = new PresetNameClient(midi);
        client.SetDeviceModel(DeviceModel.FM9);
        midi.Send = query =>
        {
            Assert.Equal(SysexProtocol.BuildPresetNameQuery(511), query);
            byte[] payload = new byte[34]; payload[0] = 127; payload[1] = 3;
            System.Text.Encoding.ASCII.GetBytes("<EMPTY>").CopyTo(payload, 2);
            midi.Reply(SysexProtocol.Frame(0x12, 0x0d, payload));
        };
        var reader = new PresetIndexReader(client, AmpModelCatalogRegistry.CreateStarter());
        var preset = await reader.ReadAsync(FractalDeviceDefinition.For(FractalDeviceVariant.FM9), null, 511, CancellationToken.None);
        Assert.True(preset.NameOnlyEmpty);
        Assert.Equal(511, preset.Slot);
        Assert.Empty(preset.Amps);
        Assert.Single(midi.Sent);
    }

    [Theory]
    [InlineData(DeviceModel.FM9, FractalDeviceVariant.FM3)]
    [InlineData(DeviceModel.AxeFxIII, FractalDeviceVariant.FM3)]
    [InlineData(DeviceModel.FM3, FractalDeviceVariant.FM9)]
    [InlineData(DeviceModel.FM3, FractalDeviceVariant.AxeFxIIIOriginal)]
    public async Task IndexRejectsMismatchedDeviceBeforeSending(DeviceModel connected, FractalDeviceVariant requested)
    {
        var midi = new DeviceMidi();
        using var client = new PresetNameClient(midi);
        client.SetDeviceModel(connected);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.ReadStoredImageAsync(0, FractalDeviceDefinition.For(requested), CancellationToken.None));

        Assert.Empty(midi.Sent);
    }

    [Theory]
    [InlineData(58)]
    [InlineData(59)]
    public void Fm3RejectsExtraOrUnsupportedAmpInstances(int effect)
    {
        var body = Fm3BodyFixture();
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(0x104), (ushort)effect);

        Assert.Throws<InvalidDataException>(() => Gen3PresetBodyDecoder.Decode(
            new StoredPresetImage(new PresetScenes(0, "Fixture", new string[8]), new byte[16384], body),
            FractalDeviceDefinition.For(FractalDeviceVariant.FM3)));
    }

    [Theory]
    [InlineData(FractalDeviceVariant.FM3, 0x1c4)]
    [InlineData(FractalDeviceVariant.FM9, 0x254)]
    [InlineData(FractalDeviceVariant.AxeFxIIIOriginal, 0x254)]
    public void RoutingGridBoundsFollowDeviceDimensions(FractalDeviceVariant variant, int gridEnd)
    {
        var device = FractalDeviceDefinition.For(variant);
        var complete = new StoredPresetImage(new PresetScenes(0, "No amps", new string[8]),
            new byte[16384], new byte[gridEnd]);
        Assert.Empty(Gen3PresetBodyDecoder.Decode(complete, device).Amps);
        Assert.Throws<InvalidDataException>(() => Gen3PresetBodyDecoder.Decode(
            complete with { Body = new byte[gridEnd - 1] }, device));
    }

    [Theory]
    [InlineData(0x104)]
    [InlineData(0x160)]
    [InlineData(0x1bc)]
    public void Fm3AmpStateDoesNotDependOnGridPosition(int destination)
    {
        var device = FractalDeviceDefinition.For(FractalDeviceVariant.FM3);
        var image = new StoredPresetImage(new PresetScenes(0, "Fixture", new string[8]),
            new byte[16384], Fm3BodyFixture());
        var before = Gen3PresetBodyDecoder.Decode(image, device);
        BinaryPrimitives.WriteUInt16LittleEndian(image.Body.AsSpan(0x1c0), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(image.Body.AsSpan(destination), 58);

        AssertSameAmpState(before, Gen3PresetBodyDecoder.Decode(image, device));
    }

    private static void AssertSameAmpState(PresetSnapshot expected, PresetSnapshot actual)
    {
        Assert.Equal(expected.Amps.Length, actual.Amps.Length);
        for (int i = 0; i < expected.Amps.Length; i++)
        {
            Assert.Equal(expected.Amps[i].BlockNumber, actual.Amps[i].BlockNumber);
            Assert.Equal(expected.Amps[i].ModelIds, actual.Amps[i].ModelIds);
            Assert.Equal(expected.Amps[i].Scenes, actual.Amps[i].Scenes);
        }
    }

    // Independent synthetic FM3 layout: last grid cell, then a non-grid word that
    // would look like Amp 2 if parsed with FM9 dimensions. No hardware capture.
    private static byte[] Fm3BodyFixture()
    {
        byte[] body = new byte[2048];
        void Word(int offset, int value) => BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(offset), (ushort)value);
        Word(0x1c0, 58);
        Word(0x1c4, 59);
        Word(0x21e, 25); Word(0x220, 1);
        Word(0x27e, 25); Word(0x280, 1);
        Word(0x2de, 144); Word(0x2e0, 4);
        Word(0x2ee, 141); Word(0x40e, 277); Word(0x52e, 261); Word(0x64e, 145);
        // Decoys at FM9's +4-word type positions expose accidental offset reuse.
        Word(0x2f6, 900); Word(0x416, 901); Word(0x536, 902); Word(0x656, 903);
        Word(0x2be, 0); Word(0x2c0, 1); Word(0x2c2, 2); Word(0x2c4, 3);
        Word(0x2c6, 0); Word(0x2c8, 1); Word(0x2ca, 2); Word(0x2cc, 3);
        Word(0x2d0, 1); Word(0x2d4, 1); Word(0x2d8, 1); Word(0x2dc, 1);
        return body;
    }

    private static byte[][] DumpFixture(byte modelByte, int slot, byte[]? body = null)
    {
        var frames = StoredSceneTests.Fixture(body);
        foreach (var frame in frames)
        {
            frame[4] = modelByte;
            if (frame[5] == 0x77)
            {
                frame[6] = (byte)(slot >> 7);
                frame[7] = (byte)(slot & 127);
            }
            frame[^2] = SysexProtocol.ComputeChecksum(frame.AsSpan(0, frame.Length - 2));
        }
        return frames;
    }

    [Fact]
    public void SearchPredicatesDistinguishInactiveChannelAndBypassedScene()
    {
        var block = new IndexedAmpBlock(1,
        [
            new(0, new AmpModelResolution(10, "Clean", null, "fixture", true)),
            new(1, new AmpModelResolution(11, "Lead", null, "fixture", true)),
            new(2, new AmpModelResolution(12, "Other", null, "fixture", true)),
            new(3, new AmpModelResolution(13, "Fourth", null, "fixture", true)),
        ],
        [
            new AmpSceneState(0, false), new AmpSceneState(1, true),
            new AmpSceneState(1, false), new AmpSceneState(0, false),
            new AmpSceneState(0, false), new AmpSceneState(0, false),
            new AmpSceneState(0, false), new AmpSceneState(0, false),
        ]);
        var preset = new IndexedPreset(FractalDeviceVariant.FM9, 1, "Fixture", new string[8], [block], "hash");

        Assert.True(preset.Contains(11));
        Assert.True(preset.Selects(1, 11));
        Assert.False(preset.Uses(1, 11));
        Assert.True(preset.Uses(2, 11));
        Assert.False(preset.Selects(0, 11));
    }
}
