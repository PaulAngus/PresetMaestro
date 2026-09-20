using PresetMaestro.Core;

namespace PresetMaestro.Tests;

public class PresetTranslationTests
{
    // ── Translate: MIDI preset, bank, and PC ───────────────────────
    [Theory]
    // Offset 0 — spec examples
    [InlineData(123, 0, 123, 0, 123)]
    [InlineData(383, 0, 383, 2, 127)]
    // Offset 0 — bank boundaries
    [InlineData(0, 0, 0, 0, 0)]
    [InlineData(127, 0, 127, 0, 127)]
    [InlineData(128, 0, 128, 1, 0)]
    [InlineData(255, 0, 255, 1, 127)]
    [InlineData(256, 0, 256, 2, 0)]
    [InlineData(384, 0, 384, 3, 0)]
    [InlineData(511, 0, 511, 3, 127)]
    // Offset 1 — spec examples
    [InlineData(123, 1, 122, 0, 122)]
    [InlineData(384, 1, 383, 2, 127)]
    // Offset 1 — bank boundaries
    [InlineData(1, 1, 0, 0, 0)]
    [InlineData(128, 1, 127, 0, 127)]
    [InlineData(129, 1, 128, 1, 0)]
    [InlineData(256, 1, 255, 1, 127)]
    [InlineData(257, 1, 256, 2, 0)]
    [InlineData(385, 1, 384, 3, 0)]
    [InlineData(512, 1, 511, 3, 127)]
    public void Translate_ReturnsExpectedBankAndPC(
        int displayed, int offset, int expectedMidiPreset, int expectedBank, int expectedPc)
    {
        var r = PresetTranslation.Translate(displayed, offset, midiChannel: 1);

        Assert.Equal(expectedMidiPreset, r.MidiPreset);
        Assert.Equal(expectedBank, r.Bank);
        Assert.Equal(expectedPc, r.ProgramChange);
        Assert.Equal(displayed, r.DisplayedPreset);
        Assert.Equal(1, r.MidiChannel);
    }

    // ── IsValid ────────────────────────────────────────────────────
    [Theory]
    [InlineData(0, 0, 511, true)]   // min offset-0
    [InlineData(511, 0, 511, true)]   // max offset-0
    [InlineData(512, 0, 511, false)]  // over max
    [InlineData(1, 1, 512, true)]   // min offset-1
    [InlineData(512, 1, 512, true)]   // max offset-1
    [InlineData(513, 1, 512, false)]  // over max
    [InlineData(0, 1, 512, false)]  // below min offset-1
    [InlineData(100, 0, 50, false)]  // over configured max
    public void IsValid_ReturnsExpected(int displayed, int offset, int max, bool expected)
    {
        Assert.Equal(expected, PresetTranslation.IsValid(displayed, offset, max));
    }

    // ── MinDisplayed ──────────────────────────────────────────────
    [Fact] public void MinDisplayed_Offset0_Is0() => Assert.Equal(0, PresetTranslation.MinDisplayed(0));
    [Fact] public void MinDisplayed_Offset1_Is1() => Assert.Equal(1, PresetTranslation.MinDisplayed(1));

    // ── MaxMidiPreset constant ─────────────────────────────────────
    [Fact]
    public void MaxMidiPreset_Is511_FourBanks()
    {
        Assert.Equal(511, PresetTranslation.MaxMidiPreset);
        // 511 / 128 = Bank 3, 511 % 128 = 127
        var r = PresetTranslation.Translate(511, 0, 1);
        Assert.Equal(3, r.Bank);
        Assert.Equal(127, r.ProgramChange);
    }
}
