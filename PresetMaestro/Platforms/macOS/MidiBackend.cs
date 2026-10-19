using MightBeUseful.Midi;
using MightBeUseful.Midi.CoreMidi;

namespace PresetMaestro.Platforms;

// Application boundary only: all device handling and current/legacy policy live in the shared package.
internal static class MidiBackend
{
    private static bool Disabled => Environment.GetEnvironmentVariable("PRESET_MAESTRO_DISABLE_MIDI") == "1";
    public static void RefreshCapabilities() { }

    public static IReadOnlyList<(string Id, string Name)> GetInputDevices() => Disabled ? [] : CoreMidiBackend.GetInputDevices();
    public static IReadOnlyList<(int Index, string Name)> GetOutputDevices() => Disabled ? [] : CoreMidiBackend.GetOutputDevices();
    public static IMidiInputPort OpenInput(string id) => Disabled ? throw new InvalidOperationException("MIDI is disabled in this test process.") : CoreMidiBackend.OpenInput(id);
    public static IMidiOutputPort OpenOutput(int index) => Disabled ? throw new InvalidOperationException("MIDI is disabled in this test process.") : CoreMidiBackend.OpenOutput(index);
}
