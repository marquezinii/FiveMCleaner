using System.Globalization;
using System.Text;

namespace FiveMCleaner.Broker;

/// <summary>
/// Tiny, local-only, append-and-flush-per-line lifecycle log for the
/// elevated broker process. Exists because the broker can be killed
/// mid-run by antivirus/SmartScreen reacting to an elevated, unsigned
/// executable (see the 24/07/2026 "balanced profile fails without admin"
/// investigation in PROJECT_STATE.md) -- when that happens the pipe never
/// gets a terminal event, so the app-side log alone cannot tell "cancelled
/// by the user", "pipe never connected" and "killed after starting" apart.
/// Each line is flushed to disk immediately (<see cref="FileOptions.WriteThrough"/>)
/// so the last recorded step survives even if the process dies right after.
///
/// Never writes anything beyond an event name, a timestamp and the
/// transaction ID (already an opaque GUID, not personal data) -- no paths,
/// no usernames, no plan contents.
/// </summary>
internal static class BrokerDiagnosticsLog
{
    private const long MaximumBytes = 256 * 1024;
    private static readonly object Gate = new();

    public static void Record(string eventName, Guid? transactionId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        try
        {
            var path = ResolvePath();
            var directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);

            var line = string.Create(
                CultureInfo.InvariantCulture,
                $"{DateTimeOffset.UtcNow:O} {eventName}{(transactionId is { } id ? $" transactionId={id:N}" : string.Empty)}\n");

            lock (Gate)
            {
                TrimIfOversizedNoLock(path);
                using var stream = new FileStream(
                    path,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.Read,
                    4096,
                    FileOptions.WriteThrough);
                var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(line);
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException)
        {
            // Diagnostics are best-effort; never let logging break the broker itself.
        }
    }

    private static void TrimIfOversizedNoLock(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length <= MaximumBytes)
        {
            return;
        }

        try
        {
            var lines = File.ReadAllLines(path);
            var kept = lines.Length > 200 ? lines[^200..] : lines;
            File.WriteAllLines(path, kept);
        }
        catch (IOException)
        {
        }
    }

    private static string ResolvePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "FiveMCleaner", "Logs", "broker-diagnostics.log");
    }
}
