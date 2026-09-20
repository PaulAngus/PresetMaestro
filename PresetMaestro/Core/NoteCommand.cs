namespace PresetMaestro.Core;

public enum NoteCommand
{
    None = -1,
    Digit0 = 0,
    Digit1 = 1,
    Digit2 = 2,
    Digit3 = 3,
    Digit4 = 4,
    Digit5 = 5,
    Digit6 = 6,
    Digit7 = 7,
    Digit8 = 8,
    Digit9 = 9,
    Send = 10,
    Clear = 11,
    Next = 12,
    Prev = 13,
    Last = 14,
}

public static class NoteCommandHelper
{
    public static bool IsDigit(this NoteCommand cmd) =>
        cmd >= NoteCommand.Digit0 && cmd <= NoteCommand.Digit9;

    public static int ToDigit(this NoteCommand cmd) => (int)cmd;

    public static NoteCommand Parse(string value) => value.Trim().ToUpperInvariant() switch
    {
        "0" => NoteCommand.Digit0,
        "1" => NoteCommand.Digit1,
        "2" => NoteCommand.Digit2,
        "3" => NoteCommand.Digit3,
        "4" => NoteCommand.Digit4,
        "5" => NoteCommand.Digit5,
        "6" => NoteCommand.Digit6,
        "7" => NoteCommand.Digit7,
        "8" => NoteCommand.Digit8,
        "9" => NoteCommand.Digit9,
        "SEND" => NoteCommand.Send,
        "CLEAR" => NoteCommand.Clear,
        "NEXT" => NoteCommand.Next,
        "PREV" => NoteCommand.Prev,
        "LAST" => NoteCommand.Last,
        _ => NoteCommand.None,
    };
}
