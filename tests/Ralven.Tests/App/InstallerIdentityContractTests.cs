using Xunit;

namespace Ralven.Tests.App;

public sealed class InstallerIdentityContractTests
{
    [Fact]
    public void ReleasePipeline_UsesOnlyRalvenInstallerIdentity()
    {
        var root = TestHelpers.FindRepositoryRoot();
        var installer = File.ReadAllText(Path.Combine(root, "installer", "Ralven.iss"));
        var build = File.ReadAllText(Path.Combine(root, "scripts", "Build-Installer.ps1"));
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "release.yml"));

        Assert.Contains("#define AppName \"Ralven\"", installer, StringComparison.Ordinal);
        Assert.Contains("#define InstallerBaseName \"Ralven-Setup-\"", installer, StringComparison.Ordinal);
        Assert.Contains("#define AppExeName \"Ralven.Launcher.exe\"", installer, StringComparison.Ordinal);
        Assert.Contains("#define StableAppId \"{{35FF816F-9EFD-42C8-A63B-CC5EA138805A}\"", installer, StringComparison.Ordinal);
        Assert.Contains("UsePreviousGroup=yes", installer, StringComparison.Ordinal);
        Assert.Contains("Name: \"startup\"; Description: \"{cm:StartWithWindowsTask}\"; GroupDescription: \"{cm:AdditionalShortcuts}:\"", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("Tasks: startup; Flags: unchecked", installer, StringComparison.Ordinal);
        Assert.Equal(2, installer.Split("Check: not IsAutomaticUpdateRelaunch", StringSplitOptions.None).Length - 1);

        Assert.Contains("$baseName = \"Ralven-Setup-$Version-win-x64\"", build, StringComparison.Ordinal);
        Assert.DoesNotContain("legacyBaseName", build, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("Ralven-Setup-$env:ASSET_VERSION-win-x64.exe", workflow, StringComparison.Ordinal);
        Assert.Contains("Ralven-Setup-latest-win-x64.exe", workflow, StringComparison.Ordinal);
    }
}
