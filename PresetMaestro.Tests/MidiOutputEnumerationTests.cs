using NAudio;
using PresetMaestro.Midi;

namespace PresetMaestro.Tests;

public class MidiOutputEnumerationTests
{
    [Fact]
    public void UnavailableDriverDoesNotHideHealthyOutputsOrChangeDeviceIndices()
    {
        var visited = new List<int>();
        var ports = MidiManager.GetOutputPorts(4, index =>
        {
            visited.Add(index);
            return index switch
            {
                0 => "Studio 68 MIDI Out",
                2 => "FM9 MIDI Out",
                _ => throw new MmException(MmResult.NoDriver, "midiOutGetDevCaps")
            };
        });

        Assert.Equal([0, 1, 2, 3], visited);
        Assert.Equal([(0, "Studio 68 MIDI Out"), (2, "FM9 MIDI Out")], ports);
    }
}
