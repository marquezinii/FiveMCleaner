using System.Security.Cryptography;
using System.Text;

namespace FiveMCleaner.UpdateRuntime;

public sealed class VersionFloorStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("FiveMCleaner/version-floor/v1");
    private readonly string path;

    public VersionFloorStore(string dataRoot) =>
        path = Path.Combine(Path.GetFullPath(dataRoot), "UpdateSecurity", "version-floor.dpapi");

    public string Read(string fallbackVersion)
    {
        if (!Version.TryParse(fallbackVersion, out _))
            throw new ArgumentException("Versão de fallback inválida.", nameof(fallbackVersion));
        if (!File.Exists(path)) return fallbackVersion;
        try
        {
            var value = Encoding.UTF8.GetString(ProtectedData.Unprotect(
                File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser));
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
        var current = Version.Parse(Read(version));
        if (candidate <= current && File.Exists(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
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
