using System.Globalization;

namespace PresetSender;

public sealed record MidiBinding(int Kind, int Channel, int Number, string Command);

public sealed class SenderState
{
    public const int EntryLength = 3;
    public int NumberingOffset { get; set; }
    public int OutputChannel { get; set; } = 1;
    public int InputChannel { get; set; }
    public bool ForwardProgramChanges { get; set; }
    public string Entry { get; private set; } = "";
    public int? LastProgram { get; private set; }
    public List<MidiBinding> Bindings { get; } = [];
    private readonly HashSet<(int Kind, int Channel, int Number)> _held = [];

    public bool Digit(int digit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(digit);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(digit, 9);
        if (Entry.Length >= EntryLength)
        {
            return false;
        }

        Entry += digit.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    public void Clear() => Entry = "";
    public void Backspace()
    {
        if (Entry.Length > 0)
        {
            Entry = Entry[..^1];
        }
    }
    public void ResetInput() => _held.Clear();

    public bool TryGetProgram(out int program)
    {
        program = -1;
        if (!int.TryParse(Entry, NumberStyles.None, CultureInfo.InvariantCulture, out int display))
        {
            return false;
        }

        program = display - NumberingOffset;
        return program is >= 0 and <= 127;
    }

    public void Sent(int program) { LastProgram = program; Clear(); }
    public string Display(int program) => (program + NumberingOffset).ToString("D3", CultureInfo.InvariantCulture);

    public string? Receive(int message, out MidiBinding? pressed)
    {
        pressed = null;
        int kind = message & 0xf0, channel = (message & 15) + 1;
        if (InputChannel != 0 && channel != InputChannel)
        {
            return null;
        }

        int number = (message >> 8) & 127, value = (message >> 16) & 127;
        if (kind == 0xc0)
        {
            pressed = new(kind, channel, number, "");
            return Bindings.FirstOrDefault(binding => binding.Kind == kind && binding.Channel == channel && binding.Number == number)?.Command
                ?? (ForwardProgramChanges ? $"PC:{number}" : null);
        }

        if (kind is not (0x80 or 0x90 or 0xb0))
        {
            return null;
        }

        int normalizedKind = kind == 0x80 ? 0x90 : kind;
        var key = (normalizedKind, channel, number);
        if (kind == 0x80 || value == 0) { _held.Remove(key); return null; }
        if (!_held.Add(key))
        {
            return null;
        }

        pressed = new(normalizedKind, channel, number, "");
        return Bindings.FirstOrDefault(binding => binding.Kind == normalizedKind && binding.Channel == channel && binding.Number == number)?.Command;
    }
}
