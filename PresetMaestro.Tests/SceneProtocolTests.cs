using PresetNameSync.Core;

namespace PresetMaestro.Tests;

public class SceneProtocolTests
{
    [Theory]
    [InlineData(0, "F0 00 01 74 12 0E 00 19 F7")]
    [InlineData(7, "F0 00 01 74 12 0E 07 1E F7")]
    public void SceneQueryUsesDeviceAndZeroBasedIndex(int scene, string expected) =>
        Assert.Equal(expected, SysexProtocol.ToHex(SysexProtocol.BuildSceneNameQuery(scene)));

    [Theory]
    [InlineData(-1)]
    [InlineData(8)]
    public void SceneQueryRejectsInvalidIndex(int scene) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SysexProtocol.BuildSceneNameQuery(scene));

    [Fact]
    public void SceneResponseHandlesBlankAndNullPaddedNames()
    {
        var frame = Response(0x0e, [3], "Lead\0ignored");
        Assert.True(SysexProtocol.TryParseSceneNameResponse(frame, out var result));
        Assert.Equal(3, result!.Index);
        Assert.Equal("Lead", result.Name);
        Assert.True(SysexProtocol.TryParseSceneNameResponse(Response(0x0e, [0], "   "), out result));
        Assert.Equal("", result!.Name);
    }

    [Fact]
    public void SceneResponseRejectsMalformedDataEvenWithRecomputedChecksum()
    {
        foreach (int offset in new[] { 0, 3, 4, 5, 6, 7 })
        {
            var frame = Response(0x0e, [0], "Clean");
            frame[offset] = 0xff;
            frame[^2] = SysexProtocol.ComputeChecksum(frame.AsSpan(0, frame.Length - 2));
            Assert.False(SysexProtocol.TryParseSceneNameResponse(frame, out _));
        }
        var bad = Response(0x0e, [0], "Clean"); bad[^2] ^= 1;
        Assert.False(SysexProtocol.TryParseSceneNameResponse(bad, out _));
        Assert.False(SysexProtocol.TryParseSceneNameResponse(bad.AsSpan()[..^1], out _));
    }

    [Fact]
    public void StreamAssemblerHandlesSplitCombinedAndRealtimeBytes()
    {
        var assembler = new SysexAssembler();
        byte[] a = SysexProtocol.BuildSceneNameQuery(0), b = SysexProtocol.BuildSceneNameQuery(7);
        Assert.Empty(assembler.Feed(a.AsSpan()[..4]));
        var frames = assembler.Feed([0xf8, .. a[4..], .. b]);
        Assert.Equal(2, frames.Count);
        Assert.Equal(a, frames[0]); Assert.Equal(b, frames[1]);
        Assert.Empty(assembler.Feed([0xf0, 0, 0x90, 1, 0xf7]));
    }

    internal static byte[] Response(byte opcode, byte[] address, string name)
    {
        var text = new byte[32]; Array.Fill(text, (byte)' ');
        System.Text.Encoding.ASCII.GetBytes(name.AsSpan(), text);
        return SysexProtocol.Frame(opcode, [.. address, .. text]);
    }
}
