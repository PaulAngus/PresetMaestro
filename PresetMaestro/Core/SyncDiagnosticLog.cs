using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using PresetNameSync.Core;

namespace PresetMaestro.Core;

/// <summary>Bounded, best-effort local JSONL log. File I/O stays off the read/UI paths.</summary>
internal sealed class SyncDiagnosticLog : IAsyncDisposable
{
    internal static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PresetMaestro", "Logs");
    private readonly string? _directory;
    private readonly long _maxBytes;
    private readonly Channel<object> _queue = Channel.CreateBounded<object>(new BoundedChannelOptions(4096)
    { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly Task _writer;
    private long _dropped;
    private int _sessions, _closing;
    internal string? WriteFailure { get; private set; }

    internal SyncDiagnosticLog(string? directory, long maxBytes = 2 * 1024 * 1024)
    {
        _directory = directory;
        _maxBytes = maxBytes;
        _writer = directory is null ? Task.CompletedTask : Task.Run(WriteAsync);
    }

    internal Session Begin(string operation, bool detailed, string version, string model, string? firmware)
    {
        Interlocked.Increment(ref _sessions);
        return new(this, operation, detailed, version, model, firmware);
    }

    internal void BackgroundFailure(string operation, string version, string model, string? firmware,
        string failure, IReadOnlyList<ReadTiming> timings) => Enqueue(new
        {
            timestamp = DateTimeOffset.UtcNow,
            kind = "background-failure",
            operation,
            version,
            model,
            firmware,
            failure,
            timings = timings.ToArray()
        });

    private void Enqueue(object record)
    {
        if (_directory is null) { return; }
        if (!_queue.Writer.TryWrite(record)) { Interlocked.Increment(ref _dropped); }
    }

    private async Task WriteAsync()
    {
        if (_directory is null) { return; }
        FileStream? stream = null;
        try
        {
            Directory.CreateDirectory(_directory);
            string path = Path.Combine(_directory, "sync.jsonl");
            await foreach (object record in _queue.Reader.ReadAllAsync())
            {
                var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record) + "\n");
                if (stream is not null && stream.Length + bytes.Length > _maxBytes)
                { await stream.DisposeAsync(); stream = null; }
                if (stream is null)
                {
                    if (File.Exists(path) && new FileInfo(path).Length + bytes.Length > _maxBytes)
                    {
                        // Current plus two backups: at most approximately 6 MiB.
                        if (File.Exists(path + ".1")) { File.Move(path + ".1", path + ".2", true); }
                        File.Move(path, path + ".1", true);
                    }
                    stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read,
                        16 * 1024, FileOptions.Asynchronous);
                }
                await stream.WriteAsync(bytes);
                // Flush each drained batch, not each preset, and never request a durable disk flush.
                if (!_queue.Reader.TryPeek(out _)) { await stream.FlushAsync(); }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { WriteFailure = ex.GetType().Name; _queue.Writer.TryComplete(); }
        finally
        {
            if (stream is not null)
            {
                try { await stream.DisposeAsync(); }
                catch (IOException) { WriteFailure = nameof(IOException); }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        Volatile.Write(ref _closing, 1);
        if (Volatile.Read(ref _sessions) == 0) { _queue.Writer.TryComplete(); }
        await _writer.ConfigureAwait(false);
    }

    internal sealed class Session : IDisposable
    {
        private readonly SyncDiagnosticLog _log;
        private readonly string _operation;
        private readonly bool _detailed;
        private readonly long _started = Stopwatch.GetTimestamp();
        private readonly IDisposable _capture;
        private readonly object _gate = new();
        private readonly Dictionary<string, Aggregate> _phases = [];
        private readonly Guid _id = Guid.NewGuid();
        private bool _finished;
        private string _status = "interrupted";
        private int? _slots, _populated, _empty, _errors;
        internal Session(SyncDiagnosticLog log, string operation, bool detailed, string version, string model, string? firmware)
        {
            _log = log; _operation = operation; _detailed = detailed;
            log.Enqueue(new
            {
                timestamp = DateTimeOffset.UtcNow,
                kind = "start",
                operationId = _id,
                operation,
                detailed,
                version,
                model,
                firmware
            });
            _capture = ReadDiagnostics.Capture(Record);
        }
        internal void Record(ReadTiming timing)
        {
            lock (_gate)
            {
                if (_finished) { return; }
                if (!_phases.TryGetValue(timing.Phase, out var phase)) { _phases[timing.Phase] = phase = new(); }
                phase.Count++; phase.TotalMilliseconds += timing.Milliseconds;
                phase.MaxMilliseconds = Math.Max(phase.MaxMilliseconds, timing.Milliseconds);
                if (timing.Outcome == "timeout") { phase.Timeouts++; }
                else if (timing.Outcome != "complete") { phase.Failures++; }
                if (_detailed || timing.Outcome != "complete" || timing.Phase is "preset.retry" or "names.retry")
                { _log.Enqueue(new { timestamp = DateTimeOffset.UtcNow, kind = "timing", operationId = _id, timing }); }
            }
        }
        internal void Result(string status, int? slots = null, int? populated = null, int? empty = null, int? errors = null)
        { _status = status; _slots = slots; _populated = populated; _empty = empty; _errors = errors; }
        public void Dispose()
        {
            _capture.Dispose();
            lock (_gate)
            {
                if (_finished) { return; }
                _finished = true;
                _log.Enqueue(new
                {
                    timestamp = DateTimeOffset.UtcNow,
                    kind = "summary",
                    operationId = _id,
                    operation = _operation,
                    status = _status,
                    milliseconds = Stopwatch.GetElapsedTime(_started).TotalMilliseconds,
                    slots = _slots,
                    populated = _populated,
                    empty = _empty,
                    errors = _errors,
                    phases = _phases,
                    droppedRecords = Interlocked.Read(ref _log._dropped),
                    writeFailure = _log.WriteFailure
                });
                if (Interlocked.Decrement(ref _log._sessions) == 0 && Volatile.Read(ref _log._closing) != 0)
                { _log._queue.Writer.TryComplete(); }
            }
        }
        private sealed class Aggregate
        {
            public int Count { get; set; }
            public double TotalMilliseconds { get; set; }
            public double MaxMilliseconds { get; set; }
            public int Timeouts { get; set; }
            public int Failures { get; set; }
        }
    }
}
