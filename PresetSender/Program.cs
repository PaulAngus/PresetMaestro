using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace PresetSender;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
#if MAESTRO_WINDOWS
        if (args is ["--virtual-midi-host"])
        {
            // MIDI initialization runs in an MTA, without starting Avalonia.
            Task.Run(VirtualMidiHost.Run).GetAwaiter().GetResult();
            return;
        }
#endif
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<SenderApp>();
#if MAESTRO_WINDOWS
        builder.UseWin32();
#else
        builder.UseAvaloniaNative();
#endif
        return builder.UseSkia().LogToTrace();
    }
}

public sealed class SenderApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        // Retain PresetMaestro's light/dark palette and control shape with Fluent interaction states.
        Resources.ThemeDictionaries[ThemeVariant.Light] = Palette("#E9EDF0", "#FFFFFF", "#F4F6F8", "#CDD5DC", "#17232D");
        Resources.ThemeDictionaries[ThemeVariant.Dark] = Palette("#11151B", "#181D24", "#11161D", "#303844", "#F2F5F8");
        Styles.Add(new Style(selector => selector.OfType<Button>())
        {
            Setters =
            {
                new Setter(Button.BackgroundProperty, new DynamicResourceExtension("SenderSurface")),
                new Setter(Button.ForegroundProperty, new DynamicResourceExtension("SenderText")),
                new Setter(Button.BorderBrushProperty, new DynamicResourceExtension("SenderBorder")),
                new Setter(Button.BorderThicknessProperty, new Thickness(1)),
                new Setter(Button.CornerRadiusProperty, new CornerRadius(9))
            }
        });
    }

    private static ResourceDictionary Palette(string background, string surface, string inset, string border, string text) => new()
    {
        ["SenderBackground"] = SolidColorBrush.Parse(background),
        ["SenderSurface"] = SolidColorBrush.Parse(surface),
        ["SenderInset"] = SolidColorBrush.Parse(inset),
        ["SenderBorder"] = SolidColorBrush.Parse(border),
        ["SenderText"] = SolidColorBrush.Parse(text)
    };

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new SenderWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
