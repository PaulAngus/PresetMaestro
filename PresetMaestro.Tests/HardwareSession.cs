using System.Diagnostics;
using System.Text.Json;
using PresetMaestro.Core;
using PresetMaestro.FractalIndex;
using PresetMaestro.Midi;
using Xunit.Abstractions;

namespace PresetMaestro.Tests;

// Physical transport shared by explicitly opted-in suites. The policy checks every send.
internal sealed class HardwareSession : IMidiManager
{
    private readonly IMidiManager _midi = new MidiManager();
    private readonly StreamWriter _trace;
    private readonly object _traceLock = new();
    private readonly HardwareRequestPolicy _policy;
    private readonly FractalDeviceDefinition _definition;
    private bool _disposed;
    private readonly string _input, _output;
    private volatile bool _dropReplies;
    public bool DropReplies { get => _dropReplies; set => _dropReplies = value; }
    public string DirectoryPath { get; }
    public HardwareSession(ITestOutputHelper output, FractalDeviceDefinition? definition = null, bool allowNavigation = false, int? ampTypeParameter = null)
    {
        _definition = definition ?? FractalDeviceDefinition.For(FractalDeviceVariant.FM9);
        _policy = new(_definition, allowNavigation, ampTypeParameter);
        DirectoryPath = Path.Combine(Environment.GetEnvironmentVariable("PRESET_MAESTRO_HARDWARE_RESULTS") ?? Path.Combine(Path.GetTempPath(), "PresetMaestro-hardware"), DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        _trace = new StreamWriter(Path.Combine(DirectoryPath, "trace.jsonl")) { AutoFlush = true };
        output.WriteLine("Hardware evidence: " + DirectoryPath);
        try
        {
            var timer = Stopwatch.StartNew();
            var inputs = _midi.GetInputPortNames(); var outputs = _midi.GetOutputPortNames();
            Record("ports", new { inputs, outputs, milliseconds = timer.ElapsedMilliseconds });
            _input = Choose(inputs, "PRESET_MAESTRO_MIDI_IN"); _output = Choose(outputs, "PRESET_MAESTRO_MIDI_OUT");
            _midi.SysexMessageReceived += Receive;
            OpenSelectedPorts();
        }
        catch { Dispose(); throw; }
    }
    private string Choose(IReadOnlyList<string> ports, string variable)
    {
        string? chosen = Environment.GetEnvironmentVariable(variable);
        var matches = ports.Where(p => string.IsNullOrWhiteSpace(chosen) ? p.Contains(_definition.CatalogFamily == "AxeFxIII" ? "Axe" : _definition.CatalogFamily, StringComparison.OrdinalIgnoreCase) : p == chosen).ToArray();
        if (matches.Length != 1) { throw new InvalidOperationException($"Select exactly one {_definition.Variant} port using {variable}. Available: {string.Join(", ", ports)}"); }
        return matches[0];
    }
    public async Task<FractalDeviceInformation> IdentifyAsync(CancellationToken token)
    {
        var timer = Stopwatch.StartNew();
        var ports = await new MidiPortDiscovery(_midi).RefreshAsync(token);
        Assert.Contains(_input, ports.Inputs); Assert.Contains(_output, ports.Outputs);
        Record("async-discovery", new { milliseconds = timer.ElapsedMilliseconds });
        var device = await new FractalDeviceInformationClient(this).QueryDeviceInformationAsync(_input, _output, TimeSpan.FromSeconds(2), token);
        Assert.Equal(_definition.ModelByte switch { 0x10 => DeviceModel.AxeFxIII, 0x11 => DeviceModel.FM3, _ => DeviceModel.FM9 }, device.Model); Assert.True(Version.TryParse(device.Firmware, out _));
        Record("device", device); return device;
    }
    public void OpenSelectedPorts()
    {
        if (!_midi.OpenInput(_input, out var error)) { throw new IOException(error); }
        if (!_midi.OpenOutput(_output, out error)) { throw new IOException(error); }
    }
    public void Record(string kind, object value)
    {
        lock (_traceLock) { if (_disposed) { return; } _trace.WriteLine(JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, kind, value })); }
    }
    private void Receive(object? sender, byte[] frame)
    {
        Record("rx", Convert.ToHexString(frame));
        if (!DropReplies) { SysexMessageReceived?.Invoke(this, frame); }
    }
    public bool SendSysEx(byte[] frame)
    {
        _policy.ValidateSysEx(frame);
        Record("tx", Convert.ToHexString(frame)); return _midi.SendSysEx(frame);
    }
    public event EventHandler<byte[]>? SysexMessageReceived;
    public event EventHandler<string>? LogMessage { add => _midi.LogMessage += value; remove => _midi.LogMessage -= value; }
    public event EventHandler<NoteOnEventArgs>? NoteOnReceived { add => _midi.NoteOnReceived += value; remove => _midi.NoteOnReceived -= value; }
    public event EventHandler<int>? PresetChangeReceived { add => _midi.PresetChangeReceived += value; remove => _midi.PresetChangeReceived -= value; }
    public bool InputOpen => _midi.InputOpen;
    public bool OutputOpen => _midi.OutputOpen;
    public IReadOnlyCollection<string> ThruInputPorts => [];
    public IReadOnlyList<string> GetInputPortNames() => _midi.GetInputPortNames();
    public IReadOnlyList<string> GetOutputPortNames() => _midi.GetOutputPortNames();
    public bool OpenInput(string portName, out string? errorMessage) => _midi.OpenInput(portName, out errorMessage);
    public bool OpenOutput(string portName, out string? errorMessage) => _midi.OpenOutput(portName, out errorMessage);
    public void CloseInput() => _midi.CloseInput();
    public void CloseOutput() => _midi.CloseOutput();
    public bool OpenThruInput(string portName, out string? errorMessage) => throw new NotSupportedException();
    public void CloseThruInput(string portName) { }
    public void CloseAllThruInputs() { }
    public bool SendBankAndPC(int bank, int pc, int midiChannel)
    {
        _policy.ValidateProgramChange(bank, pc, midiChannel);
        Record("program-change", new { bank, pc, midiChannel });
        return _midi.SendBankAndPC(bank, pc, midiChannel);
    }
    public bool SendFavorite(int bank, int pc, int scene, int sceneCc, int midiChannel) => throw new NotSupportedException();
    public bool SendScene(int scene, int sceneCc, int midiChannel) => throw new NotSupportedException();
    public void Dispose()
    {
        _midi.SysexMessageReceived -= Receive;
        _midi.Dispose();
        lock (_traceLock) { if (!_disposed) { _disposed = true; _trace.Dispose(); } }
    }
}
