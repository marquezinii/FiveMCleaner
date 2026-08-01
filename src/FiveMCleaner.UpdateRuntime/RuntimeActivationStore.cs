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
        AtomicFile.WriteText(PointerPath, JsonSerializer.Serialize(new ActiveRuntime(version)));
    }

    public string ReadActiveVersion()
    {
        var active = JsonSerializer.Deserialize<ActiveRuntime>(ReadPointerTextWithTransientRetry())
            ?? throw new InvalidDataException("Ponteiro de runtime inválido.");
        if (!Version.TryParse(active.Version, out _) || !Directory.Exists(Path.Combine(VersionsRoot, active.Version)))
            throw new InvalidDataException("Ponteiro aponta para versão indisponível.");
        return active.Version;
    }

    // active.json é lido a cada inicialização do launcher enquanto outro
    // processo (uma instância em execução, ou este mesmo AtomicFile.ReplaceInto)
    // pode estar no meio de uma escrita atômica; um antivírus também pode
    // segurar o arquivo por poucos milissegundos. Sem essa tentativa curta, esse
    // lock transitório derrubaria a abertura do app inteiro em vez de só
    // esperar o processo concorrente terminar.
    private string ReadPointerTextWithTransientRetry()
    {
        const int maxAttempts = 15;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return File.ReadAllText(PointerPath);
            }
            catch (Exception exception) when (attempt < maxAttempts
                && exception is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(100);
            }
        }
    }

    private sealed record ActiveRuntime(string Version);
}
