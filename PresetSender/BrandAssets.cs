using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace PresetSender;

internal static class BrandAssets
{
    // One immutable image is shared by the application header and window icon.
    public static Bitmap Logo { get; } = LoadLogo();

    private static Bitmap LoadLogo()
    {
        using var stream = AssetLoader.Open(new Uri("avares://PresetSender/Assets/preset-sender-256.png"));
        return new Bitmap(stream);
    }
}
