using PresetMaestro.Core;
using PresetMaestro.Midi;
using PresetNameSync.Core;

namespace PresetMaestro.Tests;

public class FractalDeviceInformationTests
{
    internal static byte[] Capture(string name) => Convert.FromHexString(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fractal", name + ".hex")).Replace(" ", "").Trim());

    [Fact]
    public void RequestsExactlyMatchEditorCapture()
    {
        Assert.Equal(Capture("identity-request"), FractalDeviceInformationClient.BuildDeviceInformationRequest());
        Assert.Equal(Capture("name-request"), FractalDeviceInformationClient.BuildDeviceNameRequest(0x12));
        Assert.Throws<ArgumentOutOfRangeException>(() => FractalDeviceInformationClient.BuildDeviceNameRequest(0x11));
        Assert.Throws<ArgumentOutOfRangeException>(() => FractalDeviceInformationClient.BuildDeviceNameRequest(0x10));
    }

    [Theory]
    [InlineData("name-pm-test", "PM-TEST")]
    [InlineData("name-fm9", "FM9")]
    public void CapturedNamesDecode(string fixture, string expected)
    {
        Assert.True(FractalDeviceInformationClient.TryParseDeviceNameResponse(Capture(fixture), out var name));
        Assert.Equal(expected, name);
        Assert.Equal("FM9 · " + expected, new FractalDeviceInformation(DeviceModel.FM9, name).Label);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void CorruptedIdentityIsRejected(int index)
    {
        var frame = Capture("identity-fm9"); frame[index] ^= 1;
        Assert.False(FractalDeviceInformationClient.TryValidateFractalFrame(frame));
    }

    [Theory]
    [InlineData(0x10, DeviceModel.AxeFxIII)]
    [InlineData(0x11, DeviceModel.FM3)]
    [InlineData(0x12, DeviceModel.FM9)]
    public async Task ValidIdentityMapsSupportedModels(int model, DeviceModel expected)
    {
        var midi = new DeviceMidi();
        midi.Send = request => { if (request[5] == 0) { midi.Reply(Frame((byte)model, 0x64, [0, 0])); } };
        var result = await new FractalDeviceInformationClient(midi).QueryDeviceInformationAsync("USB", "USB", TimeSpan.FromMilliseconds(20), default);
        Assert.Equal(expected, result.Model);
        Assert.Null(result.DeviceName);
        Assert.Null(result.Firmware);
        Assert.Equal(model == 0x12 ? 3 : 2, midi.Sent.Count);
    }

    [Theory]
    [InlineData("PM-TEST8", "FM9 · PM-TEST8")]
    [InlineData("", "FM9 · Unnamed device")]
    [InlineData(" A ", "FM9 ·  A ")]
    public void SyntheticBoundaryCasesPreserveVisibleText(string input, string label)
    {
        // Not hardware fixtures: tests of the bounded decoder's boundary behavior.
        Assert.True(FractalDeviceInformationClient.TryParseDeviceNameResponse(SyntheticName(input), out var name));
        Assert.Equal(input, name);
        Assert.Equal(label, new FractalDeviceInformation(DeviceModel.FM9, name).Label);
    }

    [Theory]
    [InlineData("envelope")]
    [InlineData("parameter")]
    [InlineData("model")]
    [InlineData("padding")]
    [InlineData("checksum")]
    [InlineData("truncated")]
    [InlineData("long")]
    [InlineData("nonascii")]
    public void InvalidNamesAreNotDecoded(string reason)
    {
        var frame = Capture("name-pm-test");
        switch (reason)
        {
            case "envelope": frame[6] = 0; break;
            case "parameter": frame[10] ^= 1; break;
            case "model": frame[4] = 0x11; break;
            case "padding": frame[57] = 1; break;
            case "truncated": frame = frame[..^1]; break;
            case "long": frame = SyntheticName("123456789"); break;
            case "nonascii": frame = SyntheticName("\u0001"); break;
        }
        if (reason != "truncated") { frame[^2] = SysexProtocol.ComputeChecksum(frame.AsSpan(0, frame.Length - 2)); }
        if (reason == "checksum") { frame[^2] ^= 1; }
        Assert.False(FractalDeviceInformationClient.TryParseDeviceNameResponse(frame, out var name));
        Assert.Null(name);
    }

    [Fact]
    public async Task ImmediateAndFragmentedResponsesProduceNameWithoutLoggingPayload()
    {
        var midi = new DeviceMidi();
        midi.Send = request =>
        {
            if (request[5] == 0) { midi.Reply(Capture("identity-fm9")); }
            else if (request[5] == 1)
            {
                midi.Reply(Frame(0x11, 0x64, [0, 0]));
                var frame = Capture("name-pm-test");
                midi.Reply(frame[..25]); midi.Reply([0xf8]); midi.Reply(frame[25..]);
            }
        };
        var logs = new List<string>();
        var result = await new FractalDeviceInformationClient(midi, logs.Add).QueryDeviceInformationAsync("FM9", "FM9", TimeSpan.FromSeconds(1), default);
        Assert.Equal("FM9 · PM-TEST", result.Label);
        Assert.Equal(3, midi.Sent.Count);
        Assert.DoesNotContain(logs, s => s.Contains("PM-TEST") || s.Contains("28 13 25 55"));
        Assert.Contains(logs, s => s.Contains("payload redacted"));
    }

    [Fact]
    public async Task UnrelatedAndRejectedResponsesNeverIdentify()
    {
        var midi = new DeviceMidi();
        midi.Send = request =>
        {
            midi.Reply(request);
            midi.Reply(Capture("name-pm-test"));
            midi.Reply(Frame(0x12, 0x64, [0x46, 5])); // Actual rejection from previous diagnostics.
            midi.Reply(Frame(0x12, 0x64, [0, 5]));
            midi.Reply(Frame(3, 0x64, [0, 0]));
            midi.Reply([0xf8, 0xfe]);
        };
        await Assert.ThrowsAsync<TimeoutException>(() => new FractalDeviceInformationClient(midi).QueryDeviceInformationAsync("FM9", "FM9", TimeSpan.FromMilliseconds(20), default));
    }

    [Fact]
    public async Task TimeoutMissingInputAndCancellationNeverIdentify()
    {
        var midi = new DeviceMidi(); var client = new FractalDeviceInformationClient(midi);
        await Assert.ThrowsAsync<TimeoutException>(() => client.QueryDeviceInformationAsync("FM9", "FM9", TimeSpan.FromMilliseconds(10), default));
        midi.InputOpen = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.QueryDeviceInformationAsync("FM9", "FM9", TimeSpan.FromMilliseconds(10), default));
        midi.InputOpen = true;
        using var cancellation = new CancellationTokenSource();
        var pending = client.QueryDeviceInformationAsync("FM9", "FM9", TimeSpan.FromSeconds(1), cancellation.Token);
        var oldCallback = midi.SnapshotListener(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Null(midi.SnapshotListener());
        midi.Send = _ => oldCallback?.Invoke(midi, Capture("identity-fm9"));
        await Assert.ThrowsAsync<TimeoutException>(() => client.QueryDeviceInformationAsync("FM9", "FM9", TimeSpan.FromMilliseconds(10), default));
    }

    [Theory]
    [InlineData(0x10, 0x1d)]
    [InlineData(0x11, 0x1c)]
    [InlineData(0x12, 0x1f)]
    public void FirmwareQueryHasNoWritePayload(int model, int checksum)
    {
        Assert.Equal(new byte[] { 0xf0, 0, 1, 0x74, (byte)model, 0x08, (byte)checksum, 0xf7 },
            FractalDeviceInformationClient.BuildFirmwareRequest((byte)model));
        Assert.Throws<ArgumentOutOfRangeException>(() => FractalDeviceInformationClient.BuildFirmwareRequest(0x7f));
    }

    [Theory]
    [InlineData("loopback")]
    [InlineData("missing minor")]
    [InlineData("checksum")]
    [InlineData("model")]
    [InlineData("function")]
    [InlineData("seven bit")]
    [InlineData("framing")]
    public void FirmwareRejectsInvalidOrUnrelatedFrames(string reason)
    {
        var frame = Frame(0x12, 0x08, [12, 0, 0, 1]);
        switch (reason)
        {
            case "loopback": frame = FractalDeviceInformationClient.BuildFirmwareRequest(0x12); break;
            case "missing minor": frame = Frame(0x12, 0x08, [12]); break;
            case "checksum": frame[^2] ^= 1; break;
            case "model": frame = Frame(0x11, 0x08, [12, 0]); break;
            case "function": frame = Frame(0x12, 0x64, [8, 0]); break;
            case "seven bit": frame = Frame(0x12, 0x08, [12, 0x80]); break;
            case "framing": frame[^1] = 0; break;
        }
        Assert.False(FractalDeviceInformationClient.TryParseFirmwareResponse(frame, 0x12, out var firmware));
        Assert.Null(firmware);
    }

    [Fact]
    public async Task FragmentedFirmwareResponseUsesPayloadVersionAndCannotLeakAcrossConnections()
    {
        var midi = new DeviceMidi();
        bool provideFirmware = true;
        midi.Send = request =>
        {
            if (request[5] == 0) { midi.Reply(Capture("identity-fm9")); }
            if (request[5] == 1) { midi.Reply(Capture("name-fm9")); }
            if (request[5] == 8 && provideFirmware)
            {
                midi.Reply(request); // A loopback query is not a version.
                midi.Reply(Frame(0x11, 8, [99, 99]));
                // Synthetic protocol fixture, not a locally captured hardware response.
                var reply = Frame(0x12, 8, [12, 3, 0, 1, .. System.Text.Encoding.ASCII.GetBytes("08/19/2026"), 0]);
                midi.Reply(reply[..7]); midi.Reply([0xf8]); midi.Reply(reply[7..]);
            }
        };
        var client = new FractalDeviceInformationClient(midi);
        var detected = await client.QueryDeviceInformationAsync("FM9", "FM9", TimeSpan.FromMilliseconds(20), default);
        Assert.Equal("12.03", detected.Firmware);
        Assert.Equal("FM9", detected.DeviceName);
        Assert.Equal(new byte[] { 0, 8, 1 }, midi.Sent.Select(frame => frame[5]));
        provideFirmware = false;
        detected = await client.QueryDeviceInformationAsync("FM9", "FM9", TimeSpan.FromMilliseconds(20), default);
        Assert.Null(detected.Firmware);
        Assert.Equal("FM9", detected.DeviceName);
        Assert.Null(midi.SnapshotListener());
    }

    internal static byte[] SyntheticName(string text)
    {
        var frame = Capture("name-pm-test");
        byte[] decoded = new byte[32];
        System.Text.Encoding.ASCII.GetBytes(text).CopyTo(decoded, 0);
        string bits = string.Concat(decoded.Select(b => Convert.ToString(b, 2).PadLeft(8, '0'))) + "000";
        for (int i = 0; i < 37; i++) { frame[21 + i] = Convert.ToByte(bits.Substring(i * 7, 7), 2); }
        frame[^2] = SysexProtocol.ComputeChecksum(frame.AsSpan(0, frame.Length - 2));
        return frame;
    }

    internal static byte[] Frame(byte model, byte function, byte[] payload)
    {
        byte[] frame = [0xf0, 0, 1, 0x74, model, function, .. payload, 0, 0xf7];
        frame[^2] = SysexProtocol.ComputeChecksum(frame.AsSpan(0, frame.Length - 2));
        return frame;
    }
}

internal sealed class DeviceMidi : IMidiManager
{
    public Func<Task<MidiPorts>>? Discover { get; set; }
    public Task<MidiPorts> DiscoverPortsAsync() => Discover?.Invoke() ?? Task.FromResult(new MidiPorts(GetInputPortNames(), GetOutputPortNames()));
    public event EventHandler<byte[]>? SysexMessageReceived;
    public event EventHandler<string>? LogMessage;
    public event EventHandler<int>? PresetChangeReceived { add { } remove { } }
    public event EventHandler<NoteOnEventArgs>? NoteOnReceived { add { } remove { } }
    public bool InputOpen { get; set; } = true;
    public bool OutputOpen { get; set; } = true;
    public bool FailInput { get; set; }
    public bool ThrowThru { get; set; }
    public bool Removed { get; set; }
    public Exception? OutputEnumerationError { get; set; }
    public bool OutputRemoved { get; set; }
    public List<string> InputPorts { get; } = ["FM9"];
    public List<string> OpenedThruPorts { get; } = [];
    public List<string> ClosedThruPorts { get; } = [];
    public List<byte[]> Sent { get; } = [];
    public Action<byte[]>? Send { get; set; }
    public bool AllowWrites { get; set; }
    public bool FailWrites { get; set; }
    public List<(string Kind, int Bank, int Program, int Scene, int Cc, int Channel)> Writes { get; } = [];
    public void Reply(byte[] frame) => SysexMessageReceived?.Invoke(this, frame);
    public void FailTransport() => LogMessage?.Invoke(this, "OUTPUT ERROR: removed");
    public EventHandler<byte[]>? SnapshotListener() => SysexMessageReceived;
    public IReadOnlyCollection<string> ThruInputPorts => [];
    public IReadOnlyList<string> GetInputPortNames() => Removed ? [] : InputPorts;
    public IReadOnlyList<string> GetOutputPortNames() => OutputEnumerationError is { } error
        ? throw error : Removed || OutputRemoved ? [] : ["FM9"];
    public bool OpenInput(string name, out string? error) { error = FailInput ? "Unavailable" : null; InputOpen = !FailInput; return InputOpen; }
    public bool OpenOutput(string name, out string? error) { error = null; OutputOpen = true; return true; }
    public bool OpenThruInput(string name, out string? error)
    {
        if (ThrowThru) { throw new InvalidOperationException("thru driver failure"); }
        OpenedThruPorts.Add(name); error = null; return true;
    }
    public void CloseInput() => InputOpen = false;
    public void CloseOutput() => OutputOpen = false;
    public void CloseThruInput(string name) => ClosedThruPorts.Add(name);
    public void CloseAllThruInputs() { }
    public bool SendSysEx(byte[] frame) { Sent.Add(frame); Send?.Invoke(frame); return true; }
    public bool SendBankAndPC(int bank, int pc, int channel) => RecordWrite("Preset", bank, pc, 0, 0, channel);
    public bool SendFavorite(int bank, int pc, int scene, int cc, int channel) => RecordWrite("PresetScene", bank, pc, scene, cc, channel);
    public bool SendScene(int scene, int cc, int channel) => RecordWrite("Scene", 0, 0, scene, cc, channel);
    private bool RecordWrite(string kind, int bank, int program, int scene, int cc, int channel)
    {
        if (!AllowWrites) { throw new InvalidOperationException("Unexpected write"); }
        Writes.Add((kind, bank, program, scene, cc, channel)); return !FailWrites;
    }
    public void Dispose() { CloseInput(); CloseOutput(); }
}
