namespace PresetMaestro.Core;

// The device has 512 presets: 4 banks × 128 (internal 0–511).
// internal = displayed - offset;  bank = internal / 128;  pc = internal % 128
public readonly record struct TranslationResult(
    int DisplayedPreset,
    int MidiPreset,
    int Bank,
    int ProgramChange,
    int MidiChannel);

public static class PresetTranslation
{
    public const int MaxMidiPreset = 511; // Bank 3, PC 127

    public static TranslationResult Translate(int displayedPreset, int offset, int midiChannel)
    {
        int midiPreset = displayedPreset - offset;
        return new TranslationResult(
            displayedPreset,
            midiPreset,
            midiPreset / 128,
            midiPreset % 128,
            midiChannel);
    }

    public static int MinDisplayed(int offset) => offset == 0 ? 0 : 1;

    public static bool IsValid(int displayed, int offset, int max)
    {
        int midiPreset = displayed - offset;
        return displayed >= MinDisplayed(offset)
            && displayed <= max
            && midiPreset >= 0
            && midiPreset <= MaxMidiPreset;
    }
}
