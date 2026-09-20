namespace PresetNameSync.Core;

/// <summary>Reassembles fragmented WinMM callbacks; ignores interleaved MIDI realtime bytes.</summary>
public sealed class SysexAssembler
{
    private readonly List<byte> _buffer = [];
    public List<byte[]> Feed(ReadOnlySpan<byte> bytes)
    {
        var frames = new List<byte[]>();
        foreach (byte b in bytes)
        {
            if (b >= 0xf8)
            {
                continue;
            }

            if (b == 0xf0) { _buffer.Clear(); _buffer.Add(b); }
            else if (_buffer.Count > 0)
            {
                if (b == 0xf7) { _buffer.Add(b); frames.Add(_buffer.ToArray()); _buffer.Clear(); }
                else if (b >= 0x80 || _buffer.Count >= 4096)
                {
                    _buffer.Clear();
                }
                else
                {
                    _buffer.Add(b);
                }
            }
        }
        return frames;
    }
}
