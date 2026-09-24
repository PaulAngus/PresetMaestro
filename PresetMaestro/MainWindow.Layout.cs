using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using PresetMaestro.Core;

namespace PresetMaestro;

public partial class MainWindow
{
    // ── Preset Sender tab ────────────────────────────────────────────
    private TextBlock _displayLabel = null!;
    private TextBlock _favoritesDisplayLabel = null!;
    private Border _senderFeedbackPanel = null!;
    private TextBlock _senderFeedbackIcon = null!;
    private TextBlock _senderFeedbackTitle = null!;
    private TextBlock _senderFeedbackDetail = null!;
    private Border _favoriteFeedbackPanel = null!;
    private TextBlock _favoriteFeedbackIcon = null!;
    private TextBlock _favoriteFeedbackTitle = null!;
    private TextBlock _favoriteFeedbackDetail = null!;
    private CheckBox _autoSendCheck = null!;
    private NumericUpDown _autoSendDelaySpinner = null!;
    private CheckBox _keyboardEntryCheck = null!;
    private CheckBox _midiEntryCheck = null!;
    private TextBlock _currentPresetNameLabel = null!;
    private TextBlock _currentSceneNameLabel = null!;
    private TextBlock _diagLastRxLabel = null!;
    private TextBlock _diagEnteredLabel = null!;
    private TextBlock _diagFavoriteLabel = null!;
    private TextBlock _diagDeviceDisplayLabel = null!;
    private TextBlock _diagMidiPresetLabel = null!;
    private TextBlock _diagBankLabel = null!;
    private TextBlock _diagPcLabel = null!;
    private TextBlock _diagSceneLabel = null!;
    private TextBlock _diagChannelLabel = null!;
    private TextBox _logTextBox = null!;
    private readonly Queue<string> _logLines = [];

    // ── Config tab ────────────────────────────────────────────────
    private ComboBox _inputPortCombo = null!;
    private ComboBox _mainInputChannelCombo = null!;
    private ComboBox _outputPortCombo = null!;
    private StackPanel _thruInputPanel = null!;
    private ComboBox _channelCombo = null!;
    private ComboBox _offsetCombo = null!;
    private Button _connectButton = null!;
    private Button _disconnectButton = null!;
    private CheckBox _debugCheck = null!;
    private TextBlock _statusLabel = null!;
    private TextBlock _headerStatusLabel = null!;
    private TextBlock _activeProfileLabel = null!;
    private Border _activeProfileBadge = null!;
    private TextBlock _senderStatusLabel = null!;
    private RadioButton _darkThemeRadio = null!;
    private RadioButton _lightThemeRadio = null!;

    // ── Favorites tab ────────────────────────────────────────────────
    private Grid _favoritesPage = null!;
    private TextBox _favSearchBox = null!;
    private Button _favNewBtn = null!;
    private Button _favSendBtn = null!;
    private Button _favDetailsToggleBtn = null!;
    private PathIcon _favDetailsToggleIcon = null!;
    private ListBox _favListBox = null!;
    private Border _favEditorCard = null!;
    private Panel _favEditorPanel = null!;
    private TextBlock _favEditorPlaceholder = null!;
    private TextBlock _favEditorTitle = null!;
    private TextBox _favNameBox = null!;
    private NumericUpDown _favPresetSpinner = null!;
    private TextBlock _favPresetDisplayLabel = null!;
    private Button _favPresetPickerButton = null!;
    private NumericUpDown _favSceneSpinner = null!;
    private Button _favScenePickerButton = null!;
    private StackPanel _favPickerActions = null!;
    private Button _favSaveBtn = null!;
    private Button _favDeleteBtn = null!;
    private Button _favCancelBtn = null!;
    private Button _favSyncPresetsButton = null!;
    private Border _deleteSlotDialogBackdrop = null!;
    private Button _deleteSlotDialogRemoveBtn = null!;
    private Favorite? _deleteSlotDialogFavorite;
    private bool _showFavoriteDetailsForAll;

    private readonly (string Header, GridLength Width)[] FavColumns =
    [
        ("Slot", new GridLength(38)),
        ("Name", new GridLength(1, GridUnitType.Star)),
        ("Preset", new GridLength(54)),
        ("Scene", new GridLength(48)),
    ];
    private static readonly double[] FavColumnMinimumWidths = [36, 252, 52, 46];

    private Grid BuildFavColumnGrid(Control[] cells)
    {
        var grid = new Grid();
        foreach (var column in FavColumns)
        {
            int index = grid.ColumnDefinitions.Count;
            grid.ColumnDefinitions.Add(new ColumnDefinition(column.Width) { MinWidth = FavColumnMinimumWidths[index] });
        }
        for (int i = 0; i < cells.Length; i++)
        {
            Grid.SetColumn(cells[i], i);
            grid.Children.Add(cells[i]);
        }
        return grid;
    }

    private static TextBox CreateSelectableLog(string fontFamily, double fontSize)
    {
        var log = new TextBox
        {
            Name = "MidiLogText",
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily(fontFamily),
            FontSize = fontSize,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(log, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(log, ScrollBarVisibility.Auto);
        return log;
    }
}
