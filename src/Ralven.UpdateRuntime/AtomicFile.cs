namespace Ralven.UpdateRuntime;

/// <summary>
/// Writes a file atomically: stage the content in a sibling temp file, then
/// File.Replace/Move it into place, so a concurrent reader never observes a
/// partially written state file. Shared by every store that owns a single
/// mutable pointer file (active.json, recovery.json, health.json,
/// version-floor.dpapi).
/// </summary>
internal static class AtomicFile
{
    public static void WriteBytes(string path, byte[] bytes)
    {
        path = UpdatePathSafety.EnsureNoReparsePoints(path);
        EnsureDirectory(path);
        path = UpdatePathSafety.EnsureNoReparsePoints(path);
        var temporary = TemporaryPathFor(path);
        UpdatePathSafety.EnsureNoReparsePoints(temporary);
        File.WriteAllBytes(temporary, bytes);
        UpdatePathSafety.EnsureNoReparsePoints(path);
        ReplaceInto(path, temporary);
    }

    public static void WriteText(string path, string contents)
    {
        path = UpdatePathSafety.EnsureNoReparsePoints(path);
        EnsureDirectory(path);
        path = UpdatePathSafety.EnsureNoReparsePoints(path);
        var temporary = TemporaryPathFor(path);
        UpdatePathSafety.EnsureNoReparsePoints(temporary);
        File.WriteAllText(temporary, contents);
        UpdatePathSafety.EnsureNoReparsePoints(path);
        ReplaceInto(path, temporary);
    }

    private static void EnsureDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static string TemporaryPathFor(string path) => path + $".{Guid.NewGuid():N}.new";

    private static void ReplaceInto(string path, string temporary)
    {
        try
        {
            if (File.Exists(path))
            {
                try
                {
                    File.Replace(temporary, path, destinationBackupFileName: null);
                    return;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
                {
                    File.Move(temporary, path, overwrite: true);
                    return;
                }
            }

            File.Move(temporary, path);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }
}
