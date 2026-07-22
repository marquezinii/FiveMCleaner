using Microsoft.Win32;

namespace FiveMCleaner.Windows.Infrastructure;

public sealed record GtaVInstallationInfo
{
    public string? InstallationRoot { get; init; }

    public string? ExecutablePath { get; init; }

    public required string GraphicsSettingsPath { get; init; }

    public bool IsInstalled => InstallationRoot is not null && ExecutablePath is not null;
}

public static class GtaVLocator
{
    public static GtaVInstallationInfo Detect(string? fiveMInstallationRoot = null)
    {
        var candidates = new List<string>();
        AddCitizenFxCandidate(candidates, fiveMInstallationRoot);
        AddRegistryCandidates(candidates);

        string? detectedRoot = null;
        string? executablePath = null;
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var root = Path.GetFullPath(candidate.Trim().Trim('"'));
                var executable = Path.Combine(root, "GTA5.exe");
                if (!File.Exists(executable))
                {
                    continue;
                }

                detectedRoot = root;
                executablePath = Path.GetFullPath(executable);
                break;
            }
            catch (Exception exception) when (exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
            {
            }
        }

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(documents))
        {
            throw new InvalidOperationException("A pasta Documentos do Windows não está disponível.");
        }

        return new GtaVInstallationInfo
        {
            InstallationRoot = detectedRoot,
            ExecutablePath = executablePath,
            GraphicsSettingsPath = Path.Combine(
                documents,
                "Rockstar Games",
                "GTA V",
                "settings.xml")
        };
    }

    internal static string? ReadIvPath(string citizenFxIniPath)
    {
        if (!File.Exists(citizenFxIniPath)
            || new FileInfo(citizenFxIniPath).Length > 1024 * 1024)
        {
            return null;
        }

        foreach (var line in File.ReadLines(citizenFxIniPath).Take(500))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0
                || !line[..separator].Trim().Equals("IVPath", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = line[(separator + 1)..].Trim().Trim('"');
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return null;
    }

    private static void AddCitizenFxCandidate(ICollection<string> candidates, string? fiveMRoot)
    {
        if (string.IsNullOrWhiteSpace(fiveMRoot))
        {
            return;
        }

        foreach (var iniPath in new[]
                 {
                     Path.Combine(fiveMRoot, "FiveM.app", "CitizenFX.ini"),
                     Path.Combine(fiveMRoot, "CitizenFX.ini")
                 })
        {
            try
            {
                var value = ReadIvPath(iniPath);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    candidates.Add(value);
                }
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException)
            {
            }
        }
    }

    private static void AddRegistryCandidates(ICollection<string> candidates)
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using (var rockstar = localMachine.OpenSubKey(
                           @"SOFTWARE\Rockstar Games\Grand Theft Auto V"))
                {
                    AddRegistryValue(candidates, rockstar, "InstallFolder");
                    AddRegistryValue(candidates, rockstar, "InstallFolderSteam");
                }

                using var uninstall = localMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null)
                {
                    continue;
                }

                foreach (var subKeyName in uninstall.GetSubKeyNames())
                {
                    using var entry = uninstall.OpenSubKey(subKeyName);
                    var displayName = entry?.GetValue("DisplayName") as string;
                    if (string.IsNullOrWhiteSpace(displayName)
                        || displayName.Contains("Enhanced", StringComparison.OrdinalIgnoreCase)
                        || !(displayName.Contains("Grand Theft Auto V", StringComparison.OrdinalIgnoreCase)
                            || displayName.Contains("GTA V Legacy", StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    AddRegistryValue(candidates, entry, "InstallLocation");
                }
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException
                or System.Security.SecurityException
                or IOException)
            {
            }
        }
    }

    private static void AddRegistryValue(
        ICollection<string> candidates,
        RegistryKey? key,
        string valueName)
    {
        if (key?.GetValue(valueName) is string value && !string.IsNullOrWhiteSpace(value))
        {
            candidates.Add(value);
        }
    }
}
