using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace PresetMaestro;

public partial class MainWindow
{
    private Border _profileNotice = null!;
    private TextBlock _profileNoticeTitle = null!;
    private TextBlock _profileNoticeDetail = null!;
    private ProgressBar _profileNoticeProgress = null!;
    private Button _profileNoticeAction = null!;
    private Button _profileNoticeClose = null!;
    private Func<Task>? _profileNoticeCommand;
    private (string Title, string Detail, bool Checking, string? Action, Func<Task>? Command)? _profileNoticeState;
    private IInputElement? _profileNoticeOrigin;

    private Control BuildProfileNotice()
    {
        _profileNoticeTitle = new TextBlock { Name = "ProfileNoticeTitle", FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = TextBrush, TextWrapping = TextWrapping.Wrap };
        _profileNoticeDetail = new TextBlock { Name = "ProfileNoticeDetail", FontSize = 13, Foreground = TextBrush, TextWrapping = TextWrapping.Wrap };
        _profileNoticeProgress = new ProgressBar { Name = "ProfileNoticeProgress", Height = 4, IsIndeterminate = true, IsVisible = false };
        AutomationProperties.SetName(_profileNoticeProgress, "Checking profile against the connected device");
        var text = new StackPanel { Spacing = 4, Children = { _profileNoticeTitle, _profileNoticeDetail, _profileNoticeProgress } };
        var layout = new Grid { ColumnDefinitions = new("*,12,Auto,12,Auto") };
        layout.Children.Add(text);
        _profileNoticeAction = ProfileCommand("ProfileNoticeAction", "Use profile");
        _profileNoticeAction.Height = _profileNoticeAction.MinHeight = 32;
        _profileNoticeAction.VerticalAlignment = VerticalAlignment.Center;
        _profileNoticeAction.Click += async (_, _) => { if (_profileNoticeCommand is not null) { await _profileNoticeCommand(); } };
        Grid.SetColumn(_profileNoticeAction, 2); layout.Children.Add(_profileNoticeAction);
        _profileNoticeClose = ProfileCommand("ProfileNoticeClose", "Dismiss");
        _profileNoticeClose.Height = _profileNoticeClose.MinHeight = 32;
        _profileNoticeClose.VerticalAlignment = VerticalAlignment.Top;
        AutomationProperties.SetName(_profileNoticeClose, "Dismiss profile notification");
        _profileNoticeClose.Click += (_, _) => DismissProfileNotice();
        Grid.SetColumn(_profileNoticeClose, 4); layout.Children.Add(_profileNoticeClose);
        _profileNotice = new Border
        {
            Name = "ProfileNotice",
            Margin = new Thickness(20, 12, 20, 0),
            Padding = new Thickness(12),
            Background = InsetBrush,
            BorderBrush = AccentBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = layout,
            IsVisible = false,
        };
        AutomationProperties.SetLiveSetting(_profileNoticeTitle, AutomationLiveSetting.Polite);
        _profileNotice.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && _profileNoticeClose.IsVisible)
            {
                DismissProfileNotice(); e.Handled = true;
            }
        };
        if (_profileNoticeState is { } state) { ShowProfileNotice(state.Title, state.Detail, state.Checking, state.Action, state.Command); }
        return _profileNotice;
    }

    private void DismissProfileNotice()
    {
        _profileNotice.IsVisible = false;
        _profileNoticeState = null;
        if (_profileNoticeOrigin is Control { IsEffectivelyVisible: true } origin && origin.Focus()) { return; }
        this.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.Name == "NavConfig")?.Focus();
    }

    private static string ProfileFavoriteCount(int count) => $"{count} {(count == 1 ? "favorite" : "favorites")}";

    private void ShowProfileNotice(string title, string detail, bool checking = false, string? action = null, Func<Task>? command = null)
    {
        if (_profileNoticeState is null) { _profileNoticeOrigin = FocusManager?.GetFocusedElement(); }
        bool actionHadFocus = _profileNoticeAction.IsKeyboardFocusWithin;
        _profileNoticeState = (title, detail, checking, action, command);
        _profileNoticeTitle.Text = title;
        _profileNoticeDetail.Text = detail;
        _profileNoticeProgress.IsVisible = checking;
        _profileNoticeAction.Content = action;
        _profileNoticeAction.IsVisible = action is not null;
        _profileNoticeAction.IsEnabled = true;
        _profileNoticeClose.IsVisible = !checking;
        _profileNoticeCommand = command;
        _profileNotice.IsVisible = true;
        AutomationProperties.SetName(_profileNotice, title + ". " + detail);
        if (actionHadFocus && action is null) { _profileNoticeClose.Focus(); }
    }

    private void ShowProfileCheckProgress(string name, string step)
    {
        AutomationProperties.SetName(_profileNoticeProgress, "Checking profile against the connected device");
        _profileStatus.Text = step;
        ShowProfileNotice($"Checking profile '{name}' against the device", step + " Checking saved names for a device match before switching favorites.",
            checking: true, action: "Cancel switch", command: () =>
            {
                _profileValidationCancelled = true;
                _profileValidationCts?.Cancel();
                _profileNoticeAction.IsEnabled = false;
                return Task.CompletedTask;
            });
    }
}
