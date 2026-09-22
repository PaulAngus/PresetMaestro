namespace PresetMaestro.Core;

// Supported devices use banks of 128 presets (up to 8 banks / internal 0–1023).
// internal = displayed - offset;  bank = internal / 128;  pc = internal % 128
public readonly record struct TranslationResult(
    int DisplayedPreset,
    int MidiPreset,
    int Bank,
    int ProgramChange,
    int MidiChannel);

public static class PresetTranslation
{
    public const int MaxMidiPreset = 1023; // Bank 7, PC 127

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
