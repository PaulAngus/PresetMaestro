using Avalonia.Controls;
using Avalonia.Threading;
using PresetMaestro.Midi;

namespace PresetMaestro;

public partial class MainWindow
{
    private Core.DeviceModel PickerDeviceModel => _detectedDevice?.Model ?? _settings.DeviceModel;
    private int EffectiveMaximum => Core.DevicePresets.Capacity(PickerDeviceModel) - 1 + _settings.DisplayOffset;
    private bool CanReadDeviceNames => _detectedDevice?.Model is null or Core.DeviceModel.FM9;
    private const string UnsupportedNameReads = "Preset and scene name reads are currently verified for FM9 only. Preset sending remains available.";

    internal const string ConnectionError = "Could not connect to a supported Fractal device. Check the selected MIDI IN and MIDI OUT ports, then try again.";
    internal DeviceConnectionSession ConnectionState { get; } = new();
    private CancellationTokenSource? _connectionCts => ConnectionState.Pending;
    private long _connectionGeneration => ConnectionState.Generation;
    private FractalDeviceInformation? _detectedDevice => ConnectionState.Device;
    private FractalDeviceInformationClient? _deviceInformationClient;
    private DispatcherTimer? _connectionMonitor;
    private bool _deviceCheckFailed;
    private bool _checkingDevices;
    private Border? _connectionDot;
    internal Func<string, Task>? ConnectionErrorOverride { get; set; }
    internal TimeSpan DeviceInformationTimeout { get; set; } = TimeSpan.FromMilliseconds(700);
    // Some USB MIDI devices enumerate before their receive callback is ready. Reopening once
    // shortly after connection mirrors the manual checkbox recovery without changing its state.
    internal TimeSpan ThruInputRetryDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    internal async Task ConnectAsync()
    {
        if (_connectionCts is not null)
        {
            return;
        }

        Disconnect();
        using var cancellation = ConnectionState.Begin();
        long generation = _connectionGeneration;
        UpdateConnectButtons();
        UpdatePresetSyncButtons();
        try
        {
            string input = _inputPortCombo.SelectedItem as string ?? "";
            string output = _outputPortCombo.SelectedItem as string ?? "";
            if (input.Length == 0 || output.Length == 0)
            {
                throw new IOException("Select both MIDI IN and MIDI OUT ports.");
            }

            if (!_midi.OpenInput(input, out var inputError))
            {
                throw new IOException($"MIDI IN: {inputError}");
            }

            if (!_midi.OpenOutput(output, out var outputError))
            {
                throw new IOException($"MIDI OUT: {outputError}");
            }

            UpdateConnectButtons();
            _deviceInformationClient ??= new FractalDeviceInformationClient(_midi, AppendLog);
            var device = await _deviceInformationClient.QueryDeviceInformationAsync(input, output, DeviceInformationTimeout, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (generation != _connectionGeneration)
            {
                return;
            }

            ConnectionState.Device = device;
            _presetNameClient.SetDeviceModel(device.Model);
            _settings.DeviceModel = device.Model;
            _settings.DeviceName = device.DeviceName;
            UpdatePresetCapacityUI();
            UpdateFavoritePresetDisplay();
            SetStatus(device.Label, StatusKind.ConnectedBoth);
            SaveSettingsFromUI();
            RefreshProfileList();
            if (!CanReadDeviceNames)
            {
                AppendLog($"CONNECT: {UnsupportedNameReads}");
            }
            AppendLog($"CONNECT: validated {device.ModelLabel} on selected MIDI IN '{input}'.");
            // A fresh timer avoids retaining a previous connection's selected port names.
            _connectionMonitor?.Stop();
            _deviceCheckFailed = false;
            _connectionMonitor = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _connectionMonitor.Tick += async (_, _) =>
                await CheckConnectedDevicesAsync(input, output);
            _connectionMonitor.Start();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (generation != _connectionGeneration)
            {
                return;
            }

            AppendLog($"CONNECT FAILED: {ex.Message}");
            Disconnect();
            if (ConnectionErrorOverride is { } show)
            {
                await show(ConnectionError);
            }
            else
            {
                await ShowMessageAsync("Connection error", ConnectionError);
            }
        }
        finally
        {
            ConnectionState.Finish(cancellation);

            UpdateConnectButtons();
            UpdatePresetSyncButtons();
#if FRACTAL_INDEX
            RefreshIndexContext();
#endif
        }
        if (generation == _connectionGeneration && _detectedDevice is not null)
        {
            OpenCheckedThruInputs(reopen: false);
#if FRACTAL_INDEX
            PrepareConnectionLibrarySetup();
            // An empty local library can record the detected firmware without
            // reading presets or starting a comparison before the user's choice.
            if (_connectionDefaultLibrary is null && _indexCache is { Browsable: null, Imported: false } emptyLibrary &&
                emptyLibrary.Device.Variant.ToDeviceModel() == _detectedDevice.Model)
            { SaveDetectedLibraryFirmware(emptyLibrary); }
#endif
            OpenConnectionSyncOptions(initialConnection: true);
#if FRACTAL_INDEX
            if (!_connectionLibrarySetupRequired) { StartSceneTracking(); }
#else
            StartSceneTracking();
#endif

            await Task.Delay(ThruInputRetryDelay);
            if (generation == _connectionGeneration && _detectedDevice is not null && _midi.InputOpen && _midi.OutputOpen)
            {
                OpenCheckedThruInputs(reopen: true);
            }
        }
    }

    internal async Task CheckConnectedDevicesAsync(string input, string output)
    {
        if (_checkingDevices) { return; }
        _checkingDevices = true;
        long generation = _connectionGeneration;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            var ports = await _portDiscovery.RefreshAsync(deadline.Token);
            if (_sceneClosing || generation != _connectionGeneration) { return; }
            if (!_midi.InputOpen || !_midi.OutputOpen || !ports.Inputs.Contains(input) || !ports.Outputs.Contains(output))
            {
                AppendLog("CONNECT: selected MIDI device removed; disconnected.");
                Disconnect();
                return;
            }

            if (_deviceCheckFailed)
            {
                AppendLog("CONNECT: MIDI device check recovered.");
                _deviceCheckFailed = false;
            }
        }
        catch (Exception ex)
        {
            if (_sceneClosing || generation != _connectionGeneration) { return; }
            // Enumeration failure is not evidence that the open transport failed.
            // Retry on the next tick; actual send failures still disconnect immediately.
            if (!_deviceCheckFailed)
            {
                AppendLog($"CONNECT: MIDI device check unavailable; keeping connection and retrying: {ex.Message}");
                _deviceCheckFailed = true;
            }
        }
        finally { _checkingDevices = false; }
    }

    private void OpenCheckedThruInputs(bool reopen)
    {
        foreach (string port in GetCheckedThruPorts())
        {
            try
            {
                if (reopen)
                {
                    _midi.CloseThruInput(port);
                }

                bool opened = _midi.OpenThruInput(port, out var error);
                if (opened)
                {
                    AppendLog(reopen ? $"THRU: reopened '{port}' after connection" : $"THRU: opened '{port}' on connection");
                }
                else
                {
                    AppendLog($"THRU: failed to {(reopen ? "reopen" : "open")} '{port}' — {error}");
                }
            }
            catch (Exception ex)
            {
                // A secondary controller must not terminate an otherwise valid
                // Fractal connection when its driver throws from a WinMM call.
                AppendLog($"THRU: failed to {(reopen ? "reopen" : "open")} '{port}' — {ex.Message}");
            }
        }
    }

    private void UpdatePresetCapacityUI()
    {
        _settings.MaxDisplayedPreset = EffectiveMaximum;
        _favPresetSpinner.Maximum = EffectiveMaximum;
        // The preset capacity is kept in settings for device limits, but the UI no longer shows it.
    }
}
