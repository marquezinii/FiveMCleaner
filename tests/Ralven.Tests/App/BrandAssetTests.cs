using System.Security.Cryptography;
using System.Windows.Media.Imaging;
using Xunit;

namespace Ralven.Tests.App;

public sealed class BrandAssetTests
{
    [Fact]
    public void ReShadeCard_UsesAHighResolutionLicensedAsset()
    {
        var root = TestHelpers.FindRepositoryRoot();
        var assetPath = Path.Combine(root, "src", "Ralven.App", "Assets", "ReShade.png");
        var image = BitmapDecoder.Create(
            new Uri(assetPath),
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad).Frames.Single();

        Assert.Equal((256, 256), (image.PixelWidth, image.PixelHeight));
        Assert.True(File.Exists(Path.Combine(root, "src", "Ralven.App", "Assets", "ReShade.LICENSE.txt")));
    }

    [Fact]
    public void OfficialBrandSourcesAndExportsStaySynchronized()
    {
        var root = TestHelpers.FindRepositoryRoot();
        var sourcePath = Path.Combine(root, "assets", "brand", "source", "received", "ralven-app-icon-original.png");
        var canonicalPngPath = Path.Combine(root, "assets", "brand", "export", "app-icon", "ralven-app-icon-1024.png");
        var canonicalIcoPath = Path.Combine(root, "assets", "brand", "export", "app-icon", "Ralven.ico");
        var appPngPath = Path.Combine(root, "src", "Ralven.App", "Assets", "Ralven.png");
        var appIcoPath = Path.Combine(root, "src", "Ralven.App", "Assets", "Ralven.ico");

        Assert.Equal(
            Convert.FromHexString("07B4C6E60C1AD68CB57162BF7F10D81BABCF060F47BD0022C182658A9773C928"),
            SHA256.HashData(File.ReadAllBytes(sourcePath)));
        Assert.Equal(SHA256.HashData(File.ReadAllBytes(canonicalPngPath)), SHA256.HashData(File.ReadAllBytes(appPngPath)));
        Assert.Equal(SHA256.HashData(File.ReadAllBytes(canonicalIcoPath)), SHA256.HashData(File.ReadAllBytes(appIcoPath)));

        var png = BitmapDecoder.Create(new Uri(appPngPath), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames.Single();
        Assert.Equal((1024, 1024), (png.PixelWidth, png.PixelHeight));
        Assert.Equal(4, png.Format.Masks.Count);

        var iconSizes = BitmapDecoder.Create(new Uri(appIcoPath), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad)
            .Frames
            .Select(frame => frame.PixelWidth)
            .ToHashSet();
        Assert.True(new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 }.All(iconSizes.Contains));

        var canonical512 = SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "assets", "brand", "export", "app-icon", "ralven-app-icon-512.png")));
        foreach (var relativePath in new[]
        {
            Path.Combine("website", "public", "icon.png"),
            Path.Combine("docs", "assets", "icon.png"),
            Path.Combine("infra", "dashboard", "assets", "img", "logo.png")
        })
        {
            Assert.Equal(canonical512, SHA256.HashData(File.ReadAllBytes(Path.Combine(root, relativePath))));
        }

        var background = SHA256.HashData(File.ReadAllBytes(Path.Combine(
            root, "assets", "brand", "export", "background", "ralven-atmosphere-1672x941.png")));
        Assert.Equal(background, SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "website", "public", "og.png"))));
        Assert.Equal(background, SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "docs", "assets", "hero-ralven.png"))));
    }
}
