#if MAESTRO_WINDOWS
using System.Threading.Channels;
using PresetMaestro.Midi;

namespace PresetSender.Tests;

public sealed class VirtualMidiIntegrationTests
{
    [NativeMidiFact]
    public async Task PublishedDeviceDeliversEveryChannelToTwoLegacyReceiversAndDisappearsOnClose()
    {
        var backend = new NativeMidiBackend();
        var before = backend.GetInputDevices().Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        string? createdId = null;
        string? relayId = null;
        using (var output = await Task.Run(backend.OpenVirtualOutput))
        {
            await Until(() =>
            {
                var ports = backend.GetInputDevices();
                createdId = ports.FirstOrDefault(p => !before.Contains(p.Id) && p.Name == "Preset Sender").Id;
                relayId = ports.FirstOrDefault(p => !before.Contains(p.Id) && p.Name == "Preset Sender (Relay)").Id;
                return createdId is not null && relayId is not null;
            });
            using var input = backend.OpenInput(createdId!);
            using var secondInput = backend.OpenInput(createdId!);
            var received = Channel.CreateUnbounded<int>();
            var secondReceived = Channel.CreateUnbounded<int>();
            input.MessageReceived += (_, e) => received.Writer.TryWrite(e.RawMessage);
            secondInput.MessageReceived += (_, e) => secondReceived.Writer.TryWrite(e.RawMessage);
            input.Start();
            secondInput.Start();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            output.Send(MidiMessageEncoding.ProgramChange(42, 6));
            Assert.Equal(0xc5 | (42 << 8), await received.Reader.ReadAsync(timeout.Token));
            Assert.Equal(0xc5 | (42 << 8), await secondReceived.Reader.ReadAsync(timeout.Token));
            for (int channel = 1; channel <= 16; channel++)
            {
                output.Send(MidiMessageEncoding.ProgramChange(127, channel));
            }
            for (int channel = 1; channel <= 16; channel++)
            {
                int expected = 0xc0 | (channel - 1) | (127 << 8);
                Assert.Equal(expected, await received.Reader.ReadAsync(timeout.Token));
                Assert.Equal(expected, await secondReceived.Reader.ReadAsync(timeout.Token));
            }
            input.Stop();
            secondInput.Stop();
        }
        await Until(() => backend.GetInputDevices().All(p => p.Id != createdId && p.Id != relayId));
    }

    private static async Task Until(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!predicate()) { await Task.Delay(100, timeout.Token); }
    }

    private sealed class NativeMidiFactAttribute : FactAttribute
    {
        public NativeMidiFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("PRESET_SENDER_NATIVE_MIDI_TEST") != "1")
            {
                Skip = "Opt in with PRESET_SENDER_NATIVE_MIDI_TEST=1 on Windows 11 25H2+ with Windows MIDI Services.";
            }
        }
    }
}
#endif
