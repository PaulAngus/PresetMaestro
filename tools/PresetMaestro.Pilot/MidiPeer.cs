using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using MightBeUseful.Midi;
using PresetMaestro.Midi;
#if MAESTRO_WINDOWS
using MightBeUseful.Midi.Windows;
#else
using MightBeUseful.Midi.CoreMidi;
#endif

namespace PresetMaestro.Pilot;

internal sealed class MidiPeer : IAsyncDisposable
{
    private readonly IMidiDuplexPort port;
    private readonly CancellationTokenSource stop = new();
    private readonly Channel<byte[]> messages = Channel.CreateUnbounded<byte[]>();
    private readonly Task reader;
    private readonly StreamWriter trace;
    private readonly object traceGate = new();
    internal string Name { get; }
    internal bool EmulateDevice { get; set; }
    internal ConcurrentQueue<byte[]> ShortMessages { get; } = new();
    internal MidiPeer(string directory)
    {
        Name = "PresetMaestro-Test-" + Guid.NewGuid().ToString("N")[..10];
#if MAESTRO_WINDOWS
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26200) || !WindowsMidiBackend.Capabilities.CanCreateVirtualPorts)
        { throw new PrerequisiteException("Native tests require Windows MIDI Services virtual ports on this host."); }
        port = WindowsMidiServicesDuplexPort.Open(Name);
#else
        port = CoreMidiDuplexPort.OpenVirtual(Name);
#endif
        trace = new StreamWriter(Path.Combine(directory, "midi-" + Guid.NewGuid().ToString("N")[..6] + ".jsonl")) { AutoFlush = true };
        reader = Task.Run(Read);
        Checks.Wait(() => MidiManager.GetInputPortNames().Contains(Name) && MidiManager.GetOutputPortNames().Contains(Name), "Dedicated virtual MIDI endpoints did not appear.");
    }
    private async Task Read()
    {
        var assemblers = Enumerable.Range(0, 16).Select(_ => new SysEx7Assembler()).ToArray();
        try
        {
            await foreach (var packet in port.ReadAllAsync(stop.Token))
            {
                byte[]? data;
                if (packet.Format == MidiPacketFormat.Midi1) { data = packet.Data.ToArray(); }
                else
                {
                    uint[] words = packet.ToUmpWords();
                    if ((words[0] >> 28) is not (1 or 2 or 3)) { continue; }
                    data = UmpMidi1Codec.Decode(words, assemblers[(words[0] >> 24) & 15]);
                }
                if (data is null) { continue; }
                Log("from-app", data);
                if (data[0] != 0xf0) { ShortMessages.Enqueue(data); }
                messages.Writer.TryWrite(data);
                if (EmulateDevice)
                {
                    // Fixed synthetic FM3 replies. Never opens or impersonates a physical endpoint.
                    if (data.AsSpan().SequenceEqual(new byte[] { 0xf0, 0, 1, 0x74, 0x7f, 0, 0x7a, 0xf7 }))
                    { await Send([0xf0, 0, 1, 0x74, 0x11, 0x64, 0, 0, 0x70, 0xf7]); }
                    else if (data.Length == 8 && data[0] == 0xf0 && data[4] == 0x11 && data[5] == 8)
                    { await Send([0xf0, 0, 1, 0x74, 0x11, 8, 9, 0, 0x15, 0xf7]); }
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception error) { messages.Writer.TryComplete(error); throw; }
        finally { foreach (var assembler in assemblers) { assembler.Dispose(); } }
    }
    private void Log(string direction, byte[] bytes)
    {
        lock (traceGate) { trace.WriteLine(JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, direction, hex = Convert.ToHexString(bytes) })); }
    }
    internal async Task Send(byte[] bytes) { Log("to-app", bytes); await port.SendAsync(new MidiPacket(MidiPacketFormat.Midi1, bytes), stop.Token); }
    internal async Task Expect(params byte[][] expected)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        foreach (var bytes in expected)
        {
            var actual = await messages.Reader.ReadAsync(timeout.Token);
            Checks.Require(actual.SequenceEqual(bytes), $"Wire mismatch: expected {Convert.ToHexString(bytes)}, received {Convert.ToHexString(actual)}.");
        }
        await Task.Delay(150);
        Checks.Require(!messages.Reader.TryRead(out _), "Unexpected additional MIDI message.");
    }
    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        try { await reader; } finally { await port.DisposeAsync(); trace.Dispose(); stop.Dispose(); }
    }
}
