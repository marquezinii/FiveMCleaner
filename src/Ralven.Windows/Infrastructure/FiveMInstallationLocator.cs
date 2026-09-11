using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;

namespace Ralven.Windows.Infrastructure;

public enum FiveMInstallationSource
{
    Manual = 0,
    RunningProcess = 1,
    Cache = 2,
    AppPath = 3,
    Registry = 4,
    Shortcut = 5,
    DefaultLocation = 6
}

public enum FiveMInstallationDetectionStatus
{
    NotFound,
    Legacy,
    Enhanced,
    Ambiguous
}

public sealed record FiveMInstallationCandidate(string Path, FiveMInstallationSource Source);

public sealed record FiveMInstallationInfo(
    string Root,
    string ExecutablePath,
    string AppRoot,
    FiveMInstallationSource Source);

public sealed record FiveMInstallationDetectionResult(
    FiveMInstallationDetectionStatus Status,
    FiveMInstallationInfo? Installation,
    IReadOnlyList<FiveMInstallationInfo> Candidates)
{
    public bool IsLegacy => Status == FiveMInstallationDetectionStatus.Legacy;
}

/// <summary>
/// Discovers a FiveM Legacy installation without treating an arbitrary folder
/// named FiveM as a game client. All callers share this validation boundary.
/// </summary>
public sealed class FiveMInstallationLocator
{
    private const string UninstallKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string AppPathsKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\FiveM.exe";
    private const int MaximumShortcutCandidates = 256;
    private const int MaximumShortcutDirectories = 512;

    public FiveMInstallationDetectionResult Detect(
        string? manualRoot = null,
        string? cachedRoot = null)
    {
        var candidates = new List<FiveMInstallationCandidate>();
        AddCandidate(candidates, manualRoot, FiveMInstallationSource.Manual);
        AddCandidate(candidates, cachedRoot, FiveMInstallationSource.Cache);

        if (OperatingSystem.IsWindows())
        {
            AddRunningProcessCandidates(candidates);
            AddRegistryCandidates(candidates);
            AddShortcutCandidates(candidates);
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            AddCandidate(candidates, Path.Combine(localAppData, "FiveM"), FiveMInstallationSource.DefaultLocation);
        }
        AddKnownDirectoryCandidates(candidates);

        var result = DetectCandidates(candidates);
        if (result.Status != FiveMInstallationDetectionStatus.NotFound)
        {
            return result;
        }

        return HasEnhancedInstallation() ? result with { Status = FiveMInstallationDetectionStatus.Enhanced } : result;
    }

    public static FiveMInstallationDetectionResult DetectCandidates(
        IEnumerable<FiveMInstallationCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var valid = new Dictionary<string, FiveMInstallationInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (!TryValidateLegacyCandidate(candidate.Path, candidate.Source, out var installation))
            {
                continue;
            }

            if (!valid.TryGetValue(installation.Root, out var current)
                || installation.Source < current.Source)
            {
                valid[installation.Root] = installation;
            }
        }

        var ordered = valid.Values
            .OrderBy(candidate => candidate.Source)
            .ThenBy(candidate => candidate.Root, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (ordered.Length == 0)
        {
            return new FiveMInstallationDetectionResult(
                FiveMInstallationDetectionStatus.NotFound,
                null,
                []);
        }

        var strongestSource = ordered[0].Source;
        var strongest = ordered.Where(candidate => candidate.Source == strongestSource).ToArray();
        return strongest.Length == 1
            && (strongestSource is FiveMInstallationSource.Manual
                or FiveMInstallationSource.RunningProcess
                or FiveMInstallationSource.Cache
                || ordered.Length == 1)
            ? new FiveMInstallationDetectionResult(
                FiveMInstallationDetectionStatus.Legacy,
                strongest[0],
                ordered)
            : new FiveMInstallationDetectionResult(
                FiveMInstallationDetectionStatus.Ambiguous,
                null,
                ordered);
    }

    public static bool TryValidateLegacyCandidate(
        string? candidate,
        FiveMInstallationSource source,
        out FiveMInstallationInfo installation)
    {
        installation = null!;
        if (!TryResolveRoot(candidate, out var root))
        {
            return false;
        }

        var executable = Path.Combine(root, "FiveM.exe");
        var appRoot = Path.Combine(root, "FiveM.app");
        var dataRoot = Path.Combine(appRoot, "data");
        try
        {
            if (!Directory.Exists(root)
                || !File.Exists(executable)
                || !Directory.Exists(appRoot)
                || !Directory.Exists(dataRoot)
                || IsReparsePoint(root)
                || IsReparsePoint(executable)
                || IsReparsePoint(appRoot)
                || IsReparsePoint(dataRoot))
            {
                return false;
            }

            _ = SafePath.EnsureNoReparsePoints(root);
            _ = SafePath.EnsureNoReparsePoints(executable);
            _ = SafePath.EnsureNoReparsePoints(appRoot);
            _ = SafePath.EnsureNoReparsePoints(dataRoot);
            _ = SafePath.EnsureDescendant(root, executable);
            _ = SafePath.EnsureDescendant(root, appRoot);
            _ = SafePath.EnsureDescendant(root, dataRoot);
            installation = new FiveMInstallationInfo(root, executable, appRoot, source);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException
            or IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryResolveRoot(string? candidate, out string root)
    {
        root = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(candidate.Trim().Trim('"'));
            if (Path.GetFileName(fullPath).Equals("FiveM.exe", StringComparison.OrdinalIgnoreCase))
            {
                root = Path.GetDirectoryName(fullPath) ?? string.Empty;
            }
            else if (Path.GetFileName(fullPath).Equals("FiveM.app", StringComparison.OrdinalIgnoreCase))
            {
                root = Path.GetDirectoryName(fullPath) ?? string.Empty;
            }
            else if (Path.GetDirectoryName(fullPath) is { } appDirectory
                && Path.GetFileName(fullPath).Equals("data", StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(appDirectory).Equals("FiveM.app", StringComparison.OrdinalIgnoreCase))
            {
                root = Path.GetDirectoryName(appDirectory) ?? string.Empty;
            }
            else if (Path.GetExtension(fullPath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            {
                root = FindContainingRoot(Path.GetDirectoryName(fullPath));
            }
            else
            {
                root = fullPath;
            }

            root = SafePath.Normalize(root);
            return !string.IsNullOrWhiteSpace(root);
        }
        catch (Exception exception) when (exception is ArgumentException
            or NotSupportedException
            or IOException)
        {
            return false;
        }
    }

    private static string FindContainingRoot(string? directory)
    {
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, "FiveM.exe"))
                && Directory.Exists(Path.Combine(directory, "FiveM.app")))
            {
                return directory;
            }

            directory = Path.GetDirectoryName(directory);
        }

        return string.Empty;
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static void AddCandidate(
        ICollection<FiveMInstallationCandidate> candidates,
        string? path,
        FiveMInstallationSource source)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            candidates.Add(new FiveMInstallationCandidate(path, source));
        }
    }

    private static void AddKnownDirectoryCandidates(ICollection<FiveMInstallationCandidate> candidates)
    {
        foreach (var programFiles in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        })
        {
            if (!string.IsNullOrWhiteSpace(programFiles))
            {
                AddCandidate(candidates, Path.Combine(programFiles, "FiveM"), FiveMInstallationSource.DefaultLocation);
            }
        }

        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType == DriveType.Fixed && drive.IsReady)
                {
                    AddCandidate(candidates, Path.Combine(drive.RootDirectory.FullName, "FiveM"), FiveMInstallationSource.DefaultLocation);
                }
            }
            catch (IOException)
            {
                // An unavailable volume is not a reason to stop discovery on other disks.
            }
        }
    }

    private static void AddRunningProcessCandidates(ICollection<FiveMInstallationCandidate> candidates)
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return;
        }

        foreach (var process in processes)
        {
            using (process)
            {
                try
                {
                    if (!WindowsFiveMProcessInspector.LooksLikeFiveMProcessName(process.ProcessName))
                    {
                        continue;
                    }

                    var imagePath = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(imagePath))
                    {
                        AddCandidate(candidates, imagePath, FiveMInstallationSource.RunningProcess);
                    }
                }
                catch (Exception exception) when (exception is InvalidOperationException
                    or System.ComponentModel.Win32Exception
                    or NotSupportedException)
                {
                    // A protected process is not a reliable installation path.
                }
            }
        }
    }

    private static void AddRegistryCandidates(ICollection<FiveMInstallationCandidate> candidates)
    {
        AddAppPathCandidate(RegistryHive.CurrentUser, RegistryView.Default, candidates);
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            AddAppPathCandidate(RegistryHive.LocalMachine, view, candidates);
            AddUninstallCandidates(RegistryHive.LocalMachine, view, candidates);
            AddUninstallCandidates(RegistryHive.CurrentUser, view, candidates);
        }
    }

    private static void AddAppPathCandidate(
        RegistryHive hive,
        RegistryView view,
        ICollection<FiveMInstallationCandidate> candidates)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(AppPathsKeyPath);
            AddCandidate(candidates, key?.GetValue(null) as string, FiveMInstallationSource.AppPath);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
            or SecurityException
            or IOException)
        {
        }
    }

    private static void AddUninstallCandidates(
        RegistryHive hive,
        RegistryView view,
        ICollection<FiveMInstallationCandidate> candidates)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = baseKey.OpenSubKey(UninstallKeyPath);
            if (uninstall is null)
            {
                return;
            }

            foreach (var subkeyName in uninstall.GetSubKeyNames())
            {
                using var subkey = uninstall.OpenSubKey(subkeyName);
                if (subkey?.GetValue("DisplayName") is not string displayName
                    || string.IsNullOrWhiteSpace(displayName)
                    || !displayName.Contains("FiveM", StringComparison.OrdinalIgnoreCase)
                    || displayName.Contains("Enhanced", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                AddCandidate(candidates, subkey.GetValue("InstallLocation") as string, FiveMInstallationSource.Registry);
                AddCandidate(candidates, ExtractExecutablePath(subkey.GetValue("DisplayIcon") as string), FiveMInstallationSource.Registry);
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
            or SecurityException
            or IOException)
        {
        }
    }

    private static string? ExtractExecutablePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith('"'))
        {
            var closingQuote = trimmed.IndexOf('"', 1);
            return closingQuote > 1 ? trimmed[1..closingQuote] : null;
        }

        var separator = trimmed.IndexOf(',');
        return separator >= 0 ? trimmed[..separator].Trim() : trimmed;
    }

    private static void AddShortcutCandidates(ICollection<FiveMInstallationCandidate> candidates)
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
        };
        var remaining = MaximumShortcutCandidates;
        foreach (var root in roots.Where(static path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var shortcut in EnumerateFiveMShortcuts(root, ref remaining))
            {
                AddCandidate(candidates, ResolveShortcutTarget(shortcut), FiveMInstallationSource.Shortcut);
            }
        }
    }

    private static IReadOnlyList<string> EnumerateFiveMShortcuts(string root, ref int remaining)
    {
        var shortcuts = new List<string>();
        if (remaining <= 0 || !Directory.Exists(root))
        {
            return shortcuts;
        }

        var directories = new Stack<string>();
        directories.Push(root);
        var visitedDirectories = 0;
        while (directories.Count > 0 && remaining > 0 && visitedDirectories < MaximumShortcutDirectories)
        {
            var directory = directories.Pop();
            visitedDirectories++;
            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(directory);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                try
                {
                    if (IsReparsePoint(entry))
                    {
                        continue;
                    }

                    if (Directory.Exists(entry))
                    {
                        directories.Push(entry);
                    }
                    else if (Path.GetExtension(entry).Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                        && Path.GetFileNameWithoutExtension(entry).Contains("FiveM", StringComparison.OrdinalIgnoreCase))
                    {
                        remaining--;
                        shortcuts.Add(entry);
                    }
                }
                catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
                {
                }
            }
        }

        return shortcuts;
    }

    private static string? ResolveShortcutTarget(string shortcutPath)
    {
        object? shell = null;
        object? shortcut = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return null;
            }

            shell = Activator.CreateInstance(shellType);
            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                shell,
                [shortcutPath]);
            return shortcut?.GetType().InvokeMember(
                "TargetPath",
                System.Reflection.BindingFlags.GetProperty,
                null,
                shortcut,
                null) as string;
        }
        catch (Exception exception) when (exception is COMException
            or InvalidOperationException
            or System.Reflection.TargetInvocationException)
        {
            return null;
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut))
            {
                Marshal.FinalReleaseComObject(shortcut);
            }

            if (shell is not null && Marshal.IsComObject(shell))
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    private static bool HasEnhancedInstallation()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData)
            && HasFiveMExecutable(Path.Combine(localAppData, "FiveM Enhanced")))
        {
            return true;
        }

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var uninstall = baseKey.OpenSubKey(UninstallKeyPath);
                    if (uninstall is null)
                    {
                        continue;
                    }

                    foreach (var subkeyName in uninstall.GetSubKeyNames())
                    {
                        using var subkey = uninstall.OpenSubKey(subkeyName);
                        if (subkey?.GetValue("DisplayName") is string displayName
                            && displayName.Contains("FiveM", StringComparison.OrdinalIgnoreCase)
                            && displayName.Contains("Enhanced", StringComparison.OrdinalIgnoreCase)
                            && subkey.GetValue("InstallLocation") is string location
                            && HasFiveMExecutable(location))
                        {
                            return true;
                        }
                    }
                }
                catch (Exception exception) when (exception is UnauthorizedAccessException
                    or SecurityException
                    or IOException)
                {
                }
            }
        }

        return false;
    }

    private static bool HasFiveMExecutable(string root)
    {
        try
        {
            return Directory.Exists(root)
                && !IsReparsePoint(root)
                && Directory.EnumerateFiles(root, "FiveM*.exe", SearchOption.TopDirectoryOnly).Any();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
