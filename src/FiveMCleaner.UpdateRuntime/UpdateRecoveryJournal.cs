using System.Text.Json;
using System.Security.Cryptography;

namespace FiveMCleaner.UpdateRuntime;

public sealed record UpdateTransaction(string Id, string PreviousVersion, string CandidateVersion, DateTimeOffset CreatedAtUtc, string Nonce);

/// <summary>Durable recovery record; rollback never accepts an arbitrary version.</summary>
public sealed class UpdateRecoveryJournal
{
    private readonly string path;
    public UpdateRecoveryJournal(string runtimeRoot) => path = Path.Combine(Path.GetFullPath(runtimeRoot), "recovery.json");

    public UpdateTransaction Begin(string previousVersion, string candidateVersion)
    {
        if (!Version.TryParse(previousVersion, out _) || !Version.TryParse(candidateVersion, out _) || candidateVersion <= previousVersion)
            throw new ArgumentException("Transação de atualização inválida.");
        var transaction = new UpdateTransaction(Guid.NewGuid().ToString("N"), previousVersion, candidateVersion, DateTimeOffset.UtcNow, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".new";
        File.WriteAllText(temporary, JsonSerializer.Serialize(transaction));
        if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        return transaction;
    }

    public UpdateTransaction Read() => JsonSerializer.Deserialize<UpdateTransaction>(File.ReadAllText(path))
        ?? throw new InvalidDataException("Journal de recuperação inválido.");
}
