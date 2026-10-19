using MightBeUseful.Midi;
using MightBeUseful.Midi.Windows;

namespace PresetMaestro.Platforms;

// Application boundary only: all device handling and current/legacy policy live in the shared package.
internal static class MidiBackend
{
    private static bool Disabled =>
#if MAESTRO_OFFLINE_NO_MIDI
        true;
#else
        Environment.GetEnvironmentVariable("PRESET_MAESTRO_DISABLE_MIDI") == "1";
#endif
    public static void RefreshCapabilities() { if (!Disabled) { WindowsMidiBackend.RefreshCapabilities(); } }

    public static IReadOnlyList<(string Id, string Name)> GetInputDevices() => Disabled ? [] : WindowsMidiBackend.GetInputDevices();
    public static IReadOnlyList<(int Index, string Name)> GetOutputDevices() => Disabled ? [] : WindowsMidiBackend.GetOutputDevices();
    public static IMidiInputPort OpenInput(string id) => Disabled ? throw new InvalidOperationException("MIDI is disabled in this test process.") : WindowsMidiBackend.OpenInput(id);
    public static IMidiOutputPort OpenOutput(int index) => Disabled ? throw new InvalidOperationException("MIDI is disabled in this test process.") : WindowsMidiBackend.OpenOutput(index);

    internal static IReadOnlyList<(int Index, string Name)> GetOutputPorts(int count, Func<int, string> getName) =>
        MightBeUseful.Midi.Windows.WindowsLegacyMidiBackend.GetOutputPorts(count, getName);
}
