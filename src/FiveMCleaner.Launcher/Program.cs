using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FiveMCleaner.UpdateRuntime;

namespace FiveMCleaner.Launcher;

internal static class Program
{
    private static readonly TimeSpan HealthTimeout = TimeSpan.FromSeconds(45);

    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        var forwardedArguments = args
            .Where(argument => !argument.StartsWith("--wait-for-pid=", StringComparison.OrdinalIgnoreCase)
                && !argument.StartsWith("--wait-for-start=", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var runtimeRoot = Path.Combine(AppContext.BaseDirectory, "Runtime");
        var dataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FiveMCleaner");
        var diagnostics = new UpdaterDiagnostics(
            dataRoot,
            new Uri("https://fivemcleaner-telemetry.felipemarquesini10.workers.dev/updater-events"));
        UpdateTransaction? currentTransaction = null;
        try
        {
            await diagnostics.FlushPendingAsync(UpdaterDiagnostics.IsTelemetryAuthorized(dataRoot));
            WaitForParent(args);
            var journal = new UpdateRecoveryJournal(runtimeRoot);
            journal.TryRead(out currentTransaction!);
            var recovery = new RecoveryCoordinator(runtimeRoot);
            var initialDecision = recovery.Reconcile(DateTimeOffset.UtcNow, HealthTimeout);
            if (initialDecision == RecoveryDecision.RolledBack && currentTransaction is not null)
                await RecordAsync(diagnostics, currentTransaction, "rollback", "rolled-back", "health-timeout", null, dataRoot);
            var activation = new RuntimeActivationStore(runtimeRoot);
            var version = activation.ReadActiveVersion();
            var floor = new VersionFloorStore(dataRoot).Read(version);
            if (Version.Parse(version) < Version.Parse(floor))
            {
                if (!Directory.Exists(Path.Combine(activation.VersionsRoot, floor)))
                    throw new CryptographicException("A versão ativa está abaixo do piso anti-downgrade confirmado.");
                activation.Activate(floor);
                version = floor;
            }
            var executable = Path.Combine(activation.VersionsRoot, version, "FiveMCleaner.exe");
            if (!File.Exists(executable)) throw new FileNotFoundException("A versão ativa não contém o aplicativo.", executable);

            var hasCandidate = journal.TryRead(out var transaction) && transaction.CandidateVersion == version;
            if (hasCandidate && transaction.CandidateLaunchedAtUtc is null)
                transaction = journal.MarkCandidateLaunched(transaction);

            var start = new ProcessStartInfo(executable) { WorkingDirectory = Path.GetDirectoryName(executable)!, UseShellExecute = false };
            foreach (var argument in forwardedArguments) start.ArgumentList.Add(argument);
            if (hasCandidate)
            {
                start.ArgumentList.Add($"--update-transaction={transaction.Id}");
                start.ArgumentList.Add($"--update-nonce={transaction.Nonce}");
            }
            using var process = Process.Start(start) ?? throw new InvalidOperationException("O Windows não iniciou o FiveMCleaner.");
            if (!hasCandidate) return 0;

            var receipt = new UpdateHealthReceiptStore(runtimeRoot);
            var deadline = DateTimeOffset.UtcNow + HealthTimeout;

            async Task<bool> TryConfirmHealthAsync()
            {
                if (!receipt.Confirms(transaction)) return false;
                recovery.Reconcile(DateTimeOffset.UtcNow, HealthTimeout);
                await RecordAsync(diagnostics, transaction, "health-check", "completed", "healthy", null, dataRoot);
                return true;
            }

            while (!process.HasExited && DateTimeOffset.UtcNow < deadline)
            {
                if (await TryConfirmHealthAsync()) return 0;
                await Task.Delay(250);
            }
            if (await TryConfirmHealthAsync()) return 0;
            recovery.Reconcile(DateTimeOffset.UtcNow, TimeSpan.Zero);
            await RecordAsync(diagnostics, transaction, "rollback", "rolled-back", "health-timeout", null, dataRoot);
            MessageBox.Show(
                "A nova versão não confirmou uma inicialização saudável. A versão anterior foi restaurada e será usada na próxima abertura.",
                "Recuperação do FiveMCleaner", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }
        catch (Exception exception)
        {
            if (currentTransaction is not null)
            {
                try { new RecoveryCoordinator(runtimeRoot).Reconcile(DateTimeOffset.UtcNow, TimeSpan.Zero); }
                catch (Exception recoveryException) when (recoveryException is not (
                    OutOfMemoryException or StackOverflowException or AccessViolationException)) { }
                await RecordAsync(diagnostics, currentTransaction, "activation", "failed", Classify(exception), exception.ToString(), dataRoot);
            }
            MessageBox.Show(exception.Message, "FiveMCleaner", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 2;
        }
    }

    private static void WaitForParent(string[] args)
    {
        var pidText = args.FirstOrDefault(value => value.StartsWith("--wait-for-pid=", StringComparison.OrdinalIgnoreCase))?["--wait-for-pid=".Length..];
        var startText = args.FirstOrDefault(value => value.StartsWith("--wait-for-start=", StringComparison.OrdinalIgnoreCase))?["--wait-for-start=".Length..];
        if (pidText is null && startText is null) return;
        if (!int.TryParse(pidText, out var pid) || pid <= 0 || !long.TryParse(startText, out var expectedStart) || expectedStart <= 0)
            throw new InvalidDataException("Identidade do processo anterior inválida.");
        try
        {
            using var parent = Process.GetProcessById(pid);
            if (parent.StartTime.ToUniversalTime().ToFileTimeUtc() == expectedStart
                && !parent.WaitForExit(30_000))
                throw new TimeoutException("O FiveMCleaner anterior não encerrou a tempo.");
        }
        catch (ArgumentException) { }
    }

    private static Task RecordAsync(
        UpdaterDiagnostics diagnostics, UpdateTransaction transaction, string stage,
        string outcome, string code, string? detail, string dataRoot) =>
        diagnostics.RecordAsync(
            new UpdaterEvent(transaction.Id, stage, outcome, code, transaction.PreviousVersion,
                transaction.CandidateVersion, "Production"),
            detail,
            UpdaterDiagnostics.IsTelemetryAuthorized(dataRoot));

    private static string Classify(Exception exception) => exception switch
    {
        CryptographicException => "signature-invalid",
        InvalidDataException => "invalid-data",
        UnauthorizedAccessException => "access-denied",
        IOException => "io",
        _ => "unexpected",
    };
}
