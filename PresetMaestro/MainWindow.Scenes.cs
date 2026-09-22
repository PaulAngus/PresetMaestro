using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PresetMaestro.Core;
using PresetNameSync.Core;

namespace PresetMaestro;

public partial class MainWindow
{
    private readonly Button[] _sceneButtons = new Button[8];
    private TextBlock _sceneStatus = new();
    private DispatcherTimer _scenePoll = null!;
    private CancellationTokenSource? _sceneCts;
    private CancellationTokenSource? _sceneSyncCts;
    private CancellationTokenSource? _scenePollCts;
    private int _sceneGeneration;
    private int? _sceneSlot, _activeScene;
    private bool _sceneClosing, _pollBusy;
    private string _sceneCacheKey = "";
    private TextBlock _favoriteSceneDisplay = null!;
    private CancellationTokenSource? _favoriteSceneCts;
    private Button _favoriteUseCurrentButton = null!;

    private Dictionary<int, SceneCacheEntry> SceneCache
    {
        get
        {
            if (!_settings.SceneNameCaches.TryGetValue(_sceneCacheKey, out var cache))
            {
                _settings.SceneNameCaches[_sceneCacheKey] = cache = [];
            }

            return cache;
        }
    }

    private void InitializeSceneTracking()
    {
        _sceneCacheKey = System.Text.Json.JsonSerializer.Serialize(new[] { _settings.MidiInputPort, _settings.MidiOutputPort });
        _midi.PresetChangeReceived += OnDevicePresetChange;
        _scenePoll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _scenePoll.Tick += async (_, _) => await PollSceneStateAsync();
    }

    private Control BuildSceneCard()
    {
        var card = ApprovedCard("Scenes", "Names refresh when a preset is loaded");
        var stack = new StackPanel { Spacing = 12 };
        _sceneStatus = new TextBlock { Text = "Connect MIDI input and output to read scene names.", TextWrapping = TextWrapping.Wrap, Foreground = SecondaryBrush };
        stack.Children.Add(_sceneStatus);
        var grid = new UniformGrid { Columns = 2, Rows = 4 };
        for (int i = 0; i < 8; i++)
        {
            int scene = i + 1;
            var button = new Button { Content = $"Scene {scene}", Margin = new Thickness(4), MinHeight = 50, HorizontalAlignment = HorizontalAlignment.Stretch };
            button.Click += (_, _) =>
            {
                if (_connectionCts is null && _sceneSlot.HasValue && _midi.SendScene(scene, _settings.SceneCc, _settings.MidiChannel == 0 ? 1 : _settings.MidiChannel))
                { _activeScene = scene; _currentFavoriteScene = scene; UpdateDisplay(); RenderScenes(); }
            };
            _sceneButtons[i] = button; grid.Children.Add(button);
        }
        stack.Children.Add(grid);
        var refresh = new Button { Content = "Refresh current scene names" };
        refresh.Click += async (_, _) =>
        {
            if (_sceneSlot is int slot)
            {
                await RefreshScenesAsync(slot);
            }
            else
            {
                await PollSceneStateAsync();
            }
        };
        stack.Children.Add(refresh);
        var sync = new Button { Content = "Sync all stored scene names" };
        sync.Click += async (_, _) => await SyncSceneNamesAsync();
        stack.Children.Add(sync);
        var cancel = new Button { Content = "Cancel scene sync" };
        cancel.Click += (_, _) => _sceneSyncCts?.Cancel(); stack.Children.Add(cancel);
        var clear = new Button { Content = "Clear scene-name cache" };
        clear.Click += (_, _) =>
        {
            _sceneSyncCts?.Cancel(); _sceneCts?.Cancel(); _favoriteSceneCts?.Cancel(); _sceneGeneration++;
            SceneCache.Clear(); _saveSettings(_settings); UpdateDisplay(); RenderScenes();
            _sceneStatus.Text = "Scene-name cache cleared for these MIDI ports.";
        };
        stack.Children.Add(clear);
        stack.Children.Add(new TextBlock { Text = "Bulk sync reads saved presets without selecting them. For DIN MIDI, connect both directions and enable Send MIDI PC on the device. USB changes are also checked periodically.", Foreground = SecondaryBrush, TextWrapping = TextWrapping.Wrap });
        RenderScenes();
        SetApprovedCardContent(card, stack);
        return new ScrollViewer { Content = card, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private void RenderScenes()
    {
        string[]? names = _sceneSlot is int slot && SceneCache.TryGetValue(slot, out var entry) ? entry.Names : null;
        for (int i = 0; i < 8; i++)
        {
            if (_sceneButtons[i] == null)
            {
                continue;
            }

            string name = names != null && i < names.Length ? names[i]?.Trim() ?? "" : "";
            _sceneButtons[i].Content = string.IsNullOrEmpty(name) ? $"Scene {i + 1}" : $"{i + 1} · {name}";
            _sceneButtons[i].IsEnabled = _sceneSlot.HasValue && _midi.OutputOpen;
            _sceneButtons[i].Background = _activeScene == i + 1 ? AccentBrush : SurfaceBrush;
            _sceneButtons[i].Foreground = _activeScene == i + 1 ? Brushes.White : TextBrush;
        }
    }

    private void CacheScenes(PresetScenes result, string source, string key)
    {
        if (!_settings.SceneNameCaches.TryGetValue(key, out var cache))
        {
            _settings.SceneNameCaches[key] = cache = [];
        }
        // A stored dump must not replace the live edit-buffer names currently on screen.
        if (!(source == "stored" && result.Slot == _sceneSlot && cache.TryGetValue(result.Slot, out var old) && old.Source == "live"))
        {
            cache[result.Slot] = new SceneCacheEntry { Names = result.Names, Source = source, RetrievedAt = DateTimeOffset.UtcNow };
        }

        _settings.PresetNameCache[result.Slot] = result.PresetName;
        UpdateFavoritePresetDisplay();
        UpdateDisplay();
    }

    private void PresetSent(int slot, int? scene)
    {
        _presetNamesCts?.Cancel(); _sceneSyncCts?.Cancel(); _scenePollCts?.Cancel();
        _sceneSlot = slot; _activeScene = scene;
        RenderScenes();
        _ = RefreshScenesAsync(slot);
    }

    private void AttachFavoriteScenePicker()
    {
        if (_favSceneSpinner.Parent is not StackPanel field)
        {
            return;
        }

        field.Children.Remove(_favSceneSpinner);

        _favoriteSceneDisplay = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = TextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var pickerContent = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        pickerContent.Children.Add(_favoriteSceneDisplay);
        var chevron = new PathIcon
        {
            Data = Geometry.Parse("M5,1 L10,6 L5,11 Z"),
            Width = 10,
            Height = 12,
            Foreground = SecondaryBrush,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(chevron, 1);
        pickerContent.Children.Add(chevron);
        _favScenePickerButton = new Button
        {
            Name = "FavoriteScenePicker",
            MinHeight = 40,
            Padding = new Thickness(12, 0),
            Background = InsetBrush,
            BorderBrush = UiBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = pickerContent,
        };
        AutomationProperties.SetName(_favScenePickerButton, "Select scene");
        _favScenePickerButton.Click += async (_, _) => await SelectFavoriteSceneAsync();
        field.Children.Add(_favScenePickerButton);

        _favoriteUseCurrentButton = new Button
        {
            Name = "FavoriteUseCurrent",
            Content = "Use Current",
            MinHeight = 38,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Left,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        _favoriteUseCurrentButton.Click += async (_, _) => await UseCurrentFavoriteStateAsync();
        _favPickerActions.Children.Add(_favoriteUseCurrentButton);

        _favSceneSpinner.ValueChanged += (_, _) => UpdateFavoriteSceneDisplay();
        _favPresetSpinner.ValueChanged += (_, _) => PopulateFavoriteScenes();
        PopulateFavoriteScenes();
    }

    private async Task UseCurrentFavoriteStateAsync()
    {
        if (_connectionCts is not null || _favEditingId == null || !_midi.InputOpen || !_midi.OutputOpen)
        {
            AppendLog("SCENES: connect both MIDI ports to use the current preset and scene.");
            return;
        }

        _favoriteUseCurrentButton.IsEnabled = false;
        int profileGeneration = _profileGeneration;
        try
        {
            var state = await _presetNameClient.CurrentStateAsync(CancellationToken.None);
            if (_connectionCts is not null || _favEditingId == null || _sceneClosing || profileGeneration != _profileGeneration)
            {
                return;
            }

            _favPresetSpinner.Value = state.Preset.Slot + _settings.DisplayOffset;
            _favSceneSpinner.Value = state.Scene.Index + 1;
            _sceneSlot = state.Preset.Slot;
            _activeScene = state.Scene.Index + 1;
            _currentPreset = state.Preset.Slot + _settings.DisplayOffset;
            _currentFavoriteName = null;
            _currentFavoriteScene = null;
            _settings.PresetNameCache[state.Preset.Slot] = state.Preset.PresetName;
            UpdateFavoritePresetDisplay();
            PopulateFavoriteScenes();
            RenderScenes();
            UpdateDisplay();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AppendLog($"SCENES: could not read the current preset and scene: {ex.Message}");
        }
        finally
        {
            if (_favoriteUseCurrentButton != null)
            {
                _favoriteUseCurrentButton.IsEnabled = true;
            }
        }
    }

    private void PopulateFavoriteScenes()
    {
        _favoriteSceneCts?.Cancel();
        UpdateFavoriteSceneDisplay();
    }

    private void UpdateFavoriteSceneDisplay()
    {
        if (_favoriteSceneDisplay == null)
        {
            return;
        }

        if (_favSceneSpinner.Value is not decimal value)
        {
            _favoriteSceneDisplay.Text = string.Empty;
            return;
        }

        int slot = (int)(_favPresetSpinner.Value ?? _settings.DisplayOffset) - _settings.DisplayOffset;
        int scene = Math.Clamp((int)value, 1, 8);
        SceneCache.TryGetValue(slot, out var entry);
        string? name = entry?.Names is { Length: 8 } ? entry.Names[scene - 1]?.Trim() : null;
        _favoriteSceneDisplay.Text = string.IsNullOrWhiteSpace(name) ? $"{scene} · Scene {scene}" : $"{scene} · {name}";
    }

    private async Task SelectFavoriteSceneAsync()
    {
        if (_favEditingId == null)
        {
            return;
        }

        int slot = (int)(_favPresetSpinner.Value ?? _settings.DisplayOffset) - _settings.DisplayOffset;
        SceneCache.TryGetValue(slot, out var entry);
        int currentScene = Math.Clamp((int)(_favSceneSpinner.Value ?? 1), 1, 8);
        var dialog = new SceneSelectionWindow(entry?.Names, currentScene) { Icon = Icon };
        int? scene = _scenePickerOverride is not null
            ? await _scenePickerOverride(dialog)
            : await dialog.ShowDialog<int?>(this);
        if (scene.HasValue && _favEditingId != null)
        {
            _favSceneSpinner.Value = scene.Value;
        }
    }

    private async Task ReadFavoriteScenesAsync()
    {
        if (_connectionCts is not null || !_midi.InputOpen || !_midi.OutputOpen) { AppendLog("SCENES: connect both MIDI ports first."); return; }
        int slot = (int)(_favPresetSpinner.Value ?? _settings.DisplayOffset) - _settings.DisplayOffset;
        if (slot is < 0 or > 511)
        {
            return;
        }

        _favoriteSceneCts?.Cancel();
        using var cts = new CancellationTokenSource(); _favoriteSceneCts = cts;
        string key = _sceneCacheKey;
        try
        {
            // A browse/edit operation never loads that preset on the device.
            var result = slot == _sceneSlot
                ? await _presetNameClient.SceneNamesAsync(slot, cts.Token)
                : await _presetNameClient.StoredScenesAsync(slot, cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            CacheScenes(result, slot == _sceneSlot ? "live" : "stored", key);
            _saveSettings(_settings); PopulateFavoriteScenes(); RenderScenes();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AppendLog($"SCENES: {ex.Message}"); }
        finally
        {
            if (ReferenceEquals(_favoriteSceneCts, cts))
            {
                _favoriteSceneCts = null;
            }
        }
    }

    private async Task RefreshScenesAsync(int slot)
    {
        _sceneCts?.Cancel();
        int generation = ++_sceneGeneration;
        if (_connectionCts is not null || !_midi.InputOpen || !_midi.OutputOpen) { _sceneStatus.Text = "Cached names; connect both MIDI directions to refresh."; return; }
        using var cts = new CancellationTokenSource(); _sceneCts = cts;
        string key = _sceneCacheKey;
        RenderScenes(); _sceneStatus.Text = $"Preset {slot + _settings.DisplayOffset:000}: refreshing scene names…";
        try
        {
            await Task.Delay(180, cts.Token); // coalesce rapid selection and allow device preset load
            var result = await _presetNameClient.SceneNamesAsync(slot, cts.Token);
            if (generation != _sceneGeneration || cts.IsCancellationRequested || _sceneClosing)
            {
                return;
            }

            CacheScenes(result, "live", key); _saveSettings(_settings); RenderScenes();
            _sceneStatus.Text = $"{result.PresetName} · eight scenes · refreshed";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (generation == _sceneGeneration && !_sceneClosing)
            {
                _sceneStatus.Text = $"Showing cached/default names: {ex.Message}";
            }
        }
        finally
        {
            if (ReferenceEquals(_sceneCts, cts))
            {
                _sceneCts = null;
            }
        }
    }

    private async Task PollSceneStateAsync()
    {
        if (_connectionCts is not null || _pollBusy || _sceneCts != null || _sceneSyncCts != null || _presetNamesCts != null || !_midi.InputOpen || !_midi.OutputOpen || _sceneClosing)
        {
            return;
        }

        _pollBusy = true;
        using var cts = new CancellationTokenSource(); _scenePollCts = cts;
        int generation = _sceneGeneration;
        try
        {
            var preset = await _presetNameClient.CurrentPresetAsync(cts.Token);
            if (cts.IsCancellationRequested || generation != _sceneGeneration)
            {
                return;
            }

            if (_sceneSlot != preset.Slot)
            {
                _sceneSlot = preset.Slot; _activeScene = null;
                _currentPreset = preset.Slot + _settings.DisplayOffset;
                _currentFavoriteName = null; _currentFavoriteScene = null;
                UpdateDisplay(); RenderScenes();
                await RefreshScenesAsync(preset.Slot);
            }
            int sceneGeneration = _sceneGeneration;
            var scene = await _presetNameClient.CurrentSceneAsync(cts.Token);
            if (!cts.IsCancellationRequested && sceneGeneration == _sceneGeneration && _sceneSlot == preset.Slot)
            { _activeScene = scene.Index + 1; UpdateDisplay(); RenderScenes(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!_sceneClosing)
            {
                AppendLog($"SCENE STATUS: {ex.Message}");
            }
        }
        finally
        {
            _pollBusy = false; if (ReferenceEquals(_scenePollCts, cts))
            {
                _scenePollCts = null;
            }
        }
    }

    private void OnDevicePresetChange(object? sender, int channel) => Dispatcher.UIThread.Post(() =>
    {
        if (_sceneClosing || (_settings.MidiChannel != 0 && channel != _settings.MidiChannel))
        {
            return;
        }

        _sceneCts?.Cancel(); _scenePollCts?.Cancel(); _sceneSyncCts?.Cancel(); _presetNamesCts?.Cancel();
        _sceneGeneration++; _sceneSlot = null; _activeScene = null; RenderScenes();
        // Query the full current slot rather than guessing the bank from a PC notification.
        _ = PollAfterChangeAsync();
    });
    private async Task PollAfterChangeAsync()
    {
        await Task.Delay(200); if (!_sceneClosing)
        {
            await PollSceneStateAsync();
        }
    }

    private void StartSceneTracking()
    {
        _sceneCacheKey = System.Text.Json.JsonSerializer.Serialize(new[] { _inputPortCombo.SelectedItem as string ?? "", _outputPortCombo.SelectedItem as string ?? "" });
        PopulateFavoriteScenes();
        RenderScenes();
        if (_midi.InputOpen && _midi.OutputOpen) { _scenePoll.Start(); _ = PollSceneStateAsync(); }
    }
    private void StopSceneTracking()
    {
        _scenePoll.Stop(); _sceneCts?.Cancel(); _scenePollCts?.Cancel(); _sceneSyncCts?.Cancel(); _favoriteSceneCts?.Cancel();
        _sceneGeneration++; _sceneSlot = null; _activeScene = null; RenderScenes();
    }

    private async Task SyncSceneNamesAsync()
    {
        if (_connectionCts is not null || _sceneSyncCts != null || !_midi.InputOpen || !_midi.OutputOpen) { _sceneStatus.Text = "Connect MIDI input and output before syncing."; return; }
        _presetNamesCts?.Cancel(); _sceneCts?.Cancel(); _scenePollCts?.Cancel();
        using var cts = new CancellationTokenSource(); _sceneSyncCts = cts;
        string key = _sceneCacheKey; int count = 0;
        try
        {
            for (int slot = 0; slot < 512; slot++)
            {
                _sceneStatus.Text = $"Reading stored scenes: preset {slot + _settings.DisplayOffset:000} ({count}/512 complete)…";
                var result = await _presetNameClient.StoredScenesAsync(slot, cts.Token);
                cts.Token.ThrowIfCancellationRequested();
                CacheScenes(result, "stored", key); count++;
                if (count % 16 == 0)
                {
                    _saveSettings(_settings);
                }

                RenderScenes();
            }
            _sceneStatus.Text = "Stored scene names synchronized for all 512 presets.";
        }
        catch (OperationCanceledException)
        {
            if (!_sceneClosing && _sceneCts == null)
            {
                _sceneStatus.Text = $"Scene sync stopped; {count} presets cached.";
            }
        }
        catch (Exception ex) { _sceneStatus.Text = $"Stopped after {count} presets: {ex.Message}. Cached names retained."; }
        finally
        {
            _saveSettings(_settings);
            if (ReferenceEquals(_sceneSyncCts, cts))
            {
                _sceneSyncCts = null;
            }

            UpdateFavoritePresetDisplay();
        }
    }
}
