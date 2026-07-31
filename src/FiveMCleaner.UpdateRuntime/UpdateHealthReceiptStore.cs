using System.Text.Json;

namespace FiveMCleaner.UpdateRuntime;

public sealed class UpdateHealthReceiptStore
{
    private readonly string path;
    public UpdateHealthReceiptStore(string runtimeRoot) => path = Path.Combine(Path.GetFullPath(runtimeRoot), "health.json");

    public void Confirm(UpdateTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var receipt = new HealthReceipt(transaction.Id, transaction.CandidateVersion, transaction.Nonce, DateTimeOffset.UtcNow);
        var temporary = path + ".new";
        File.WriteAllText(temporary, JsonSerializer.Serialize(receipt));
        if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
    }

    public bool Confirms(UpdateTransaction transaction)
    {
        if (!File.Exists(path)) return false;
        var receipt = JsonSerializer.Deserialize<HealthReceipt>(File.ReadAllText(path));
        return receipt is not null && receipt.TransactionId == transaction.Id
            && receipt.Version == transaction.CandidateVersion && receipt.Nonce == transaction.Nonce;
    }

    private sealed record HealthReceipt(string TransactionId, string Version, string Nonce, DateTimeOffset ConfirmedAtUtc);
}
