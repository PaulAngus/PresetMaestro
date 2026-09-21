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
    private CheckBox _autoSendCheck = null!;
    private NumericUpDown _autoSendDelaySpinner = null!;
    private CheckBox _keyboardEntryCheck = null!;
    private CheckBox _midiEntryCheck = null!;
    private TextBlock _activePresetStatusLabel = null!;
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
    private ComboBox _outputPortCombo = null!;
    private StackPanel _thruInputPanel = null!;
    private ComboBox _channelCombo = null!;
    private ComboBox _offsetCombo = null!;
    private NumericUpDown _maxPresetSpinner = null!;
    private NumericUpDown _sceneCcSpinner = null!;
    private Button _connectButton = null!;
    private Button _disconnectButton = null!;
    private CheckBox _debugCheck = null!;
    private TextBlock _statusLabel = null!;
    private TextBlock _headerStatusLabel = null!;
    private TextBlock _senderStatusLabel = null!;
    private RadioButton _darkThemeRadio = null!;
    private RadioButton _lightThemeRadio = null!;

    // ── Favorites tab ────────────────────────────────────────────────
    private ListBox _favCategoryTree = null!;
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
    private AutoCompleteBox _favCategoryBox = null!;
    private TextBox _favTagsBox = null!;
    private NumericUpDown _favSlotSpinner = null!;
    private NumericUpDown _favPresetSpinner = null!;
    private TextBlock _favPresetDisplayLabel = null!;
    private Button _favPresetPickerButton = null!;
    private NumericUpDown _favSceneSpinner = null!;
    private Button _favScenePickerButton = null!;
    private StackPanel _favPickerActions = null!;
    private Button _favSaveBtn = null!;
    private Button _favDeleteBtn = null!;
    private Button _favCancelBtn = null!;
    // Legacy layout only; the approved editor uses the Delete confirmation dialog.
    private Button _favClearSlotBtn = null!;
    private Button _favRemoveSlotBtn = null!;
    private Button _favSyncPresetsButton = null!;
    private Border _deleteSlotDialogBackdrop = null!;
    private Button _deleteSlotDialogRemoveBtn = null!;
    private Favorite? _deleteSlotDialogFavorite;
    private bool _showFavoriteDetailsForAll;

    // Preserve the existing detail-strip geometry independently of main-table columns.
    private static readonly (string Header, GridLength Width)[] FavoriteDetailColumns =
    [
        ("Slot", new GridLength(54)),
        ("Name", new GridLength(1, GridUnitType.Star)),
        ("Tags", new GridLength(1.12, GridUnitType.Star)),
        ("Preset", new GridLength(106)),
        ("Scene", new GridLength(66)),
    ];

    private static readonly (string Header, GridLength Width)[] FavColumns =
    [
        ("Slot", new GridLength(54)),
        ("Name", new GridLength(1.8, GridUnitType.Star)),
        ("Collection", new GridLength(1, GridUnitType.Star)),
        ("Tags", new GridLength(1.12, GridUnitType.Star)),
        ("Preset", new GridLength(62)),
        ("Scene", new GridLength(52)),
    ];

    private static readonly double[] FavColumnMinimumWidths = [44, 120, 90, 100, 58, 50];

    private void BuildLegacyLayout()
    {
        var tabs = new TabControl();

        var senderTab = new TabItem { Header = "Preset Sender" };
        BuildSenderTab(senderTab);
        tabs.Items.Add(senderTab);

        var favTab = new TabItem { Header = "Favorites" };
        BuildFavoritesTab(favTab);
        tabs.Items.Add(favTab);

        var configTab = new TabItem { Header = "Config" };
        BuildConfigTab(configTab);
        tabs.Items.Add(configTab);

        Content = tabs;
    }

    // ── Preset Sender tab ────────────────────────────────────────────
    private void BuildSenderTab(TabItem tab)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("340,*"), Margin = new Thickness(12) };

        grid.Children.Add(BuildSenderLeft());
        var right = BuildSenderRight();
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);

        tab.Content = grid;
    }

    private Control BuildSenderLeft()
    {
        var stack = new StackPanel { Spacing = 12, Margin = new Thickness(0, 0, 16, 0) };

        _displayLabel = new TextBlock
        {
            Text = "---",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 36,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.DimGray,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var displayBorder = new Border
        {
            Background = Brushes.Black,
            CornerRadius = new CornerRadius(4),
            Height = 76,
            Child = _displayLabel,
        };
        stack.Children.Add(displayBorder);

        var keypad = new UniformGrid { Columns = 3, Rows = 5, Height = 190 };
        int[][] digitGrid = [[1, 2, 3], [4, 5, 6], [7, 8, 9]];
        foreach (var row in digitGrid)
        {
            foreach (var d in row)
            {
                keypad.Children.Add(KeypadButton(d.ToString(), () => HandleDigit(d)));
            }
        }

        keypad.Children.Add(KeypadButton("CLR", HandleClear, Brushes.IndianRed));
        keypad.Children.Add(KeypadButton("0", () => HandleDigit(0)));
        keypad.Children.Add(KeypadButton("SEND", HandleSend, Brushes.DodgerBlue));
        keypad.Children.Add(KeypadButton("◄◄", HandlePrev));
        keypad.Children.Add(KeypadButton("LAST", HandleLast));
        keypad.Children.Add(KeypadButton("►►", HandleNext));
        stack.Children.Add(keypad);

        var autoRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _autoSendCheck = new CheckBox { Content = "Auto-send after 3 digits", VerticalAlignment = VerticalAlignment.Center };
        _autoSendCheck.IsCheckedChanged += (_, _) => _settings.AutoSend = _autoSendCheck.IsChecked == true;
        _autoSendDelaySpinner = new NumericUpDown { Minimum = 10, Maximum = 2000, Value = 35, Width = 90, FormatString = "0" };
        _autoSendDelaySpinner.ValueChanged += (_, _) =>
        {
            _settings.AutoSendDelayMs = (int)(_autoSendDelaySpinner.Value ?? 35);
            _autoSendTimer.Interval = TimeSpan.FromMilliseconds(_settings.AutoSendDelayMs);
        };
        autoRow.Children.Add(_autoSendCheck);
        autoRow.Children.Add(_autoSendDelaySpinner);
        autoRow.Children.Add(new TextBlock { Text = "ms", VerticalAlignment = VerticalAlignment.Center });
        stack.Children.Add(autoRow);

        return stack;
    }

    private Control BuildSenderRight()
    {
        var grid = new Grid { RowDefinitions = new RowDefinitions("2*,Auto,1*") };

        var diagTable = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*"), Margin = new Thickness(2) };
        (string label, TextBlock value)[] diagRows =
        [
            ("Last Rx note:",    _diagLastRxLabel     = DiagValueLabel()),
            ("Entered:",         _diagEnteredLabel     = DiagValueLabel()),
            ("Favorite:",        _diagFavoriteLabel    = DiagValueLabel()),
            ("device display:",     _diagDeviceDisplayLabel  = DiagValueLabel()),
            ("MIDI preset:",     _diagMidiPresetLabel  = DiagValueLabel()),
            ("Bank Select CC#0:",_diagBankLabel        = DiagValueLabel()),
            ("Program Change:",  _diagPcLabel          = DiagValueLabel()),
            ("Scene:",           _diagSceneLabel       = DiagValueLabel()),
            ("MIDI channel:",    _diagChannelLabel     = DiagValueLabel()),
        ];
        for (int r = 0; r < diagRows.Length; r++)
        {
            diagTable.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var caption = new TextBlock { Text = diagRows[r].label, Foreground = Brushes.Gray, Margin = new Thickness(0, 3, 8, 3) };
            Grid.SetRow(caption, r);
            Grid.SetRow(diagRows[r].value, r);
            Grid.SetColumn(diagRows[r].value, 1);
            diagTable.Children.Add(caption);
            diagTable.Children.Add(diagRows[r].value);
        }
        var testBtn = new Button { Content = "Test Translation (No MIDI)", Margin = new Thickness(0, 8, 0, 0) };
        testBtn.Click += OnTestTranslation;
        Grid.SetRow(testBtn, diagRows.Length);
        Grid.SetColumnSpan(testBtn, 2);
        diagTable.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        diagTable.Children.Add(testBtn);

        var diagCard = Card("Diagnostics", new ScrollViewer { Content = diagTable });
        Grid.SetRow(diagCard, 0);
        grid.Children.Add(diagCard);

        var splitter = new GridSplitter { Height = 6, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent };
        Grid.SetRow(splitter, 1);
        grid.Children.Add(splitter);

        _logTextBox = CreateSelectableLog("Consolas", 11);
        var clearBtn = new Button { Name = "ClearMidiLog", Content = "Clear Log", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
        clearBtn.Click += (_, _) => ClearLog();
        var logStack = new DockPanel();
        DockPanel.SetDock(clearBtn, Dock.Bottom);
        logStack.Children.Add(clearBtn);
        logStack.Children.Add(_logTextBox);
        var logCard = Card("MIDI Log", logStack);
        Grid.SetRow(logCard, 2);
        grid.Children.Add(logCard);

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

    // ── Config tab ────────────────────────────────────────────────
    private void BuildConfigTab(TabItem tab)
    {
        var stack = new StackPanel { Margin = new Thickness(16), MaxWidth = 520 };

        // MIDI Connection card
        var midiStack = new StackPanel { Spacing = 8 };

        var inRow = new Grid { ColumnDefinitions = new ColumnDefinitions("100,*,32") };
        _inputPortCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        Grid.SetColumn(_inputPortCombo, 1);
        var refreshBtn = new Button { Content = "↺" };
        refreshBtn.Click += (_, _) => RefreshPortLists();
        Grid.SetColumn(refreshBtn, 2);
        inRow.Children.Add(new TextBlock { Text = "MIDI In:", VerticalAlignment = VerticalAlignment.Center });
        inRow.Children.Add(_inputPortCombo);
        inRow.Children.Add(refreshBtn);
        midiStack.Children.Add(inRow);

        var outRow = new Grid { ColumnDefinitions = new ColumnDefinitions("100,*") };
        _outputPortCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        Grid.SetColumn(_outputPortCombo, 1);
        outRow.Children.Add(new TextBlock { Text = "MIDI Out:", VerticalAlignment = VerticalAlignment.Center });
        outRow.Children.Add(_outputPortCombo);
        midiStack.Children.Add(outRow);

        var thruRow = new Grid { ColumnDefinitions = new ColumnDefinitions("100,*") };
        _thruInputPanel = new StackPanel();
        Grid.SetColumn(_thruInputPanel, 1);
        thruRow.Children.Add(new TextBlock { Text = "Thru In(s):" });
        thruRow.Children.Add(_thruInputPanel);
        midiStack.Children.Add(thruRow);

        var connectRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _connectButton = new Button { Content = "Connect", Background = Brushes.DodgerBlue, Foreground = Brushes.White };
        _connectButton.Click += (_, _) => Connect();
        _disconnectButton = new Button { Content = "Disconnect", IsEnabled = false };
        _disconnectButton.Click += (_, _) => Disconnect();
        connectRow.Children.Add(_connectButton);
        connectRow.Children.Add(_disconnectButton);
        midiStack.Children.Add(connectRow);

        _debugCheck = new CheckBox { Content = "Debug mode (bypass channel filter)", Foreground = Brushes.Orange };
        _debugCheck.IsCheckedChanged += (_, _) => _settings.DebugMode = _debugCheck.IsChecked == true;
        midiStack.Children.Add(_debugCheck);

        _statusLabel = new TextBlock { Text = "○ Not connected", Foreground = Brushes.Gray, FontWeight = FontWeight.Bold };
        midiStack.Children.Add(_statusLabel);

        stack.Children.Add(Card("MIDI Connection", midiStack));

        // Preset Mapping card
        var mapStack = new StackPanel { Spacing = 8 };

        var chRow = new Grid { ColumnDefinitions = new ColumnDefinitions("120,*") };
        _channelCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Left, Width = 90 };
        _channelCombo.Items.Add("Omni");
        for (int ch = 1; ch <= 16; ch++)
        {
            _channelCombo.Items.Add(ch.ToString());
        }

        _channelCombo.SelectedIndex = 0;
        _channelCombo.SelectionChanged += (_, _) =>
        {
            if (_channelCombo.SelectedIndex >= 0)
            {
                _settings.MidiChannel = _channelCombo.SelectedIndex;
            }
        };
        Grid.SetColumn(_channelCombo, 1);
        chRow.Children.Add(new TextBlock { Text = "Channel:", VerticalAlignment = VerticalAlignment.Center });
        chRow.Children.Add(_channelCombo);
        mapStack.Children.Add(chRow);

        var offRow = new Grid { ColumnDefinitions = new ColumnDefinitions("120,Auto,*") };
        _offsetCombo = new ComboBox { Width = 70 };
        _offsetCombo.Items.Add("0");
        _offsetCombo.Items.Add("1");
        _offsetCombo.SelectedIndex = 0;
        _offsetCombo.SelectionChanged += OnOffsetChanged;
        Grid.SetColumn(_offsetCombo, 1);
        var offNote = new TextBlock { Text = "PC Mapping must be OFF on device", Foreground = Brushes.Gray, FontSize = 10, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(offNote, 2);
        offRow.Children.Add(new TextBlock { Text = "Display Offset:", VerticalAlignment = VerticalAlignment.Center });
        offRow.Children.Add(_offsetCombo);
        offRow.Children.Add(offNote);
        mapStack.Children.Add(offRow);

        var maxRow = new Grid { ColumnDefinitions = new ColumnDefinitions("120,*") };
        _maxPresetSpinner = new NumericUpDown { Minimum = 1, Maximum = 512, Value = 511, Width = 100, FormatString = "0" };
        _maxPresetSpinner.ValueChanged += (_, _) => _settings.MaxDisplayedPreset = (int)(_maxPresetSpinner.Value ?? 511);
        Grid.SetColumn(_maxPresetSpinner, 1);
        maxRow.Children.Add(new TextBlock { Text = "Max Preset:", VerticalAlignment = VerticalAlignment.Center });
        maxRow.Children.Add(_maxPresetSpinner);
        mapStack.Children.Add(maxRow);

        var sceneRow = new Grid { ColumnDefinitions = new ColumnDefinitions("120,*") };
        _sceneCcSpinner = new NumericUpDown { Minimum = 0, Maximum = 127, Value = 34, Width = 100, FormatString = "0" };
        _sceneCcSpinner.ValueChanged += (_, _) => _settings.SceneCc = (int)(_sceneCcSpinner.Value ?? 34);
        Grid.SetColumn(_sceneCcSpinner, 1);
        sceneRow.Children.Add(new TextBlock { Text = "Scene CC#:", VerticalAlignment = VerticalAlignment.Center });
        sceneRow.Children.Add(_sceneCcSpinner);
        mapStack.Children.Add(sceneRow);

        stack.Children.Add(Card("Preset Mapping", mapStack));

        // Appearance card
        var themeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        _darkThemeRadio = new RadioButton { Content = "Dark", GroupName = "Theme", IsChecked = true };
        _lightThemeRadio = new RadioButton { Content = "Light", GroupName = "Theme" };
        _darkThemeRadio.IsCheckedChanged += (_, _) => { if (_darkThemeRadio.IsChecked == true) { _settings.Theme = "Dark"; SetTheme("Dark"); } };
        _lightThemeRadio.IsCheckedChanged += (_, _) => { if (_lightThemeRadio.IsChecked == true) { _settings.Theme = "Light"; SetTheme("Light"); } };
        themeRow.Children.Add(_darkThemeRadio);
        themeRow.Children.Add(_lightThemeRadio);
        stack.Children.Add(Card("Appearance", themeRow));

        tab.Content = new ScrollViewer { Content = stack };
    }

    // ── Favorites tab (category tree + list, inline editor) ──────────
    private void BuildFavoritesTab(TabItem tab)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("170,4,*,4,280"),
            Margin = new Thickness(12),
        };

        _favCategoryTree = new ListBox();
        _favCategoryTree.SelectionChanged += OnFavCategorySelectionChanged;
        var treeCard = Card("Collections", _favCategoryTree);
        grid.Children.Add(treeCard);

        var splitter1 = new GridSplitter { Width = 4, Background = Brushes.Transparent };
        Grid.SetColumn(splitter1, 1);
        grid.Children.Add(splitter1);

        var listPane = BuildFavoritesListPane();
        Grid.SetColumn(listPane, 2);
        grid.Children.Add(listPane);

        var splitter2 = new GridSplitter { Width = 4, Background = Brushes.Transparent };
        Grid.SetColumn(splitter2, 3);
        grid.Children.Add(splitter2);

        var editorPane = BuildFavoritesEditorPane();
        Grid.SetColumn(editorPane, 4);
        grid.Children.Add(editorPane);

        tab.Content = grid;
    }

    private Control BuildFavoritesListPane()
    {
        var stack = new StackPanel { Spacing = 8 };

        var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,70") };
        _favSearchBox = new TextBox { Watermark = "Search name / collection / tag..." };
        _favSearchBox.TextChanged += OnFavFilterChanged;
        _favNewBtn = new Button { Content = "+ New", HorizontalAlignment = HorizontalAlignment.Stretch };
        _favNewBtn.Click += OnFavNew;
        Grid.SetColumn(_favNewBtn, 1);
        toolbar.Children.Add(_favSearchBox);
        toolbar.Children.Add(_favNewBtn);
        stack.Children.Add(toolbar);

        var header = BuildFavColumnGrid(FavColumns.Select((c, i) =>
        {
            var btn = new Button { Content = c.Header, HorizontalContentAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent };
            int col = i;
            btn.Click += (_, _) => OnFavColumnClick(col);
            return (Control)btn;
        }).ToArray());
        stack.Children.Add(header);

        _favListBox = new ListBox { Height = 320 };
        _favListBox.SelectionChanged += OnFavListSelectionChanged;
        _favListBox.DoubleTapped += OnFavListDoubleClick;
        stack.Children.Add(_favListBox);

        _favSendBtn = new Button { Content = "Send", Background = Brushes.DodgerBlue, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left };
        _favSendBtn.Click += OnFavSend;
        stack.Children.Add(_favSendBtn);

        return Card("Favorites", stack);
    }

    private static Grid BuildFavColumnGrid(Control[] cells)
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

    private static string GridLengthToString(GridLength length) =>
        length.IsStar ? "*" : length.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private Control BuildFavoritesEditorPane()
    {
        var container = new Panel();

        _favEditorPlaceholder = new TextBlock
        {
            Text = "Select a favorite to edit,\nor click \"+ New\" to create one.",
            Foreground = Brushes.Gray,
            TextWrapping = TextWrapping.Wrap,
        };
        container.Children.Add(_favEditorPlaceholder);

        var editorStack = new StackPanel { Spacing = 10 };
        _favEditorTitle = new TextBlock { Text = "New Favorite", FontWeight = FontWeight.Bold, FontSize = 14 };
        editorStack.Children.Add(_favEditorTitle);

        _favNameBox = new TextBox();
        _favCategoryBox = new AutoCompleteBox { FilterMode = AutoCompleteFilterMode.Contains, HorizontalAlignment = HorizontalAlignment.Stretch };
        _favTagsBox = new TextBox { Watermark = "comma, separated, tags" };
        _favSlotSpinner = new NumericUpDown { Minimum = 1, Maximum = 999, FormatString = "0" };
        _favPresetSpinner = new NumericUpDown { Minimum = 0, Maximum = 512, FormatString = "0" };
        _favSceneSpinner = new NumericUpDown { Minimum = 1, Maximum = 8, FormatString = "0" };

        editorStack.Children.Add(EditorField("Name", _favNameBox));
        editorStack.Children.Add(EditorField("Collection", _favCategoryBox));
        editorStack.Children.Add(EditorField("Tags", _favTagsBox));
        editorStack.Children.Add(EditorField("Slot #", _favSlotSpinner));
        editorStack.Children.Add(EditorField("Preset", _favPresetSpinner));
        editorStack.Children.Add(EditorField("Scene", _favSceneSpinner));

        var buttonsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _favSaveBtn = new Button { Content = "Save", Background = Brushes.DodgerBlue, Foreground = Brushes.White };
        _favSaveBtn.Click += OnFavSave;
        _favClearSlotBtn = new Button { Content = "Clear Slot" };
        _favClearSlotBtn.Click += OnFavClearSlot;
        _favRemoveSlotBtn = new Button { Content = "Remove Slot…", Background = Brushes.IndianRed, Foreground = Brushes.White };
        _favRemoveSlotBtn.Click += OnFavRemoveSlot;
        _favCancelBtn = new Button { Content = "Cancel" };
        _favCancelBtn.Click += OnFavCancel;
        buttonsRow.Children.Add(_favSaveBtn);
        buttonsRow.Children.Add(_favClearSlotBtn);
        buttonsRow.Children.Add(_favRemoveSlotBtn);
        buttonsRow.Children.Add(_favCancelBtn);
        editorStack.Children.Add(buttonsRow);

        _favEditorPanel = new Panel { IsVisible = false, Children = { editorStack } };
        container.Children.Add(_favEditorPanel);

        return Card("Favorite", container);
    }

    private static StackPanel EditorField(string label, Control input)
    {
        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(new TextBlock { Text = label, Foreground = Brushes.Gray, FontSize = 11 });
        stack.Children.Add(input);
        return stack;
    }

    // ── Shared helpers ────────────────────────────────────────────
    private static Border Card(string title, Control content)
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.Bold, FontSize = 14 });
        panel.Children.Add(content);
        return new Border
        {
            Background = new SolidColorBrush(Colors.Gray, 0.08),
            BorderBrush = new SolidColorBrush(Colors.Gray, 0.25),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 12),
            Child = panel,
        };
    }

    private static Button KeypadButton(string text, Action onClick, IBrush? backColor = null)
    {
        var btn = new Button
        {
            Content = text,
            Margin = new Thickness(3),
            FontWeight = FontWeight.Bold,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        if (backColor != null)
        {
            btn.Background = backColor;
            btn.Foreground = Brushes.White;
        }
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private static TextBlock DiagValueLabel() => new()
    {
        Text = "---",
        FontFamily = new FontFamily("Consolas"),
        FontWeight = FontWeight.Bold,
        VerticalAlignment = VerticalAlignment.Center,
    };
}
