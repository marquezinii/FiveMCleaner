using System.Text.Json;

namespace Ralven.UpdateRuntime;

/// <summary>Owns the only mutable pointer in an otherwise immutable runtime.</summary>
public sealed class RuntimeActivationStore
{
    private readonly string runtimeRoot;

    public RuntimeActivationStore(string runtimeRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeRoot);
        this.runtimeRoot = UpdatePathSafety.EnsureNoReparsePoints(runtimeRoot);
    }

    public string VersionsRoot => Path.Combine(runtimeRoot, "versions");
    private string PointerPath => Path.Combine(runtimeRoot, "active.json");

    public void Activate(string version)
    {
        if (!Version.TryParse(version, out _)) throw new ArgumentException("Versão inválida.", nameof(version));
        var versionPath = Path.Combine(VersionsRoot, version);
        UpdatePathSafety.EnsureNoReparsePoints(versionPath);
        if (!Directory.Exists(versionPath)) throw new DirectoryNotFoundException("A versão candidata não está estagiada.");

        Directory.CreateDirectory(runtimeRoot);
        AtomicFile.WriteText(PointerPath, JsonSerializer.Serialize(new ActiveRuntime(version)));
    }

    public string ReadActiveVersion()
    {
        UpdatePathSafety.EnsureNoReparsePoints(PointerPath);
        var active = JsonSerializer.Deserialize<ActiveRuntime>(
            TransientRetry.Read(() => File.ReadAllText(PointerPath)))
            ?? throw new InvalidDataException("Ponteiro de runtime inválido.");
        var versionPath = Path.Combine(VersionsRoot, active.Version);
        if (!Version.TryParse(active.Version, out _) || !Directory.Exists(versionPath))
            throw new InvalidDataException("Ponteiro aponta para versão indisponível.");
        UpdatePathSafety.EnsureNoReparsePoints(versionPath);
        return active.Version;
    }

    public void PruneVersionsExcept(params string[] preservedVersions)
    {
        ArgumentNullException.ThrowIfNull(preservedVersions);
        if (preservedVersions.Length == 0
            || preservedVersions.Any(version => !Version.TryParse(version, out _)))
            throw new ArgumentException("Versões preservadas inválidas.", nameof(preservedVersions));

        var preserved = new HashSet<string>(preservedVersions, StringComparer.OrdinalIgnoreCase);
        try
        {
            preserved.Add(ReadActiveVersion());
            foreach (var directory in Directory.EnumerateDirectories(VersionsRoot))
            {
                var version = Path.GetFileName(directory);
                if (preserved.Contains(version) || !Version.TryParse(version, out _)) continue;
                try
                {
                    UpdatePathSafety.EnsureNoReparsePoints(directory);
                    Directory.Delete(directory, recursive: true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // A próxima atualização tenta novamente a pasta bloqueada.
                }
            }
        }
        catch (Exception exception) when (exception is DirectoryNotFoundException or IOException
            or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            // Limpeza é manutenção de espaço, não parte da ativação. Um AV ou
            // processo segurando a versão antiga não pode invalidar um update
            // já confirmado; a próxima atualização tenta novamente.
        }
    }

    public void PruneInactiveVersions()
    {
        try
        {
            var activeName = ReadActiveVersion();
            var activeVersion = Version.Parse(activeName);
            string? predecessorName = null;
            Version? predecessorVersion = null;
            foreach (var directory in Directory.EnumerateDirectories(VersionsRoot))
            {
                var name = Path.GetFileName(directory);
                UpdatePathSafety.EnsureNoReparsePoints(directory);
                if (!Version.TryParse(name, out var version)
                    || version >= activeVersion
                    || predecessorVersion is not null && version <= predecessorVersion) continue;
                predecessorName = name;
                predecessorVersion = version;
            }

            if (predecessorName is null)
                PruneVersionsExcept(activeName);
            else
                PruneVersionsExcept(activeName, predecessorName);
        }
        catch (Exception exception) when (exception is DirectoryNotFoundException or IOException
            or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            // A próxima abertura tenta novamente sem impedir o aplicativo.
        }
    }

    private sealed record ActiveRuntime(string Version);
}
