using PresetMaestro.Platforms;

namespace PresetMaestro.Tests;

public sealed class MidiDisabledProcessTests
{
    [Fact]
    public void DisabledMidiProcessCannotEnumerateOrOpenNativePorts()
    {
        string? previous = Environment.GetEnvironmentVariable("PRESET_MAESTRO_DISABLE_MIDI");
        try
        {
            Environment.SetEnvironmentVariable("PRESET_MAESTRO_DISABLE_MIDI", "1");
            MidiBackend.RefreshCapabilities();
            Assert.Empty(MidiBackend.GetInputDevices());
            Assert.Empty(MidiBackend.GetOutputDevices());
            Assert.Throws<InvalidOperationException>(() => MidiBackend.OpenInput("FM9"));
            Assert.Throws<InvalidOperationException>(() => MidiBackend.OpenOutput(0));
        }
        finally { Environment.SetEnvironmentVariable("PRESET_MAESTRO_DISABLE_MIDI", previous); }
    }
}
