using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using FiveMCleaner.App.Services;
using FiveMCleaner.Contracts;
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
        var diagnostics = new UpdaterDiagnostics(dataRoot);
        var telemetryAuthorized = UpdaterDiagnostics.IsTelemetryAuthorized(dataRoot);
        UpdateTransaction? currentTransaction = null;
        try
        {
            await diagnostics.FlushPendingAsync(telemetryAuthorized);
            // Read the journal before WaitForParent (not after): WaitForParent
            // is exactly the step that can fail (the previous process not
            // exiting in time), and the catch block below needs
            // currentTransaction populated to be able to abandon/roll back a
            // candidate that never got the chance to launch.
            var journal = new UpdateRecoveryJournal(runtimeRoot);
            journal.TryRead(out currentTransaction!);
            WaitForParent(args);
            var recovery = new RecoveryCoordinator(runtimeRoot);
            var initialDecision = recovery.Reconcile(DateTimeOffset.UtcNow, HealthTimeout);
            if (initialDecision == RecoveryDecision.RolledBack && currentTransaction is not null)
                await RecordAsync(diagnostics, currentTransaction, "rollback", "rolled-back", "health-timeout", null, dataRoot, telemetryAuthorized);
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
                await RecordAsync(diagnostics, transaction, "health-check", "completed", "healthy", null, dataRoot, telemetryAuthorized);
                return true;
            }

            while (DateTimeOffset.UtcNow < deadline && !HasExitedSafely(process))
            {
                if (await TryConfirmHealthAsync()) return 0;
                await Task.Delay(250);
            }
            if (await TryConfirmHealthAsync()) return 0;
            recovery.Reconcile(DateTimeOffset.UtcNow, TimeSpan.Zero);
            await RecordAsync(diagnostics, transaction, "rollback", "rolled-back", "health-timeout", null, dataRoot, telemetryAuthorized);
            MessageBox.Show(
                "A nova versão não confirmou uma inicialização saudável. A versão anterior foi restaurada e será usada na próxima abertura.",
                "Recuperação do FiveMCleaner", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }
        catch (Exception exception)
        {
            if (currentTransaction is not null)
            {
                try
                {
                    var recovery = new RecoveryCoordinator(runtimeRoot);
                    // Re-read the journal instead of trusting the snapshot taken
                    // before WaitForParent: MarkCandidateLaunched may have run
                    // since then, and only the current on-disk state can say
                    // whether the candidate actually got its process started.
                    var latest = new UpdateRecoveryJournal(runtimeRoot).TryRead(out var current)
                        ? current
                        : currentTransaction;
                    // A candidate that was never launched (this failure struck
                    // before Process.Start, e.g. the previous process did not
                    // exit in time) has no running process for Reconcile's
                    // health-timeout wait to apply to -- Abandon reverts it
                    // unconditionally instead of leaving active.json pointed
                    // at a version that never ran.
                    if (latest.CandidateLaunchedAtUtc is null)
                        recovery.Abandon(latest);
                    else
                        recovery.Reconcile(DateTimeOffset.UtcNow, TimeSpan.Zero);
                }
                catch (Exception recoveryException) when (recoveryException is not (
                    OutOfMemoryException or StackOverflowException or AccessViolationException))
                {
                }
                await RecordAsync(diagnostics, currentTransaction, "activation", "failed", Classify(exception), exception.ToString(), dataRoot, telemetryAuthorized);
            }
            MessageBox.Show(DescribeFailure(exception), "FiveMCleaner", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
        // O processo anterior pode sair entre GetProcessById e a leitura de
        // StartTime: o Windows recusa o acesso ao processo já encerrado
        // (Win32Exception) ou nega a propriedade (InvalidOperationException),
        // o mesmo caso "já se foi" que ArgumentException já tratava.
        catch (ArgumentException) { }
        catch (Win32Exception) { }
        catch (InvalidOperationException) { }
    }

    private static Task RecordAsync(
        UpdaterDiagnostics diagnostics, UpdateTransaction transaction, string stage,
        string outcome, string code, string? detail, string dataRoot, bool telemetryAuthorized) =>
        diagnostics.RecordAsync(
            new UpdaterEvent(transaction.Id, stage, outcome, code, transaction.PreviousVersion,
                transaction.CandidateVersion, "Production", BugCodeClassifier.ClassifyUpdaterException(new Exception(code), stage)),
            detail,
            telemetryAuthorized);

    // O processo pode sair entre o Process.Start e a leitura de HasExited, e
    // o Windows nega a consulta (Win32Exception) ou a propriedade
    // (InvalidOperationException) no processo já encerrado -- o mesmo caso
    // "já se foi" que o health-check precisa tratar como exit, não como erro.
    private static bool HasExitedSafely(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return true;
        }
    }

    private static string Classify(Exception exception) => exception switch
    {
        CryptographicException => "signature-invalid",
        InvalidDataException => "invalid-data",
        UnauthorizedAccessException => "access-denied",
        IOException => "io",
        _ => "unexpected",
    };

    private static string DescribeFailure(Exception exception) => exception switch
    {
        TimeoutException => "O FiveMCleaner anterior não encerrou a tempo. Aguarde alguns instantes e tente abrir novamente.",
        UnauthorizedAccessException => "O Windows não permitiu abrir esta versão. Verifique a permissão e tente novamente.",
        CryptographicException or InvalidDataException => "Não foi possível verificar esta atualização com segurança. Nada foi alterado.",
        FileNotFoundException => "Os arquivos necessários para abrir o FiveMCleaner não foram encontrados. Tente reparar ou reinstalar o aplicativo.",
        _ => "Não foi possível abrir o FiveMCleaner agora. Tente novamente; se continuar, reinstale o aplicativo."
    };
}
