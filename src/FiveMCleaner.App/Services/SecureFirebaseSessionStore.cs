using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FiveMCleaner.Contracts;

namespace FiveMCleaner.App.Services;

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
            return JsonSerializer.Deserialize<PersistedFirebaseSession>(json, FiveMCleanerJson.Options);
        }
        catch (Exception exception) when (exception is CryptographicException or IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    internal async Task WriteAsync(string refreshToken, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(new PersistedFirebaseSession(refreshToken), FiveMCleanerJson.Options);
            var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser);
            try { await File.WriteAllBytesAsync(path, encrypted, cancellationToken).ConfigureAwait(false); }
            finally { CryptographicOperations.ZeroMemory(encrypted); }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
        {
            // Best-effort: losing the persistent "keep me signed in" token
            // degrades to a manual login next launch, never a crash during
            // the signup/sign-in flow that created it.
        }
    }

    internal Task ClearAsync()
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        return Task.CompletedTask;
    }
}
