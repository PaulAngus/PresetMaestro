using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PresetNameSyncProbe.Midi;
using PresetNameSync.Core;

namespace PresetNameSyncProbe;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<PresetNameResult> _results = [];
    private readonly object _logLock = new();
    private readonly StringBuilder _logBuilder = new();
    private readonly List<string> _pendingLogLines = [];
    private readonly List<PresetNameResult> _pendingResults = [];
    private readonly DispatcherTimer _logFlushTimer;

    private MidiProbeClient? _client;
    private CancellationTokenSource? _scanCts;
    private bool _isBusy;

    public MainWindow()
    {
        InitializeComponent();
        ResultsDataGrid.ItemsSource = _results;

        _logFlushTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) =>
        {
            FlushPendingLog(!_isBusy);
            FlushPendingResults(!_isBusy);
        });
        _logFlushTimer.Start();

        RefreshPorts();
        SetStatus("Disconnected");
        UpdateUiState();
    }

    private void RefreshPortsButton_OnClick(object? sender, RoutedEventArgs e) => RefreshPorts();

    private async void QueryLiveScenesButton_OnClick(object? sender, RoutedEventArgs e) => await QueryScenesAsync(null);
    private async void QueryStoredScenesButton_OnClick(object? sender, RoutedEventArgs e) => await QueryScenesAsync(ReadSelectedSlot());
    private async Task QueryScenesAsync(int? storedSlot)
    {
        if (_client == null || !_client.IsConnected || _isBusy)
        {
            return;
        }

        using var sceneCts = new CancellationTokenSource(); _scanCts = sceneCts;
        await RunBusyOperationAsync(async token =>
        {
            try
            {
                var result = await _client.QueryScenesAsync(storedSlot, token);
                AddLog($"SCENES slot {result.Slot}: {result.PresetName}");
                for (int i = 0; i < result.Names.Length; i++)
                {
                    AddLog($"  Scene {i + 1}: {result.Names[i]}");
                }

                SetStatus($"Read eight scene names for slot {result.Slot}; see log.");
            }
            catch (OperationCanceledException) { SetStatus("Scene read cancelled."); }
            catch (Exception ex) { SetStatus($"Scene read failed: {ex.Message}"); AddLog(ex.ToString()); }
        }, sceneCts.Token);
        if (ReferenceEquals(_scanCts, sceneCts))
        {
            _scanCts = null;
        }

        UpdateUiState();
    }

    private void ConnectButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        string? inputName = InputComboBox.SelectedItem as string;
        string? outputName = OutputComboBox.SelectedItem as string;

        if (string.IsNullOrWhiteSpace(inputName) || string.IsNullOrWhiteSpace(outputName))
        {
            AddLog("Select both MIDI input and output ports before connecting.");
            return;
        }

        DisconnectClient();

        var client = new MidiProbeClient();
        client.LogMessage += ClientOnLogMessage;

        if (!client.Connect(inputName, outputName, out string error))
        {
            AddLog(error);
            client.LogMessage -= ClientOnLogMessage;
            client.Dispose();
            SetStatus("Connect failed");
            UpdateUiState();
            return;
        }

        _client = client;
        SetStatus("Connected");
        UpdateUiState();
    }

    private void DisconnectButton_OnClick(object? sender, RoutedEventArgs e)
    {
        CancelScan();
        DisconnectClient();
        SetStatus("Disconnected");
        UpdateUiState();
    }

    private async void QueryPresetNameButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_client == null || !_client.IsConnected || _isBusy)
        {
            return;
        }

        int slot = ReadSelectedSlot();

        await RunBusyOperationAsync(async token =>
        {
            SetStatus($"Querying slot {slot}...");

            try
            {
                PresetNameResult result = await _client.QueryPresetNameAsync(slot, TimeSpan.FromSeconds(1.5), token);
                AddOrUpdateResult(result);
                SetStatus($"Slot {result.Slot}: {result.PresetName}");
            }
            catch (TimeoutException ex)
            {
                AddLog(ex.Message);
                SetStatus("Query timed out");
            }
            catch (OperationCanceledException)
            {
                SetStatus("Query cancelled");
            }
            catch (Exception ex)
            {
                AddLog($"Query failed: {ex.Message}");
                SetStatus("Query failed");
            }
        });
    }

    private async void ScanAllButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_client == null || !_client.IsConnected || _isBusy)
        {
            return;
        }

        _scanCts?.Dispose();
        var scanCts = new CancellationTokenSource();
        _scanCts = scanCts;

        await RunBusyOperationAsync(async _ =>
        {
            CancellationToken token = scanCts.Token;
            _results.Clear();
            ScanProgressBar.Value = 0;
            SetStatus("Starting scan of slots 0-511...");
            UpdateUiState();

            for (int slot = 0; slot <= 511; slot++)
            {
                if (token.IsCancellationRequested)
                {
                    SetStatus($"Scan cancelled at slot {slot}.");
                    break;
                }

                SetStatus($"Scanning slot {slot}/511...");

                try
                {
                    PresetNameResult result = await _client.QueryPresetNameAsync(slot, TimeSpan.FromSeconds(1.5), token);
                    AddOrUpdateResult(result);
                }
                catch (TimeoutException)
                {
                    AddLog($"Slot {slot}: timeout waiting for response.");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    AddLog($"Slot {slot}: query failed: {ex.Message}");
                }

                ScanProgressBar.Value = slot + 1;
            }

            if (!scanCts.IsCancellationRequested)
            {
                SetStatus($"Scan complete. Retrieved {_results.Count} names.");
            }
        }, scanCts.Token);

        if (ReferenceEquals(_scanCts, scanCts))
        {
            scanCts.Dispose();
            _scanCts = null;
            UpdateUiState();
        }
    }

    private void CancelScanButton_OnClick(object? sender, RoutedEventArgs e)
    {
        CancelScan();
        SetStatus("Cancelling scan...");
    }

    private async void ExportCsvButton_OnClick(object? sender, RoutedEventArgs e)
    {
        FlushPendingResults();

        if (_results.Count == 0)
        {
            AddLog("No scan/query results available to export.");
            return;
        }

        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
        {
            return;
        }

        IStorageFile? file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Preset Names CSV",
            SuggestedFileName = "preset-names.csv",
            DefaultExtension = "csv",
            FileTypeChoices =
            [
                new FilePickerFileType("CSV")
                {
                    Patterns = ["*.csv"]
                }
            ]
        });

        if (file == null)
        {
            return;
        }

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));

        await writer.WriteLineAsync("Slot,PresetName");
        foreach (PresetNameResult item in _results.OrderBy(r => r.Slot))
        {
            await writer.WriteLineAsync($"{item.Slot},\"{EscapeCsv(item.PresetName)}\"");
        }

        await writer.FlushAsync();
        SetStatus("CSV exported successfully.");
    }

    private void RefreshPorts()
    {
        IReadOnlyList<string> inputs = MidiProbeClient.GetInputPortNames();
        IReadOnlyList<string> outputs = MidiProbeClient.GetOutputPortNames();

        InputComboBox.ItemsSource = inputs;
        OutputComboBox.ItemsSource = outputs;

        if (inputs.Count > 0)
        {
            InputComboBox.SelectedIndex = 0;
        }

        if (outputs.Count > 0)
        {
            OutputComboBox.SelectedIndex = 0;
        }

        AddLog($"Discovered {inputs.Count} MIDI inputs and {outputs.Count} MIDI outputs.");
        UpdateUiState();
    }

    private void AddOrUpdateResult(PresetNameResult result)
    {
        _pendingResults.Add(result);
    }

    private void FlushPendingResults() => FlushPendingResults(true);

    private void FlushPendingResults(bool render)
    {
        if (!render || _pendingResults.Count == 0)
        {
            return;
        }

        foreach (PresetNameResult result in _pendingResults)
        {
            int existingIndex = -1;
            for (int i = 0; i < _results.Count; i++)
            {
                if (_results[i].Slot == result.Slot)
                {
                    existingIndex = i;
                    break;
                }
            }

            if (existingIndex >= 0)
            {
                _results[existingIndex] = result;
            }
            else
            {
                _results.Add(result);
            }
        }

        _pendingResults.Clear();
        UpdateUiState();
    }

    private void AddLog(string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        lock (_logLock)
        {
            _pendingLogLines.Add(line);
        }
    }

    private void FlushPendingLog() => FlushPendingLog(true);

    private void FlushPendingLog(bool render)
    {
        List<string> lines;
        lock (_logLock)
        {
            if (_pendingLogLines.Count == 0)
            {
                return;
            }

            lines = [.. _pendingLogLines];
            _pendingLogLines.Clear();
        }

        foreach (string line in lines)
        {
            if (_logBuilder.Length > 0)
            {
                _logBuilder.AppendLine();
            }

            _logBuilder.Append(line);
        }

        if (!render)
        {
            return;
        }

        LogTextBox.Text = _logBuilder.ToString();
        LogTextBox.CaretIndex = _logBuilder.Length;
    }

    private void SetStatus(string message)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            StatusTextBlock.Text = message;
        }
        else
        {
            Dispatcher.UIThread.Post(() => StatusTextBlock.Text = message);
        }
    }

    private void ClientOnLogMessage(object? sender, string message) => AddLog(message);

    private int ReadSelectedSlot()
    {
        decimal raw = SlotNumeric.Value ?? 0;
        return (int)Math.Clamp(raw, 0, 511);
    }

    private async Task RunBusyOperationAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken = default)
    {
        _isBusy = true;
        UpdateUiState();

        try
        {
            await work(cancellationToken);
        }
        finally
        {
            _isBusy = false;
            FlushPendingLog();
            FlushPendingResults();
            UpdateUiState();
        }
    }

    private void UpdateUiState()
    {
        bool connected = _client?.IsConnected == true;
        bool scanning = _scanCts is { IsCancellationRequested: false } && _isBusy;

        ConnectButton.IsEnabled = !_isBusy;
        DisconnectButton.IsEnabled = connected && !_isBusy;
        RefreshPortsButton.IsEnabled = !_isBusy;

        QueryPresetNameButton.IsEnabled = connected && !_isBusy;
        QueryLiveScenesButton.IsEnabled = connected && !_isBusy;
        QueryStoredScenesButton.IsEnabled = connected && !_isBusy;
        ScanAllButton.IsEnabled = connected && !_isBusy;
        CancelScanButton.IsEnabled = scanning;

        ExportCsvButton.IsEnabled = _results.Count > 0;
    }

    private void CancelScan()
    {
        if (_scanCts is { IsCancellationRequested: false })
        {
            _scanCts.Cancel();
        }
    }

    private void DisconnectClient()
    {
        if (_client == null)
        {
            return;
        }

        _client.LogMessage -= ClientOnLogMessage;
        _client.Dispose();
        _client = null;
    }

    private static string EscapeCsv(string value) => value.Replace("\"", "\"\"");

    protected override void OnClosed(EventArgs e)
    {
        _logFlushTimer.Stop();
        FlushPendingLog();
        FlushPendingResults();

        CancelScan();
        _scanCts?.Dispose();
        _scanCts = null;

        DisconnectClient();

        base.OnClosed(e);
    }
}
