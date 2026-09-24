using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using PresetMaestro.Core;
using PresetMaestro.Midi;
using PresetNameSync.Core;

namespace PresetMaestro;

public partial class MainWindow : Window
{
    private enum EntryMode { Preset, Favorite }
    private enum AppPage { PresetSender, Favorites, Config, Diagnostics }
    private enum StatusKind { NotConnected, ConnectedBoth, InputOnly, OutputOnly, Disconnected, DeviceError }

    // ── Persistent state ────────────────────────────────────────────
    private string _enteredDigits = string.Empty;
    private int? _currentPreset;
    private int? _lastPreset;
    private string? _currentFavoriteName;
    private int? _currentFavoriteScene;
    private EntryMode _mode = EntryMode.Preset;
    private AppPage _currentPage = AppPage.PresetSender;
    private string _statusText = "Not connected";
    private StatusKind _statusKind = StatusKind.NotConnected;
    private bool _isApplyingTheme;

    private readonly AppSettings _settings;
    private readonly List<Favorite> _favorites;
    private readonly IMidiManager _midi;
    private readonly PresetNameClient _presetNameClient;
    private readonly Func<int, TimeSpan, CancellationToken, Task<PresetNameResult>> _queryPresetNameAsync;
    private readonly Action<AppSettings> _saveSettings;
    private readonly Action<List<Favorite>> _saveFavorites;
    private readonly Func<string, string, string, string, Task<bool>>? _confirmOverride;
    private readonly Func<PresetSelectionWindow, Task<int?>>? _presetPickerOverride;
    private readonly Func<SceneSelectionWindow, Task<int?>>? _scenePickerOverride;
    private readonly DispatcherTimer _autoSendTimer;
    private readonly DispatcherTimer _sendFeedbackTimer;

    public MainWindow() : this(new ProfileStore(Path.GetDirectoryName(SettingsManager.SettingsPath)!))
    {
    }

    private MainWindow(ProfileStore profiles) : this(profiles.LoadSettings(), [], new MidiManager(), profileStore: profiles) { }

    internal MainWindow(
        AppSettings settings,
        List<Favorite> favorites,
        IMidiManager midi,
        Func<int, TimeSpan, CancellationToken, Task<PresetNameResult>>? queryPresetNameAsync = null,
        Action<AppSettings>? saveSettings = null,
        Action<List<Favorite>>? saveFavorites = null,
        Func<string, string, string, string, Task<bool>>? confirm = null,
        Func<PresetSelectionWindow, Task<int?>>? presetPicker = null,
        Func<SceneSelectionWindow, Task<int?>>? scenePicker = null,
        ProfileStore? profileStore = null,
        Func<bool, Task<string?>>? profileFilePicker = null)
    {
        _settings = settings;
        _favorites = favorites;
        _profileStore = profileStore;
        _profileFilePickerOverride = profileFilePicker;
        if (profileStore is not null)
        {
            _favorites.AddRange(profileStore.LoadFavorites(settings.ActiveProfile));
        }

        _saveSettings = saveSettings ?? (profileStore is null ? SettingsManager.Save : profileStore.SaveSettings);
        _saveFavorites = saveFavorites ?? (profileStore is null ? FavoritesManager.Save : values => profileStore.SaveFavorites(_settings.ActiveProfile, values));
        _confirmOverride = confirm;
        _presetPickerOverride = presetPicker;
        _scenePickerOverride = scenePicker;

        _midi = midi;
        _midi.LogMessage += OnMidiLogMessage;
        _midi.NoteOnReceived += OnNoteOnReceived;
        _presetNameClient = new PresetNameClient(_midi);
        _queryPresetNameAsync = queryPresetNameAsync ?? _presetNameClient.QueryAsync;
        InitializeSceneTracking();

        _autoSendTimer = new DispatcherTimer();
        _autoSendTimer.Tick += (_, _) => { _autoSendTimer.Stop(); ExecuteSend(); };
        _sendFeedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _sendFeedbackTimer.Tick += (_, _) => HideSendFeedback();

        InitializeComponent();
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        Application.Current!.RequestedThemeVariant = string.Equals(_settings.Theme, "Light", StringComparison.OrdinalIgnoreCase)
            ? ThemeVariant.Light
            : ThemeVariant.Dark;
        ApplyThemePalette();
        BuildLayout();
        SetStatus(_statusText, _statusKind);

        ApplySettingsToUI();
        RefreshPortLists();
        RefreshFavoritesList();
        UpdateDisplay();

        SizeChanged += (_, _) => QueueFavoriteViewportLayout();
        Closing += (_, _) => OnClosing();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        // Buttons and list controls consume Enter before the window's bubbling handler sees it.
        // Once a command has been entered, Enter should send it regardless of which non-editor
        // control currently owns keyboard focus.
        if (_settings.KeyboardEntryEnabled &&
            e.Key == Key.Enter &&
            _enteredDigits.Length > 0 &&
            FocusManager?.GetFocusedElement() is not TextBox && !FavoriteSearchHasFocus)
        {
            HandleSend();
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (!_settings.KeyboardEntryEnabled || e.Handled || FocusManager?.GetFocusedElement() is TextBox || FavoriteSearchHasFocus)
        {
            return;
        }

        int? digit = e.Key switch
        {
            Key.D0 or Key.NumPad0 => 0,
            Key.D1 or Key.NumPad1 => 1,
            Key.D2 or Key.NumPad2 => 2,
            Key.D3 or Key.NumPad3 => 3,
            Key.D4 or Key.NumPad4 => 4,
            Key.D5 or Key.NumPad5 => 5,
            Key.D6 or Key.NumPad6 => 6,
            Key.D7 or Key.NumPad7 => 7,
            Key.D8 or Key.NumPad8 => 8,
            Key.D9 or Key.NumPad9 => 9,
            _ => null,
        };

        if (digit.HasValue)
        {
            HandleDigit(digit.Value);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            HandleSend();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            HandleClear();
            e.Handled = true;
        }
        else if (e.Key == Key.Back)
        {
            HandleBackspace();
            e.Handled = true;
        }
        else if (e.Key == Key.Left)
        {
            HandlePrev();
            e.Handled = true;
        }
        else if (e.Key == Key.Right)
        {
            HandleNext();
            e.Handled = true;
        }
    }

    private void SetTheme(string name)
    {
        if (_isApplyingTheme)
        {
            return;
        }

        _isApplyingTheme = true;
        Application.Current!.RequestedThemeVariant = string.Equals(name, "Light", StringComparison.OrdinalIgnoreCase)
            ? ThemeVariant.Light
            : ThemeVariant.Dark;
        ApplyThemePalette();
        BuildLayout();
        ApplySettingsToUI();
        RefreshPortLists();
        RefreshFavoritesList();
        UpdateDisplay();
        _isApplyingTheme = false;
    }
}
