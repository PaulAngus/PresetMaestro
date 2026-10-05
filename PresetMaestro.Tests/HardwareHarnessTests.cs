using System.Text.Json;
using PresetMaestro.FractalIndex;
using PresetNameSync.Core;

namespace PresetMaestro.Tests;

public sealed class HardwareHarnessTests
{
    [Theory]
    [InlineData(FractalDeviceVariant.FM9)]
    [InlineData(FractalDeviceVariant.FM3)]
    [InlineData(FractalDeviceVariant.AxeFxIIIOriginal)]
    [InlineData(FractalDeviceVariant.AxeFxIIIMarkII)]
    [InlineData(FractalDeviceVariant.AxeFxIIIMarkIITurbo)]
    public void PoliciesAllowOnlyReadsOrExplicitBoundedNavigation(FractalDeviceVariant variant)
    {
        var device = FractalDeviceDefinition.For(variant);
        var readOnly = new HardwareRequestPolicy(device);
        var navigation = new HardwareRequestPolicy(device, navigation: true);
        foreach (var policy in new[] { readOnly, navigation })
        {
            policy.ValidateSysEx(StoredPresetDecoder.BuildQuery(device.PresetSlots - 1, device.ModelByte, device.PresetSlots - 1));
            Assert.Throws<InvalidOperationException>(() => policy.ValidateSysEx(SysexProtocol.Frame(device.ModelByte, 0x0a, [58, 0, 0])));
            Assert.Throws<InvalidOperationException>(() => policy.ValidateSysEx(SysexProtocol.Frame(device.ModelByte, 1, [0x1b, 0, 58, 0, 10, 0, 0])));
            Assert.Throws<InvalidOperationException>(() => policy.ValidateSysEx(SysexProtocol.Frame(device.ModelByte, 0x0c, [8])));
        }
        Assert.Throws<InvalidOperationException>(() => readOnly.ValidateProgramChange(0, 0, 1));
        Assert.Throws<InvalidOperationException>(() => readOnly.ValidateSysEx(SysexProtocol.Frame(device.ModelByte, 0x0c, [0])));
        navigation.ValidateProgramChange(device.PresetSlots / 128 - 1, 127, 16);
        navigation.ValidateSysEx(SysexProtocol.Frame(device.ModelByte, 0x0c, [7]));
        Assert.Throws<ArgumentOutOfRangeException>(() => navigation.ValidateProgramChange(device.PresetSlots / 128, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => navigation.ValidateProgramChange(0, 128, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => navigation.ValidateProgramChange(0, 0, 0));
        Assert.Throws<InvalidOperationException>(() => navigation.ValidateSysEx(SysexProtocol.Frame(0x7f, 0x0c, [0])));
    }

    [Fact]
    public void AllCapturedAmpNamesDecodeAndCompareAndCorruptionIsRejected()
    {
        using var capture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fractal", "fm9-12-device-roster.json")));
        var device = FractalDeviceDefinition.For(FractalDeviceVariant.FM9);
        foreach (var row in capture.RootElement.GetProperty("Entries").EnumerateArray())
        {
            int id = row.GetProperty("Id").GetInt32(); string name = row.GetProperty("Name").GetString()!;
            byte[] frame = Convert.FromHexString(row.GetProperty("ResponseHex").GetString()!);
            Assert.Equal(name, HardwareProtocol.AmpName(frame, 0x12, 10));
            Assert.Equal("Match", AmpCompatibilityRow.Compare(device, new Version(12, 0), id, name).Status);
            Assert.Null(HardwareProtocol.AmpName(frame, 0x11, 10));
            Assert.Null(HardwareProtocol.AmpName(frame, 0x12, 11));
            frame[22] ^= 1;
            Assert.Null(HardwareProtocol.AmpName(frame, 0x12, 10));
        }
        Assert.Null(HardwareProtocol.AmpName(Convert.FromHexString(capture.RootElement.GetProperty("UnsupportedBoundaryResponseHex").GetString()!), 0x12, 10));
        Assert.Equal("Unknown ID or firmware scope", AmpCompatibilityRow.Compare(device, new Version(13, 0), 0, "New name").Status);
        Assert.Equal("Unknown ID or firmware scope", AmpCompatibilityRow.Compare(device, new Version(12, 0), 999, "New amp").Status);
        Assert.Equal("Name differs", AmpCompatibilityRow.Compare(device, new Version(12, 0), 0, "Renamed").Status);
        Assert.Equal("F000017412011F003A000A0000000000000000000039F7", Convert.ToHexString(HardwareProtocol.AmpNameQuery(0x12, 10, 0)));
    }

    [Fact]
    public async Task ExchangeRejectsWrongSlotAndModelAndReassemblesFragments()
    {
        var device = FractalDeviceDefinition.For(FractalDeviceVariant.AxeFxIIIMarkII);
        var midi = new DeviceMidi();
        midi.Send = _ =>
        {
            byte[] payload = new byte[34]; payload[0] = 127; payload[1] = 7; payload[2] = (byte)'X';
            midi.Reply(SysexProtocol.Frame(0x12, 0x0d, payload));
            payload[0] = 126; midi.Reply(SysexProtocol.Frame(0x10, 0x0d, payload));
            payload[0] = 127; byte[] valid = SysexProtocol.Frame(0x10, 0x0d, payload);
            midi.Reply(valid[..12]); midi.Reply(valid[12..]);
        };
        Assert.Equal((1023, "X"), await HardwareProtocol.PresetAsync(midi, device, 1023, default));
        midi.Send = _ => { };
        using var cancelled = new CancellationTokenSource(20);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => HardwareProtocol.PresetAsync(midi, device, 0, cancelled.Token));
    }

    [Fact]
    public async Task NavigationReadbackWaitsForActualStateInsteadOfAcceptingSendSuccess()
    {
        var device = FractalDeviceDefinition.For(FractalDeviceVariant.FM9);
        var midi = new DeviceMidi(); int queries = 0;
        midi.Send = frame =>
        {
            if (frame[5] == 0x0d)
            {
                byte[] payload = new byte[34]; payload[0] = (byte)(++queries < 2 ? 0 : 3);
                midi.Reply(SysexProtocol.Frame(0x0d, payload));
            }
            else { midi.Reply(SysexProtocol.Frame(0x0c, [2])); }
        };
        await HardwareProtocol.WaitForStateAsync(midi, device, 3, 2, default);
        Assert.Equal(2, queries);
        using var cancelled = new CancellationTokenSource(20);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => HardwareProtocol.WaitForStateAsync(midi, device, 4, 2, cancelled.Token));
    }
    [Fact]
    public async Task NavigationReadbackRecoversWhenDeviceDropsQueriesWhileChangingScene()
    {
        var device = FractalDeviceDefinition.For(FractalDeviceVariant.FM9);
        var midi = new DeviceMidi(); int presetQueries = 0, sceneQueries = 0;
        midi.Send = frame =>
        {
            if (frame[5] == 0x0d)
            {
                if (++presetQueries == 1) { return; }
                byte[] payload = new byte[34]; payload[0] = 127;
                midi.Reply(SysexProtocol.Frame(0x0d, payload));
            }
            else
            {
                Assert.Equal(SysexProtocol.BuildCurrentSceneQuery(), frame);
                if (++sceneQueries == 1) { return; }
                midi.Reply(SysexProtocol.Frame(0x0c, [0]));
            }
        };
        await HardwareProtocol.WaitForStateAsync(midi, device, 127, 0, default);
        Assert.Equal(3, presetQueries); Assert.Equal(2, sceneQueries);
    }

    [Fact]
    public async Task NavigationDeadlineReportsObservedStateAndMissingRepliesRemainFailures()
    {
        var device = FractalDeviceDefinition.For(FractalDeviceVariant.FM9);
        var midi = new DeviceMidi();
        midi.Send = frame =>
        {
            if (frame[5] == 0x0d)
            {
                byte[] payload = new byte[34]; payload[0] = 12;
                midi.Reply(SysexProtocol.Frame(0x0d, payload));
            }
            else { midi.Reply(SysexProtocol.Frame(0x0c, [3])); }
        };
        var wrong = await Assert.ThrowsAsync<TimeoutException>(() => HardwareProtocol.WaitForStateAsync(midi, device, 127, 0, default, TimeSpan.FromMilliseconds(100)));
        Assert.Contains("preset slot 127, scene 0", wrong.Message);
        Assert.Contains("Last observed slot: 12; scene: 3", wrong.Message);
        midi.Send = _ => { };
        var silent = await Assert.ThrowsAsync<TimeoutException>(() => HardwareProtocol.WaitForStateAsync(midi, device, 127, 0, default, TimeSpan.FromMilliseconds(100)));
        Assert.Contains("Last observed slot: unavailable", silent.Message);
    }

    [Fact]
    public async Task SceneNameReadRecoversFromBusyDeviceWithoutAcceptingAnotherScenesName()
    {
        var device = FractalDeviceDefinition.For(FractalDeviceVariant.FM9);
        var midi = new DeviceMidi(); int calls = 0;
        midi.Send = frame =>
        {
            Assert.Equal(SysexProtocol.BuildSceneNameQuery(7), frame);
            byte[] payload = new byte[33]; payload[0] = 6; payload[1] = (byte)'X';
            midi.Reply(SysexProtocol.Frame(0x0e, payload));
            if (++calls == 1) { return; }
            payload[0] = 7; payload[1] = (byte)'Y';
            midi.Reply(SysexProtocol.Frame(0x0e, payload));
        };
        Assert.Equal("Y", await HardwareProtocol.SceneNameAfterNavigationAsync(midi, device, 7, default));
        Assert.Equal(2, calls);
    }

}
