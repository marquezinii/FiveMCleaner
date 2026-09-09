using System.Security.Cryptography;
using System.Text;

namespace Ralven.UpdateRuntime;

public sealed class VersionFloorStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Ralven/version-floor/v1");
    private readonly string path;

    public VersionFloorStore(string dataRoot) =>
        path = Path.Combine(UpdatePathSafety.EnsureNoReparsePoints(dataRoot), "UpdateSecurity", "version-floor.dpapi");

    public string Read(string fallbackVersion)
    {
        if (!Version.TryParse(fallbackVersion, out _))
            throw new ArgumentException("Versão de fallback inválida.", nameof(fallbackVersion));
        try
        {
            UpdatePathSafety.EnsureNoReparsePoints(path);
            if (!File.Exists(path)) return fallbackVersion;
            var value = Encoding.UTF8.GetString(ProtectedData.Unprotect(
                TransientRetry.Read(() => File.ReadAllBytes(path)), Entropy, DataProtectionScope.CurrentUser));
            return Version.TryParse(value, out _) ? value : throw new CryptographicException("Piso de versão inválido.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
        {
            throw new CryptographicException("O estado anti-downgrade não pôde ser validado.", exception);
        }
    }

    public void Advance(string version)
    {
        if (!Version.TryParse(version, out var candidate))
            throw new ArgumentException("Versão inválida.", nameof(version));
        var current = Version.Parse(Read("0.0.0"));
        if (candidate <= current) return;
        UpdatePathSafety.EnsureNoReparsePoints(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        UpdatePathSafety.EnsureNoReparsePoints(path);
        var encrypted = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(version), Entropy, DataProtectionScope.CurrentUser);
        try
        {
            AtomicFile.WriteBytes(path, encrypted);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encrypted);
        }
    }
}
