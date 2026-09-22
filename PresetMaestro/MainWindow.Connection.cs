using Avalonia.Controls;
using Avalonia.Threading;
using PresetMaestro.Midi;

namespace PresetMaestro;

public partial class MainWindow
{
    private Core.DeviceModel PickerDeviceModel => _detectedDevice?.Model ?? _settings.DeviceModel;

    internal const string ConnectionError = "Could not connect to a supported Fractal device. Check the selected MIDI IN and MIDI OUT ports, then try again.";
    private CancellationTokenSource? _connectionCts;
    private long _connectionGeneration;
    private FractalDeviceInformation? _detectedDevice;
    private FractalDeviceInformationClient? _deviceInformationClient;
    private DispatcherTimer? _connectionMonitor;
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
        long generation = ++_connectionGeneration;
        using var cancellation = new CancellationTokenSource();
        _connectionCts = cancellation;
        UpdateConnectButtons();
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

            _detectedDevice = device;
            _settings.DeviceModel = device.Model;
            _favPresetSpinner.Maximum = Core.DevicePresets.Capacity(PickerDeviceModel) - 1 + _settings.DisplayOffset;
            UpdateFavoritePresetDisplay();
            SetStatus(device.Label, StatusKind.ConnectedBoth);
            AppendLog($"CONNECT: validated {device.ModelLabel} on selected MIDI IN '{input}'.");
            // A fresh timer avoids retaining a previous connection's selected port names.
            _connectionMonitor?.Stop();
            _connectionMonitor = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _connectionMonitor.Tick += (_, _) =>
            {
                try
                {
                    if (!_midi.InputOpen || !_midi.OutputOpen || !_midi.GetInputPortNames().Contains(input) || !_midi.GetOutputPortNames().Contains(output))
                    {
                        AppendLog("CONNECT: selected MIDI device removed; disconnected.");
                        Disconnect();
                    }
                }
                catch (Exception ex) { AppendLog($"CONNECT: MIDI transport failed: {ex.Message}"); Disconnect(); }
            };
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
            if (ReferenceEquals(_connectionCts, cancellation))
            {
                _connectionCts = null;
            }

            UpdateConnectButtons();
            UpdatePresetSyncButtons();
        }
        if (generation == _connectionGeneration && _detectedDevice is not null)
        {
            OpenCheckedThruInputs(reopen: false);
            StartSceneTracking();

            await Task.Delay(ThruInputRetryDelay);
            if (generation == _connectionGeneration && _detectedDevice is not null && _midi.InputOpen && _midi.OutputOpen)
            {
                OpenCheckedThruInputs(reopen: true);
            }
        }
    }

    private void OpenCheckedThruInputs(bool reopen)
    {
        foreach (string port in GetCheckedThruPorts())
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
    }
}
