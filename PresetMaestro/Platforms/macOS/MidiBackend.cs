using MightBeUseful.Midi;
using MightBeUseful.Midi.CoreMidi;

namespace PresetMaestro.Platforms;

// Application boundary only: all device handling and current/legacy policy live in the shared package.
internal static class MidiBackend
{
    public static void RefreshCapabilities() { }

    public static IReadOnlyList<(string Id, string Name)> GetInputDevices() => CoreMidiBackend.GetInputDevices();
    public static IReadOnlyList<(int Index, string Name)> GetOutputDevices() => CoreMidiBackend.GetOutputDevices();
    public static IMidiInputPort OpenInput(string id) => CoreMidiBackend.OpenInput(id);
    public static IMidiOutputPort OpenOutput(int index) => CoreMidiBackend.OpenOutput(index);
}
