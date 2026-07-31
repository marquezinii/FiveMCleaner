using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace FiveMCleaner.App.Services;

public sealed record SilentUpdateLaunch(bool Started, int? ExitCode, string? FailureReason)
{
    public static SilentUpdateLaunch Running() => new(true, null, null);

    public static SilentUpdateLaunch Failed(int? exitCode, string reason) =>
        new(false, exitCode, reason);
}

public interface ISilentUpdateInstaller
{
    Task<SilentUpdateLaunch> StartAsync(DownloadedUpdate update, CancellationToken cancellationToken = default);
}

public interface IUpdateProcessLauncher
{
    void Start(string updaterPath, IReadOnlyList<string> arguments);
}

/// <summary>
/// Starts the dedicated updater from local app data, never from the folder
/// being replaced. The updater owns the wait-for-parent, integrity recheck,
/// setup execution, error display and restart lifecycle.
/// </summary>
public sealed class SilentUpdateInstaller : ISilentUpdateInstaller
{
    private const string UpdaterFileName = "FiveMCleaner.Updater.exe";
    private readonly IUpdateProcessLauncher launcher;
    private readonly string updatesRootDirectory;
    private readonly string? logDirectory;
    private readonly string updaterSourcePath;
    private readonly string updaterRuntimeDirectory;

    public SilentUpdateInstaller(
        string updatesRootDirectory,
        string? logDirectory,
        string updaterSourcePath,
        string updaterRuntimeDirectory,
        IUpdateProcessLauncher? launcher = null)
    {
        this.updatesRootDirectory = RequireAbsoluteDirectory(updatesRootDirectory, nameof(updatesRootDirectory));
        this.updaterRuntimeDirectory = RequireAbsoluteDirectory(updaterRuntimeDirectory, nameof(updaterRuntimeDirectory));
        ArgumentException.ThrowIfNullOrWhiteSpace(updaterSourcePath);
        this.updaterSourcePath = Path.GetFullPath(updaterSourcePath);
        this.logDirectory = logDirectory;
        this.launcher = launcher ?? new ProcessUpdateLauncher();
    }

    internal IReadOnlyList<string> BuildHandoffArguments(
        DownloadedUpdate update,
        int parentProcessId,
        long parentStartTimeUtcFileTime)
    {
        var arguments = new List<string>
        {
            "--installer", update.InstallerPath,
            "--installer-size", update.SizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--installer-sha256", update.Sha256Hex,
            "--parent-pid", parentProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--parent-start-time", parentStartTimeUtcFileTime.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        var preparedLogDirectory = TryPrepareLogDirectory();
        if (preparedLogDirectory is not null)
        {
            arguments.Add("--log");
            arguments.Add(Path.Combine(preparedLogDirectory, "update-install.log"));
        }

        return arguments;
    }

    public Task<SilentUpdateLaunch> StartAsync(DownloadedUpdate update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            ResolveVerifiedInstallerPath(update);
            var updaterPath = CopyUpdaterOutsideInstallDirectory();
            using var currentProcess = Process.GetCurrentProcess();
            launcher.Start(
                updaterPath,
                BuildHandoffArguments(
                    update,
                    currentProcess.Id,
                    currentProcess.StartTime.ToUniversalTime().ToFileTimeUtc()));
            return Task.FromResult(SilentUpdateLaunch.Running());
        }
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            return Task.FromResult(SilentUpdateLaunch.Failed(null, exception.Message));
        }
    }

    private static string RequireAbsoluteDirectory(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (!Path.IsPathFullyQualified(value))
        {
            throw new ArgumentException("O caminho precisa ser absoluto.", parameterName);
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
    }

    private string? TryPrepareLogDirectory()
    {
        if (string.IsNullOrWhiteSpace(logDirectory)) return null;
        try
        {
            Directory.CreateDirectory(logDirectory);
            return logDirectory;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void ResolveVerifiedInstallerPath(DownloadedUpdate update)
    {
        if (string.IsNullOrWhiteSpace(update.InstallerPath) || !Path.IsPathFullyQualified(update.InstallerPath))
        {
            throw new UpdateSecurityException("O caminho do instalador da atualização não é absoluto.");
        }

        var fullPath = Path.GetFullPath(update.InstallerPath);
        var requiredPrefix = updatesRootDirectory + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase)
            || !fullPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(fullPath)
            || update.SizeBytes <= 0
            || !IsSha256(update.Sha256Hex))
        {
            throw new UpdateSecurityException("O instalador verificado ou seus metadados de integridade não são válidos.");
        }
    }

    private string CopyUpdaterOutsideInstallDirectory()
    {
        if (!File.Exists(updaterSourcePath)
            || !Path.GetFileName(updaterSourcePath).Equals(UpdaterFileName, StringComparison.OrdinalIgnoreCase))
        {
            throw new FileNotFoundException("O atualizador independente não foi encontrado na instalação.", updaterSourcePath);
        }

        var sourceHash = ComputeSha256(updaterSourcePath);
        Directory.CreateDirectory(updaterRuntimeDirectory);
        var destination = Path.Combine(updaterRuntimeDirectory, UpdaterFileName);
        var temporary = Path.Combine(updaterRuntimeDirectory, $"{UpdaterFileName}.{Guid.NewGuid():N}.new");
        try
        {
            File.Copy(updaterSourcePath, temporary, overwrite: false);
            // Reverify before the rename so a swap of the temp file mid-copy is caught
            // before it ever occupies the path we are about to execute.
            if (!ComputeSha256(temporary).Equals(sourceHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new UpdateSecurityException("A cópia local do atualizador independente falhou na verificação de integridade.");
            }
            File.Move(temporary, destination, overwrite: true);
            // Reverify again immediately before launch: closes the race window between
            // the rename and Process.Start where another local process could have
            // replaced the destination file at the same path.
            if (!ComputeSha256(destination).Equals(sourceHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new UpdateSecurityException("O atualizador independente foi alterado após a cópia local.");
            }
            return destination;
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private static string ComputeSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool IsSha256(string? value) => value is { Length: 64 }
        && value.All(static character => char.IsAsciiHexDigit(character));

    private sealed class ProcessUpdateLauncher : IUpdateProcessLauncher
    {
        public void Start(string updaterPath, IReadOnlyList<string> arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = updaterPath,
                WorkingDirectory = Path.GetDirectoryName(updaterPath)!,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("O Windows não iniciou o atualizador independente.");
        }
    }
}
