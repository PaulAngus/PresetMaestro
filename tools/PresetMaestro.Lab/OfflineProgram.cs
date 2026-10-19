using ApplicationTestLab;

if (!OperatingSystem.IsWindows()) { throw new PlatformNotSupportedException("This profile requires Windows."); }
LabStage[] stages = [
    new("offline-functional-logic-and-headless-ui", "Functional", "bin/tests/PresetMaestro.LabTests.exe", ["PresetMaestro.Tests", "{result}"]),
    new("native-midi", "Native", UnavailableReason: "Native MIDI is excluded by the offline profile.", IncludeInAll: false),
    new("desktop", "Desktop", UnavailableReason: "Interactive desktop automation is excluded by the offline profile; UI checks are headless.", IncludeInAll: false)
];
return await PortableLab.RunAsync(args, new LabApplication("preset-maestro", "PresetMaestro", stages,
    ["All runs only the audited offline Functional checks. Hardware, MIDI routing and interactive desktop coverage are excluded.",
     "Hardware/compatibility test classes and their physical transport are not compiled. The desktop driver is not packaged.",
     "The packaged application boundary refuses native MIDI access at compile time, regardless of inherited environment settings.",
     "In-memory protocol and injected-interface fixtures create no operating-system or network MIDI endpoints."],
    new Dictionary<string, string?>
    {
        ["PRESET_MAESTRO_HARDWARE"] = null,
        ["PRESET_MAESTRO_COMPATIBILITY"] = null,
        ["PRESET_MAESTRO_ALLOW_NAVIGATION"] = null,
        ["PRESET_MAESTRO_FULL_SCAN"] = null,
        ["PRESET_MAESTRO_MIDI_IN"] = null,
        ["PRESET_MAESTRO_MIDI_OUT"] = null,
        ["PRESET_MAESTRO_DISABLE_MIDI"] = "1",
        ["PRESET_MAESTRO_DATA_DIRECTORY"] = "{output}/profile",
        ["PRESET_MAESTRO_SCREENSHOT_DIR"] = "{output}/screenshots",
        ["MIDI_FORWARDER_UI_SNAPSHOTS"] = "{output}/screenshots",
        ["MBU_MIDI_BACKEND"] = null,
        ["MBU_MIDI_API_DIRECTORY"] = null,
        ["MIDI_FORWARDER_MIDI_API"] = null,
        ["TMPDIR"] = "{output}/profile",
        ["TMP"] = "{output}/profile",
        ["TEMP"] = "{output}/profile"
    }, RunTimeout: TimeSpan.FromMinutes(30)));
