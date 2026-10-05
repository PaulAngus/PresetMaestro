using PresetMaestro.Core;
using PresetMaestro.FractalIndex;
using PresetMaestro.Midi;
using PresetNameSync.Core;

namespace PresetMaestro.Tests;

public sealed class CoordinatorTests
{
    [Fact]
    public async Task DiscoveryCoalescesCallersAndCancellationDoesNotStartOverlappingDriverWork()
    {
        var first = new TaskCompletionSource<MidiPorts>(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        var midi = new DeviceMidi { Discover = () => { calls++; return first.Task; } };
        var discovery = new MidiPortDiscovery(midi);
        using var cancel = new CancellationTokenSource();
        var cancelled = discovery.RefreshAsync(cancel.Token);
        var waiting = discovery.RefreshAsync();
        Assert.Equal(1, calls); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        var third = discovery.RefreshAsync(); Assert.Equal(1, calls);
        first.SetResult(new(["FM9"], ["FM9"]));
        Assert.Equal(await waiting, await third);
        Assert.Equal(["FM9"], discovery.Current.Inputs);
        midi.Discover = () => Task.FromException<MidiPorts>(new IOException("Driver unavailable"));
        await Assert.ThrowsAsync<IOException>(() => discovery.RefreshAsync());
        Assert.Equal(["FM9"], discovery.Current.Inputs);
        midi.Discover = () => Task.FromResult(new MidiPorts([], []));
        Assert.Empty((await discovery.RefreshAsync()).Inputs);
    }

    [Fact]
    public void DisconnectInvalidatesPendingConnectionAndOldCompletionCannotClearNewOperation()
    {
        var session = new DeviceConnectionSession();
        using var first = session.Begin(); long original = session.Generation;
        session.Device = new(DeviceModel.FM9, "Test"); session.Disconnect();
        Assert.True(first.IsCancellationRequested); Assert.Null(session.Device); Assert.True(session.Generation > original);
        session.Finish(first);
        using var second = session.Begin(); session.Finish(first);
        Assert.Same(second, session.Pending);
        session.Finish(second); Assert.Null(session.Pending);
    }

    [Fact]
    public async Task NameReadSessionKeepsCachedValuesAndStopsAfterThreeConsecutiveTimeouts()
    {
        var cache = new Dictionary<int, string> { [0] = "Old", [3] = "Kept" };
        var read = new PresetNameReadSession(cache);
        int calls = 0;
        await Assert.ThrowsAsync<IOException>(() => read.ReadAsync((slot, _, _) =>
        {
            calls++;
            return slot == 0 ? Task.FromResult(new PresetNameResult(slot, "New")) : Task.FromException<PresetNameResult>(new TimeoutException());
        }, _ => { }, default));
        Assert.Equal(4, calls); Assert.Equal("New", read.Names[0]); Assert.Equal("Kept", read.Names[3]);
        Assert.Equal("Old", cache[0]); Assert.Equal([0], read.RefreshedSlots); Assert.Equal(3, read.CheckedSlots);
    }

    [Fact]
    public void ProfileLibraryCoordinatorRollsBackFailedEditsAndDoesNotMaterializeHistoricalReferences()
    {
        string path = Path.Combine(Path.GetTempPath(), "PresetMaestro-coordinator-" + Guid.NewGuid().ToString("N"));
        try
        {
            var library = new IndexLibrary(path);
            var device = new IndexDevice(Guid.NewGuid(), "Missing", FractalDeviceVariant.FM9);
            var profile = new IndexProfile { Devices = [device], SelectedDeviceId = device.Id };
            var settings = new AppSettings { FractalIndex = IndexJson.ToElement(profile) };
            string original = settings.FractalIndex!.Value.GetRawText();
            var coordinator = new ProfileLibraryCoordinator(settings, library, _ => throw new IOException("Read only"));
            Assert.Throws<IOException>(() => coordinator.Update(profile, p => p.PresetMatchThresholdPercent = 50));
            Assert.Equal(original, settings.FractalIndex!.Value.GetRawText()); Assert.Equal(99, profile.PresetMatchThresholdPercent);
            var loaded = coordinator.Load(true);
            Assert.True(loaded.Cache!.Imported); Assert.Empty(library.ListDevices());
            Assert.Null(library.Load(device.Id));
        }
        finally { if (Directory.Exists(path)) { Directory.Delete(path, true); } }
    }
}
