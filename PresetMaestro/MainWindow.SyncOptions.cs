using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
#if FRACTAL_INDEX
using PresetMaestro.Midi;
#endif

namespace PresetMaestro;

public partial class MainWindow
{
    private Window? _connectionSyncDialog;
    private Border? _connectionSyncStatusPanel;
    private TextBlock? _connectionSyncStatusTitle, _connectionSyncNext;
    private TextBlock? _connectionSyncStatus;
    private ProgressBar? _connectionSyncProgress;
    private Button? _connectionSyncNames, _connectionSyncScenes, _connectionSyncCancel;
#if FRACTAL_INDEX
    private Button? _connectionSyncLibrary, _connectionSyncResume, _connectionSyncCheck, _connectionSyncFullCheck;
#endif
    private bool _syncingFavoriteScenes;
    private readonly Dictionary<string, TextBlock> _connectionSyncDescriptions = [];
    private enum SyncStatusTone { Neutral, Activity, Success, Attention }
    private sealed record SyncDialogOutcome(string Context, string Title, string Detail, string Next, SyncStatusTone Tone);
    private SyncDialogOutcome? _connectionSyncOutcome;

    private string ConnectionSyncContext => $"{_connectionGeneration}:{_profileGeneration}"
#if FRACTAL_INDEX
        + $":{_indexProfile.SelectedDeviceId}"
#endif
        ;

    private void SetConnectionSyncOutcome(string title, string detail, string next, bool needsAttention = false)
        => _connectionSyncOutcome = new(ConnectionSyncContext, title, detail, next, needsAttention ? SyncStatusTone.Attention : SyncStatusTone.Success);

    private void OpenConnectionSyncOptions()
    {
        if (_detectedDevice is null || !_midi.InputOpen || !_midi.OutputOpen) { return; }
        if (_connectionSyncDialog is not null) { _connectionSyncDialog.Activate(); return; }
        var dialog = new Window
        {
            Name = "ConnectionSyncDialog",
            Title = "Sync with connected device",
            Width = 650,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = SurfaceBrush,
            Foreground = TextBrush,
            FontSize = 14,
            RequestedThemeVariant = RequestedThemeVariant,
            Icon = Icon,
        };
        _connectionSyncDialog = dialog;
        _connectionSyncDescriptions.Clear();
        var body = new StackPanel { Margin = new(20, 20, 20, 12), Spacing = 12 };
        string library = "Not assigned";
#if FRACTAL_INDEX
        library = _indexCache?.Device.Name ?? "Not assigned";
#endif
        var identity = new StackPanel { Spacing = 14 };
        identity.Children.Add(new TextBlock { Text = "Sync with " + _detectedDevice.Label, FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = TextBrush, TextWrapping = TextWrapping.Wrap });
        var details = new Grid { ColumnDefinitions = new("2*,16,2*,16,*") };
        void AddDetail(string label, string value, int column)
        {
            var field = new StackPanel { Spacing = 4 };
            field.Children.Add(new TextBlock { Text = label, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = TextBrush });
            field.Children.Add(new TextBlock { Text = value, FontSize = 14, Foreground = TextBrush, TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(field, column);
            details.Children.Add(field);
        }
        AddDetail("Profile", _settings.ActiveProfile, 0);
        AddDetail("Library", library, 2);
        AddDetail("Firmware", _detectedDevice.Firmware ?? "Unavailable", 4);
        identity.Children.Add(details);
        body.Children.Add(new Border { Name = "ConnectionSyncHeader", Background = InsetBrush, BorderBrush = AccentBrush, BorderThickness = new(4, 0, 0, 0), Padding = new(16), Child = identity });
        var status = new StackPanel { Spacing = 8 };
        _connectionSyncStatusTitle = new TextBlock { Name = "ConnectionSyncStatusTitle", FontSize = 18, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        _connectionSyncStatus = new TextBlock { Name = "ConnectionSyncStatus", TextWrapping = TextWrapping.Wrap, Foreground = TextBrush, FontSize = 14 };
        _connectionSyncProgress = new ProgressBar { Name = "ConnectionSyncProgress", Height = 12, Foreground = AccentBrush };
        _connectionSyncNext = new TextBlock { Name = "ConnectionSyncNext", TextWrapping = TextWrapping.Wrap, FontSize = 14, FontWeight = FontWeight.Medium, Foreground = TextBrush };
        status.Children.Add(_connectionSyncStatusTitle); status.Children.Add(_connectionSyncStatus);
        status.Children.Add(_connectionSyncProgress); status.Children.Add(_connectionSyncNext);
        _connectionSyncStatusPanel = new Border { Name = "ConnectionSyncStatusPanel", Padding = new(16), BorderThickness = new(4, 0, 0, 0), Child = status };
        body.Children.Add(_connectionSyncStatusPanel);
        var rows = new StackPanel { Spacing = 0 };
        body.Children.Add(rows);
        Button AddOption(string title, string description, string label, string name)
        {
            var row = new Grid { ColumnDefinitions = new("*,16,Auto"), Margin = new(0, 10) };
            var copy = new StackPanel { Spacing = 6 };
            copy.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold });
            var descriptionText = new TextBlock { Name = name + "Description", Text = description, FontSize = 14, Foreground = TextBrush, TextWrapping = TextWrapping.Wrap };
            _connectionSyncDescriptions[descriptionText.Name] = descriptionText;
            copy.Children.Add(descriptionText);
            row.Children.Add(copy);
            var button = new Button { Name = name, Content = label, MinWidth = 105, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(button, 2); row.Children.Add(button);
            rows.Children.Add(new Border { Child = row, BorderBrush = UiBorderBrush, BorderThickness = new(0, 0, 0, 1) });
            return button;
        }
#if FRACTAL_INDEX
        _connectionSyncCheck = AddOption("Quick library match", "Compare up to 16 random presets and all their saved scenes.", "Quick match", "ConnectionSyncCheck");
        _connectionSyncCheck.Click += async (_, _) => await CheckAssignedLibraryAsync();
        _connectionSyncFullCheck = AddOption("Full library match", "Compare every preset slot and all saved scenes with this library. May take several minutes.", "Full match", "ConnectionSyncFullCheck");
        _connectionSyncFullCheck.Click += async (_, _) => await CheckAssignedLibraryAsync(full: true);
#endif
        _connectionSyncNames = AddOption("Preset names", "Refresh the preset-name list saved in this profile.", "Sync names", "ConnectionSyncNames");
        _connectionSyncNames.Click += async (_, _) => { await SyncPresetNamesAsync(); RefreshConnectionSyncOptions(); };
        _connectionSyncScenes = AddOption("Favorite scene names", "Refresh saved scene names for presets used by this profile’s favorites.", "Sync scenes", "ConnectionSyncScenes");
        _connectionSyncScenes.Click += async (_, _) => await SyncFavoriteSceneNamesAsync();
#if FRACTAL_INDEX
        _connectionSyncLibrary = AddOption("Device library", "Read presets, scenes and amp channels. Shared by profiles using this library; may take several minutes.", "Sync library", "ConnectionSyncLibrary");
        _connectionSyncLibrary.Click += async (_, _) => { dialog.Close(); _presetIndexNavigation!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await SyncIndexAsync(); };
        _connectionSyncResume = AddOption("Incomplete library sync", "Continue saved reads and retry missing presets.", "Resume", "ConnectionSyncResume");
        _connectionSyncResume.Click += async (_, _) => { dialog.Close(); _presetIndexNavigation!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await SyncIndexAsync(resume: true); };
#endif
        var footer = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new(20, 0, 20, 20) };
        var manageProfiles = new Button { Content = "Manage profiles", Name = "ConnectionManageProfiles", Margin = new(0, 0, 8, 0) };
        manageProfiles.Click += (_, _) => OpenSyncManagement(dialog, "ManageProfiles"); footer.Children.Add(manageProfiles);
#if FRACTAL_INDEX
        var manageLibraries = new Button { Content = "Manage libraries", Name = "ConnectionManageLibraries", Margin = new(0, 0, 8, 0) };
        manageLibraries.Click += (_, _) => OpenSyncManagement(dialog, "ManageLibraries"); footer.Children.Add(manageLibraries);
#endif
        _connectionSyncCancel = new Button { Content = "Cancel read", Name = "ConnectionSyncCancel", Margin = new(0, 0, 8, 0) };
        _connectionSyncCancel.Click += (_, _) => _presetNamesCts?.Cancel(); footer.Children.Add(_connectionSyncCancel);
        var close = new Button { Content = "Done", Name = "ConnectionSyncDone" }; close.Click += (_, _) => dialog.Close(); footer.Children.Add(close);
        var layout = new Grid { RowDefinitions = new("*,Auto"), MaxHeight = Math.Max(300, Math.Min(720, Bounds.Height - 60)) };
        layout.Children.Add(new ScrollViewer { Content = body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        Grid.SetRow(footer, 1); layout.Children.Add(footer);
        dialog.Content = layout;
        dialog.KeyDown += (_, e) => { if (e.Key == Key.Escape) { dialog.Close(); e.Handled = true; } };
        dialog.Closed += (_, _) => { if (_connectionSyncDialog == dialog) { _connectionSyncDialog = null; } };
        RefreshConnectionSyncOptions();
        _ = dialog.ShowDialog(this);
    }

    private void OpenSyncManagement(Window dialog, string name)
    {
        dialog.Close();
        this.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "NavConfig").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        this.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private void RefreshConnectionSyncOptions()
    {
        if (_connectionSyncDialog is null) { return; }
        bool busy = _connectionCts is not null || _presetNamesCts is not null || _changingProfile;
        bool ready = _detectedDevice is not null && _midi.InputOpen && _midi.OutputOpen && !busy;
        bool checking = false;
#if FRACTAL_INDEX
        checking = _indexChecking;
        _connectionSyncLibrary!.IsEnabled = _indexSyncButton?.IsEnabled == true;
        _connectionSyncResume!.IsEnabled = _indexResumeButton?.IsEnabled == true;
        if (_connectionSyncResume.Parent is Grid resume && resume.Parent is Border border) { border.IsVisible = _indexCache?.LastAttempt is { Status: not "Complete" }; }
        _connectionSyncCheck!.IsEnabled = _indexCheckButton?.IsEnabled == true;
        _connectionSyncFullCheck!.IsEnabled = _connectionSyncCheck.IsEnabled;
        SetSyncOptionDescription("ConnectionSyncLibraryDescription", _indexCache is null
            ? "Assign a library with Manage libraries to save presets, scenes and amp channels."
            : "Read presets, scenes and amp channels. Shared by profiles using this library; may take several minutes.");
        SetSyncOptionDescription("ConnectionSyncCheckDescription", _indexCache?.Committed is null
            ? "No saved baseline yet. Sync the library first to enable a match check."
            : "Compare up to 16 random presets and all their saved scenes.");
        SetSyncOptionDescription("ConnectionSyncFullCheckDescription", _indexCache?.Committed is null
            ? "No saved baseline yet. Sync the library first to enable a match check."
            : "Compare every preset slot and all saved scenes with this library. May take several minutes.");
        ToolTip.SetTip(_connectionSyncLibrary, _indexCache is null ? "Assign a library using Manage libraries first." :
            _detectedDevice?.Model != _indexCache.Device.Variant.ToDeviceModel() ? "The assigned library uses a different device model." : null);
#endif
        _connectionSyncNames!.IsEnabled = ready && CanReadDeviceNames;
        _connectionSyncScenes!.IsEnabled = ready && CanReadDeviceNames && _favorites.Any(f => !f.IsEmpty);
        SetSyncOptionDescription("ConnectionSyncScenesDescription", !_favorites.Any(f => !f.IsEmpty)
            ? "No favorites in this profile yet. Add favorites to sync their saved scene names."
            : "Refresh saved scene names for presets used by this profile’s favorites.");
        ToolTip.SetTip(_connectionSyncScenes, !CanReadDeviceNames ? UnsupportedNameReads : !_favorites.Any(f => !f.IsEmpty) ? "This profile has no favorites to read." : null);
        ToolTip.SetTip(_connectionSyncNames, CanReadDeviceNames ? null : UnsupportedNameReads);
        _connectionSyncCancel!.IsVisible = _presetNamesCts is not null;
        foreach (var button in _connectionSyncDialog.GetVisualDescendants().OfType<Button>().Where(b => b.Name is "ConnectionManageProfiles" or "ConnectionManageLibraries")) { button.IsEnabled = !busy; }
        RefreshConnectionSyncStatus(checking);
    }

    private void RefreshConnectionSyncStatus(bool checking)
    {
        string title = "Connected — choose what to sync";
        string detail = "Device information has been read. Your saved data is available.";
        string next = "Next: Choose a sync option to refresh saved data, or Done to continue. Syncing is optional.";
        var tone = SyncStatusTone.Neutral;
        bool running = _presetNamesCts is not null || _connectionCts is not null;
        bool indeterminate = _connectionCts is not null;
        double maximum = _presetNamesProgress?.Maximum ?? 512;
        double value = _presetNamesProgress?.Value ?? 0;
        if (checking)
        {
#if FRACTAL_INDEX
            title = _indexCheckingFull ? "Checking full library match…" : "Checking quick library match…";
            maximum = Math.Max(1, _libraryMatchProgress?.Total ?? 1);
            value = _libraryMatchProgress?.Checked ?? 0;
            detail = $"{value:0} of {_libraryMatchProgress?.Total ?? 0} {(_indexCheckingFull ? "preset slots" : "random presets")} checked, including all saved scenes.";
#endif
            next = "Please wait. Sync options will be available when the check finishes. Cancel read stops this check.";
            tone = SyncStatusTone.Activity;
        }
        else if (_syncingPresetNames || _syncingFavoriteScenes)
        {
            title = _syncingFavoriteScenes ? "Syncing favorite scene names…" : "Syncing preset names…";
            detail = _syncingFavoriteScenes ? $"{value:0} of {maximum:0} favorite presets read." : $"{value:0} of {maximum:0} preset slots checked.";
            next = "Please wait. Completed reads are kept if you cancel. Other sync options will be available when this finishes.";
            tone = SyncStatusTone.Activity;
        }
#if FRACTAL_INDEX
        else if (_indexScanning)
        {
            title = "Syncing device library…";
            detail = _indexProgressText?.Text ?? "Reading saved presets, scenes and amp channels.";
            next = "This can take several minutes. Completed reads are saved; Cancel read stops the scan.";
            maximum = _indexProgress?.Maximum ?? 1;
            value = _indexProgress?.Value ?? 0;
            tone = SyncStatusTone.Activity;
        }
#endif
        else if (_connectionCts is not null)
        {
            title = "Reading device information…";
            detail = "Preparing the connection.";
            next = "Please wait for the sync options to become available.";
            tone = SyncStatusTone.Activity;
        }
        else if (_connectionSyncOutcome is { } outcome && outcome.Context == ConnectionSyncContext)
        {
            title = outcome.Title; detail = outcome.Detail; next = outcome.Next; tone = outcome.Tone;
        }
#if FRACTAL_INDEX
        else if (_indexCache is null)
        {
            detail = "Device information has been read. This profile has no assigned device library.";
            next = "Next: Sync names for the preset picker, or use Manage libraries to assign a library for presets, scenes and amp data.";
        }
        else if (_indexCache.Device.Variant.ToDeviceModel() != _detectedDevice?.Model)
        {
            title = "Library uses a different device model";
            detail = $"“{_indexCache.Device.Name}” does not use the connected device’s model. Library data has not been refreshed.";
            next = "Next: Use Manage libraries to assign a library for this device.";
            tone = SyncStatusTone.Attention;
        }
        else if (_indexCache.Committed is null)
        {
            detail = "Device information has been read. This library has no complete saved scan to compare.";
            next = _indexCache.LastAttempt is { Status: not "Complete" }
                ? "Next: Choose Resume to finish the saved scan, or Done to continue with the data already saved."
                : "Next: Choose Sync library to read presets, scenes and amp data for the first time. This can take several minutes.";
        }
        else
        {
            title = "Connected — choose a library match";
            detail = "Compare this device’s saved presets and scenes with the assigned library.";
            next = "Next: Choose Quick match for 16 random presets, or Full match for every slot. You can also sync data below or choose Done.";
        }
#endif
        if (!CanReadDeviceNames && !running)
        {
            detail += " " + UnsupportedNameReads;
        }
        _connectionSyncStatusTitle!.Text = title;
        _connectionSyncStatus!.Text = detail;
        _connectionSyncNext!.Text = next;
        IBrush accent = tone switch
        {
            SyncStatusTone.Success => ThemeBrush("SendSuccessForegroundBrush"),
            SyncStatusTone.Attention => ThemeBrush("SendWarningForegroundBrush"),
            _ => AccentBrush,
        };
        _connectionSyncStatusTitle.Foreground = tone is SyncStatusTone.Activity or SyncStatusTone.Neutral ? TextBrush : accent;
        _connectionSyncStatusPanel!.BorderBrush = accent;
        _connectionSyncStatusPanel.Background = tone switch
        {
            SyncStatusTone.Activity => ThemeBrush("SelectedBrush"),
            SyncStatusTone.Success => ThemeBrush("SendSuccessBackgroundBrush"),
            SyncStatusTone.Attention => ThemeBrush("SendWarningBackgroundBrush"),
            _ => InsetBrush,
        };
        _connectionSyncProgress!.IsVisible = running;
        _connectionSyncProgress.IsIndeterminate = indeterminate;
        _connectionSyncProgress.Maximum = maximum;
        _connectionSyncProgress.Value = value;
    }

    private void SetSyncOptionDescription(string name, string text)
    {
        if (_connectionSyncDescriptions.TryGetValue(name, out var label)) { label.Text = text; }
    }

    internal async Task SyncFavoriteSceneNamesAsync()
    {
        if (!CanSyncPresetNames || _syncingFavoriteScenes) { return; }
        var slots = _favorites.Where(f => !f.IsEmpty).Select(f => f.Preset - _settings.DisplayOffset).Where(s => s is >= 0 and <= 511).Distinct().Order().ToArray();
        if (slots.Length == 0) { return; }
        using var cancellation = new CancellationTokenSource();
        _connectionSyncOutcome = null;
        _presetNamesCts = cancellation; _syncingFavoriteScenes = true;
        _sceneCts?.Cancel(); _scenePollCts?.Cancel(); _favoriteSceneCts?.Cancel();
        string key = _sceneCacheKey;
        _presetNamesProgress.Maximum = slots.Length; _presetNamesProgress.Value = 0;
        _presetNamesStatus.Text = $"Reading favorite scene names: 0 of {slots.Length} presets…";
        UpdatePresetSyncButtons();
        try
        {
            for (int i = 0; i < slots.Length; i++)
            {
                var result = await _queryStoredScenesAsync(slots[i], cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (result.Slot != slots[i]) { throw new InvalidDataException("The device returned a different preset."); }
                CacheScenes(result, "stored", key); _saveSettings(_settings);
                _presetNamesProgress.Value = i + 1;
                _presetNamesStatus.Text = $"Reading favorite scene names: {i + 1} of {slots.Length} presets…";
                RefreshConnectionSyncOptions();
            }
            _presetNamesStatus.Text = $"Favorite scene names refreshed for {slots.Length} presets.";
            SetConnectionSyncOutcome("Favorite scene names synced", $"Scene names refreshed for {slots.Length} presets used by this profile’s favorites.", "Next: Choose Done to continue. Other sync options are optional.");
        }
        catch (OperationCanceledException)
        {
            _presetNamesStatus.Text = "Scene-name sync cancelled. Completed reads were saved.";
            SetConnectionSyncOutcome("Scene-name sync cancelled", $"{_presetNamesProgress.Value:0} of {slots.Length} presets refreshed. Completed reads were saved.", "Next: Choose Sync scenes to retry, or Done to use the saved names.", needsAttention: true);
        }
        catch (Exception ex)
        {
            _presetNamesStatus.Text = "Scene-name sync stopped: " + ex.Message;
            SetConnectionSyncOutcome("Scene-name sync stopped", $"{_presetNamesProgress.Value:0} of {slots.Length} presets refreshed. Completed reads were saved. {ex.Message}", "Next: Check the MIDI connection, then choose Sync scenes to retry.", needsAttention: true);
        }
        finally
        {
            _presetNamesCts = null; _syncingFavoriteScenes = false;
            PopulateFavoriteScenes(); UpdatePresetSyncButtons();
        }
    }
}
