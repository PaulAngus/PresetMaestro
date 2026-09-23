using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace PresetMaestro;

/// <summary>A fixed set of action assignments, edited privately until Save mapping succeeds.</summary>
public sealed class MidiMappingWindow : Window
{
    private static readonly string[] Actions = ["1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "SEND", "CLEAR", "NEXT", "PREV", "LAST"];
    private readonly int?[] _notes = new int?[15];
    private readonly Button[] _tiles = new Button[15];
    private readonly TextBlock[] _noteLabels = new TextBlock[15];
    private readonly TextBox _sceneCc;
    private readonly TextBox _note;
    private readonly TextBlock _selectedLabel;
    private readonly TextBlock _message;
    private readonly TextBlock _sceneError;
    private readonly Action<int, Dictionary<int, string>> _save;
    private readonly bool _light;
    private readonly string? _loadError;
    private int _selected;

    public MidiMappingWindow(string profile, int sceneCc, IReadOnlyDictionary<int, string> noteMap,
        Action<int, Dictionary<int, string>> save)
    {
        _save = save;
        _light = Application.Current?.ActualThemeVariant != ThemeVariant.Dark;
        Title = "MIDI Mapping";
        Width = 450;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = ColorBrush("#FFFFFF", "#202833");
        FontFamily = new FontFamily("Segoe UI");
        Foreground = ColorBrush("#1D2B3B", "#E9EEF5");

        foreach (var pair in noteMap)
        {
            int index = Array.IndexOf(Actions, pair.Value?.Trim().ToUpperInvariant());
            if (index < 0 || _notes[index].HasValue)
            {
                _loadError = "The saved map contains extra or unrecognized assignments. Resolve these in settings.json before saving.";
                continue;
            }
            _notes[index] = pair.Key;
        }

        var root = new StackPanel { Margin = new Thickness(15) };
        root.Children.Add(Label("MIDI Mapping", 17, bold: true, bottom: 13));

        var sceneRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 12) };
        var sceneHeading = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        sceneHeading.Children.Add(Label("Scene CC#", 13, bold: true));
        sceneHeading.Children.Add(Label($"{profile} profile", 12, secondary: true));
        sceneRow.Children.Add(sceneHeading);
        _sceneCc = NumberBox("SceneCc", sceneCc.ToString(CultureInfo.InvariantCulture), "Scene Select CC number");
        var sceneInput = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        sceneInput.Children.Add(Label("CC number", 12, bold: true));
        sceneInput.Children.Add(_sceneCc);
        Grid.SetColumn(sceneInput, 1);
        sceneRow.Children.Add(sceneInput);
        root.Children.Add(sceneRow);
        root.Children.Add(Divider());
        _sceneError = Label("", 12);
        _sceneError.Name = "SceneCcError";
        _sceneError.Foreground = ErrorBrush;
        _sceneError.IsVisible = false;
        root.Children.Add(_sceneError);

        var mapHeading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 12, 0, 8) };
        var mapTitle = new StackPanel();
        mapTitle.Children.Add(Label("MIDI Note Map", 13, bold: true));
        mapTitle.Children.Add(Label("Select an action to change its note.", 12, secondary: true));
        mapHeading.Children.Add(mapTitle);
        var scope = Label("Global · 15 fixed actions", 12, secondary: true);
        scope.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(scope, 1);
        mapHeading.Children.Add(scope);
        root.Children.Add(mapHeading);

        var columns = new Grid { Name = "MappingColumns", ColumnDefinitions = new ColumnDefinitions("238,12,156"), HorizontalAlignment = HorizontalAlignment.Left };
        var digitSection = new StackPanel();
        digitSection.Children.Add(Label("PRESET DIGITS", 12, bold: true, secondary: true, bottom: 8));
        var digits = new Grid { Name = "MappingDigits", ColumnDefinitions = new ColumnDefinitions("*,5,*,5,*"), RowDefinitions = new RowDefinitions("Auto,5,Auto,5,Auto,5,Auto") };
        var commandSection = new StackPanel();
        commandSection.Children.Add(Label("COMMANDS", 12, bold: true, secondary: true, bottom: 8));
        var commands = new StackPanel { Name = "MappingCommands", Spacing = 5 };
        for (int i = 0; i < Actions.Length; i++)
        {
            int index = i;
            bool command = i >= 10;
            var actionLabel = Label(Actions[i], 13, bold: true);
            _noteLabels[i] = Label("", 12, secondary: true);
            Control content;
            if (command)
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                row.Children.Add(actionLabel);
                Grid.SetColumn(_noteLabels[i], 1);
                row.Children.Add(_noteLabels[i]);
                content = row;
            }
            else
            {
                actionLabel.HorizontalAlignment = HorizontalAlignment.Center;
                _noteLabels[i].HorizontalAlignment = HorizontalAlignment.Center;
                content = new StackPanel { Spacing = 1, Children = { actionLabel, _noteLabels[i] } };
            }
            var button = new Button
            {
                Name = $"MapAction{Actions[i]}",
                Content = content,
                Height = command ? 33 : 43,
                MinHeight = 0,
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(command ? 8 : 6, 4),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            button.Click += (_, _) => SelectAction(index);
            _tiles[i] = button;
            if (command) { commands.Children.Add(button); }
            else { Grid.SetRow(button, i / 3 * 2); Grid.SetColumn(button, i % 3 * 2); digits.Children.Add(button); }
        }
        digitSection.Children.Add(digits);
        commandSection.Children.Add(commands);
        columns.Children.Add(digitSection);
        Grid.SetColumn(commandSection, 2);
        columns.Children.Add(commandSection);
        root.Children.Add(columns);
        var divider = Divider();
        divider.Margin = new Thickness(0, 12, 0, 10);
        root.Children.Add(divider);

        var editor = new StackPanel { Name = "MappingNoteEditor", Orientation = Orientation.Horizontal, Spacing = 9 };
        _selectedLabel = Label("1 · MIDI note", 12, bold: true);
        _selectedLabel.Name = "MappingSelectedAction";
        _note = NumberBox("MappingNoteNumber", _notes[0]?.ToString(CultureInfo.InvariantCulture) ?? "", "Incoming MIDI note for 1");
        _note.LostFocus += (_, _) => ApplyNote();
        editor.Children.Add(_selectedLabel);
        editor.Children.Add(_note);
        editor.Children.Add(Label("0–127", 12, secondary: true));
        root.Children.Add(editor);
        _message = Label(_loadError ?? "", 12);
        _message.Name = "MappingMessage";
        _message.MinHeight = 18;
        _message.Margin = new Thickness(0, 6, 0, 0);
        _message.Foreground = ErrorBrush;
        root.Children.Add(_message);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 11, 0, 0) };
        var cancel = FooterButton("CancelMapping", "Cancel");
        cancel.Click += (_, _) => Close();
        var saveButton = FooterButton("SaveMapping", "Save mapping");
        saveButton.Background = ColorBrush("#1D67A9", "#458ED0");
        saveButton.BorderBrush = saveButton.Background;
        saveButton.Foreground = Brushes.White;
        saveButton.Click += (_, _) => Save();
        footer.Children.Add(cancel);
        footer.Children.Add(saveButton);
        root.Children.Add(footer);
        Content = root;
        RefreshTiles();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            else if (e.Key == Key.Enter) { Save(); e.Handled = true; }
        };
    }

    private IBrush ColorBrush(string light, string dark) => new SolidColorBrush(Color.Parse(_light ? light : dark));
    private IBrush ErrorBrush => ColorBrush("#9F2E2E", "#FFA2A2");
    private Border Divider() => new() { Height = 1, Background = ColorBrush("#E0E8F0", "#3B4655") };
    private TextBlock Label(string text, double size, bool bold = false, bool secondary = false, double bottom = 0) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
        Foreground = secondary ? ColorBrush("#596B81", "#B2C2D2") : Foreground,
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, bottom),
    };
    private TextBox NumberBox(string name, string value, string accessibleName)
    {
        var input = new TextBox
        {
            Name = name,
            Text = value,
            Width = 68,
            Height = 31,
            MinHeight = 0,
            FontSize = 14,
            Padding = new Thickness(7, 4),
            CornerRadius = new CornerRadius(4),
            Background = ColorBrush("#FFFFFF", "#2B3543"),
            Foreground = Foreground,
            BorderBrush = ColorBrush("#B9C9DA", "#65758A"),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(input, accessibleName);
        return input;
    }
    private Button FooterButton(string name, string caption) => new()
    {
        Name = name,
        Content = caption,
        FontSize = 12,
        MinHeight = 0,
        Height = 32,
        Padding = new Thickness(10, 5),
        CornerRadius = new CornerRadius(4),
        Background = ColorBrush("#FFFFFF", "#303D4C"),
        Foreground = Foreground,
        BorderBrush = ColorBrush("#BBCADB", "#617289"),
    };

    private static bool TryNumber(string? text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value is >= 0 and <= 127;

    private void SelectAction(int index)
    {
        if (index == _selected) { _note.Focus(); _note.SelectAll(); return; }
        // Keep invalid text attached to its action so switching tiles cannot silently lose an edit.
        if (!ApplyNote()) { _note.Focus(); return; }
        _selected = index;
        _selectedLabel.Text = $"{Actions[index]} · MIDI note";
        _note.Text = _notes[index]?.ToString(CultureInfo.InvariantCulture) ?? "";
        AutomationProperties.SetName(_note, $"Incoming MIDI note for {Actions[index]}");
        _message.Text = _loadError ?? "";
        RefreshTiles();
        _note.Focus();
        _note.SelectAll();
    }

    private bool ApplyNote()
    {
        if (!TryNumber(_note.Text, out int note)) { return Error("Enter a whole MIDI note number from 0 to 127."); }
        int duplicate = Array.FindIndex(_notes, value => value == note);
        if (duplicate >= 0 && duplicate != _selected) { return Error($"Note {note} is already assigned to {Actions[duplicate]}."); }
        bool changed = _notes[_selected] != note;
        _notes[_selected] = note;
        RefreshTiles();
        _message.Text = _loadError ?? (changed ? $"Note {note} assigned to {Actions[_selected]}." : "");
        _message.Foreground = _loadError is null ? ColorBrush("#226C45", "#90D9AE") : ErrorBrush;
        return true;
    }

    private bool Error(string message) { _message.Text = message; _message.Foreground = ErrorBrush; return false; }

    private void RefreshTiles()
    {
        for (int i = 0; i < Actions.Length; i++)
        {
            bool selected = i == _selected;
            _noteLabels[i].Text = _notes[i].HasValue ? $"Note {_notes[i]}" : "Unassigned";
            _tiles[i].Background = selected ? ColorBrush("#E9F3FD", "#24405D") : ColorBrush("#F8FAFC", "#2B3543");
            _tiles[i].BorderBrush = selected ? ColorBrush("#2671B4", "#6CB0EB") : ColorBrush("#CBD6E3", "#526073");
            _tiles[i].BorderThickness = new Thickness(selected ? 2 : 1);
            _tiles[i].Padding = new Thickness((i >= 10 ? 8 : 6) - (selected ? 1 : 0), selected ? 3 : 4);
            AutomationProperties.SetName(_tiles[i], $"{Actions[i]}, {_noteLabels[i].Text}. Edit note{(selected ? ", selected" : "")}");
        }
    }

    private void Save()
    {
        bool noteValid = ApplyNote();
        bool sceneValid = TryNumber(_sceneCc.Text, out int sceneCc);
        _sceneError.Text = "Scene CC# must be a whole number from 0 to 127.";
        _sceneError.IsVisible = !sceneValid;
        if (!noteValid || !sceneValid) { return; }
        if (_loadError is not null) { Error(_loadError); return; }
        if (_notes.Any(note => note is null or < 0 or > 127) || _notes.Distinct().Count() != Actions.Length)
        { Error("Assign a unique note from 0 to 127 to each of the 15 actions."); return; }
        var map = Actions.Select((action, i) => (Action: action, Note: _notes[i]!.Value))
            .ToDictionary(pair => pair.Note, pair => pair.Action);
        try { _save(sceneCc, map); }
        catch (Exception ex) { Error($"Could not save mapping: {ex.Message}"); return; }
        Close();
    }
}
