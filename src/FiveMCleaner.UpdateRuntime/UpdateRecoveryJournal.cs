using System.Text.Json;
using System.Security.Cryptography;

namespace FiveMCleaner.UpdateRuntime;

public sealed record UpdateTransaction(
    string Id,
    string PreviousVersion,
    string CandidateVersion,
    DateTimeOffset CreatedAtUtc,
    string Nonce,
    DateTimeOffset? CandidateLaunchedAtUtc = null);

/// <summary>Durable recovery record; rollback never accepts an arbitrary version.</summary>
public sealed class UpdateRecoveryJournal
{
    private readonly string path;
    public UpdateRecoveryJournal(string runtimeRoot) => path = Path.Combine(Path.GetFullPath(runtimeRoot), "recovery.json");

    public UpdateTransaction Begin(string previousVersion, string candidateVersion)
    {
        if (!Version.TryParse(previousVersion, out var previous)
            || !Version.TryParse(candidateVersion, out var candidate)
            || candidate <= previous)
            throw new ArgumentException("Transação de atualização inválida.");
        var transaction = new UpdateTransaction(Guid.NewGuid().ToString("N"), previousVersion, candidateVersion, DateTimeOffset.UtcNow, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        Write(transaction);
        return transaction;
    }

    public UpdateTransaction Read()
    {
        var transaction = JsonSerializer.Deserialize<UpdateTransaction>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Journal de recuperação inválido.");
        if (transaction.Id.Length != 32 || !transaction.Id.All(char.IsAsciiHexDigit)
            || transaction.Nonce.Length != 64 || !transaction.Nonce.All(char.IsAsciiHexDigit)
            || !Version.TryParse(transaction.PreviousVersion, out var previous)
            || !Version.TryParse(transaction.CandidateVersion, out var candidate)
            || candidate <= previous)
            throw new InvalidDataException("Journal de recuperação inválido.");
        return transaction;
    }

    public UpdateTransaction MarkCandidateLaunched(UpdateTransaction transaction)
    {
        var updated = transaction with { CandidateLaunchedAtUtc = DateTimeOffset.UtcNow };
        Write(updated);
        return updated;
    }

    public bool TryRead(out UpdateTransaction transaction)
    {
        transaction = null!;
        if (!File.Exists(path)) return false;
        try
        {
            transaction = Read();
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            try { File.Move(path, $"{path}.corrupt.{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}", false); }
            catch (Exception moveException) when (moveException is IOException or UnauthorizedAccessException) { }
            return false;
        }
    }

    public void Complete()
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private void Write(UpdateTransaction transaction)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + $".{Guid.NewGuid():N}.new";
        File.WriteAllText(temporary, JsonSerializer.Serialize(transaction));
        try
        {
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }
}
