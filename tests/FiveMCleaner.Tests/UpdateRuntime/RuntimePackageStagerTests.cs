using System.IO.Compression;
using System.Security.Cryptography;
using FiveMCleaner.UpdateRuntime;
using Xunit;

namespace FiveMCleaner.Tests.UpdateRuntime;

public sealed class RuntimePackageStagerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "FiveMCleanerStager", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Fact]
    public void Stage_VerifiesArchiveAndEveryExtractedFile()
    {
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "source");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "FiveMCleaner.exe"), "payload");
        var fileHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(source, "FiveMCleaner.exe")))).ToLowerInvariant();
        File.WriteAllText(Path.Combine(source, "SHA256SUMS.txt"), $"{fileHash}  FiveMCleaner.exe");
        var zip = Path.Combine(root, "package.zip");
        ZipFile.CreateFromDirectory(source, zip);
        var zipHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zip)));

        var result = new RuntimePackageStager(Path.Combine(root, "runtime")).Stage(zip, "2.0.0", zipHash, new FileInfo(zip).Length);

        Assert.True(File.Exists(Path.Combine(result, "FiveMCleaner.exe")));
    }

    [Fact]
    public void Stage_RejectsAnExtractedFileMissingFromTheFileManifest()
    {
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "source");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "FiveMCleaner.exe"), "payload");
        File.WriteAllText(Path.Combine(source, "undeclared.dll"), "surprise");
        var fileHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(source, "FiveMCleaner.exe"))));
        File.WriteAllText(Path.Combine(source, "SHA256SUMS.txt"), $"{fileHash}  FiveMCleaner.exe");
        var zip = Path.Combine(root, "package.zip");
        ZipFile.CreateFromDirectory(source, zip);
        var zipHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zip)));

        Assert.Throws<InvalidDataException>(() =>
            new RuntimePackageStager(Path.Combine(root, "runtime")).Stage(
                zip, "2.0.0", zipHash, new FileInfo(zip).Length));
    }
}
