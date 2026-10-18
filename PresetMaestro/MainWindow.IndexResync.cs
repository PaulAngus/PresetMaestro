#if FRACTAL_INDEX
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PresetMaestro.Core;
using PresetMaestro.FractalIndex;
using PresetMaestro.Midi;

namespace PresetMaestro;

public partial class MainWindow
{
    private Button? _indexResyncPresets, _indexResyncScenes;

    private string? IndexResyncUnavailable(bool scenes)
    {
        if (_profileStore is null || _indexCache?.Committed is null || _indexCache.Imported)
        { return "Complete the device sync in Config before resyncing saved data here."; }
        if (_indexError is not null) { return _indexError; }
        if (_detectedDevice is null || !_midi.InputOpen || !_midi.OutputOpen) { return "Connect a Fractal device to resync saved data."; }
        if (DeviceNavigationUnavailable(_indexCache.Device.Variant.ToDeviceModel()) is { } unavailable) { return unavailable; }
        if (_indexCache.Device.Variant.ToDeviceModel() != _settings.DeviceModel) { return "Select the profile for this device model."; }
        if (scenes && _indexSelectedSlot is null) { return "Select a preset to resync its saved scenes."; }
        if (scenes && !AmpBrowserCatalog.SameFirmware(_indexCache.Committed.EffectiveFirmware, _detectedDevice?.Firmware))
        { return "Firmware differs from the saved scan. Resync Presets first."; }
        return null;
    }

    private void UpdateIndexResyncButtons()
    {
        foreach (var (button, scenes) in new[] { (_indexResyncPresets, false), (_indexResyncScenes, true) })
        {
            if (button is null) { continue; }
            string? unavailable = IndexResyncUnavailable(scenes);
            button.IsEnabled = unavailable is null;
            ToolTip.SetTip(button, unavailable ?? (scenes
                ? "Reread the selected preset's eight saved scenes and amp settings. This does not load or save a device scene."
                : "Reread all saved presets, scenes and amp settings from the connected device."));
        }
    }

    internal async Task ResyncIndexAsync(int? slot = null)
    {
        if (IndexResyncUnavailable(slot.HasValue) is not null || _indexCache?.Committed is not { } baseline) { return; }
        if (slot is int selected && !baseline.Presets.ContainsKey(selected)) { return; }
        int generation = _profileGeneration;
        long connection = _connectionGeneration;
        var expected = IndexJson.Clone(_indexCache);
        var candidate = IndexJson.Clone(expected);
        using var cancellation = new CancellationTokenSource();
        _autoSendTimer.Stop();
        _presetNamesCts = cancellation; _indexScanning = true;
        _sceneCts?.Cancel(); _scenePollCts?.Cancel();
        _indexProgressMessage = slot is int number ? $"Reading saved scenes for preset {number + _settings.DisplayOffset:D3}…" : "Resyncing saved presets…";
        _indexProgressText!.Text = _indexProgressMessage;
        _indexProgress!.Maximum = slot.HasValue ? 1 : FractalDeviceDefinition.For(candidate.Device).PresetSlots;
        _indexProgress.Value = 0;
        UpdatePresetSyncButtons();
        var staging = new IndexLibrary(Path.Combine(_indexLibrary.DirectoryPath, "Resync"));
        bool committed = false;
        string outcome = "Resync stopped because the device or profile changed. Previous saved data and tags were kept.";
        try
        {
            if (slot is int target)
            {
                Version? firmware = Version.TryParse(_detectedDevice!.Firmware, out var parsed) ? parsed : null;
                var current = await _fractalIndexReader.ReadAsync(FractalDeviceDefinition.For(candidate.Device), firmware, target, cancellation.Token);
                candidate.Committed!.Presets[target] = current;
                candidate.Committed.FirmwareConfirmation = null;
                _indexProgress.Value = 1;
            }
            else
            {
                candidate.Device = candidate.Device with { Firmware = _detectedDevice!.Firmware };
                await new IndexScanner(_fractalIndexReader, staging).ScanAsync(candidate, false, progress =>
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (!ReferenceEquals(_presetNamesCts, cancellation) || cancellation.IsCancellationRequested) { return; }
                        _indexProgress.Value = progress.Read + progress.Failed;
                        _indexProgressText.Text = $"Resyncing presets… {progress.Read + progress.Failed} of {progress.Total} checked.";
                    }), cancellation.Token, publish: false, connectedDeviceName: _detectedDevice.DeviceName);
                if (candidate.LastAttempt?.Status != "Complete") { throw new IOException("Some presets could not be read. Previous data and tags were kept."); }
                candidate.Committed = IndexJson.Clone(candidate.LastAttempt);
            }
            cancellation.Token.ThrowIfCancellationRequested();
            if (!Current()) { return; }
            var updated = IndexJson.Clone(_indexProfile);
            int followed = 0;
            foreach (var current in candidate.Committed!.Presets.Values.OrderBy(p => p.Slot))
            {
                if (slot.HasValue && slot != current.Slot || !baseline.Presets.TryGetValue(current.Slot, out var before)) { continue; }
                var annotation = updated.Find(candidate.Device.Id, before, baseline.Firmware);
                if (!AmpBrowserCatalog.SameFirmware(baseline.EffectiveFirmware, candidate.Committed.EffectiveFirmware))
                { before = before with { SceneContentSha256 = null }; }
                var plan = SceneTagMoves.Compare(before, current, annotation);
                if (!plan.HasTaggedMoves) { continue; }
                bool? follow = plan.Exact ? true : await ConfirmSceneTagMovesAsync(current, plan);
                cancellation.Token.ThrowIfCancellationRequested();
                if (!Current()) { return; }
                if (follow is null) { outcome = "Resync cancelled. Previous saved data and tags were kept."; return; }
                if (follow == true) { plan.Apply(annotation!); followed += plan.Moves.Count(move => move.Tags.Length > 0); }
            }
            cancellation.Token.ThrowIfCancellationRequested();
            if (!Current()) { return; }
            var settings = IndexJson.Clone(_settings);
            var favorites = IndexJson.Clone(_favorites);
            SharedSceneTags.Reconcile(updated, candidate, favorites, settings.DisplayOffset, importLegacy: false);
            settings.FractalIndex = IndexJson.ToElement(updated);
            settings.PresetNameCache = candidate.Committed.Presets.ToDictionary(pair => pair.Key, pair => pair.Value.Name);
            if (!settings.SceneNameCaches.TryGetValue(_sceneCacheKey, out var names)) { settings.SceneNameCaches[_sceneCacheKey] = names = []; }
            foreach (var preset in candidate.Committed.Presets.Values.Where(p => slot is null || p.Slot == slot))
            { names[preset.Slot] = new SceneCacheEntry { Names = preset.SceneNames.ToArray(), Source = "stored", RetrievedAt = DateTimeOffset.UtcNow }; }
            _profileStore!.SaveIndexRefresh(settings, favorites, candidate, expected);
            _settings.FractalIndex = settings.FractalIndex;
            _settings.PresetNameCache = settings.PresetNameCache;
            _settings.SceneNameCaches = settings.SceneNameCaches;
            _favorites.Clear(); _favorites.AddRange(favorites);
            _indexProfile = updated; _indexCache = candidate; _indexViewContext = null;
            committed = true;
            outcome = (slot is int refreshed ? $"Scenes refreshed for preset {refreshed + _settings.DisplayOffset:D3}." : "Presets, scenes and amp settings refreshed.") +
                (followed > 0 ? $" Tags followed {followed} moved {(followed == 1 ? "scene" : "scenes")}." : " Tags kept at their existing scene positions.");
        }
        catch (OperationCanceledException) { outcome = "Resync cancelled. Previous saved data and tags were kept."; }
        catch (Exception ex) { outcome = "Could not resync: " + ex.Message; }
        finally
        {
            _indexScanning = false;
            if (ReferenceEquals(_presetNamesCts, cancellation)) { _presetNamesCts = null; }
            try { staging.Delete(candidate.Device.Id); } catch (Exception ex) { AppendLog("RESYNC: staging cleanup: " + ex.Message); }
            if (generation == _profileGeneration)
            {
                RefreshIndexContext();
                if (committed) { UpdateFavoritePresetDisplay(); PopulateFavoriteScenes(); RefreshFavoritesList(); UpdateDisplay(); }
                SetIndexMessage(outcome);
                UpdatePresetSyncButtons();
                var focus = slot.HasValue ? _indexResyncScenes : _indexResyncPresets;
                if (focus?.IsEffectivelyEnabled == true) { focus.Focus(); }
            }
            if (!_connectionLibrarySetupRequired && _detectedDevice is not null && !_sceneClosing) { StartSceneTracking(); }
        }

        bool Current() => generation == _profileGeneration && connection == _connectionGeneration &&
            _indexCache?.Device.Id == expected.Device.Id;
    }

    internal async Task<bool?> ConfirmSceneTagMovesAsync(IndexedPreset preset, SceneTagMovePlan plan)
    {
        var dialog = new Window
        {
            Name = "SceneTagMoveDialog",
            Title = "Should tags follow these scenes?",
            Width = Math.Min(540, Math.Max(360, Bounds.Width - 48)),
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = SurfaceBrush,
            Foreground = TextBrush,
            RequestedThemeVariant = RequestedThemeVariant,
            Icon = Icon,
        };
        var body = new StackPanel { Spacing = 12, Margin = new(20) };
        body.Children.Add(new TextBlock { Text = "Should tags follow these scenes?", FontSize = 19, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(DetailText("The scene names match, but an exact settings swap could not be confirmed. Should their tags follow them?"));
        body.Children.Add(DetailText($"{preset.Slot + _settings.DisplayOffset:D3}  {preset.Name}"));
        foreach (var move in plan.Moves)
        {
            body.Children.Add(new StackPanel
            {
                Spacing = 4,
                Children =
            {
                new TextBlock { Text = $"{move.Name}: scene {move.From + 1} → scene {move.To + 1}", FontSize = 14, TextWrapping = TextWrapping.Wrap },
                DetailText(move.Tags.Length > 0 ? "Tags: " + string.Join(", ", move.Tags) : "No tags"),
            }
            });
        }
        var follow = IndexButton("Follow scenes", "SceneTagMoveFollow");
        follow.Background = AccentBrush; follow.Foreground = Brushes.White;
        var keep = IndexButton("Keep at positions", "SceneTagMoveKeep");
        var cancel = IndexButton("Cancel", "SceneTagMoveCancel");
        follow.Click += (_, _) => dialog.Close((bool?)true);
        keep.Click += (_, _) => dialog.Close((bool?)false);
        cancel.Click += (_, _) => dialog.Close((bool?)null);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(20, 12) };
        actions.Children.Add(follow); actions.Children.Add(keep); actions.Children.Add(cancel);
        var layout = new Grid { RowDefinitions = new("*,Auto"), MaxHeight = Math.Max(300, Bounds.Height - 80) };
        layout.Children.Add(new ScrollViewer { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Grid.SetRow(actions, 1); layout.Children.Add(actions); dialog.Content = layout;
        dialog.Opened += (_, _) => follow.Focus();
        dialog.KeyDown += (_, e) => { if (e.Key == Key.Escape) { dialog.Close((bool?)null); e.Handled = true; } };
        return await dialog.ShowDialog<bool?>(this);
    }
}
#endif
