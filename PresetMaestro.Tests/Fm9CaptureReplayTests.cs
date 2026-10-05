using System.Buffers.Binary;
using System.Text.Json;
using PresetMaestro.FractalIndex;
using PresetMaestro.Midi;
using PresetNameSync.Core;

namespace PresetMaestro.Tests;

public sealed class Fm9CaptureReplayTests
{
    private static byte[][] Frames() => File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fractal", "fm9-12-slot-000.hex"))
        .Where(line => !string.IsNullOrWhiteSpace(line)).Select(Convert.FromHexString).ToArray();

    [Fact]
    public async Task ProductionReaderReplaysPhysicalFm9CaptureWithExpectedScenesAmpsAndFingerprints()
    {
        var midi = new DeviceMidi();
        midi.Send = request =>
        {
            Assert.Equal(StoredPresetDecoder.BuildQuery(0), request);
            foreach (var frame in Frames()) { midi.Reply(frame); }
        };
        using var client = new PresetNameClient(midi);
        var device = FractalDeviceDefinition.For(FractalDeviceVariant.FM9);
        var image = await client.ReadStoredImageAsync(0, device, default);
        var actual = Gen3PresetBodyDecoder.Decode(image, device);
        var expected = JsonSerializer.Deserialize<IndexedPreset>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fractal", "fm9-12-slot-000.json")))!;
        Assert.Equal(expected.Name, actual.Name); Assert.Equal(expected.SceneNames, actual.SceneNames);
        Assert.Equal(expected.ContentSha256, actual.ContentSha256); Assert.Equal(expected.BypassIgnoredSha256, actual.BypassIgnoredSha256);
        Assert.Equal(expected.Amps.SelectMany(a => a.Channels.Select(c => c.Model.Id)), actual.Amps.SelectMany(a => a.ModelIds));
        Assert.Equal(expected.Amps.SelectMany(a => a.Scenes), actual.Amps.SelectMany(a => a.Scenes));

        // Modify the captured body only in memory; no request is sent to hardware.
        int amp = Enumerable.Range(0, (image.Body.Length - 0x200 - 34) / 2).Select(i => 0x200 + i * 2)
            .First(offset => BinaryPrimitives.ReadUInt16LittleEndian(image.Body.AsSpan(offset + 30, 2)) == 147 &&
                BinaryPrimitives.ReadUInt16LittleEndian(image.Body.AsSpan(offset + 32, 2)) == 4);
        image.Body[amp + 14] ^= 1;
        var bypass = Gen3PresetBodyDecoder.Decode(image, device);
        Assert.NotEqual(actual.Amps[0].Scenes[0].Bypassed, bypass.Amps[0].Scenes[0].Bypassed);
        Assert.Equal(actual.BypassIgnoredSha256, bypass.BypassIgnoredSha256);
        image.Body[amp + 48] ^= 1;
        Assert.NotEqual(actual.BypassIgnoredSha256, Gen3PresetBodyDecoder.Decode(image, device).BypassIgnoredSha256);
    }

    [Fact]
    public void DamagedHardwareCaptureFailsIntegrityChecks()
    {
        var frames = Frames(); var decoder = new StoredPresetDecoder();
        Assert.Null(decoder.AcceptImage(frames[0], 0));
        frames[1][30] ^= 1;
        Assert.Throws<InvalidDataException>(() => decoder.AcceptImage(frames[1], 0));
    }

    [Fact]
    public void HardwareAllowlistExcludesSceneChangesAndParameterWrites()
    {
        var allowed = new HardwareRequestPolicy(FractalDeviceDefinition.For(FractalDeviceVariant.FM9)).AllowedReads;
        Assert.DoesNotContain(Convert.ToHexString(SysexProtocol.Frame(0x0c, [0])), allowed);
        Assert.DoesNotContain(Convert.ToHexString(SysexProtocol.Frame(0x0a, [58, 0, 0])), allowed);
        Assert.Contains(Convert.ToHexString(SysexProtocol.BuildCurrentSceneQuery()), allowed);
        Assert.Contains(Convert.ToHexString(StoredPresetDecoder.BuildQuery(511)), allowed);
    }
}
