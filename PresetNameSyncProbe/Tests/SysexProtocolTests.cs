using PresetNameSync.Core;

namespace PresetNameSyncProbe.Tests;

public sealed class SysexProtocolTests
{
    [Fact]
    public void ComputeChecksum_ForPresetNameQuerySlot0_IsExpectedValue()
    {
        byte[] payload = [0xF0, 0x00, 0x01, 0x74, 0x12, 0x0D, 0x00, 0x00];

        byte checksum = SysexProtocol.ComputeChecksum(payload);

        Assert.Equal(0x1A, checksum);
    }

    [Theory]
    [InlineData(0, "F0 00 01 74 12 0D 00 00 1A F7")]
    [InlineData(127, "F0 00 01 74 12 0D 7F 00 65 F7")]
    [InlineData(128, "F0 00 01 74 12 0D 00 01 1B F7")]
    [InlineData(511, "F0 00 01 74 12 0D 7F 03 66 F7")]
    public void BuildPresetNameQuery_ProducesExpectedFrames(int slot, string expectedHex)
    {
        byte[] frame = SysexProtocol.BuildPresetNameQuery(slot);

        Assert.Equal(expectedHex, SysexProtocol.ToHex(frame));
    }

    [Fact]
    public void TryParsePresetNameResponse_ValidFrame_ReturnsSlotAndName()
    {
        byte[] frame = BuildValidResponse(128, "MY PRESET");

        bool ok = SysexProtocol.TryParsePresetNameResponse(frame, out PresetNameResult? result, out string error);

        Assert.True(ok, error);
        Assert.NotNull(result);
        Assert.Equal(128, result!.Slot);
        Assert.Equal("MY PRESET", result.PresetName);
    }

    [Fact]
    public void TryParsePresetNameResponse_BadHeader_IsRejected()
    {
        byte[] frame = BuildValidResponse(1, "A");
        frame[3] = 0x75;

        bool ok = SysexProtocol.TryParsePresetNameResponse(frame, out _, out string error);

        Assert.False(ok);
        Assert.Contains("manufacturer", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryParsePresetNameResponse_BadOpcode_IsRejected()
    {
        byte[] frame = BuildValidResponse(1, "A");
        frame[5] = 0x0E;

        bool ok = SysexProtocol.TryParsePresetNameResponse(frame, out _, out string error);

        Assert.False(ok);
        Assert.Contains("opcode", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryParsePresetNameResponse_MalformedFrame_IsRejected()
    {
        byte[] frame = BuildValidResponse(1, "A");
        frame[0] = 0x00;

        bool ok = SysexProtocol.TryParsePresetNameResponse(frame, out _, out string error);

        Assert.False(ok);
        Assert.Contains("framing", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryParsePresetNameResponse_BadChecksum_IsRejected()
    {
        byte[] frame = BuildValidResponse(1, "A");
        frame[^2] ^= 0x01;

        bool ok = SysexProtocol.TryParsePresetNameResponse(frame, out _, out string error);

        Assert.False(ok);
        Assert.Contains("checksum", error, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] BuildValidResponse(int slot, string name)
    {
        byte nn0 = (byte)(slot & 0x7F);
        byte nn1 = (byte)((slot >> 7) & 0x7F);

        byte[] frame = new byte[42];
        frame[0] = 0xF0;
        frame[1] = 0x00;
        frame[2] = 0x01;
        frame[3] = 0x74;
        frame[4] = 0x12;
        frame[5] = 0x0D;
        frame[6] = nn0;
        frame[7] = nn1;

        byte[] nameBytes = System.Text.Encoding.ASCII.GetBytes(name);
        Array.Fill(frame, (byte)0x20, 8, 32);
        Array.Copy(nameBytes, 0, frame, 8, Math.Min(nameBytes.Length, 32));

        frame[40] = SysexProtocol.ComputeChecksum(frame.AsSpan(0, 40));
        frame[41] = 0xF7;
        return frame;
    }
}
