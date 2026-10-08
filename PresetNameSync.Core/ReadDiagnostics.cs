using System.Diagnostics;

namespace PresetNameSync.Core;

/// <summary>Timings only: never includes preset names, content or raw MIDI frames.</summary>
public sealed record ReadTiming(string Phase, int? Slot, double Milliseconds, string Outcome)
{
    public MidiReadEvidence? Receive { get; init; }
}

public sealed record MidiReadEvidence(int Opcode, int FramesReceived, int MatchingOpcodeFrames,
    int RejectedMatchingFrames, int InvalidMatchingFrames, int DiscardedBeforeSend, long QueueDrops)
{
    public IReadOnlyDictionary<string, int>? OpcodeCounts { get; init; }
}

/// <summary>Async operation scope; logging never blocks or changes a device read.</summary>
public static class ReadDiagnostics
{
    private static readonly AsyncLocal<Action<ReadTiming>?> Sink = new();

    public static IDisposable Capture(Action<ReadTiming> sink)
    {
        var previous = Sink.Value;
        Sink.Value = sink;
        return new Scope(() => Sink.Value = previous);
    }

    public static void Record(string phase, int? slot, double milliseconds = 0, string outcome = "complete") =>
        Emit(Sink.Value, new(phase, slot, milliseconds, outcome));
    public static void Record(ReadTiming timing) => Emit(Sink.Value, timing);

    public static async Task<T> MeasureAsync<T>(string phase, int? slot, Func<Task<T>> action)
    {
        var sink = Sink.Value;
        if (sink is null) { return await action().ConfigureAwait(false); }
        long started = Stopwatch.GetTimestamp();
        string outcome = "complete";
        try { return await action().ConfigureAwait(false); }
        catch (Exception ex) { outcome = Failure(ex); throw; }
        finally { Emit(sink, new(phase, slot, Stopwatch.GetElapsedTime(started).TotalMilliseconds, outcome)); }
    }

    public static T Measure<T>(string phase, int? slot, Func<T> action)
    {
        var sink = Sink.Value;
        if (sink is null) { return action(); }
        long started = Stopwatch.GetTimestamp();
        string outcome = "complete";
        try { return action(); }
        catch (Exception ex) { outcome = Failure(ex); throw; }
        finally { Emit(sink, new(phase, slot, Stopwatch.GetElapsedTime(started).TotalMilliseconds, outcome)); }
    }

    private static string Failure(Exception ex) => ex is OperationCanceledException ? "cancelled" :
        ex is TimeoutException ? "timeout" : ex.GetType().Name;
    private static void Emit(Action<ReadTiming>? sink, ReadTiming timing)
    {
        try { sink?.Invoke(timing); }
        catch { /* Diagnostics must never affect the operation being diagnosed. */ }
    }
    private sealed class Scope(Action restore) : IDisposable
    {
        private Action? _restore = restore;
        public void Dispose() => Interlocked.Exchange(ref _restore, null)?.Invoke();
    }
}
