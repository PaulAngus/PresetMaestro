using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace PresetMaestro;

internal sealed class DeviceGoToButton : Button
{
    protected override Type StyleKeyOverride => typeof(Button);
    private readonly Func<int?> _selection;
    private readonly Func<int, bool>? _goTo;
    private readonly Func<string?> _unavailable;
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMilliseconds(400) };

    public DeviceGoToButton(string text, string name, Func<int?> selection, Func<int, bool>? goTo, Func<string?>? unavailable = null)
    {
        Content = text; Name = name; MinHeight = 36; FontSize = 14; Padding = new Thickness(12, 6);
        _selection = selection; _goTo = goTo;
        _unavailable = unavailable ?? (() => goTo is null ? "Connect a Fractal device to go to this selection." : null);
        Click += (_, _) => Go();
        _refresh.Tick += (_, _) => Refresh();
        AttachedToVisualTree += (_, _) => { Refresh(); _refresh.Start(); };
        DetachedFromVisualTree += (_, _) => _refresh.Stop();
    }

    public void Refresh()
    {
        string? reason = _unavailable();
        IsEnabled = _goTo is not null && _selection() is not null && reason is null;
        ToolTip.SetTip(this, reason ?? "Load this selection on the connected Fractal device.");
    }

    public bool Go()
    {
        Refresh();
        return IsEnabled && _selection() is int selected && _goTo!(selected);
    }
}
