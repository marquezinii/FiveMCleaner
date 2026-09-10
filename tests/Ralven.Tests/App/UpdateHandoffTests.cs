using Ralven.Updater;
using Xunit;

namespace Ralven.Tests.App;

public sealed class UpdateHandoffTests
{
    [Fact]
    public void TryParse_AcceptsOnlyTheFixedHandoffContract()
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var accepted = UpdateHandoff.TryParse([
            "--installer", Path.Combine(localData, "Ralven", "Updates", "1.2.0", "Ralven-Setup-1.2.0-win-x64.exe"),
            "--installer-size", "1024",
            "--installer-sha256", new string('a', 64),
            "--parent-pid", "123",
            "--parent-start-time", "456",
            "--culture", "fr-FR",
            "--log", Path.Combine(localData, "Ralven", "Logs", "update-install.log"),
        ], out var handoff, out _);

        Assert.True(accepted);
        Assert.Equal(123, handoff.ParentProcessId);
        Assert.Equal(456, handoff.ParentStartTimeUtcFileTime);
        Assert.Equal("fr-FR", handoff.CultureName);
        Assert.Contains("/AUTOUPDATE=yes", handoff.BuildInstallerArguments());
    }

    [Fact]
    public void TryParse_RejectsUnknownArgumentsAndRelativePaths()
    {
        var installer = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Ralven", "Updates", "update.exe");
        var unknown = UpdateHandoff.TryParse([
            "--installer", installer, "--installer-size", "1", "--installer-sha256", new string('a', 64), "--parent-pid", "1", "--parent-start-time", "2", "--culture", "en-US", "--extra", "value",
        ], out _, out _);
        var relative = UpdateHandoff.TryParse([
            "--installer", "update.exe", "--installer-size", "1", "--installer-sha256", new string('a', 64), "--parent-pid", "1", "--parent-start-time", "2", "--culture", "en-US",
        ], out _, out _);
        var missingProcessIdentity = UpdateHandoff.TryParse([
            "--installer", installer, "--installer-size", "1", "--installer-sha256", new string('a', 64), "--parent-pid", "1",
        ], out _, out _);

        Assert.False(unknown);
        Assert.False(relative);
        Assert.False(missingProcessIdentity);
    }
}
