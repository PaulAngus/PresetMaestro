using PresetMaestro.FractalIndex;
using PresetNameSync.Core;

namespace PresetMaestro.Tests;

public sealed class PresetReadRecoveryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PresetMaestro-recovery-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_directory)) { Directory.Delete(_directory, true); } }
    private static readonly FractalDeviceDefinition Device = FractalDeviceDefinition.For(FractalDeviceVariant.FM9);
    private static PresetIndexReader Reader(RecoverySource source) => new(source, AmpModelCatalogRegistry.CreateStarter());

    [Fact]
    public async Task TimeoutRetriesTheSameDumpWithoutRepeatingItsNameQuery()
    {
        var source = new RecoverySource { Timeouts = 1 };
        int recovering = 0;
        var preset = await Reader(source).ReadAsync(Device, new Version(12, 0), 58, default, () => recovering++);
        Assert.Equal(58, preset.Slot);
        Assert.Equal("Fixture 58", preset.Name);
        Assert.Equal([58], source.Inner.NameReads);
        Assert.Equal([58, 58], source.Attempts);
        Assert.Equal(1, recovering);
    }

    [Fact]
    public async Task PersistentTimeoutStopsAfterOneRetry()
    {
        var source = new RecoverySource { Timeouts = 10 };
        await Assert.ThrowsAsync<TimeoutException>(() => Reader(source).ReadAsync(Device, null, 58, default));
        Assert.Equal([58, 58], source.Attempts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidDataAndClosedTransportAreNotRetried(bool invalid)
    {
        var source = new RecoverySource { Failure = invalid ? new InvalidDataException("Invalid body") : new IOException("Port closed") };
        var error = await Record.ExceptionAsync(() => Reader(source).ReadAsync(Device, null, 58, default));
        Assert.Same(source.Failure, error);
        Assert.Equal([58], source.Attempts);
    }

    [Fact]
    public async Task CancellationDuringRecoveryPreventsAnotherRequest()
    {
        using var cancellation = new CancellationTokenSource();
        var source = new RecoverySource { Timeouts = 1 };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Reader(source)
            .ReadAsync(Device, null, 58, cancellation.Token, cancellation.Cancel));
        Assert.Equal([58], source.Attempts);
    }

    [Fact]
    public async Task RecoveredTimeoutCompletesTheScanAndReportsRecovery()
    {
        var source = new RecoverySource { Timeouts = 1 };
        var cache = new DeviceIndex { Device = new(Guid.NewGuid(), "Stage", FractalDeviceVariant.FM9, "12.00") };
        var progress = new List<IndexScanProgress>();
        var library = new IndexLibrary(_directory);
        await new IndexScanner(Reader(source), library).ScanAsync(cache, false, progress.Add, default);
        Assert.Equal("Complete", library.Load(cache.Device.Id)!.Committed!.Status);
        Assert.Equal(512, cache.Committed!.Presets.Count);
        Assert.Empty(cache.LastAttempt!.Errors);
        Assert.Equal(58, Assert.Single(progress, p => p.Retrying).Slot);
        Assert.Equal(2, source.Attempts.Count(s => s == 58));
    }

    [Fact]
    public async Task PersistentFailureKeepsTheBaselineAndCanBeResumed()
    {
        var source = new RecoverySource { Timeouts = 2 };
        var baseline = new IndexScan
        {
            Status = "Complete",
            FinishedAt = DateTimeOffset.UtcNow,
            Presets = Enumerable.Range(0, 512).ToDictionary(s => s, s => FractalIndexWorkflowTests.Preset(s))
        };
        var cache = new DeviceIndex { Device = new(Guid.NewGuid(), "Stage", FractalDeviceVariant.FM9, "12.00"), Committed = baseline };
        var library = new IndexLibrary(_directory);
        var scanner = new IndexScanner(Reader(source), library);
        await scanner.ScanAsync(cache, false, null, default);
        Assert.Equal("Partial", cache.LastAttempt!.Status);
        Assert.Equal(511, cache.LastAttempt.Presets.Count);
        Assert.Equal(58, Assert.Single(cache.LastAttempt.Errors).Key);
        Assert.Equal(baseline.Id, library.Load(cache.Device.Id)!.Committed!.Id);
        source.Attempts.Clear(); source.Inner.NameReads.Clear();
        await scanner.ScanAsync(cache, true, null, default);
        Assert.Equal("Complete", cache.Committed!.Status);
        Assert.Empty(cache.LastAttempt.Errors);
        Assert.Equal([58], source.Attempts);
        Assert.DoesNotContain(0, source.Inner.NameReads);
    }

    private sealed class RecoverySource : IStoredPresetImageSource, IStoredPresetNameSource
    {
        public FractalIndexWorkflowTests.NameSource Inner { get; } = new(s => s is 0 or 58 ? "Populated" : "<EMPTY>");
        public List<int> Attempts { get; } = [];
        public int Timeouts { get; set; }
        public Exception? Failure { get; init; }
        public Task<string?> ReadStoredPresetNameAsync(int slot, FractalDeviceDefinition device, CancellationToken token) => Inner.ReadStoredPresetNameAsync(slot, device, token);
        public Task<StoredPresetImage> ReadStoredImageAsync(int slot, FractalDeviceDefinition device, CancellationToken token)
        {
            Attempts.Add(slot);
            if (Failure is not null) { throw Failure; }
            if (slot == 58 && Timeouts-- > 0) { throw new TimeoutException("device query 0x03 timed out."); }
            return Inner.ReadStoredImageAsync(slot, device, token);
        }
    }
}
