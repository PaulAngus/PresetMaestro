using System.Buffers.Binary;
using PresetNameSync.Core;

namespace PresetMaestro.Tests;

public class StoredSceneTests
{
    [Fact]
    public void DumpQueryUsesBigEndianAddressAndReservedByte() =>
        Assert.Equal("F0 00 01 74 12 03 01 18 00 0D F7", SysexProtocol.ToHex(StoredPresetDecoder.BuildQuery(152)));

    [Fact]
    public void CompleteDumpDecodesEightNamesAndPresetWithoutSelectingIt()
    {
        var decoder = new StoredPresetDecoder();
        var frames = Fixture();
        foreach (var frame in frames[..^1])
        {
            Assert.Null(decoder.Accept(frame, 152));
        }

        var result = decoder.Accept(frames[^1], 152);
        Assert.Equal(152, result!.Slot); Assert.Equal("Test preset", result.PresetName);
        Assert.Equal(new[] { "Clean", "Drive", "Lead", "", "Five", "Six", "Seven", "Eight" }, result.Names);
    }

    [Fact]
    public void WrongSlotIsNotAttributedToRequestedPreset()
    {
        var decoder = new StoredPresetDecoder();
        foreach (var frame in Fixture())
        {
            Assert.Null(decoder.Accept(frame, 151));
        }
    }

    [Fact]
    public void MissingChunkAndCorruptRawBodyAreRejected()
    {
        var frames = Fixture(); var decoder = new StoredPresetDecoder();
        foreach (var frame in frames[..^2])
        {
            decoder.Accept(frame);
        }

        Assert.Throws<InvalidDataException>(() => decoder.Accept(frames[^1]));
        frames = Fixture(); frames[1][100] ^= 1;
        frames[1][^2] = SysexProtocol.ComputeChecksum(frames[1].AsSpan(0, 3080));
        decoder = new StoredPresetDecoder();
        foreach (var frame in frames[..^1])
        {
            decoder.Accept(frame);
        }

        Assert.Throws<InvalidDataException>(() => decoder.Accept(frames[^1]));
    }

    // Independent synthetic encoder: balanced 256-symbol tree, fixed eight-bit codes.
    internal static byte[][] Fixture()
    {
        var body = new byte[260];
        string[] names = ["Clean", "Drive", "Lead", "", "Five", "Six", "Seven", "Eight"];
        for (int i = 0; i < 8; i++)
        {
            System.Text.Encoding.ASCII.GetBytes(names[i]).CopyTo(body, 4 + 32 * i);
        }

        var bits = new List<int>();
        void Byte(int n) { for (int i = 7; i >= 0; i--) { bits.Add((n >> i) & 1); } }
        void Tree(int lo, int count)
        {
            bits.Add(count == 1 ? 1 : 0);
            if (count == 1) { Byte(lo); } else { Tree(lo, count / 2); Tree(lo + count / 2, count / 2); }
        }
        Tree(0, 256); foreach (byte b in body)
        {
            Byte(b);
        }

        var packed = new byte[(bits.Count + 7) / 8];
        for (int i = 0; i < bits.Count; i++)
        {
            packed[i / 8] |= (byte)(bits[i] << (7 - i % 8));
        }

        var raw = new byte[16384];
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(2), 0xaa55);
        System.Text.Encoding.ASCII.GetBytes("Test preset").CopyTo(raw, 8);
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(0x48), (ushort)body.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(0x4a), (ushort)packed.Length);
        packed.CopyTo(raw, 0x4c);
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(4), StoredPresetDecoder.ComputeCrc(raw));
        var frames = new List<byte[]> { SysexProtocol.Frame(0x77, [1, 24, 0, 0x40, 0]) };
        int xor = 0;
        for (int chunk = 0; chunk < 8; chunk++)
        {
            var payload = new byte[3074]; payload[1] = (byte)chunk;
            for (int w = 0; w < 1024; w++)
            {
                int word = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(chunk * 2048 + w * 2)); xor ^= word;
                payload[2 + 3 * w] = (byte)(word & 127); payload[3 + 3 * w] = (byte)((word >> 7) & 127); payload[4 + 3 * w] = (byte)(word >> 14);
            }
            frames.Add(SysexProtocol.Frame(0x78, payload));
        }
        frames.Add(SysexProtocol.Frame(0x79, [(byte)(xor & 127), (byte)((xor >> 7) & 127), (byte)(xor >> 14)]));
        return frames.ToArray();
    }
}
