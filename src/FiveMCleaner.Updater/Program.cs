using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Windows.Forms;

namespace FiveMCleaner.Updater;

public static class Program
{
    private const int ParentExitTimeoutMilliseconds = 120_000;
    private const int InstallerTimeoutMilliseconds = 600_000;

    [STAThread]
    public static int Main(string[] args)
    {
        if (!UpdateHandoff.TryParse(args, out var handoff, out var error))
        {
            ShowFailure(error);
            return 2;
        }

        try
        {
            WaitForParentExit(handoff.ParentProcessId, handoff.ParentStartTimeUtcFileTime);
            using var verifiedInstaller = VerifyInstaller(handoff);
            RunInstaller(handoff);
            return 0;
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            ShowFailure(exception.Message);
            return 1;
        }
    }

    private static void WaitForParentExit(int parentProcessId, long parentStartTimeUtcFileTime)
    {
        try
        {
            using var parent = Process.GetProcessById(parentProcessId);
            if (!parent.HasExited
                && parent.StartTime.ToUniversalTime().ToFileTimeUtc() == parentStartTimeUtcFileTime
                && !parent.WaitForExit(ParentExitTimeoutMilliseconds))
            {
                throw new TimeoutException("O FiveMCleaner não foi encerrado a tempo para instalar a atualização.");
            }
        }
        catch (ArgumentException) { }
    }

    private static FileStream VerifyInstaller(UpdateHandoff handoff)
    {
        if (!File.Exists(handoff.InstallerPath)) throw new FileNotFoundException("O instalador baixado não foi encontrado.", handoff.InstallerPath);
        if (new FileInfo(handoff.InstallerPath).Length != handoff.InstallerSizeBytes)
        {
            throw new InvalidDataException("O tamanho do instalador baixado não confere com a atualização verificada.");
        }

        var stream = new FileStream(
            handoff.InstallerPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        if (stream.Length != handoff.InstallerSizeBytes)
        {
            stream.Dispose();
            throw new InvalidDataException("Installer size changed during verification.");
        }
        if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(handoff.InstallerSha256, StringComparison.OrdinalIgnoreCase))
        {
            stream.Dispose();
            throw new InvalidDataException("A verificação de integridade do instalador falhou.");
        }

        return stream;
    }

    private static void RunInstaller(UpdateHandoff handoff)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = handoff.InstallerPath,
            WorkingDirectory = Path.GetDirectoryName(handoff.InstallerPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in handoff.BuildInstallerArguments()) startInfo.ArgumentList.Add(argument);
        using var installer = Process.Start(startInfo) ?? throw new InvalidOperationException("O Windows não iniciou o instalador da atualização.");
        if (!installer.WaitForExit(InstallerTimeoutMilliseconds))
        {
            TryKill(installer);
            throw new TimeoutException($"O instalador da atualização não terminou a tempo e foi encerrado. {handoff.LogHint}");
        }
        if (installer.ExitCode != 0)
        {
            throw new InvalidOperationException($"A instalação da atualização foi encerrada com código {installer.ExitCode}. {handoff.LogHint}");
        }
    }

    private static void TryKill(Process process)
    {
        try { process.Kill(entireProcessTree: true); }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    private static void ShowFailure(string? detail) => MessageBox.Show(
        $"Não foi possível concluir a atualização do FiveMCleaner.\n\n{detail}\n\nAbra o aplicativo novamente e tente outra vez.",
        "Atualização do FiveMCleaner", MessageBoxButtons.OK, MessageBoxIcon.Error);
}

public sealed record UpdateHandoff(
    string InstallerPath,
    long InstallerSizeBytes,
    string InstallerSha256,
    int ParentProcessId,
    long ParentStartTimeUtcFileTime,
    string? LogPath)
{
    public string LogHint => LogPath is null ? string.Empty : $"Log: {LogPath}";

    public IReadOnlyList<string> BuildInstallerArguments()
    {
        var arguments = new List<string> { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/NOCANCEL", "/AUTOUPDATE=yes" };
        if (LogPath is not null) arguments.Add($"/LOG={LogPath}");
        return arguments;
    }

    public static bool TryParse(string[] args, out UpdateHandoff handoff, out string error)
    {
        handoff = null!;
        error = "Os dados da atualização são inválidos.";
        if (args is null || args.Length is 0 or > 12 || args.Length % 2 != 0) return false;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) || !values.TryAdd(args[index], args[index + 1])) return false;
        }

        if (!values.TryGetValue("--installer", out var installerPath)
            || !IsUnderLocalData("Updates", installerPath)
            || !installerPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || !values.TryGetValue("--installer-size", out var sizeText)
            || !long.TryParse(sizeText, NumberStyles.None, CultureInfo.InvariantCulture, out var size) || size <= 0
            || !values.TryGetValue("--installer-sha256", out var hash) || hash.Length != 64 || !hash.All(char.IsAsciiHexDigit)
            || !values.TryGetValue("--parent-pid", out var pidText)
            || !int.TryParse(pidText, NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid <= 0
            || !values.TryGetValue("--parent-start-time", out var startTimeText)
            || !long.TryParse(startTimeText, NumberStyles.None, CultureInfo.InvariantCulture, out var startTime) || startTime <= 0
            || values.Keys.Any(key => key is not "--installer" and not "--installer-size" and not "--installer-sha256" and not "--parent-pid" and not "--parent-start-time" and not "--log")) return false;

        values.TryGetValue("--log", out var logPath);
        if (logPath is not null && !IsUnderLocalData("Logs", logPath)) return false;
        handoff = new UpdateHandoff(Path.GetFullPath(installerPath), size, hash, pid, startTime, logPath);
        return true;
    }

    private static bool IsUnderLocalData(string directoryName, string path)
    {
        if (!Path.IsPathFullyQualified(path)) return false;
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FiveMCleaner",
            directoryName);
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
