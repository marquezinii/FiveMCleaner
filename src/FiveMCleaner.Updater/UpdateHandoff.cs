using System.Globalization;

namespace FiveMCleaner.Updater;

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
