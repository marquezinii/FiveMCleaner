using System.Diagnostics;
using System.Text;

namespace FiveMCleaner.Windows.Infrastructure;

public sealed record CommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

public interface ICommandRunner
{
    Task<CommandResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

public sealed class ProcessCommandRunner : ICommandRunner
{
    public async Task<CommandResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(arguments);

        if (!Path.IsPathFullyQualified(executable))
        {
            throw new ArgumentException(
                "System command executables must use an absolute path.",
                nameof(executable));
        }

        executable = Path.GetFullPath(executable);
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException("The system command executable was not found.", executable);
        }

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start '{executable}'.");
        }

        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);

        // The reads must be bounded by the same token as the wait. A child that
        // hands its stdout/stderr handle to a grandchild keeps the pipe open
        // after exiting, so an unbounded read here would hang forever despite
        // the timeout. Reading and waiting together also means both drain on
        // the failure path instead of being abandoned as unobserved tasks.
        var outputTask = process.StandardOutput.ReadToEndAsync(linkedSource.Token);
        var errorTask = process.StandardError.ReadToEndAsync(linkedSource.Token);

        string output;
        string error;
        try
        {
            await process.WaitForExitAsync(linkedSource.Token).ConfigureAwait(false);
            output = await outputTask.ConfigureAwait(false);
            error = await errorTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested
            && !cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            await ObserveAsync(outputTask, errorTask).ConfigureAwait(false);
            throw new TimeoutException($"'{executable}' exceeded the {timeout} timeout.");
        }
        catch
        {
            TryKill(process);
            await ObserveAsync(outputTask, errorTask).ConfigureAwait(false);
            throw;
        }

        return new CommandResult(process.ExitCode, output, error);
    }

    /// <summary>
    /// Killing the process closes the pipes, so the pending reads complete
    /// (or fault) shortly after. Awaiting them here keeps their exceptions
    /// from surfacing later as unobserved task exceptions on the finalizer
    /// thread, far from the call that actually failed.
    /// </summary>
    private static async Task ObserveAsync(params Task[] tasks)
    {
        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}
