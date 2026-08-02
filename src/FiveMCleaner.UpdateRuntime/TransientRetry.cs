namespace FiveMCleaner.UpdateRuntime;

/// <summary>
/// Short retry for transient file locks: a concurrent writer mid-replace or an
/// antivirus holding the file for a few milliseconds must not turn into a
/// closed app-launch failure. Shared by the stores that own mutable pointer
/// files (active.json, version-floor.dpapi).
/// </summary>
internal static class TransientRetry
{
    public static T Read<T>(Func<T> operation)
    {
        const int maxAttempts = 15;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return operation();
            }
            catch (Exception exception) when (attempt < maxAttempts
                && exception is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(100);
            }
        }
    }
}
