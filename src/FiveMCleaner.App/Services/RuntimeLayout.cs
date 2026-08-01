using System.IO;

namespace FiveMCleaner.App.Services;

/// <summary>
/// Resolves whether the running process sits under the transactional runtime
/// layout (<c>{installRoot}/Runtime/versions/{version}/</c>, owned by
/// <c>FiveMCleaner.Launcher.exe</c>) or is running standalone (legacy
/// Inno-Setup-only install, dev run, or a unit test). Both call sites in
/// <c>MainWindow</c> (choosing the update service, and confirming a
/// post-update health receipt) need the exact same answer, so this is the
/// single place that walks the directory chain.
/// </summary>
public static class RuntimeLayout
{
    public static RuntimeLayoutResult Resolve(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        // baseDirectory is ".../Runtime/versions/{version}/" -- one GetParent
        // per directory level climbed, verifying each expected name on the
        // way up so a non-matching layout (legacy install, dev run) safely
        // resolves to "not the transactional runtime" instead of guessing.
        var versionsDirectory = Directory.GetParent(Path.TrimEndingDirectorySeparator(baseDirectory));
        var runtimeDirectory = versionsDirectory?.Parent;
        var installRoot = runtimeDirectory?.Parent;

        if (versionsDirectory?.Name.Equals("versions", StringComparison.OrdinalIgnoreCase) != true
            || runtimeDirectory?.Name.Equals("Runtime", StringComparison.OrdinalIgnoreCase) != true
            || installRoot is null)
        {
            return new RuntimeLayoutResult(null, null);
        }

        return new RuntimeLayoutResult(installRoot.FullName, runtimeDirectory.FullName);
    }
}

public sealed record RuntimeLayoutResult(string? InstallRoot, string? RuntimeRoot);
