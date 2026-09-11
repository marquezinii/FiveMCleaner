using System.IO;
using System.Text.Json;
using Ralven.Contracts;
using Ralven.Windows.Infrastructure;

namespace Ralven.App.Services;

/// <summary>
/// Cache pequeno e descartável: acelera a próxima descoberta, mas nunca é
/// confiado sem revalidar a estrutura e o fingerprint local do executável.
/// </summary>
internal static class FiveMInstallationCache
{
    private sealed record Entry(string Root, long ExecutableLength, long ExecutableLastWriteUtcTicks);

    public static async Task<string?> ReadValidRootAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var entry = JsonSerializer.Deserialize<Entry>(
                await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false),
                RalvenJson.Options);
            if (entry is null
                || !FiveMInstallationLocator.TryValidateLegacyCandidate(
                    entry.Root,
                    FiveMInstallationSource.Cache,
                    out var installation))
            {
                return null;
            }

            var executable = new FileInfo(installation.ExecutablePath);
            return executable.Length == entry.ExecutableLength
                && executable.LastWriteTimeUtc.Ticks == entry.ExecutableLastWriteUtcTicks
                ? installation.Root
                : null;
        }
        catch (Exception exception) when (exception is IOException
            or JsonException
            or UnauthorizedAccessException
            or NotSupportedException)
        {
            return null;
        }
    }

    public static async Task WriteAsync(
        string path,
        FiveMInstallationInfo installation,
        CancellationToken cancellationToken)
    {
        var executable = new FileInfo(installation.ExecutablePath);
        var entry = new Entry(
            installation.Root,
            executable.Length,
            executable.LastWriteTimeUtc.Ticks);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(
                temporary,
                JsonSerializer.Serialize(entry, RalvenJson.Options),
                cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
