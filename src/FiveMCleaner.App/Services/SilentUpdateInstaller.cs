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

/// <summary>Minimal surface over a launched installer process, so the launch
/// contract can be tested without actually installing anything.</summary>
public interface IInstallerProcess : IDisposable
{
    bool HasExited { get; }

    int ExitCode { get; }

    Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken);
}

public interface IInstallerProcessLauncher
{
    IInstallerProcess Start(string installerPath, IReadOnlyList<string> arguments);
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
    /// <summary>
    /// How long to watch a freshly started installer before trusting it. Inno
    /// Setup reports its own startup problems (a corrupt payload, a directory
    /// it cannot write to, a concurrent setup holding the mutex) by exiting
    /// almost immediately with a non-zero code. Catching that here is what
    /// allows the app to stay open and explain the failure instead of closing
    /// into an update that never happens.
    /// </summary>
    internal static readonly TimeSpan SettleWindow = TimeSpan.FromSeconds(4);

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

        if (TryPrepareLogDirectory())
        {
            arguments.Add($"/LOG={Path.Combine(logDirectory, "update-install.log")}");
        }

        return arguments;
    }

    private bool TryPrepareLogDirectory()
    {
        if (string.IsNullOrWhiteSpace(logDirectory))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(logDirectory);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Diagnostics must never prevent a verified installer from running.
            return false;
        }
    }

    public async Task<SilentUpdateLaunch> StartAsync(
        DownloadedUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        string installerPath;
        try
        {
            installerPath = ResolveVerifiedInstallerPath(update.InstallerPath);
        }
        catch (UpdateSecurityException exception)
        {
            return SilentUpdateLaunch.Failed(null, exception.Message);
        }

        IInstallerProcess process;
        try
        {
            process = launcher.Start(installerPath, BuildArguments());
        }
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            return SilentUpdateLaunch.Failed(null, exception.Message);
        }

        using (process)
        {
            try
            {
                var exited = await process
                    .WaitForExitAsync(SettleWindow, cancellationToken)
                    .ConfigureAwait(false);
                if (!exited)
                {
                    // Still installing. This is the normal path: the caller now
                    // closes the app so its files can be replaced.
                    return SilentUpdateLaunch.Running();
                }

                return process.ExitCode == 0
                    ? SilentUpdateLaunch.Running()
                    : SilentUpdateLaunch.Failed(
                        process.ExitCode,
                        DescribeInnoExitCode(process.ExitCode));
            }
            catch (Exception exception) when (exception is not (
                OutOfMemoryException or StackOverflowException or AccessViolationException))
            {
                return SilentUpdateLaunch.Failed(null, exception.Message);
            }
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

    /// <summary>Documented Inno Setup exit codes, translated into something a
    /// player can act on.</summary>
    internal static string DescribeInnoExitCode(int exitCode) => exitCode switch
    {
        1 => "O instalador da atualização não pôde ser iniciado.",
        2 or 5 => "A atualização foi cancelada antes de ser aplicada.",
        3 or 4 => "O instalador da atualização encontrou um erro e não aplicou mudanças.",
        6 => "A atualização foi interrompida.",
        8 => "A atualização precisa que o Windows seja reiniciado antes de continuar.",
        _ => $"O instalador da atualização terminou com o código {exitCode}.",
    };

    private sealed class ProcessInstallerLauncher : IInstallerProcessLauncher
    {
        public IInstallerProcess Start(string installerPath, IReadOnlyList<string> arguments)
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

            var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException(
                    "O Windows não iniciou o instalador da atualização.");
            return new ProcessHandle(process);
        }

        private sealed class ProcessHandle(Process process) : IInstallerProcess
        {
            public bool HasExited => process.HasExited;

            public int ExitCode => process.ExitCode;

            public async Task<bool> WaitForExitAsync(
                TimeSpan timeout,
                CancellationToken cancellationToken)
            {
                using var timeoutSource = new CancellationTokenSource(timeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    timeoutSource.Token);
                try
                {
                    await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
                    return true;
                }
                catch (OperationCanceledException) when (
                    timeoutSource.IsCancellationRequested
                    && !cancellationToken.IsCancellationRequested)
                {
                    // The installer is still working, which is what we want.
                    // It deliberately outlives this process.
                    return false;
                }
            }

            public void Dispose() => process.Dispose();
        }
    }
}
