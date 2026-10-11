using PresetMaestro.Midi;
using PresetMaestro.Platforms;

namespace PresetSender;

internal interface ISenderMidiBackend
{
    IReadOnlyList<(string Id, string Name)> GetInputDevices();
    IReadOnlyList<(int Index, string Name)> GetOutputDevices();
    IMidiInputPort OpenInput(string id);
    IMidiOutputPort OpenOutput(int index);
    bool SupportsVirtualOutput { get; }
    IMidiOutputPort OpenVirtualOutput();
}

internal sealed class NativeMidiBackend : ISenderMidiBackend
{
    public IReadOnlyList<(string Id, string Name)> GetInputDevices() => MidiBackend.GetInputDevices();
    public IReadOnlyList<(int Index, string Name)> GetOutputDevices() => MidiBackend.GetOutputDevices();
    public IMidiInputPort OpenInput(string id) => MidiBackend.OpenInput(id);
    public IMidiOutputPort OpenOutput(int index) => MidiBackend.OpenOutput(index);
#if MAESTRO_WINDOWS
    public bool SupportsVirtualOutput => true;
    public IMidiOutputPort OpenVirtualOutput() => VirtualMidiHost.Open();
#else
    public bool SupportsVirtualOutput => false;
    public IMidiOutputPort OpenVirtualOutput() => throw new PlatformNotSupportedException("Built-in virtual output currently requires Windows MIDI Services.");
#endif
}
