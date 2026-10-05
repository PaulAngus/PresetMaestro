using System.Diagnostics;
using System.Text.Json;
using PresetMaestro.Core;
using PresetMaestro.FractalIndex;
using PresetMaestro.Midi;
using PresetNameSync.Core;
using Xunit.Abstractions;

namespace PresetMaestro.Tests;

public sealed class HardwareFactAttribute : FactAttribute
{
    public HardwareFactAttribute(bool fullScan = false)
    {
        if (Environment.GetEnvironmentVariable("PRESET_MAESTRO_HARDWARE") != "FM9")
        { Skip = "Opt-in physical FM9 test; run scripts/Test-Hardware.ps1."; }
        else if (fullScan && Environment.GetEnvironmentVariable("PRESET_MAESTRO_FULL_SCAN") != "1")
        { Skip = "Full read-only scan requires -FullScan."; }
    }
}

[CollectionDefinition("Physical MIDI", DisableParallelization = true)]
public sealed class PhysicalMidiCollection;

[Collection("Physical MIDI"), Trait("Category", "Hardware")]
public sealed class Fm9HardwareTests(ITestOutputHelper output)
{
    [HardwareFact]
    public async Task ReadOnlyQueriesRepeatAndRecoverWithoutChangingCurrentPresetOrScene()
    {
        using var session = new HardwareSession(output);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var token = deadline.Token;
        var device = await session.IdentifyAsync(token);
        using var client = new PresetNameClient(session);
        var before = await client.CurrentStateAsync(token);
        try
        {
            var scenes = await client.SceneNamesAsync(before.Preset.Slot, token);
            Assert.Equal(8, scenes.Names.Length);
            var reader = new PresetIndexReader(client, AmpModelCatalogRegistry.CreateStarter());
            foreach (int slot in new[] { 0, 127, 128, 255, 256, 511, before.Preset.Slot }.Distinct())
            {
                var first = await reader.ReadAsync(FractalDeviceDefinition.For(FractalDeviceVariant.FM9), Version.Parse(device.Firmware!), slot, token);
                var second = await reader.ReadAsync(FractalDeviceDefinition.For(FractalDeviceVariant.FM9), Version.Parse(device.Firmware!), slot, token);
                Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
                Assert.Equal(8, first.SceneNames.Length);
                Assert.All(first.Amps, amp => { Assert.Equal(4, amp.Channels.Length); Assert.Equal(8, amp.Scenes.Length); });
                session.Record("preset", new { slot, first.NameOnlyEmpty, first.ContentSha256, first.BypassIgnoredSha256 });
            }
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.QueryAsync(0, TimeSpan.FromSeconds(2), cancelled.Token));
            // Suppress received responses, not outgoing requests, to exercise the real
            // connection's timeout/quarantine path without sending invalid device commands.
            session.DropReplies = true;
            try
            {
                using var inFlight = CancellationTokenSource.CreateLinkedTokenSource(token);
                inFlight.CancelAfter(30);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.QueryAsync(0, TimeSpan.FromSeconds(2), inFlight.Token));
            }
            finally { session.DropReplies = false; }
            Assert.Equal(0, (await client.QueryAsync(0, TimeSpan.FromSeconds(2), token)).Slot);
            session.DropReplies = true;
            try { await Assert.ThrowsAsync<TimeoutException>(() => client.QueryAsync(0, TimeSpan.FromMilliseconds(100), token)); }
            finally { session.DropReplies = false; }
            Assert.Equal(0, (await client.QueryAsync(0, TimeSpan.FromSeconds(2), token)).Slot);
            session.CloseInput(); session.CloseOutput();
            session.OpenSelectedPorts();
            Assert.Equal(device.Model, (await session.IdentifyAsync(token)).Model);
            Assert.Equal(before, await client.CurrentStateAsync(token));
        }
        finally
        {
            var after = await client.CurrentStateAsync(token);
            session.Record("state", new { before = new { before.Preset, before.Scene }, after = new { after.Preset, after.Scene } });
            Assert.Equal(before, after);
        }
    }

    [HardwareFact(fullScan: true)]
    public async Task FullReadOnlyScanProducesReplayableLibraryAndMeasurements()
    {
        using var session = new HardwareSession(output);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        var token = deadline.Token;
        var device = await session.IdentifyAsync(token);
        using var client = new PresetNameClient(session);
        var before = await client.CurrentStateAsync(token);
        try
        {
            var cache = new DeviceIndex { Device = new(Guid.NewGuid(), "Hardware test", FractalDeviceVariant.FM9, device.Firmware) };
            var library = new IndexLibrary(Path.Combine(session.DirectoryPath, "library"));
            var reader = new PresetIndexReader(client, AmpModelCatalogRegistry.CreateStarter());
            var timer = Stopwatch.StartNew();
            await new IndexScanner(reader, library).ScanAsync(cache, false, progress =>
            {
                if (progress.Read % 64 == 0) { session.Record("progress", progress); }
            }, token);
            var saved = library.Load(cache.Device.Id)!;
            Assert.Equal("Complete", saved.Committed!.Status);
            Assert.Equal(512, saved.Committed.Presets.Count);
            Assert.Empty(saved.Committed.Errors);
            session.Record("scan", new { milliseconds = timer.ElapsedMilliseconds, populated = saved.Committed.Presets.Values.Count(p => !p.NameOnlyEmpty), bytes = Directory.GetFiles(library.DirectoryPath).Sum(p => new FileInfo(p).Length) });
        }
        finally
        {
            var after = await client.CurrentStateAsync(token);
            session.Record("state", new { before = new { before.Preset, before.Scene }, after = new { after.Preset, after.Scene } });
            Assert.Equal(before, after);
        }
    }
}

