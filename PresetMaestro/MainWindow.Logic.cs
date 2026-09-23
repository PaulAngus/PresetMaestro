using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using PresetMaestro.Core;
using PresetMaestro.Midi;

namespace PresetMaestro;

public partial class MainWindow
{
    // ── Digit / command entry ──────────────────────────────────────
    private void HandleDigit(int digit)
    {
        if (_enteredDigits.Length >= 3)
        {
            _autoSendTimer.Stop();
            _enteredDigits = string.Empty;
        }

        _enteredDigits += digit.ToString();
        UpdateDisplay();

        if (_settings.AutoSend && _enteredDigits.Length == 3)
        {
            _autoSendTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(10, _settings.AutoSendDelayMs));
            _autoSendTimer.Start();
        }
    }

    private void HandleSend()
    {
        _autoSendTimer.Stop();
        ExecuteSend();
    }

    private void SetMode(EntryMode mode)
    {
        if (_mode == mode)
        {
            return;
        }

        _mode = mode;
        _autoSendTimer.Stop();
        _enteredDigits = string.Empty;
        UpdateDisplay();
    }

    private void HandleClear()
    {
        _autoSendTimer.Stop();
        _enteredDigits = string.Empty;
        AppendLog("CLEAR");
        UpdateDisplay();
    }

    private void HandleBackspace()
    {
        if (_enteredDigits.Length == 0)
        {
            return;
        }

        _autoSendTimer.Stop();
        _enteredDigits = _enteredDigits[..^1];
        UpdateDisplay();
    }

    private void OpenSettingsFile()
    {
        SaveSettingsFromUI();
        try
        {
            Process.Start(new ProcessStartInfo(SettingsManager.SettingsPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: could not open settings file — {ex.Message}");
        }
    }

    private void HandleNext()
    {
        _autoSendTimer.Stop();
        _enteredDigits = string.Empty;
        if (_mode == EntryMode.Favorite) { AppendLog("NEXT: not available in Favorites mode"); return; }
        int min = PresetTranslation.MinDisplayed(_settings.DisplayOffset);
        int next = _currentPreset.HasValue ? _currentPreset.Value + 1 : min;
        if (next > EffectiveMaximum)
        {
            next = min;
        }

        SendPreset(next);
    }

    private void HandlePrev()
    {
        _autoSendTimer.Stop();
        _enteredDigits = string.Empty;
        if (_mode == EntryMode.Favorite) { AppendLog("PREV: not available in Favorites mode"); return; }
        int min = PresetTranslation.MinDisplayed(_settings.DisplayOffset);
        int prev = _currentPreset.HasValue ? _currentPreset.Value - 1 : EffectiveMaximum;
        if (prev < min)
        {
            prev = EffectiveMaximum;
        }

        SendPreset(prev);
    }

    private void HandleLast()
    {
        _autoSendTimer.Stop();
        _enteredDigits = string.Empty;
        if (_mode == EntryMode.Favorite) { AppendLog("LAST: not available in Favorites mode"); return; }
        if (_lastPreset.HasValue)
        {
            SendPreset(_lastPreset.Value);
        }
        else
        {
            AppendLog("LAST: no previous preset");
        }
    }

    private void ExecuteSend()
    {
        if (_connectionCts is not null) { return; }
        if (_enteredDigits.Length == 0)
        {
            return;
        }

        if (!int.TryParse(_enteredDigits, out int value))
        {
            return;
        }

        _enteredDigits = string.Empty;
        if (_mode == EntryMode.Favorite)
        {
            SendFavoriteBySlot(value);
        }
        else
        {
            SendPreset(value);
        }
    }

    private void SendFavoriteBySlot(int slot)
    {
        var fav = _favorites.FirstOrDefault(f => f.Slot == slot && !f.IsEmpty);
        if (fav == null)
        {
            AppendLog($"ERROR: no favorite assigned to slot {slot}");
            UpdateDisplay();
            return;
        }
        SendFavorite(fav);
    }

    private void SendFavorite(Favorite fav)
    {
        if (_connectionCts is not null) { return; }
        if (!PresetTranslation.IsValid(fav.Preset, _settings.DisplayOffset, EffectiveMaximum))
        {
            AppendLog($"ERROR: favorite '{fav.Name}' preset {fav.Preset} is out of range " +
                      $"(offset={_settings.DisplayOffset}, max={EffectiveMaximum})");
            _enteredDigits = string.Empty;
            UpdateDisplay();
            return;
        }

        int sendCh = _settings.MidiChannel == 0 ? 1 : _settings.MidiChannel;
        var t = PresetTranslation.Translate(fav.Preset, _settings.DisplayOffset, sendCh);
        AppendLog($"TRANSLATE: favorite slot {fav.Slot} '{fav.Name}' -> preset {t.DisplayedPreset}, " +
                  $"Bank {t.Bank}, PC {t.ProgramChange}, Scene {fav.Scene}");

        if (_midi.OutputOpen)
        {
            bool ok = _midi.SendFavorite(t.Bank, t.ProgramChange, fav.Scene, _settings.SceneCc, t.MidiChannel);
            if (ok)
            {
                _lastPreset = _currentPreset;
                _currentPreset = fav.Preset;
                _currentFavoriteName = fav.Name;
                _currentFavoriteScene = fav.Scene;
                PresetSent(t.MidiPreset, fav.Scene);
                ShowFavoriteSentFeedback();
                AppendLog($"RESULT: Favorite '{fav.Name}' -> device display {t.DisplayedPreset}, " +
                          $"Bank {t.Bank}, PC {t.ProgramChange}, Scene {fav.Scene}");
            }
        }
        else
        {
            string sel = _outputPortCombo.SelectedItem as string ?? "(none selected)";
            AppendLog($"WARNING: No MIDI output connected — selected port: '{sel}'. Click Connect.");
        }

        _enteredDigits = string.Empty;
        UpdateDisplay();
    }

    private void SendPreset(int displayed)
    {
        if (_connectionCts is not null) { return; }
        if (!PresetTranslation.IsValid(displayed, _settings.DisplayOffset, EffectiveMaximum))
        {
            AppendLog($"ERROR: {displayed} is out of range " +
                      $"(offset={_settings.DisplayOffset}, max={EffectiveMaximum})");
            _enteredDigits = string.Empty;
            UpdateDisplay();
            return;
        }

        int sendCh = _settings.MidiChannel == 0 ? 1 : _settings.MidiChannel;
        var t = PresetTranslation.Translate(displayed, _settings.DisplayOffset, sendCh);
        AppendLog($"TRANSLATE: displayed {t.DisplayedPreset} -> MIDI preset {t.MidiPreset}, " +
                  $"Bank {t.Bank}, PC {t.ProgramChange}");

        if (_midi.OutputOpen)
        {
            bool ok = _midi.SendBankAndPC(t.Bank, t.ProgramChange, t.MidiChannel);
            if (ok)
            {
                _lastPreset = _currentPreset;
                _currentPreset = displayed;
                _currentFavoriteName = null;
                _currentFavoriteScene = null;
                PresetSent(t.MidiPreset, null);
                AppendLog($"RESULT: device display {t.DisplayedPreset} <- " +
                          $"MIDI preset {t.MidiPreset}, Bank {t.Bank}, PC {t.ProgramChange}");
            }
        }
        else
        {
            string sel = _outputPortCombo.SelectedItem as string ?? "(none selected)";
            AppendLog($"WARNING: No MIDI output connected — selected port: '{sel}'. Click Connect.");
        }

        _enteredDigits = string.Empty;
        UpdateDisplay();
    }

    // ── UI update ────────────────────────────────────────────────
    private void ShowFavoriteSentFeedback()
    {
        _favoriteSentTimer.Stop();
        _favoriteSentLabel.IsVisible = true;
        _favoriteSentTimer.Start();
    }

    private void UpdateDisplay()
    {
        string text;
        IBrush fore;

        if (_enteredDigits.Length > 0)
        {
            bool isNumeric = int.TryParse(_enteredDigits, out int parsed);
            bool valid = _mode == EntryMode.Favorite
                ? isNumeric
                : isNumeric && PresetTranslation.IsValid(parsed, _settings.DisplayOffset, EffectiveMaximum);
            text = _enteredDigits;
            fore = valid ? AccentBrush : Brushes.Red;
        }
        else if (_mode == EntryMode.Favorite)
        {
            text = "---";
            fore = Brushes.DimGray;
        }
        else if (_currentPreset.HasValue)
        {
            text = _currentPreset.Value.ToString();
            fore = Brushes.White;
        }
        else
        {
            text = "---";
            fore = Brushes.DimGray;
        }

        _displayLabel.Text = text;
        _displayLabel.FontSize = text.Length > 6 ? 20 : 36;
        _displayLabel.Foreground = fore;
        _favoritesDisplayLabel.Text = text;
        _favoritesDisplayLabel.FontSize = text.Length > 12 ? 16 : 24;
        _favoritesDisplayLabel.Foreground = fore;

        bool showDetails = _mode != EntryMode.Favorite && _enteredDigits.Length == 0 && _currentPreset.HasValue;
        _currentPresetNameLabel.Text = showDetails ? GetCurrentPresetName() ?? "—" : "";
        _currentSceneNameLabel.Text = showDetails ? GetCurrentSceneName() ?? "—" : "";
        UpdateDiagnostics();
    }

    private string? GetCurrentPresetName()
    {
        int? slot = _sceneSlot ?? (_currentPreset.HasValue ? _currentPreset.Value - _settings.DisplayOffset : null);
        return slot.HasValue && _settings.PresetNameCache.TryGetValue(slot.Value, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : null;
    }

    private string? GetCurrentSceneName()
    {
        if (_sceneSlot is not int slot || _activeScene is not int scene || !SceneCache.TryGetValue(slot, out var entry) || scene < 1 || scene > entry.Names.Length)
        {
            return null;
        }

        string name = entry.Names[scene - 1]?.Trim() ?? "";
        return string.IsNullOrEmpty(name) ? $"Scene {scene}" : $"Scene {scene} · {name}";
    }

    private void UpdateDiagnostics()
    {
        if (_mode == EntryMode.Favorite)
        {
            UpdateFavoriteDiagnostics();
            return;
        }

        int? displayed = _enteredDigits.Length > 0 && int.TryParse(_enteredDigits, out int parsed) ? parsed : _currentPreset;

        SetDiagRow(_diagFavoriteLabel, "---");
        SetDiagRow(_diagSceneLabel, "---");

        if (!displayed.HasValue)
        {
            SetDiagRow(_diagEnteredLabel, "---");
            SetDiagRow(_diagDeviceDisplayLabel, "---");
            SetDiagRow(_diagMidiPresetLabel, "---");
            SetDiagRow(_diagBankLabel, "---");
            SetDiagRow(_diagPcLabel, "---");
            SetDiagRow(_diagChannelLabel, ChannelLabel(_settings.MidiChannel));
            return;
        }

        bool valid = PresetTranslation.IsValid(displayed.Value, _settings.DisplayOffset, EffectiveMaximum);
        SetDiagRow(_diagEnteredLabel, _enteredDigits.Length > 0 ? _enteredDigits : displayed.Value.ToString());

        if (valid)
        {
            int sendCh = _settings.MidiChannel == 0 ? 1 : _settings.MidiChannel;
            var t = PresetTranslation.Translate(displayed.Value, _settings.DisplayOffset, sendCh);
            SetDiagRow(_diagDeviceDisplayLabel, t.DisplayedPreset.ToString());
            SetDiagRow(_diagMidiPresetLabel, t.MidiPreset.ToString());
            SetDiagRow(_diagBankLabel, t.Bank.ToString());
            SetDiagRow(_diagPcLabel, t.ProgramChange.ToString());
            SetDiagRow(_diagChannelLabel, ChannelLabel(_settings.MidiChannel));
        }
        else
        {
            SetDiagRow(_diagDeviceDisplayLabel, "OUT OF RANGE");
            SetDiagRow(_diagMidiPresetLabel, "---");
            SetDiagRow(_diagBankLabel, "---");
            SetDiagRow(_diagPcLabel, "---");
            SetDiagRow(_diagChannelLabel, ChannelLabel(_settings.MidiChannel));
        }
    }

    private void UpdateFavoriteDiagnostics()
    {
        SetDiagRow(_diagChannelLabel, ChannelLabel(_settings.MidiChannel));

        if (_enteredDigits.Length > 0)
        {
            SetDiagRow(_diagEnteredLabel, _enteredDigits);
            var match = int.TryParse(_enteredDigits, out int slot) ? _favorites.FirstOrDefault(f => f.Slot == slot && !f.IsEmpty) : null;
            SetDiagRow(_diagFavoriteLabel, match?.Name ?? "(no favorite at this slot)");
            SetDiagRow(_diagDeviceDisplayLabel, match?.Preset.ToString() ?? "---");
            SetDiagRow(_diagSceneLabel, match?.Scene.ToString() ?? "---");
            SetDiagRow(_diagMidiPresetLabel, "---");
            SetDiagRow(_diagBankLabel, "---");
            SetDiagRow(_diagPcLabel, "---");
            return;
        }

        if (_currentFavoriteName != null && _currentPreset.HasValue)
        {
            int sendCh = _settings.MidiChannel == 0 ? 1 : _settings.MidiChannel;
            var t = PresetTranslation.Translate(_currentPreset.Value, _settings.DisplayOffset, sendCh);
            SetDiagRow(_diagEnteredLabel, "---");
            SetDiagRow(_diagFavoriteLabel, _currentFavoriteName);
            SetDiagRow(_diagDeviceDisplayLabel, t.DisplayedPreset.ToString());
            SetDiagRow(_diagMidiPresetLabel, t.MidiPreset.ToString());
            SetDiagRow(_diagBankLabel, t.Bank.ToString());
            SetDiagRow(_diagPcLabel, t.ProgramChange.ToString());
            SetDiagRow(_diagSceneLabel, _currentFavoriteScene?.ToString() ?? "---");
            return;
        }

        SetDiagRow(_diagEnteredLabel, "---");
        SetDiagRow(_diagFavoriteLabel, "---");
        SetDiagRow(_diagDeviceDisplayLabel, "---");
        SetDiagRow(_diagMidiPresetLabel, "---");
        SetDiagRow(_diagBankLabel, "---");
        SetDiagRow(_diagPcLabel, "---");
        SetDiagRow(_diagSceneLabel, "---");
    }

    private static void SetDiagRow(TextBlock lbl, string value) => lbl.Text = value;
    private static string ChannelLabel(int ch) => ch == 0 ? "Omni" : ch.ToString();

    private void AppendLog(string message)
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => AppendLog(message)); return; }
        if (_logLines.Count >= 200)
        {
            _logLines.Dequeue();
        }

        _logLines.Enqueue($"{DateTime.Now:HH:mm:ss.fff}  {message}");
        _logTextBox.Text = string.Join(Environment.NewLine, _logLines);
        _logTextBox.CaretIndex = _logTextBox.Text.Length;
    }

    private void ClearLog()
    {
        _logLines.Clear();
        _logTextBox.Clear();
    }

    // ── MIDI event handlers ────────────────────────────────────────
    private void OnMidiLogMessage(object? sender, string msg)
    {
        AppendLog(msg);
        if (msg.StartsWith("OUTPUT ERROR:", StringComparison.Ordinal) || msg.StartsWith("OUTPUT SYSEX ERROR:", StringComparison.Ordinal) ||
            msg.StartsWith("SCENE ERROR:", StringComparison.Ordinal) || msg.StartsWith("THRU ERROR:", StringComparison.Ordinal))
        {
            long generation = _connectionGeneration;
            Dispatcher.UIThread.Post(() => { if (generation == _connectionGeneration && _connectionCts is null) { Disconnect(); } });
        }
    }

    private void OnNoteOnReceived(object? sender, NoteOnEventArgs e)
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => OnNoteOnReceived(sender, e)); return; }
        if (_connectionCts is not null) { return; }

        if (!_settings.MidiEntryEnabled)
        {
            AppendLog($"INPUT: note {e.NoteNumber} ch{e.Channel} ignored (MIDI entry disabled)");
            return;
        }

        bool channelFiltered = _settings.MidiChannel != 0 && e.Channel != _settings.MidiChannel;
        if (channelFiltered)
        {
            if (!_settings.DebugMode)
            {
                AppendLog($"INPUT: note {e.NoteNumber} ch{e.Channel} ignored (filter=ch{_settings.MidiChannel})");
                return;
            }
            AppendLog($"DEBUG: ch filter bypassed — note {e.NoteNumber} arrived on ch{e.Channel}, filter=ch{_settings.MidiChannel}");
        }

        if (!_settings.MidiNoteMap.TryGetValue(e.NoteNumber, out string? cmdStr))
        {
            _diagLastRxLabel.Text = $"note {e.NoteNumber} ch{e.Channel} — UNMAPPED";
            AppendLog(_settings.DebugMode
                ? $"DEBUG: note {e.NoteNumber} ch{e.Channel} — not in MidiNoteMap (no action)"
                : $"INPUT: Unmapped note {e.NoteNumber} (ignored)");
            return;
        }

        _diagLastRxLabel.Text = $"note {e.NoteNumber} ch{e.Channel} → {cmdStr}";
        var cmd = NoteCommandHelper.Parse(cmdStr);
        AppendLog($"TRANSLATE: note {e.NoteNumber} -> {cmdStr}");

        if (cmd.IsDigit())
        {
            HandleDigit(cmd.ToDigit());
        }
        else if (cmd == NoteCommand.Send)
        {
            HandleSend();
        }
        else if (cmd == NoteCommand.Clear)
        {
            HandleClear();
        }
        else if (cmd == NoteCommand.Next)
        {
            HandleNext();
        }
        else if (cmd == NoteCommand.Prev)
        {
            HandlePrev();
        }
        else if (cmd == NoteCommand.Last)
        {
            HandleLast();
        }
    }

    // ── Port management ────────────────────────────────────────────
    private void RefreshPortLists()
    {
        string prevIn = _inputPortCombo.SelectedItem as string ?? string.Empty;
        string prevOut = _outputPortCombo.SelectedItem as string ?? string.Empty;
        var prevThru = GetCheckedThruPorts();

        _inputPortCombo.Items.Clear();
        foreach (var n in _midi.GetInputPortNames())
        {
            _inputPortCombo.Items.Add(n);
        }

        _outputPortCombo.Items.Clear();
        foreach (var n in _midi.GetOutputPortNames())
        {
            _outputPortCombo.Items.Add(n);
        }

        RestoreCombo(_inputPortCombo, prevIn, _settings.MidiInputPort);
        RestoreCombo(_outputPortCombo, prevOut, _settings.MidiOutputPort);

        RefreshThruInputOptions(prevThru.Count > 0 ? prevThru : _settings.ThruInputPorts);
    }

    private void RefreshThruInputOptions(IEnumerable<string>? requestedPorts = null)
    {
        var wantedThru = (requestedPorts ?? GetCheckedThruPorts()).ToHashSet(StringComparer.Ordinal);
        string selectedInput = _inputPortCombo.SelectedItem as string ?? string.Empty;
        string selectedOutput = _outputPortCombo.SelectedItem as string ?? string.Empty;

        if (selectedInput.Length > 0 && wantedThru.Remove(selectedInput))
        {
            _midi.CloseThruInput(selectedInput);
        }

        if (selectedOutput.Length > 0 && wantedThru.Remove(selectedOutput))
        {
            _midi.CloseThruInput(selectedOutput);
        }

        _thruInputPanel.Children.Clear();
        foreach (var n in _midi.GetInputPortNames())
        {
            if (string.Equals(n, selectedInput, StringComparison.Ordinal) ||
                string.Equals(n, selectedOutput, StringComparison.Ordinal))
            {
                continue;
            }

            var cb = new CheckBox { Content = n, Tag = n, IsChecked = wantedThru.Contains(n) };
            cb.IsCheckedChanged += OnThruInputItemCheck;
            _thruInputPanel.Children.Add(cb);
        }
    }

    private List<string> GetCheckedThruPorts() =>
        _thruInputPanel.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (string)c.Tag!).ToList();

    private void OnThruInputItemCheck(object? sender, RoutedEventArgs e)
    {
        if (_connectionCts is not null) { return; }
        if (sender is not CheckBox { Tag: string port } cb)
        {
            return;
        }

        bool enable = cb.IsChecked == true;
        if (enable)
        {
            bool ok = _midi.OpenThruInput(port, out string? err);
            AppendLog(ok ? $"THRU: opened '{port}'" : $"THRU: failed to open '{port}' — {err}");
        }
        else
        {
            _midi.CloseThruInput(port);
            AppendLog($"THRU: closed '{port}'");
        }
    }

    private static void RestoreCombo(ComboBox cb, string preferred, string fallback)
    {
        if (preferred.Length > 0 && cb.Items.Contains(preferred))
        {
            cb.SelectedItem = preferred;
        }
        else if (fallback.Length > 0 && cb.Items.Contains(fallback))
        {
            cb.SelectedItem = fallback;
        }
    }

    private async void Connect()
    {
        try
        {
            await ConnectAsync();
        }
        catch (Exception ex)
        {
            // Button event handlers are async void. Keep an unexpected driver or
            // UI follow-up exception from escaping the dispatcher and closing the app.
            AppendLog($"CONNECT ERROR: {ex.Message}");
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
    }

    private void Disconnect()
    {
        _connectionGeneration++;
        _connectionCts?.Cancel();
        _connectionMonitor?.Stop();
        StopSceneTracking();
        _presetNamesCts?.Cancel();
        _midi.CloseAllThruInputs();
        _midi.CloseInput();
        _midi.CloseOutput();
        _detectedDevice = null;
        UpdatePresetCapacityUI();
        SetStatus("Not connected", StatusKind.NotConnected);
        UpdateConnectButtons();
    }

    private void SetStatus(string text, StatusKind kind)
    {
        _statusText = text;
        _statusKind = kind;
        IBrush foreground = kind switch
        {
            StatusKind.ConnectedBoth => Avalonia.Application.Current!.RequestedThemeVariant == Avalonia.Styling.ThemeVariant.Light ? Brushes.LimeGreen : SuccessBrush,
            StatusKind.InputOnly or StatusKind.OutputOnly => Brushes.Goldenrod,
            StatusKind.DeviceError or StatusKind.NotConnected or StatusKind.Disconnected => Brushes.Red,
            _ => Brushes.Gray,
        };
        _statusLabel.Text = text;
        _statusLabel.Foreground = foreground;
        _headerStatusLabel.Text = text;
        _headerStatusLabel.Foreground = kind == StatusKind.ConnectedBoth ? TextBrush : foreground;
        if (_connectionDot is not null)
        {
            _connectionDot.IsVisible = true;
            _connectionDot.Background = kind == StatusKind.ConnectedBoth ? SuccessBrush : Brushes.Red;
        }
        _senderStatusLabel.Text = text;
        _senderStatusLabel.Foreground = foreground;
        UpdatePresetSyncButtons();
    }

    private void UpdateConnectButtons()
    {
        _connectButton.IsEnabled = _connectionCts is null;
        _disconnectButton.IsEnabled = _midi.InputOpen || _midi.OutputOpen;
    }

    // ── Settings helpers ───────────────────────────────────────────
    private void ApplySettingsToUI()
    {
        ApplyProfileSettingsToUI();
        ApplyComputerSettingsToUI();
    }

    private void ApplyProfileSettingsToUI()
    {
        if (_detectedDevice is not null) { _settings.DeviceModel = _detectedDevice.Model; }
        _channelCombo.SelectedIndex = Math.Clamp(_settings.MidiChannel, 0, 16);
        _offsetCombo.SelectedIndex = Math.Clamp(_settings.DisplayOffset, 0, 1);
        _sceneCcSpinner.Value = Math.Clamp(_settings.SceneCc, 0, 127);
        UpdatePresetCapacityUI();
    }

    private void ApplyComputerSettingsToUI()
    {
        _autoSendCheck.IsChecked = _settings.AutoSend;
        _autoSendDelaySpinner.Value = Math.Clamp(_settings.AutoSendDelayMs, 10, 2000);
        _keyboardEntryCheck.IsChecked = _settings.KeyboardEntryEnabled;
        _midiEntryCheck.IsChecked = _settings.MidiEntryEnabled;
        _debugCheck.IsChecked = _settings.DebugMode;

        bool isLight = string.Equals(_settings.Theme, "Light", StringComparison.OrdinalIgnoreCase);
        _lightThemeRadio.IsChecked = isLight;
        _darkThemeRadio.IsChecked = !isLight;
        SetTheme(_settings.Theme);
    }

    private void SaveSettingsFromUI()
    {
        _settings.MidiInputPort = _inputPortCombo.SelectedItem as string ?? string.Empty;
        _settings.MidiOutputPort = _outputPortCombo.SelectedItem as string ?? string.Empty;
        _settings.ThruInputPorts = GetCheckedThruPorts();
        _settings.MidiChannel = _channelCombo.SelectedIndex;
        _settings.DisplayOffset = _offsetCombo.SelectedIndex;
        _settings.MaxDisplayedPreset = EffectiveMaximum;
        _settings.AutoSend = _autoSendCheck.IsChecked == true;
        _settings.AutoSendDelayMs = (int)(_autoSendDelaySpinner.Value ?? 35);
        _settings.KeyboardEntryEnabled = _keyboardEntryCheck.IsChecked == true;
        _settings.MidiEntryEnabled = _midiEntryCheck.IsChecked == true;
        _settings.DebugMode = _debugCheck.IsChecked == true;
        _settings.SceneCc = (int)(_sceneCcSpinner.Value ?? 34);
        _saveSettings(_settings);
    }

    private void OnClosing()
    {
        Disconnect();
        _sceneClosing = true;
        StopSceneTracking();
        _midi.PresetChangeReceived -= OnDevicePresetChange;
        _autoSendTimer.Stop();
        _presetNamesCts?.Cancel();
        SaveSettingsFromUI();
        _saveFavorites(_favorites);
        _presetNameClient.Dispose();
        _midi.Dispose();
    }

    // ── Diagnostics test button ────────────────────────────────────
    private void OnOffsetChanged(object? sender, EventArgs e)
    {
        _settings.DisplayOffset = _offsetCombo.SelectedIndex;
        UpdatePresetCapacityUI();
        UpdateDisplay();
    }

    private void OnTestTranslation(object? sender, RoutedEventArgs e)
    {
        string src = _enteredDigits.Length > 0 ? _enteredDigits : (_currentPreset?.ToString() ?? "0");
        if (!int.TryParse(src, out int displayed))
        {
            return;
        }

        if (!PresetTranslation.IsValid(displayed, _settings.DisplayOffset, EffectiveMaximum))
        {
            AppendLog($"TEST: {displayed} is out of range (no MIDI sent)");
            return;
        }

        int sendCh = _settings.MidiChannel == 0 ? 1 : _settings.MidiChannel;
        var t = PresetTranslation.Translate(displayed, _settings.DisplayOffset, sendCh);
        string chLabel = _settings.MidiChannel == 0 ? "Omni (sends ch1)" : sendCh.ToString();
        AppendLog($"TEST (No MIDI): Entered={t.DisplayedPreset}  devicedisplay={t.DisplayedPreset}  " +
                  $"MIDIpreset={t.MidiPreset}  Bank={t.Bank}  PC={t.ProgramChange}  Ch={chLabel}");
    }
}
