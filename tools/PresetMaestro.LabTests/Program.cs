#if !MAESTRO_OFFLINE_NO_MIDI
if (args.Length == 5 && args[2] == "PhysicalFM9")
{
    if ((!OperatingSystem.IsMacOS() && !OperatingSystem.IsWindows()) || string.IsNullOrWhiteSpace(args[3]) || string.IsNullOrWhiteSpace(args[4]))
    {
        throw new ArgumentException("The physical read-only FM9 stage requires explicit existing input and output names on Windows or macOS.");
    }
    Environment.SetEnvironmentVariable("PRESET_MAESTRO_HARDWARE", "FM9");
    Environment.SetEnvironmentVariable("PRESET_MAESTRO_COMPATIBILITY", null);
    Environment.SetEnvironmentVariable("PRESET_MAESTRO_ALLOW_NAVIGATION", null);
    Environment.SetEnvironmentVariable("PRESET_MAESTRO_FULL_SCAN", null);
    Environment.SetEnvironmentVariable("PRESET_MAESTRO_DISABLE_MIDI", null);
    Environment.SetEnvironmentVariable("PRESET_MAESTRO_MIDI_IN", args[3]);
    Environment.SetEnvironmentVariable("PRESET_MAESTRO_MIDI_OUT", args[4]);
    Environment.SetEnvironmentVariable("PRESET_MAESTRO_HARDWARE_RESULTS", Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1]))!, "hardware"));
    return PortableXunit.Run(args[..2], ["PresetMaestro.Tests"],
        "PresetMaestro.Tests.Fm9HardwareTests.ReadOnlyQueriesRepeatAndRecoverWithoutChangingCurrentPresetOrScene");
}
#else
if (args.Length != 2) { throw new ArgumentException("The offline profile only accepts a test assembly and result path."); }
#endif
#if MAESTRO_OFFLINE_NO_MIDI
return PortableXunit.Run(args, ["PresetMaestro.Tests"], executionTimeout: TimeSpan.FromMinutes(20));
#else
return PortableXunit.Run(args, ["PresetMaestro.Tests"]);
#endif
