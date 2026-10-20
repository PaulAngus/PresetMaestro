using System.Buffers.Binary;
using System.Text;
using PresetMaestro.Core;
using PresetMaestro.Midi;
using PresetNameSync.Core;

namespace PresetMaestro.Tests;

public sealed class Fm9ActivePresetTests
{
    internal static byte[] Response(Fm9ActivePreset state, int? editedValue = null)
    {
        byte[] decoded = new byte[640];
        BinaryPrimitives.WriteInt32LittleEndian(decoded, state.Slot);
        BinaryPrimitives.WriteInt32LittleEndian(decoded.AsSpan(4), editedValue ?? (state.Edited ? 1 : 0));
        Encoding.ASCII.GetBytes(state.PresetName.PadRight(32)).CopyTo(decoded, 16);
        for (int i = 0; i < 8; i++) { Encoding.ASCII.GetBytes((state.SceneNames[i] ?? "").PadRight(32)).CopyTo(decoded, 48 + i * 32); }
        // Independent bit-string encoder; no production unpacking code is reused.
        string bits = string.Concat(decoded.Select(b => Convert.ToString(b, 2).PadLeft(8, '0'))).PadRight(5124, '0');
        byte[] payload = new byte[747];
        payload[0] = 0x2e; payload[14] = 5;
        for (int i = 0; i < 732; i++) { payload[15 + i] = Convert.ToByte(bits.Substring(i * 7, 7), 2); }
        return SysexProtocol.Frame(0x01, payload);
    }

    [Fact]
    public async Task ExactObservedQueryReadsAllLiveNamesAndEditedFlagWithoutWrites()
    {
        Assert.Equal("F0 00 01 74 12 01 2E 00 00 00 00 00 00 00 00 00 00 00 00 00 00 38 F7",
            SysexProtocol.ToHex(Fm9ActivePreset.BuildQuery()));
        var state = new Fm9ActivePreset(253, true, "Preset", ["First", "Second", "", "Four", "Five", "Six", "Seven", "Eight"]);
        var midi = new DeviceMidi();
        midi.Send = _ =>
        {
            var frame = Response(state);
            midi.Reply(frame[..200]); midi.Reply(frame[200..]);
        };
        using var client = new PresetNameClient(midi);
        var actual = await client.ActivePresetAsync(CancellationToken.None);
        Assert.Equal(253, actual.Slot); Assert.True(actual.Edited);
        Assert.Equal(state.SceneNames, actual.SceneNames); Assert.Equal("Preset", actual.PresetName);
        Assert.Single(midi.Sent); Assert.Empty(midi.Writes);
    }

    [Theory]
    [InlineData("checksum")]
    [InlineData("model")]
    [InlineData("subaction")]
    [InlineData("length")]
    [InlineData("slot")]
    [InlineData("padding")]
    [InlineData("edited")]
    public void RejectsMalformedOrUnsupportedStatus(string corruption)
    {
        var frame = Response(new(corruption == "slot" ? 512 : 0, false, "Preset", new string[8]), corruption == "edited" ? 2 : null);
        if (corruption == "checksum") { frame[30] ^= 1; }
        else
        {
            if (corruption == "model") { frame[4] = 0x10; }
            if (corruption == "subaction") { frame[6] = 0x2b; }
            if (corruption == "length") { frame[19] = 1; }
            if (corruption == "padding") { frame[^3] |= 1; }
            frame[^2] = SysexProtocol.ComputeChecksum(frame.AsSpan(0, frame.Length - 2));
        }
        Assert.False(Fm9ActivePreset.TryParse(frame, out _));
        Assert.False(Fm9ActivePreset.TryParse(frame.AsSpan(0, frame.Length - 1), out _));
    }

    [Fact]
    public void EditedNamesOnlyScheduleVerificationAfterCleanOrNavigation()
    {
        var tracker = new SavedSceneChangeTracker();
        string[] names = ["A", "B", "C", "D", "E", "F", "G", "H"];
        var saved = new PresetScenes(0, "Preset", names);
        tracker.Observe(new(0, false, "Preset", names), saved);
        Assert.Null(tracker.NextSlot);
        var swapped = names.ToArray(); (swapped[0], swapped[1]) = (swapped[1], swapped[0]);
        tracker.Observe(new(0, true, "Preset", swapped), saved);
        Assert.Null(tracker.NextSlot);
        tracker.Observe(new(1, false, "Other", names), new(1, "Other", names));
        Assert.Equal(0, tracker.NextSlot);
        tracker.Complete(0);
        tracker.Observe(new(0, false, "Preset", names), saved); // discarded
        Assert.Null(tracker.NextSlot);
        tracker.Observe(new(0, false, "Preset", swapped), saved); // save between polls
        Assert.Equal(0, tracker.NextSlot);
        tracker.Defer(0);
        tracker.Observe(new(0, false, "Preset", swapped), saved);
        Assert.Null(tracker.NextSlot);
        tracker.Reset();
        Assert.Null(tracker.NextSlot);
        tracker.Observe(new(0, true, "Preset", names), saved);
        tracker.Observe(new(0, false, "Preset", names), saved);
        Assert.Equal(0, tracker.NextSlot); // An edited-to-clean transition also checks swaps with unchanged names.
    }
}
