using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace FiveMCleaner.App.Services;

/// <summary>
/// Classifica apenas evidências locais explicitamente permitidas.
/// Não inspeciona argumentos, janelas, conexões, arquivos de configuração
/// nem qualquer estado capaz de indicar que uma pessoa está ao vivo.
/// </summary>
public static class StreamingSoftwareClassifier
{
    private static readonly IReadOnlyDictionary<string, StreamingSoftwareKind> ProcessNames =
        new Dictionary<string, StreamingSoftwareKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["obs64"] = StreamingSoftwareKind.ObsStudio,
            ["obs32"] = StreamingSoftwareKind.ObsStudio,
            ["Streamlabs Desktop"] = StreamingSoftwareKind.StreamlabsDesktop,
            ["TikTok LIVE Studio"] = StreamingSoftwareKind.TikTokLiveStudio,
            ["TikTokLiveStudio"] = StreamingSoftwareKind.TikTokLiveStudio
        };

    private static readonly IReadOnlyDictionary<string, StreamingSoftwareKind> InstalledProductNames =
        new Dictionary<string, StreamingSoftwareKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["OBS Studio"] = StreamingSoftwareKind.ObsStudio,
            ["OBS Studio (64bit)"] = StreamingSoftwareKind.ObsStudio,
            ["OBS Studio (32bit)"] = StreamingSoftwareKind.ObsStudio,
            ["Streamlabs Desktop"] = StreamingSoftwareKind.StreamlabsDesktop,
            ["TikTok LIVE Studio"] = StreamingSoftwareKind.TikTokLiveStudio
        };

    private static readonly (StreamingSoftwareKind Kind, string DisplayName)[] KnownSoftware =
    [
        (StreamingSoftwareKind.ObsStudio, "OBS Studio"),
        (StreamingSoftwareKind.StreamlabsDesktop, "Streamlabs Desktop"),
        (StreamingSoftwareKind.TikTokLiveStudio, "TikTok LIVE Studio")
    ];

    public static StreamingSoftwareKind? ClassifyProcessName(string? processName)
    {
        var normalized = NormalizeProcessName(processName);
        return normalized is not null && ProcessNames.TryGetValue(normalized, out var kind)
            ? kind
            : null;
    }

    public static StreamingSoftwareKind? ClassifyInstalledProductName(string? displayName)
    {
        var normalized = displayName?.Trim();
        return !string.IsNullOrEmpty(normalized)
               && InstalledProductNames.TryGetValue(normalized, out var kind)
            ? kind
            : null;
    }

    public static StreamingSoftwareSnapshot CreateSnapshot(
        IEnumerable<string?> runningProcessNames,
        IEnumerable<string?> installedProductNames,
        IEnumerable<StreamingSoftwareKind> installedExecutableKinds,
        DateTimeOffset observedAtUtc,
        bool processScanComplete = true,
        bool installationScanComplete = true)
    {
        ArgumentNullException.ThrowIfNull(runningProcessNames);
        ArgumentNullException.ThrowIfNull(installedProductNames);
        ArgumentNullException.ThrowIfNull(installedExecutableKinds);

        var runningKinds = runningProcessNames
            .Select(ClassifyProcessName)
            .Where(kind => kind.HasValue)
            .Select(kind => kind!.Value)
            .ToHashSet();

        var installedKinds = installedProductNames
            .Select(ClassifyInstalledProductName)
            .Where(kind => kind.HasValue)
            .Select(kind => kind!.Value)
            .Concat(installedExecutableKinds.Where(IsKnownKind))
            .ToHashSet();

        var applications = KnownSoftware
            .Select(software => new StreamingSoftwareStatus(
                software.Kind,
                software.DisplayName,
                installedKinds.Contains(software.Kind),
                runningKinds.Contains(software.Kind)))
            .ToArray();

        return new StreamingSoftwareSnapshot(
            applications,
            observedAtUtc.ToUniversalTime(),
            processScanComplete,
            installationScanComplete);
    }

    private static string? NormalizeProcessName(string? processName)
    {
        var normalized = processName?.Trim();
        if (string.IsNullOrEmpty(normalized)
            || normalized.IndexOfAny(['\\', '/', ':']) >= 0)
        {
            return null;
        }

        return normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? normalized[..^4]
            : normalized;
    }

    private static bool IsKnownKind(StreamingSoftwareKind kind)
    {
        return kind is StreamingSoftwareKind.ObsStudio
            or StreamingSoftwareKind.StreamlabsDesktop
            or StreamingSoftwareKind.TikTokLiveStudio;
    }
}

/// <summary>
/// Detector local, somente leitura e best-effort de software de transmissão.
/// A detecção informa somente instalação e processo em execução.
/// </summary>
public sealed class StreamingSoftwareDetector
{
    private const string UninstallRegistryPath =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public Task<StreamingSoftwareSnapshot> DetectAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Detect(cancellationToken), cancellationToken);
    }

    public StreamingSoftwareSnapshot Detect(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var processNames = new List<string>();
        var processScanComplete = CollectProcessNames(
            processNames,
            cancellationToken);

        var installedProductNames = new List<string>();
        var installationScanComplete = CollectInstalledProductNames(
            installedProductNames,
            cancellationToken);

        var executableKinds = CollectKnownExecutableEvidence(
            cancellationToken,
            ref installationScanComplete);

        return StreamingSoftwareClassifier.CreateSnapshot(
            processNames,
            installedProductNames,
            executableKinds,
            DateTimeOffset.UtcNow,
            processScanComplete,
            installationScanComplete);
    }

    private static bool CollectProcessNames(
        ICollection<string> destination,
        CancellationToken cancellationToken)
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (Exception exception) when (IsExpectedProbeException(exception))
        {
            return false;
        }

        var complete = true;
        foreach (var process in processes)
        {
            using (process)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    destination.Add(process.ProcessName);
                }
                catch (Exception exception) when (IsExpectedProbeException(exception))
                {
                    complete = false;
                }
            }
        }

        return complete;
    }

    private static bool CollectInstalledProductNames(
        ICollection<string> destination,
        CancellationToken cancellationToken)
    {
        var complete = true;
        var probes = new[]
        {
            (RegistryHive.CurrentUser, RegistryView.Registry64),
            (RegistryHive.CurrentUser, RegistryView.Registry32),
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32)
        };

        foreach (var (hive, view) in probes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CollectUninstallDisplayNames(
                    hive,
                    view,
                    destination,
                    cancellationToken))
            {
                complete = false;
            }
        }

        return complete;
    }

    private static bool CollectUninstallDisplayNames(
        RegistryHive hive,
        RegistryView view,
        ICollection<string> destination,
        CancellationToken cancellationToken)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstallKey = baseKey.OpenSubKey(
                UninstallRegistryPath,
                writable: false);
            if (uninstallKey is null)
            {
                return true;
            }

            var complete = true;
            string[] subKeyNames;
            try
            {
                subKeyNames = uninstallKey.GetSubKeyNames();
            }
            catch (Exception exception) when (IsExpectedProbeException(exception))
            {
                return false;
            }

            foreach (var subKeyName in subKeyNames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var entry = uninstallKey.OpenSubKey(subKeyName, writable: false);
                    if (entry?.GetValue("DisplayName") is string displayName
                        && StreamingSoftwareClassifier.ClassifyInstalledProductName(displayName)
                            is not null)
                    {
                        destination.Add(displayName);
                    }
                }
                catch (Exception exception) when (IsExpectedProbeException(exception))
                {
                    complete = false;
                }
            }

            return complete;
        }
        catch (Exception exception) when (IsExpectedProbeException(exception))
        {
            return false;
        }
    }

    private static IReadOnlyCollection<StreamingSoftwareKind> CollectKnownExecutableEvidence(
        CancellationToken cancellationToken,
        ref bool installationScanComplete)
    {
        var installedKinds = new HashSet<StreamingSoftwareKind>();
        foreach (var candidate in BuildKnownExecutableCandidates())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryGetFileExists(candidate.Path, out var exists))
            {
                if (exists)
                {
                    installedKinds.Add(candidate.Kind);
                }
            }
            else
            {
                installationScanComplete = false;
            }
        }

        return installedKinds;
    }

    private static bool TryGetFileExists(string path, out bool exists)
    {
        try
        {
            _ = File.GetAttributes(path);
            exists = true;
            return true;
        }
        catch (FileNotFoundException)
        {
            exists = false;
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            exists = false;
            return true;
        }
        catch (Exception exception) when (IsExpectedProbeException(exception))
        {
            exists = false;
            return false;
        }
    }

    private static IEnumerable<(StreamingSoftwareKind Kind, string Path)>
        BuildKnownExecutableCandidates()
    {
        var programFiles = Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFilesX86);
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        foreach (var root in NonEmptyDistinctRoots(programFiles, programFilesX86))
        {
            yield return (
                StreamingSoftwareKind.ObsStudio,
                Path.Combine(root, "obs-studio", "bin", "64bit", "obs64.exe"));
            yield return (
                StreamingSoftwareKind.ObsStudio,
                Path.Combine(root, "obs-studio", "bin", "32bit", "obs32.exe"));
            yield return (
                StreamingSoftwareKind.StreamlabsDesktop,
                Path.Combine(root, "Streamlabs Desktop", "Streamlabs Desktop.exe"));
            yield return (
                StreamingSoftwareKind.TikTokLiveStudio,
                Path.Combine(root, "TikTok LIVE Studio", "TikTok LIVE Studio.exe"));
        }

        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            yield return (
                StreamingSoftwareKind.ObsStudio,
                Path.Combine(
                    localAppData,
                    "Programs",
                    "obs-studio",
                    "bin",
                    "64bit",
                    "obs64.exe"));
            yield return (
                StreamingSoftwareKind.StreamlabsDesktop,
                Path.Combine(
                    localAppData,
                    "Programs",
                    "streamlabs-desktop",
                    "Streamlabs Desktop.exe"));
            yield return (
                StreamingSoftwareKind.TikTokLiveStudio,
                Path.Combine(
                    localAppData,
                    "Programs",
                    "TikTok LIVE Studio",
                    "TikTok LIVE Studio.exe"));
            yield return (
                StreamingSoftwareKind.TikTokLiveStudio,
                Path.Combine(
                    localAppData,
                    "TikTok LIVE Studio",
                    "TikTok LIVE Studio.exe"));
        }
    }

    private static IEnumerable<string> NonEmptyDistinctRoots(params string[] roots)
    {
        return roots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsExpectedProbeException(Exception exception)
    {
        return exception is UnauthorizedAccessException
            or SecurityException
            or Win32Exception
            or InvalidOperationException
            or NotSupportedException
            or IOException;
    }
}
