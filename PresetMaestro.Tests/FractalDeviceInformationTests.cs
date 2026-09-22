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
        Assert.Equal(model == 0x12 ? 2 : 1, midi.Sent.Count);
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
            else
            {
                midi.Reply(Frame(0x11, 0x64, [0, 0]));
                var frame = Capture("name-pm-test");
                midi.Reply(frame[..25]); midi.Reply([0xf8]); midi.Reply(frame[25..]);
            }
        };
        var logs = new List<string>();
        var result = await new FractalDeviceInformationClient(midi, logs.Add).QueryDeviceInformationAsync("FM9", "FM9", TimeSpan.FromSeconds(1), default);
        Assert.Equal("FM9 · PM-TEST", result.Label);
        Assert.Equal(2, midi.Sent.Count);
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
    public event EventHandler<byte[]>? SysexMessageReceived;
    public event EventHandler<string>? LogMessage;
    public event EventHandler<int>? PresetChangeReceived { add { } remove { } }
    public event EventHandler<NoteOnEventArgs>? NoteOnReceived { add { } remove { } }
    public bool InputOpen { get; set; } = true;
    public bool OutputOpen { get; set; } = true;
    public bool FailInput { get; set; }
    public bool Removed { get; set; }
    public List<string> InputPorts { get; } = ["FM9"];
    public List<string> OpenedThruPorts { get; } = [];
    public List<string> ClosedThruPorts { get; } = [];
    public List<byte[]> Sent { get; } = [];
    public Action<byte[]>? Send { get; set; }
    public void Reply(byte[] frame) => SysexMessageReceived?.Invoke(this, frame);
    public void FailTransport() => LogMessage?.Invoke(this, "OUTPUT ERROR: removed");
    public EventHandler<byte[]>? SnapshotListener() => SysexMessageReceived;
    public IReadOnlyCollection<string> ThruInputPorts => [];
    public IReadOnlyList<string> GetInputPortNames() => Removed ? [] : InputPorts;
    public IReadOnlyList<string> GetOutputPortNames() => Removed ? [] : ["FM9"];
    public bool OpenInput(string name, out string? error) { error = FailInput ? "Unavailable" : null; InputOpen = !FailInput; return InputOpen; }
    public bool OpenOutput(string name, out string? error) { error = null; OutputOpen = true; return true; }
    public bool OpenThruInput(string name, out string? error) { OpenedThruPorts.Add(name); error = null; return true; }
    public void CloseInput() => InputOpen = false;
    public void CloseOutput() => OutputOpen = false;
    public void CloseThruInput(string name) => ClosedThruPorts.Add(name);
    public void CloseAllThruInputs() { }
    public bool SendSysEx(byte[] frame) { Sent.Add(frame); Send?.Invoke(frame); return true; }
    public bool SendBankAndPC(int bank, int pc, int channel) => throw new InvalidOperationException("Unexpected write");
    public bool SendFavorite(int bank, int pc, int scene, int cc, int channel) => throw new InvalidOperationException("Unexpected write");
    public bool SendScene(int scene, int cc, int channel) => throw new InvalidOperationException("Unexpected write");
    public void Dispose() { CloseInput(); CloseOutput(); }
}
