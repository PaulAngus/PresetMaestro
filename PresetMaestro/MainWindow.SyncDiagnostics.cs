using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;
using PresetMaestro.Core;
using PresetNameSync.Core;

namespace PresetMaestro;

public partial class MainWindow
{
    private SyncDiagnosticLog? _syncDiagnosticLog;
    private readonly string? _syncLogDirectory;
    private ToggleSwitch? _syncTimingToggle;
    private SyncDiagnosticLog.Session BeginSyncDiagnostics(string operation) =>
        (_syncDiagnosticLog ??= new(_syncLogDirectory)).Begin(operation,
            _settings.DetailedSyncTiming, AppVersion.Current, _settings.DeviceModel.ToString(), _detectedDevice?.Firmware);

    private void RecordBackgroundReadFailure(string operation, Exception failure, IReadOnlyList<ReadTiming> timings) =>
        (_syncDiagnosticLog ??= new(_syncLogDirectory)).BackgroundFailure(operation, AppVersion.Current,
            _settings.DeviceModel.ToString(), _detectedDevice?.Firmware, failure.GetType().Name, timings);

    private static void PostSyncProgress(SyncDiagnosticLog.Session diagnostics, int? slot, Action update)
    {
        long posted = Stopwatch.GetTimestamp();
        Dispatcher.UIThread.Post(() =>
        {
            diagnostics.Record(new("ui.queue", slot, Stopwatch.GetElapsedTime(posted).TotalMilliseconds, "complete"));
            long started = Stopwatch.GetTimestamp();
            try { update(); }
            finally { diagnostics.Record(new("ui.update", slot, Stopwatch.GetElapsedTime(started).TotalMilliseconds, "complete")); }
        });
    }

    private void OpenSyncLogs()
    {
        try
        {
            string directory = _syncLogDirectory ?? SyncDiagnosticLog.DefaultDirectory;
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { AppendLog("SYNC LOG: could not open the logs folder — " + ex.Message); }
    }
}
