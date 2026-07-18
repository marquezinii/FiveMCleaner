using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using FiveMCleaner.Contracts;

namespace FiveMCleaner.App.Services;

internal sealed record ElevatedBrokerResult
{
    public required bool Succeeded { get; init; }

    public required bool WasCancelled { get; init; }

    public required string Message { get; init; }

    public string? State { get; init; }

    public IReadOnlyList<string> AppliedActionIds { get; init; } = [];
}

internal sealed class ElevatedBrokerClient
{
    private const int ErrorCancelled = 1223;
    private const int MaximumEvents = 128;
    private const int MaximumEventCharacters = 64 * 1024;
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromMinutes(30);
    private readonly string requestDirectory;
    private readonly string brokerPath;

    public ElevatedBrokerClient(string appDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataDirectory);
        requestDirectory = Path.Combine(Path.GetFullPath(appDataDirectory), "Requests");
        brokerPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "broker",
            "FiveMCleaner.Broker.exe"));
    }

    public async Task<ElevatedBrokerResult> ExecuteAsync(
        OptimizationPlanDto plan,
        IProgress<AppProgressUpdate> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(progress);
        cancellationToken.ThrowIfCancellationRequested();

        var requestPath = await WriteRequestAsync(plan, cancellationToken).ConfigureAwait(false);
        try
        {
            return await RunAsync(
                $"--request \"{requestPath}\" --pipe {{0}}",
                plan.PlanId,
                progress,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            TryDeleteRequest(requestPath);
        }
    }

    public Task<ElevatedBrokerResult> RollbackAsync(
        Guid transactionId,
        IProgress<AppProgressUpdate> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if (transactionId == Guid.Empty)
        {
            throw new ArgumentException("O identificador da transação não pode ser vazio.", nameof(transactionId));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return RunAsync(
            $"--rollback {transactionId:D} --pipe {{0}}",
            transactionId,
            progress,
            cancellationToken);
    }

    private async Task<ElevatedBrokerResult> RunAsync(
        string argumentTemplate,
        Guid expectedTransactionId,
        IProgress<AppProgressUpdate> progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(brokerPath))
        {
            throw new FileNotFoundException(
                "O componente administrativo não foi encontrado ao lado do aplicativo.",
                brokerPath);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var pipeId = Guid.NewGuid();
        await using var pipe = new NamedPipeServerStream(
            pipeId.ToString("N"),
            PipeDirection.In,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = brokerPath,
                Arguments = string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    argumentTemplate,
                    pipeId.ToString("N")),
                WorkingDirectory = Path.GetDirectoryName(brokerPath)!,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            }
        };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("O Windows não iniciou o componente administrativo.");
            }
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == ErrorCancelled)
        {
            return new ElevatedBrokerResult
            {
                Succeeded = false,
                WasCancelled = true,
                Message = "A confirmação do Windows foi cancelada."
            };
        }

        // Depois que o broker elevado começa, deixamos a transação terminar ou se
        // reverter com segurança mesmo se o botão Cancelar for usado na interface.
        using var timeout = new CancellationTokenSource(OperationTimeout);
        try
        {
            using (var connectionTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token))
            {
                connectionTimeout.CancelAfter(ConnectionTimeout);
                await pipe.WaitForConnectionAsync(connectionTimeout.Token).ConfigureAwait(false);
            }

            using var reader = new StreamReader(
                pipe,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 4096,
                leaveOpen: true);

            BrokerEventWire? terminal = null;
            long previousSequence = 0;
            for (var count = 0; count < MaximumEvents; count++)
            {
                var line = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                if (line.Length > MaximumEventCharacters)
                {
                    throw new InvalidDataException("O broker retornou um evento local acima do limite seguro.");
                }

                var brokerEvent = JsonSerializer.Deserialize<BrokerEventWire>(
                    line,
                    FiveMCleanerJson.Options)
                    ?? throw new JsonException("O broker retornou um evento vazio.");
                if (brokerEvent.SchemaVersion != 1 || brokerEvent.Sequence <= previousSequence)
                {
                    throw new InvalidDataException("A sequência de eventos do broker é inválida.");
                }

                if (brokerEvent.TransactionId is Guid eventTransactionId
                    && eventTransactionId != expectedTransactionId)
                {
                    throw new InvalidDataException(
                        "O broker retornou eventos para outra transação.");
                }

                if (brokerEvent.Kind is BrokerEventKindWire.Completed
                    or BrokerEventKindWire.RollbackCompleted
                    && brokerEvent.TransactionId != expectedTransactionId)
                {
                    throw new InvalidDataException(
                        "O evento terminal do broker não confirmou a transação solicitada.");
                }

                previousSequence = brokerEvent.Sequence;
                ReportBrokerProgress(brokerEvent, progress);
                if (brokerEvent.Kind is BrokerEventKindWire.Completed
                    or BrokerEventKindWire.RollbackCompleted
                    or BrokerEventKindWire.Rejected
                    or BrokerEventKindWire.Failed)
                {
                    terminal = brokerEvent;
                }
            }

            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var succeeded = process.ExitCode == 0
                && terminal?.Success == true
                && terminal.Kind is BrokerEventKindWire.Completed
                    or BrokerEventKindWire.RollbackCompleted;
            return new ElevatedBrokerResult
            {
                Succeeded = succeeded,
                WasCancelled = false,
                Message = terminal?.Message
                    ?? "O componente administrativo terminou sem uma confirmação válida.",
                State = terminal?.State,
                AppliedActionIds = terminal?.AppliedActionIds ?? []
            };
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException(
                "O componente administrativo excedeu o limite de segurança. Consulte o histórico antes de tentar novamente.");
        }
    }

    private async Task<string> WriteRequestAsync(
        OptimizationPlanDto plan,
        CancellationToken cancellationToken)
    {
        var productDirectory = Path.GetDirectoryName(requestDirectory)!;
        Directory.CreateDirectory(productDirectory);
        Directory.CreateDirectory(requestDirectory);
        EnsurePlainDirectory(productDirectory);
        EnsurePlainDirectory(requestDirectory);

        var destination = Path.Combine(requestDirectory, $"{plan.PlanId:N}.json");
        var temporary = Path.Combine(requestDirectory, $".{plan.PlanId:N}.{Guid.NewGuid():N}.tmp");
        try
        {
            var payload = new UTF8Encoding(false, true).GetBytes(FiveMCleanerJson.SerializePlan(plan));
            await using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, destination, overwrite: false);
            return destination;
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static void ReportBrokerProgress(
        BrokerEventWire brokerEvent,
        IProgress<AppProgressUpdate> progress)
    {
        var localPercent = brokerEvent.TotalWeight is > 0
            ? 72d + (23d * brokerEvent.CompletedWeight.GetValueOrDefault() / brokerEvent.TotalWeight.Value)
            : brokerEvent.Kind is BrokerEventKindWire.Completed or BrokerEventKindWire.RollbackCompleted
                ? 98d
                : 72d;
        progress.Report(new AppProgressUpdate
        {
            Timestamp = brokerEvent.TimestampUtc.ToLocalTime(),
            Kind = brokerEvent.Kind switch
            {
                BrokerEventKindWire.Completed or BrokerEventKindWire.RollbackCompleted => AppProgressKind.Verifying,
                BrokerEventKindWire.Failed or BrokerEventKindWire.Rejected => AppProgressKind.Warning,
                BrokerEventKindWire.RollbackStarted => AppProgressKind.RollingBack,
                _ => AppProgressKind.Applying
            },
            Percent = Math.Clamp(localPercent, 72, 98),
            Headline = brokerEvent.Kind is BrokerEventKindWire.RollbackStarted
                or BrokerEventKindWire.RollbackCompleted
                ? "Restaurando configurações administrativas"
                : "Aplicando ajustes administrativos",
            Detail = brokerEvent.Message,
            ActionId = brokerEvent.ActionId
        });
    }

    private static void EnsurePlainDirectory(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("A pasta local de solicitações não pode ser um link ou junction.");
        }
    }

    private static void TryDeleteRequest(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private enum BrokerEventKindWire
    {
        Started,
        Progress,
        Completed,
        RollbackStarted,
        RollbackCompleted,
        Rejected,
        Failed
    }

    private sealed record BrokerEventWire
    {
        public required int SchemaVersion { get; init; }

        public required long Sequence { get; init; }

        public required DateTimeOffset TimestampUtc { get; init; }

        public required BrokerEventKindWire Kind { get; init; }

        public Guid? TransactionId { get; init; }

        public string? ActionId { get; init; }

        public required string Message { get; init; }

        public int? CompletedWeight { get; init; }

        public int? TotalWeight { get; init; }

        public bool? Success { get; init; }

        public string? State { get; init; }

        public string? ErrorCode { get; init; }

        public IReadOnlyList<string>? AppliedActionIds { get; init; }
    }
}
