using Avalonia;

namespace PresetMaestro;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
#if MAESTRO_WINDOWS
        if (args.Contains("--dependency-report", StringComparer.Ordinal))
        {
            Environment.ExitCode = MightBeUseful.Midi.Windows.WindowsMidiDependencyReport.Write(Console.Out);
            return;
        }
#endif
        if (args.Contains("--midi-backend", StringComparer.Ordinal))
        {
#if MAESTRO_WINDOWS
            Console.WriteLine(MightBeUseful.Midi.Windows.WindowsMidiBackend.Capabilities.Description);
#else
            Console.WriteLine("CoreMIDI MIDI 1.0 (MightBeUseful.Midi.CoreMidi).");
#endif
            return;
        }
        if (args.Contains("--list-ports", StringComparer.Ordinal))
        {
#if MAESTRO_WINDOWS
            MightBeUseful.Midi.Windows.WindowsMidiBackend.Print(Console.Out);
#else
            MightBeUseful.Midi.CoreMidi.CoreMidiPortCatalog.Print(Console.Out);
#endif
            return;
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

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
