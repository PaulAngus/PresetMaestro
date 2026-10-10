using Avalonia;

namespace PresetMaestro;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>();
#if MAESTRO_WINDOWS
        builder.UseWin32();
#elif MAESTRO_MACOS
        builder.UseAvaloniaNative();
#else
#error Select a supported MaestroPlatform.
#endif
        return builder.UseSkia().LogToTrace();
    }
}
