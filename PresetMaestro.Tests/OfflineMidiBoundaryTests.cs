using PresetMaestro.Platforms;

namespace PresetMaestro.Tests;

public sealed class OfflineMidiBoundaryTests
{
    [Fact]
    public void OfflineBuildRefusesNativeMidiEvenWhenProcessOptOutIsRemoved()
    {
        string? previous = Environment.GetEnvironmentVariable("PRESET_MAESTRO_DISABLE_MIDI");
        try
        {
            Environment.SetEnvironmentVariable("PRESET_MAESTRO_DISABLE_MIDI", null);
            MidiBackend.RefreshCapabilities();
            Assert.Empty(MidiBackend.GetInputDevices());
            Assert.Empty(MidiBackend.GetOutputDevices());
            Assert.Throws<InvalidOperationException>(() => MidiBackend.OpenInput("offline-boundary-must-not-open"));
            Assert.Throws<InvalidOperationException>(() => MidiBackend.OpenOutput(-1));
        }
        finally { Environment.SetEnvironmentVariable("PRESET_MAESTRO_DISABLE_MIDI", previous); }
    }
}
