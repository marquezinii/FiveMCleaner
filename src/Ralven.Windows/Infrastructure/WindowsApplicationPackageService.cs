using System.Text.RegularExpressions;

namespace Ralven.Windows.Infrastructure;

public sealed record WindowsApplicationPackage(
    string PackageId,
    string Name,
    string Version,
    string? AvailableVersion,
    string Source);

public sealed record WindowsApplicationPackageSnapshot(
    IReadOnlyList<WindowsApplicationPackage> Packages,
    DateTimeOffset ObservedAtUtc,
    bool IsWinGetAvailable,
    IReadOnlyList<string> UnavailableSources)
{
    public bool IsPartial => UnavailableSources.Count > 0;
}

public enum WindowsApplicationPackageOperation
{
    Install,
    Update,
    Uninstall
}

public enum WindowsApplicationPackageOutcome
{
    Succeeded,
    RebootRequired,
    NoLongerAvailable,
    Cancelled,
    WinGetUnavailable,
    Failed
}

public sealed record WindowsApplicationPackageResult(
    WindowsApplicationPackageOutcome Outcome,
    int? ExitCode = null);

public interface IWindowsApplicationPackageService
{
    Task<WindowsApplicationPackageSnapshot> SearchAsync(
        string query,
        CancellationToken cancellationToken = default);

    Task<WindowsApplicationPackageSnapshot> ListInstalledAsync(
        CancellationToken cancellationToken = default);

    Task<WindowsApplicationPackageSnapshot> CheckUpdatesAsync(
        CancellationToken cancellationToken = default);

    Task<WindowsApplicationPackageResult> ExecuteAsync(
        WindowsApplicationPackageOperation operation,
        WindowsApplicationPackage package,
        CancellationToken cancellationToken = default);
}

public sealed partial class WinGetApplicationPackageService : IWindowsApplicationPackageService
{
    private const int NoApplicationsFoundExitCode = unchecked((int)0x8A150014);
    private const int UpdateNotApplicableExitCode = unchecked((int)0x8A15002B);
    private const int RebootRequiredExitCode = unchecked((int)0x8A150109);
    private static readonly string[] SupportedSources = ["winget", "msstore"];
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromHours(2);
    private readonly ICommandRunner commandRunner;
    private readonly string? winGetExecutable;
    private readonly SemaphoreSlim invocationGate = new(1, 1);

    public WinGetApplicationPackageService()
        : this(new ProcessCommandRunner(), FindWinGetExecutable())
    {
    }

    internal WinGetApplicationPackageService(
        ICommandRunner commandRunner,
        string? winGetExecutable)
    {
        this.commandRunner = commandRunner ?? throw new ArgumentNullException(nameof(commandRunner));
        if (winGetExecutable is not null && !IsOfficialWinGetExecutable(winGetExecutable))
        {
            throw new ArgumentException(
                "WinGet must use the official user alias.",
                nameof(winGetExecutable));
        }

        this.winGetExecutable = winGetExecutable;
    }

    public Task<WindowsApplicationPackageSnapshot> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        query = query.Trim();
        if (query.Length is < 2 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(query),
                "Package searches must contain between 2 and 100 characters.");
        }

        return QuerySourcesAsync(
            source =>
            [
                "search",
                "--query",
                query,
                "--source",
                source,
                "--count",
                "50",
                "--disable-interactivity"
            ],
            ParsePackages,
            cancellationToken);
    }

    public Task<WindowsApplicationPackageSnapshot> ListInstalledAsync(
        CancellationToken cancellationToken = default) => QuerySourcesAsync(
        source =>
        [
            "list",
            "--source",
            source,
            "--disable-interactivity"
        ],
        ParsePackages,
        cancellationToken);

    public Task<WindowsApplicationPackageSnapshot> CheckUpdatesAsync(
        CancellationToken cancellationToken = default) => QuerySourcesAsync(
        source =>
        [
            "list",
            "--upgrade-available",
            "--source",
            source,
            "--sort",
            "name",
            "--ascending",
            "--disable-interactivity"
        ],
        ParseUpdates,
        cancellationToken);

    public async Task<WindowsApplicationPackageResult> ExecuteAsync(
        WindowsApplicationPackageOperation operation,
        WindowsApplicationPackage package,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        ValidatePackage(package, operation);
        if (winGetExecutable is null)
        {
            return new WindowsApplicationPackageResult(
                WindowsApplicationPackageOutcome.WinGetUnavailable);
        }

        var arguments = new List<string>
        {
            operation switch
            {
                WindowsApplicationPackageOperation.Install => "install",
                WindowsApplicationPackageOperation.Update => "upgrade",
                WindowsApplicationPackageOperation.Uninstall => "uninstall",
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            },
            "--id",
            package.PackageId,
            "--exact",
            "--source",
            package.Source
        };
        if (operation is not WindowsApplicationPackageOperation.Uninstall)
        {
            arguments.AddRange(["--accept-package-agreements", "--accept-source-agreements"]);
        }

        arguments.Add("--disable-interactivity");

        await invocationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await commandRunner.RunAsync(
                winGetExecutable,
                arguments,
                OperationTimeout,
                cancellationToken).ConfigureAwait(false);
            return new WindowsApplicationPackageResult(
                result.ExitCode switch
                {
                    0 => WindowsApplicationPackageOutcome.Succeeded,
                    RebootRequiredExitCode => WindowsApplicationPackageOutcome.RebootRequired,
                    NoApplicationsFoundExitCode or UpdateNotApplicableExitCode =>
                        WindowsApplicationPackageOutcome.NoLongerAvailable,
                    unchecked((int)0x8A150077)
                        or unchecked((int)0x8A15010C)
                        or unchecked((int)0x8A150005) =>
                        WindowsApplicationPackageOutcome.Cancelled,
                    _ => WindowsApplicationPackageOutcome.Failed
                },
                result.ExitCode);
        }
        finally
        {
            invocationGate.Release();
        }
    }

    internal static IReadOnlyList<WindowsApplicationPackage> ParsePackages(
        string output,
        string source) => ParseTable(output, source, includesAvailableVersion: false);

    internal static IReadOnlyList<WindowsApplicationPackage> ParseUpdates(
        string output,
        string source) => ParseTable(output, source, includesAvailableVersion: true);

    private async Task<WindowsApplicationPackageSnapshot> QuerySourcesAsync(
        Func<string, IReadOnlyList<string>> createArguments,
        Func<string, string, IReadOnlyList<WindowsApplicationPackage>> parse,
        CancellationToken cancellationToken)
    {
        if (winGetExecutable is null)
        {
            return new WindowsApplicationPackageSnapshot(
                [],
                DateTimeOffset.UtcNow,
                IsWinGetAvailable: false,
                SupportedSources);
        }

        var packages = new List<WindowsApplicationPackage>();
        var unavailableSources = new List<string>();
        await invocationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var source in SupportedSources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var result = await commandRunner.RunAsync(
                        winGetExecutable,
                        createArguments(source),
                        QueryTimeout,
                        cancellationToken).ConfigureAwait(false);
                    if (result.ExitCode == NoApplicationsFoundExitCode)
                    {
                        continue;
                    }

                    if (!result.Succeeded)
                    {
                        unavailableSources.Add(source);
                        continue;
                    }

                    packages.AddRange(parse(result.StandardOutput, source));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (exception is not (
                    OutOfMemoryException or StackOverflowException or AccessViolationException))
                {
                    unavailableSources.Add(source);
                }
            }
        }
        finally
        {
            invocationGate.Release();
        }

        return new WindowsApplicationPackageSnapshot(
            packages
                .DistinctBy(package => $"{package.Source}\n{package.PackageId}", StringComparer.OrdinalIgnoreCase)
                .OrderBy(package => package.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(package => package.Source, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            DateTimeOffset.UtcNow,
            IsWinGetAvailable: true,
            unavailableSources);
    }

    private static IReadOnlyList<WindowsApplicationPackage> ParseTable(
        string output,
        string source,
        bool includesAvailableVersion)
    {
        ArgumentNullException.ThrowIfNull(output);
        ValidateSource(source);
        var lines = AnsiEscapePattern()
            .Replace(output, string.Empty)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var separatorIndex = -1;
        int[]? columnStarts = null;
        for (var index = 1; index < lines.Length; index++)
        {
            if (!TableSeparatorPattern().IsMatch(lines[index]))
            {
                continue;
            }

            separatorIndex = index;
            columnStarts =
            [
                0,
                .. HeaderGapPattern()
                    .Matches(lines[index - 1])
                    .Select(gap => gap.Index + gap.Length)
            ];
            break;
        }

        if (separatorIndex < 0 || columnStarts is null)
        {
            return [];
        }

        var minimumColumns = includesAvailableVersion ? 4 : 3;
        if (columnStarts.Length < minimumColumns || columnStarts.Length > 5)
        {
            throw new InvalidDataException("WinGet returned an unsupported list format.");
        }

        var packages = new List<WindowsApplicationPackage>();
        foreach (var line in lines.Skip(separatorIndex + 1))
        {
            var name = ReadColumn(line, columnStarts, 0);
            var packageId = ReadColumn(line, columnStarts, 1);
            var version = ReadColumn(line, columnStarts, 2);
            var availableVersion = includesAvailableVersion
                ? ReadColumn(line, columnStarts, 3)
                : null;
            if (name.Length == 0
                || version.Length == 0
                || (includesAvailableVersion && availableVersion?.Length == 0)
                || packageId.Contains('…', StringComparison.Ordinal)
                || !PackageIdPattern().IsMatch(packageId))
            {
                continue;
            }

            packages.Add(new WindowsApplicationPackage(
                packageId,
                name,
                version,
                availableVersion,
                source));
        }

        return packages;
    }

    private static string ReadColumn(string line, int[] columnStarts, int column)
    {
        var start = columnStarts[column];
        if (start >= line.Length)
        {
            return string.Empty;
        }

        var end = column == columnStarts.Length - 1
            ? line.Length
            : Math.Min(columnStarts[column + 1], line.Length);
        return line[start..end].Trim();
    }

    private static void ValidatePackage(
        WindowsApplicationPackage package,
        WindowsApplicationPackageOperation operation)
    {
        if (!PackageIdPattern().IsMatch(package.PackageId))
        {
            throw new ArgumentException("The package identifier is invalid.", nameof(package));
        }

        ValidateSource(package.Source);
        if (operation == WindowsApplicationPackageOperation.Update
            && string.IsNullOrWhiteSpace(package.AvailableVersion))
        {
            throw new ArgumentException("The package has no applicable update.", nameof(package));
        }
    }

    private static void ValidateSource(string source)
    {
        if (!SupportedSources.Contains(source, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The package source is not supported.", nameof(source));
        }
    }

    private static string? FindWinGetExecutable()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            return null;
        }

        var windowsApps = Path.GetFullPath(Path.Combine(localAppData, "Microsoft", "WindowsApps"));
        var candidate = Path.GetFullPath(Path.Combine(windowsApps, "winget.exe"));
        return File.Exists(candidate) ? candidate : null;
    }

    private static bool IsOfficialWinGetExecutable(string path)
    {
        if (!Path.IsPathFullyQualified(path))
        {
            return false;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            return false;
        }

        var alias = Path.GetFullPath(Path.Combine(
            localAppData,
            "Microsoft",
            "WindowsApps",
            "winget.exe"));
        return string.Equals(
            Path.GetFullPath(path),
            alias,
            StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._+\-]{0,255}$", RegexOptions.CultureInvariant)]
    private static partial Regex PackageIdPattern();

    [GeneratedRegex(@"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~])", RegexOptions.CultureInvariant)]
    private static partial Regex AnsiEscapePattern();

    [GeneratedRegex(@" {2,}", RegexOptions.CultureInvariant)]
    private static partial Regex HeaderGapPattern();

    [GeneratedRegex(@"^-{10,}$", RegexOptions.CultureInvariant)]
    private static partial Regex TableSeparatorPattern();
}
