using System.Diagnostics;
using System.IO;

namespace FiveMCleaner.App.Services;

/// <summary>
/// Result of asking Windows to run the verified installer in silent mode.
/// <see cref="Started"/> false means the caller must stay open and report the
/// failure — closing the app would strand the user with no visible progress
/// and no application to come back to.
/// </summary>
public sealed record SilentUpdateLaunch(bool Started, int? ExitCode, string? FailureReason)
{
    public static SilentUpdateLaunch Running() => new(true, null, null);

    public static SilentUpdateLaunch Failed(int? exitCode, string reason) =>
        new(false, exitCode, reason);
}

public interface ISilentUpdateInstaller
{
    Task<SilentUpdateLaunch> StartAsync(
        DownloadedUpdate update,
        CancellationToken cancellationToken = default);
}

public interface IInstallerProcessLauncher
{
    void Start(string installerPath, IReadOnlyList<string> arguments);
}

/// <summary>
/// Runs the already downloaded and SHA-256 verified installer without any
/// wizard: no welcome page, no license re-acceptance, no Next buttons. The
/// installer replaces the files and relaunches the app itself (see the
/// /AUTOUPDATE=yes [Run] entry in FiveMCleaner.iss), so the user experience is
/// a single click followed by the app reopening on the new version.
/// </summary>
public sealed class SilentUpdateInstaller : ISilentUpdateInstaller
{
    private readonly IInstallerProcessLauncher launcher;
    private readonly string updatesRootDirectory;
    private readonly string? logDirectory;

    public SilentUpdateInstaller(
        string updatesRootDirectory,
        string? logDirectory = null,
        IInstallerProcessLauncher? launcher = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(updatesRootDirectory);
        if (!Path.IsPathFullyQualified(updatesRootDirectory))
        {
            throw new ArgumentException(
                "A pasta de atualizações precisa usar um caminho absoluto.",
                nameof(updatesRootDirectory));
        }

        this.updatesRootDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(updatesRootDirectory));
        this.logDirectory = logDirectory;
        this.launcher = launcher ?? new ProcessInstallerLauncher();
    }

    /// <summary>
    /// Arguments handed to Inno Setup. VERYSILENT suppresses the whole wizard
    /// (which is also why the user is never asked to accept the license again);
    /// SUPPRESSMSGBOXES stops it from blocking on a dialog nobody can see;
    /// NORESTART keeps it from rebooting the machine behind the user's back;
    /// AUTOUPDATE=yes is our own flag and is the only thing that authorizes the
    /// installer to relaunch the app afterwards.
    /// </summary>
    internal IReadOnlyList<string> BuildArguments()
    {
        var arguments = new List<string>
        {
            "/VERYSILENT",
            "/SUPPRESSMSGBOXES",
            "/NORESTART",
            "/NOCANCEL",
            "/AUTOUPDATE=yes",
        };

        var preparedLogDirectory = TryPrepareLogDirectory();
        if (preparedLogDirectory is not null)
        {
            arguments.Add($"/LOG={Path.Combine(preparedLogDirectory, "update-install.log")}");
        }

        return arguments;
    }

    private string? TryPrepareLogDirectory()
    {
        if (string.IsNullOrWhiteSpace(logDirectory))
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(logDirectory);
            return logDirectory;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Diagnostics must never prevent a verified installer from running.
            return null;
        }
    }

    public Task<SilentUpdateLaunch> StartAsync(
        DownloadedUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        cancellationToken.ThrowIfCancellationRequested();

        string installerPath;
        try
        {
            installerPath = ResolveVerifiedInstallerPath(update.InstallerPath);
        }
        catch (UpdateSecurityException exception)
        {
            return Task.FromResult(SilentUpdateLaunch.Failed(null, exception.Message));
        }

        try
        {
            launcher.Start(installerPath, BuildArguments());
            // The caller must close immediately. Waiting here creates a
            // deadlock: Inno Setup waits for this executable to release the
            // installed files while this executable waits for Setup to stay
            // alive before closing.
            return Task.FromResult(SilentUpdateLaunch.Running());
        }
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            return Task.FromResult(SilentUpdateLaunch.Failed(null, exception.Message));
        }
    }

    /// <summary>
    /// Only a file the update service itself placed inside the updates folder
    /// may be executed. The path is already the output of a hash-verified
    /// download, and this keeps a tampered settings file or a manipulated
    /// in-memory value from turning the update button into an arbitrary
    /// "run this executable" primitive.
    /// </summary>
    private string ResolveVerifiedInstallerPath(string installerPath)
    {
        if (string.IsNullOrWhiteSpace(installerPath)
            || !Path.IsPathFullyQualified(installerPath))
        {
            throw new UpdateSecurityException(
                "O caminho do instalador da atualização não é absoluto.");
        }

        var fullPath = Path.GetFullPath(installerPath);
        var requiredPrefix = updatesRootDirectory + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdateSecurityException(
                "O instalador da atualização está fora da pasta verificada de atualizações.");
        }

        if (!fullPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdateSecurityException(
                "O instalador da atualização precisa ser um executável .exe.");
        }

        if (!File.Exists(fullPath))
        {
            throw new UpdateSecurityException(
                "O instalador verificado não foi encontrado na pasta de atualizações.");
        }

        return fullPath;
    }

    private sealed class ProcessInstallerLauncher : IInstallerProcessLauncher
    {
        public void Start(string installerPath, IReadOnlyList<string> arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = installerPath,
                WorkingDirectory = Path.GetDirectoryName(installerPath)!,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException(
                    "O Windows não iniciou o instalador da atualização.");
        }
    }
}
