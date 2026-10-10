using Avalonia.Media;

namespace PresetMaestro;

internal static class AppFonts
{
#if MAESTRO_MACOS
    // Ask Avalonia/the OS for the normal UI face; never redistribute Windows fonts.
    public static FontFamily Body { get; } = FontFamily.Default;
    public static FontFamily Command { get; } = FontFamily.Default;
    public static FontFamily Display { get; } = FontFamily.Default;
    public static FontFamily Monospace { get; } = new("Menlo");
#else
    public static FontFamily Body { get; } = new("Segoe UI");
    public static FontFamily Command { get; } = new("Segoe UI Variable");
    public static FontFamily Display { get; } = new("Bahnschrift");
    public static FontFamily Monospace { get; } = new("Cascadia Mono");
#endif
}
