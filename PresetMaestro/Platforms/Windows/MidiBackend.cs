using MightBeUseful.Midi;
using MightBeUseful.Midi.Windows;

namespace PresetMaestro.Platforms;

// Application boundary only: all device handling and current/legacy policy live in the shared package.
internal static class MidiBackend
{
    public static IReadOnlyList<(string Id, string Name)> GetInputDevices() => WindowsMidiBackend.GetInputDevices();
    public static IReadOnlyList<(int Index, string Name)> GetOutputDevices() => WindowsMidiBackend.GetOutputDevices();
    public static IMidiInputPort OpenInput(string id) => WindowsMidiBackend.OpenInput(id);
    public static IMidiOutputPort OpenOutput(int index) => WindowsMidiBackend.OpenOutput(index);

    internal static IReadOnlyList<(int Index, string Name)> GetOutputPorts(int count, Func<int, string> getName) =>
        MightBeUseful.Midi.Windows.WindowsLegacyMidiBackend.GetOutputPorts(count, getName);
}
