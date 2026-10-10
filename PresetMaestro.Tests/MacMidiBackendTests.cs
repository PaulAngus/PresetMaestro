using Melanchall.DryWetMidi.Core;
using PresetMaestro.Midi;
using PresetMaestro.Platforms;

namespace PresetMaestro.Tests;

public class MacMidiBackendTests
{
    [Theory]
    [InlineData(0x0200bf, new byte[] { 0xbf, 0, 2 })]
    [InlineData(0x05cf, new byte[] { 0xcf, 5 })]
    [InlineData(0x0722bf, new byte[] { 0xbf, 34, 7 })]
    [InlineData(0x643c92, new byte[] { 0x92, 60, 100 })]
    [InlineData(0x003c82, new byte[] { 0x82, 60, 0 })]
    [InlineData(0x403ce3, new byte[] { 0xe3, 60, 64 })]
    [InlineData(0x0102f2, new byte[] { 0xf2, 2, 1 })]
    [InlineData(0xf8, new byte[] { 0xf8 })]
    public void WireCodecPreservesShortMessagesWithoutFileMetadata(int packed, byte[] expected)
    {
        using var decoder = MacMidiCodec.CreateDecoder();
        using var encoder = MacMidiCodec.CreateEncoder();
        var bytes = MidiMessageEncoding.GetShortMessageBytes(packed);
        Assert.Equal(expected, bytes);
        Assert.Equal(expected, encoder.Convert(decoder.Convert(bytes)));
        Assert.Equal(expected, encoder.Convert(decoder.Convert(bytes))); // No running-status omission.
    }

    [Theory]
    [InlineData(4)]
    [InlineData(4096)]
    public void SysExRoundTripPreservesFramingAndPayloadIncludingLongDeviceReplies(int length)
    {
        byte[] frame = [0xf0, .. Enumerable.Range(0, length).Select(i => (byte)(i % 128)), 0xf7];
        using var decoder = MacMidiCodec.CreateDecoder();
        using var encoder = MacMidiCodec.CreateEncoder();
        var midiEvent = Assert.IsType<NormalSysExEvent>(decoder.Convert(frame));
        Assert.Equal(frame, encoder.Convert(midiEvent));
    }

    [MacFact]
    public void NativeCoreMidiLibraryLoadsAndEnumeratesWithoutHardware()
    {
        // No sends or port connections: also runs on hosted macOS CI with zero endpoints.
        _ = MidiBackend.GetInputDevices();
        _ = MidiBackend.GetOutputDevices();
    }
}

internal sealed class MacFactAttribute : FactAttribute
{
    public MacFactAttribute()
    {
        if (!OperatingSystem.IsMacOS()) { Skip = "Requires native CoreMIDI on macOS."; }
    }
}
