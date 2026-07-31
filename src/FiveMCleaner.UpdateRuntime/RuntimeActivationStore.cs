using System.Text.Json;

namespace FiveMCleaner.UpdateRuntime;

/// <summary>Owns the only mutable pointer in an otherwise immutable runtime.</summary>
public sealed class RuntimeActivationStore
{
    private readonly string runtimeRoot;

    public RuntimeActivationStore(string runtimeRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeRoot);
        this.runtimeRoot = Path.GetFullPath(runtimeRoot);
    }

    public string VersionsRoot => Path.Combine(runtimeRoot, "versions");
    private string PointerPath => Path.Combine(runtimeRoot, "active.json");

    public void Activate(string version)
    {
        if (!Version.TryParse(version, out _)) throw new ArgumentException("Versão inválida.", nameof(version));
        var versionPath = Path.Combine(VersionsRoot, version);
        if (!Directory.Exists(versionPath)) throw new DirectoryNotFoundException("A versão candidata não está estagiada.");

        Directory.CreateDirectory(runtimeRoot);
        var temporary = Path.Combine(runtimeRoot, $".active.{Guid.NewGuid():N}.json");
        File.WriteAllText(temporary, JsonSerializer.Serialize(new ActiveRuntime(version)));
        try
        {
            if (File.Exists(PointerPath)) File.Replace(temporary, PointerPath, destinationBackupFileName: null);
            else File.Move(temporary, PointerPath);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    public string ReadActiveVersion()
    {
        var active = JsonSerializer.Deserialize<ActiveRuntime>(File.ReadAllText(PointerPath))
            ?? throw new InvalidDataException("Ponteiro de runtime inválido.");
        if (!Version.TryParse(active.Version, out _) || !Directory.Exists(Path.Combine(VersionsRoot, active.Version)))
            throw new InvalidDataException("Ponteiro aponta para versão indisponível.");
        return active.Version;
    }

    private sealed record ActiveRuntime(string Version);
}
