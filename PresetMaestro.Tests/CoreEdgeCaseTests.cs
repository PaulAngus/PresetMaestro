using PresetMaestro.Core;
using PresetNameSync.Core;

namespace PresetMaestro.Tests;

public sealed class CoreEdgeCaseTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(512)]
    public void PresetNameQueryRejectsSlotsOutsideDeviceRange(int slot) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SysexProtocol.BuildPresetNameQuery(slot));

    [Theory]
    [InlineData(-1)]
    [InlineData(512)]
    public void StoredPresetQueryRejectsSlotsOutsideDeviceRange(int slot) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => StoredPresetDecoder.BuildQuery(slot));

    [Fact]
    public void CurrentStateQueriesUseReadOnlySentinelAddresses()
    {
        Assert.Equal("F0 00 01 74 12 0D 7F 7F 1A F7", SysexProtocol.ToHex(SysexProtocol.BuildCurrentPresetQuery()));
        Assert.Equal("F0 00 01 74 12 0C 7F 64 F7", SysexProtocol.ToHex(SysexProtocol.BuildCurrentSceneQuery()));
    }

    [Fact]
    public void PresetNameResponseRejectsInvalidModelAddressAndDataBytes()
    {
        byte[] invalidModel = SceneProtocolTests.Response(0x0d, [0, 0], "Name");
        invalidModel[4] = 0x11;
        RecomputeChecksum(invalidModel);
        Assert.False(SysexProtocol.TryParsePresetNameResponse(invalidModel, out _, out string modelError));
        Assert.Contains("model", modelError, StringComparison.OrdinalIgnoreCase);

        byte[] invalidAddress = SceneProtocolTests.Response(0x0d, [0x7f, 0x7f], "Name");
        Assert.False(SysexProtocol.TryParsePresetNameResponse(invalidAddress, out _, out string addressError));
        Assert.Contains("address", addressError, StringComparison.OrdinalIgnoreCase);

        byte[] invalidData = SceneProtocolTests.Response(0x0d, [0, 0], "Name");
        invalidData[8] = 0x80;
        RecomputeChecksum(invalidData);
        Assert.False(SysexProtocol.TryParsePresetNameResponse(invalidData, out _, out string dataError));
        Assert.Contains("data byte", dataError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PresetNameResponseStopsAtNullAndTrimsTrailingSpaces()
    {
        byte[] response = SceneProtocolTests.Response(0x0d, [0, 1], "Lead  \0ignored");

        Assert.True(SysexProtocol.TryParsePresetNameResponse(response, out PresetNameResult? result, out string error), error);
        Assert.Equal(128, result!.Slot);
        Assert.Equal("Lead", result.PresetName);
    }

    [Fact]
    public void StreamAssemblerResynchronizesAfterInterruptedAndOversizedFrames()
    {
        var assembler = new SysexAssembler();
        byte[] expected = SysexProtocol.BuildSceneNameQuery(2);

        Assert.Empty(assembler.Feed([0xf0, 1, 2, 0x90, 3, 0xf7]));
        Assert.Equal(expected, Assert.Single(assembler.Feed([0xf0, 1, 2, .. expected])));

        Assert.Empty(assembler.Feed([0xf0, .. Enumerable.Repeat((byte)1, 4096)]));
        Assert.Equal(expected, Assert.Single(assembler.Feed(expected)));
    }

    [Fact]
    public void StoredPresetDecoderRejectsNullFrames()
    {
        var decoder = new StoredPresetDecoder();

        Assert.Throws<ArgumentNullException>(() => decoder.Accept(null!));
    }

    [Theory]
    [InlineData(" 7 ", NoteCommand.Digit7)]
    [InlineData("send", NoteCommand.Send)]
    [InlineData("Clear", NoteCommand.Clear)]
    [InlineData("next", NoteCommand.Next)]
    [InlineData("PREV", NoteCommand.Prev)]
    [InlineData("last", NoteCommand.Last)]
    [InlineData("unknown", NoteCommand.None)]
    public void NoteCommandsAreParsedCaseInsensitively(string value, NoteCommand expected) =>
        Assert.Equal(expected, NoteCommandHelper.Parse(value));

    [Theory]
    [InlineData(NoteCommand.Digit0, 0)]
    [InlineData(NoteCommand.Digit9, 9)]
    public void DigitCommandsConvertToTheirNumericValue(NoteCommand command, int expected)
    {
        Assert.True(command.IsDigit());
        Assert.Equal(expected, command.ToDigit());
    }

    [Fact]
    public void FavoriteIdentityAndSlotHelpersHandleGapsAndEmptyCollections()
    {
        var favorites = new[]
        {
            new Favorite { Id = 7, Slot = 1 },
            new Favorite { Id = 3, Slot = 3 },
        };

        Assert.Equal(8, FavoritesManager.NextId(favorites));
        Assert.Equal(2, FavoritesManager.NextFreeSlot(favorites));
        Assert.Equal(1, FavoritesManager.NextId([]));
        Assert.Equal(1, FavoritesManager.NextFreeSlot([]));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteWidthsUseTheDefaultColumnCount(double width) =>
        Assert.Equal(5, PresetSelection.ColumnsForWidth(width));

    private static void RecomputeChecksum(byte[] frame) =>
        frame[^2] = SysexProtocol.ComputeChecksum(frame.AsSpan(0, frame.Length - 2));
}
