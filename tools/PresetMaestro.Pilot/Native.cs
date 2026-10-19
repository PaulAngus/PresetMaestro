using PresetMaestro.Midi;

namespace PresetMaestro.Pilot;

internal static class Native
{
    internal static async Task Run(Checks report)
    {
        await using var peer = new MidiPeer(report.Directory);
        using var midi = new MidiManager();
        report.Check("Discover and open the dedicated native output", () => Checks.Require(midi.OpenOutput(peer.Name, out var error), error ?? "Output failed."));
        report.Check("Bank/program boundaries preserve channel and wire order", () =>
        {
            Checks.Require(midi.SendBankAndPC(0, 0, 1) && midi.SendBankAndPC(3, 127, 16), "Preset send failed.");
            peer.Expect([0xb0, 0, 0], [0xc0, 0], [0xbf, 0, 3], [0xcf, 127]).GetAwaiter().GetResult();
        });
        report.Check("Favorite sends bank, program and scene on channel six", () =>
        {
            Checks.Require(midi.SendFavorite(1, 42, 8, 34, 6), "Favorite send failed.");
            peer.Expect([0xb5, 0, 1], [0xc5, 42], [0xb5, 34, 7]).GetAwaiter().GetResult();
        });
        report.Check("Scene boundaries and invalid values emit only valid messages", () =>
        {
            Checks.Require(midi.SendScene(1, 34, 1) && midi.SendScene(8, 34, 16), "Scene send failed.");
            Checks.Require(!midi.SendScene(9, 34, 1) && !midi.SendBankAndPC(-1, 0, 1), "Invalid input was accepted.");
            peer.Expect([0xb0, 34, 0], [0xbf, 34, 7]).GetAwaiter().GetResult();
        });
        report.Check("SysEx is delivered byte for byte through the native driver", () =>
        {
            byte[] frame = [0xf0, 0x7d, 1, 0, 127, 0xf7];
            Checks.Require(midi.SendSysEx(frame), "SysEx send failed.");
            peer.Expect(frame).GetAwaiter().GetResult();
        });
        report.Check("Native input decodes notes, ignores note-off and receives SysEx", () =>
        {
            var notes = new System.Collections.Concurrent.ConcurrentQueue<NoteOnEventArgs>();
            var sysex = new System.Collections.Concurrent.ConcurrentQueue<byte[]>();
            midi.NoteOnReceived += (_, e) => notes.Enqueue(e);
            midi.SysexMessageReceived += (_, e) => sysex.Enqueue(e);
            Checks.Require(midi.OpenInput(peer.Name, out var error), error ?? "Input failed.");
            peer.Send([0x95, 60, 100]).GetAwaiter().GetResult();
            peer.Send([0x85, 60, 0]).GetAwaiter().GetResult();
            peer.Send([0x95, 61, 0]).GetAwaiter().GetResult();
            peer.Send([0xf0, 0x7d, 2, 0x7f, 0xf7]).GetAwaiter().GetResult();
            Checks.Wait(() => !notes.IsEmpty && !sysex.IsEmpty, "Native input did not arrive.");
            Thread.Sleep(150);
            var note = notes.Single();
            Checks.Require(note.NoteNumber == 60 && note.Velocity == 100 && note.Channel == 6 && note.SourcePort == peer.Name, "Native note decoding differs.");
            Checks.Require(sysex.Single().SequenceEqual(new byte[] { 0xf0, 0x7d, 2, 0x7f, 0xf7 }), "Input SysEx differs.");
        });
        report.Check("Closing and reopening native ports releases resources", () =>
        {
            midi.CloseInput(); midi.CloseOutput();
            Checks.Require(!midi.InputOpen && !midi.OutputOpen && !midi.SendScene(1, 34, 1), "Closed port accepted output.");
            Checks.Require(midi.OpenOutput(peer.Name, out var error), error ?? "Reopen failed.");
            Checks.Require(midi.SendScene(2, 34, 1), "Reopened port failed.");
            peer.Expect([0xb0, 34, 1]).GetAwaiter().GetResult();
        });
    }
}
