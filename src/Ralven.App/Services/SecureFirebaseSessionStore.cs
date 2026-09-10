using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ralven.Contracts;

namespace Ralven.App.Services;

/// <summary>Persists only a refresh token, encrypted for the current Windows user.</summary>
public sealed class SecureFirebaseSessionStore
{
    private readonly string path;

    public SecureFirebaseSessionStore(string path) => this.path = path;

    internal async Task<PersistedFirebaseSession?> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var encrypted = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            var json = Encoding.UTF8.GetString(ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser));
            return JsonSerializer.Deserialize<PersistedFirebaseSession>(json, RalvenJson.Options);
        }
        catch (Exception exception) when (exception is CryptographicException or IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    internal async Task WriteAsync(string refreshToken, CancellationToken cancellationToken)
    {
        string? temporaryPath = null;
        byte[]? plaintext = null;
        byte[]? encrypted = null;
        try
        {
            var directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(new PersistedFirebaseSession(refreshToken), RalvenJson.Options);
            plaintext = Encoding.UTF8.GetBytes(json);
            encrypted = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);
            temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
            await File.WriteAllBytesAsync(temporaryPath, encrypted, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
        {
            // Best-effort: losing the persistent "keep me signed in" token
            // degrades to a manual login next launch, never a crash during
            // the signup/sign-in flow that created it.
        }
        finally
        {
            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
            if (encrypted is not null)
            {
                CryptographicOperations.ZeroMemory(encrypted);
            }
            if (temporaryPath is not null)
            {
                try { File.Delete(temporaryPath); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // A stale, DPAPI-protected temporary file is safer than
                    // masking the completed sign-in with a cleanup failure.
                }
            }
        }
    }

    internal Task ClearAsync()
    {
        File.Delete(path);
        if (File.Exists(path))
        {
            throw new IOException("The persisted Firebase session could not be removed.");
        }

        return Task.CompletedTask;
    }
}
