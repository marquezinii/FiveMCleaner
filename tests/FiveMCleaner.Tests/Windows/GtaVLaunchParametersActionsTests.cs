using FiveMCleaner.Contracts;
using FiveMCleaner.Windows.Actions;
using Xunit;

namespace FiveMCleaner.Tests.Windows;

public sealed class GtaVLaunchParametersDiagnosisActionTests
{
    [Fact]
    public async Task ApplyAsync_WarnsWhenRepairFlagsAreStillActive()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var gtaVRoot = temporaryDirectory.Combine("Grand Theft Auto V");
        Directory.CreateDirectory(gtaVRoot);
        File.WriteAllLines(
            Path.Combine(gtaVRoot, "commandline.txt"),
            ["-fullscreen", "-safemode", "-cityDensity 0.550000"]);
        var action = new GtaVLaunchParametersDiagnosisAction(gtaVRoot);

        var result = await action.ApplyAsync(Context(), CancellationToken.None);

        Assert.False(result.Changed);
        Assert.Contains("reparo", result.Messages.Single(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-safemode", result.Messages.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyAsync_ReportsNoFileWhenAbsent()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var gtaVRoot = temporaryDirectory.Combine("Grand Theft Auto V");
        Directory.CreateDirectory(gtaVRoot);
        var action = new GtaVLaunchParametersDiagnosisAction(gtaVRoot);

        var result = await action.ApplyAsync(Context(), CancellationToken.None);

        Assert.False(result.Changed);
        Assert.Contains("padrão", result.Messages.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyAsync_ReportsUnconfirmedInstallation()
    {
        var action = new GtaVLaunchParametersDiagnosisAction(gtaVInstallationRoot: null);

        var result = await action.ApplyAsync(Context(), CancellationToken.None);

        Assert.False(result.Changed);
        Assert.Contains("não foi confirmada", result.Messages.Single(), StringComparison.Ordinal);
    }

    private static WindowsActionContext Context()
    {
        return new WindowsActionContext
        {
            TransactionId = Guid.NewGuid(),
            StartedAtUtc = DateTimeOffset.UtcNow,
            IsElevated = false
        };
    }
}

public sealed class GtaVGraphicsLaunchParametersActionTests
{
    [Fact]
    public async Task ApplyAsync_WritesManagedLinesAndPreservesUnknownOnesThenRollsBackExactly()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var gtaVRoot = temporaryDirectory.Combine("Grand Theft Auto V");
        Directory.CreateDirectory(gtaVRoot);
        var commandLinePath = Path.Combine(gtaVRoot, "commandline.txt");
        File.WriteAllLines(commandLinePath, ["-scOfflineOnly", "-cityDensity 0.100000"]);
        var display = new FakeDisplayConfigurationInspector();
        var gtaVInspector = new FakeGtaVProcessInspector();
        var action = new GtaVGraphicsLaunchParametersAction(gtaVRoot, display, gtaVInspector);
        var context = Context();

        var result = await action.ApplyAsync(context, CancellationToken.None);

        Assert.True(result.Changed);
        var lines = File.ReadAllLines(commandLinePath);
        Assert.Contains("-scOfflineOnly", lines);
        Assert.Contains("-cityDensity 0.550000", lines);
        Assert.Contains("-fxaa", lines);
        Assert.Contains("-frameLimit 144", lines);
        Assert.DoesNotContain("-cityDensity 0.100000", lines);

        await action.RollbackAsync(context, result.SnapshotJson, CancellationToken.None);

        Assert.Equal(["-scOfflineOnly", "-cityDensity 0.100000"], File.ReadAllLines(commandLinePath));
    }

    [Fact]
    public async Task ApplyAsync_NoChangeWhenAlreadyAtDesiredValues()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var gtaVRoot = temporaryDirectory.Combine("Grand Theft Auto V");
        Directory.CreateDirectory(gtaVRoot);
        var commandLinePath = Path.Combine(gtaVRoot, "commandline.txt");
        File.WriteAllLines(
            commandLinePath,
            ["-cityDensity 0.550000", "-anisotropicQualityLevel 8", "-fxaa", "-grassQuality 1", "-lodScale 0.700000", "-frameLimit 144"]);
        var action = new GtaVGraphicsLaunchParametersAction(
            gtaVRoot, new FakeDisplayConfigurationInspector(), new FakeGtaVProcessInspector());

        var result = await action.ApplyAsync(Context(), CancellationToken.None);

        Assert.False(result.Changed);
    }

    [Fact]
    public async Task ApplyAsync_RefusesToWriteWhileGtaVIsRunning()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var gtaVRoot = temporaryDirectory.Combine("Grand Theft Auto V");
        Directory.CreateDirectory(gtaVRoot);
        var action = new GtaVGraphicsLaunchParametersAction(
            gtaVRoot, new FakeDisplayConfigurationInspector(), new FakeGtaVProcessInspector(running: true));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            action.ApplyAsync(Context(), CancellationToken.None));
    }

    private static WindowsActionContext Context()
    {
        return new WindowsActionContext
        {
            TransactionId = Guid.NewGuid(),
            StartedAtUtc = DateTimeOffset.UtcNow,
            IsElevated = false
        };
    }
}

public sealed class GtaVDisplayLaunchParametersActionTests
{
    [Theory]
    [InlineData(false, false, "-fullscreen")]
    [InlineData(true, false, "-windowed")]
    [InlineData(true, true, "-borderless")]
    public async Task ApplyAsync_WritesMutuallyExclusiveDisplayMode(
        bool windowed, bool borderless, string expectedFlag)
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var gtaVRoot = temporaryDirectory.Combine("Grand Theft Auto V");
        Directory.CreateDirectory(gtaVRoot);
        var commandLinePath = Path.Combine(gtaVRoot, "commandline.txt");
        File.WriteAllLines(commandLinePath, ["-fullscreen"]);
        var action = new GtaVDisplayLaunchParametersAction(
            gtaVRoot, windowed, borderless, GtaVDirectXVersion.Unspecified, new FakeGtaVProcessInspector());

        var result = await action.ApplyAsync(Context(), CancellationToken.None);

        var lines = File.ReadAllLines(commandLinePath);
        Assert.Contains(expectedFlag, lines);
        Assert.Single(lines, line => line is "-fullscreen" or "-windowed" or "-borderless");
        _ = result;
    }

    [Fact]
    public async Task ApplyAsync_WritesChosenDirectXVersionAndRemovesOthers()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var gtaVRoot = temporaryDirectory.Combine("Grand Theft Auto V");
        Directory.CreateDirectory(gtaVRoot);
        var commandLinePath = Path.Combine(gtaVRoot, "commandline.txt");
        File.WriteAllLines(commandLinePath, ["-DX10"]);
        var action = new GtaVDisplayLaunchParametersAction(
            gtaVRoot, false, false, GtaVDirectXVersion.DX11, new FakeGtaVProcessInspector());

        await action.ApplyAsync(Context(), CancellationToken.None);

        var lines = File.ReadAllLines(commandLinePath);
        Assert.Contains("-DX11", lines);
        Assert.DoesNotContain("-DX10", lines);
    }

    private static WindowsActionContext Context()
    {
        return new WindowsActionContext
        {
            TransactionId = Guid.NewGuid(),
            StartedAtUtc = DateTimeOffset.UtcNow,
            IsElevated = false
        };
    }
}

public sealed class GtaVRepairLaunchParametersActionTests
{
    [Fact]
    public async Task ApplyAsync_OnlyWritesRequestedRepairFlagsAndRollsBackExactly()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var gtaVRoot = temporaryDirectory.Combine("Grand Theft Auto V");
        Directory.CreateDirectory(gtaVRoot);
        var commandLinePath = Path.Combine(gtaVRoot, "commandline.txt");
        File.WriteAllLines(commandLinePath, ["-fullscreen"]);
        var action = new GtaVRepairLaunchParametersAction(
            gtaVRoot,
            useSafeMode: true,
            useMinimumSettings: false,
            useAutoSettingsRebuild: false,
            new FakeGtaVProcessInspector());
        var context = Context();

        var result = await action.ApplyAsync(context, CancellationToken.None);

        Assert.True(result.Changed);
        var lines = File.ReadAllLines(commandLinePath);
        Assert.Contains("-fullscreen", lines);
        Assert.Contains("-safemode", lines);
        Assert.DoesNotContain("-useMinimumSettings", lines);
        Assert.DoesNotContain("-UseAutoSettings", lines);

        await action.RollbackAsync(context, result.SnapshotJson, CancellationToken.None);

        Assert.Equal(["-fullscreen"], File.ReadAllLines(commandLinePath));
    }

    [Fact]
    public async Task ApplyAsync_NoChangeWhenNoRepairFlagsRequestedAndFileHasNone()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var gtaVRoot = temporaryDirectory.Combine("Grand Theft Auto V");
        Directory.CreateDirectory(gtaVRoot);
        var action = new GtaVRepairLaunchParametersAction(
            gtaVRoot, false, false, false, new FakeGtaVProcessInspector());

        var result = await action.ApplyAsync(Context(), CancellationToken.None);

        Assert.False(result.Changed);
        Assert.False(File.Exists(Path.Combine(gtaVRoot, "commandline.txt")));
    }

    private static WindowsActionContext Context()
    {
        return new WindowsActionContext
        {
            TransactionId = Guid.NewGuid(),
            StartedAtUtc = DateTimeOffset.UtcNow,
            IsElevated = false
        };
    }
}
