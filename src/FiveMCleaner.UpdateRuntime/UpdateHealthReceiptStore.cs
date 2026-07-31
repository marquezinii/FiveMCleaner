using System.Text.Json;

namespace FiveMCleaner.UpdateRuntime;

public sealed class UpdateHealthReceiptStore
{
    private readonly string path;
    public UpdateHealthReceiptStore(string runtimeRoot) => path = Path.Combine(Path.GetFullPath(runtimeRoot), "health.json");

    public void Confirm(UpdateTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        Confirm(transaction.Id, transaction.CandidateVersion, transaction.Nonce);
    }

    public void Confirm(string transactionId, string version, string nonce)
    {
        if (transactionId.Length != 32 || !transactionId.All(char.IsAsciiHexDigit)
            || !Version.TryParse(version, out _) || nonce.Length != 64 || !nonce.All(char.IsAsciiHexDigit))
            throw new ArgumentException("Recibo de saúde inválido.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var receipt = new HealthReceipt(transactionId, version, nonce, DateTimeOffset.UtcNow);
        AtomicFile.WriteText(path, JsonSerializer.Serialize(receipt));
    }

    public bool Confirms(UpdateTransaction transaction)
    {
        if (!File.Exists(path)) return false;
        try
        {
            var receipt = JsonSerializer.Deserialize<HealthReceipt>(File.ReadAllText(path));
            return receipt is not null && receipt.TransactionId == transaction.Id
                && receipt.Version == transaction.CandidateVersion && receipt.Nonce == transaction.Nonce;
        }
        // A transient read failure (e.g. another process mid-File.Replace, or an
        // AV scan holding a short-lived lock) is not proof the receipt is
        // missing or invalid; treat it the same as "not confirmed yet" so a
        // momentary lock never fails the health check or triggers a rollback.
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed record HealthReceipt(string TransactionId, string Version, string Nonce, DateTimeOffset ConfirmedAtUtc);
}
