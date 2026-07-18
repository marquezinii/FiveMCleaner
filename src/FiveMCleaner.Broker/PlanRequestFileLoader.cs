using System.Text;
using System.Text.Json;
using FiveMCleaner.Contracts;

namespace FiveMCleaner.Broker;

internal sealed class BrokerRequestException : Exception
{
    public BrokerRequestException(
        string errorCode,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}

internal sealed class PlanRequestFileLoader
{
    private const long MaximumRequestBytes = 1024 * 1024;
    private readonly string requestDirectory;

    public PlanRequestFileLoader()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new InvalidOperationException("Local application data is unavailable.");
        }

        requestDirectory = Path.GetFullPath(
            Path.Combine(localAppData, ProductIdentity.Name, "Requests"));
    }

    public async Task<OptimizationPlanDto> LoadAsync(
        string requestPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(requestPath) || !Path.IsPathFullyQualified(requestPath))
        {
            throw Reject("request-path-invalid", "The request must use a fully qualified local path.");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(requestPath);
        }
        catch (Exception exception) when (exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            throw Reject("request-path-invalid", "The request path is invalid.", exception);
        }

        if (fullPath.StartsWith("\\\\", StringComparison.Ordinal)
            || !string.Equals(
                Path.GetDirectoryName(fullPath),
                requestDirectory,
                StringComparison.OrdinalIgnoreCase)
            || !Path.GetExtension(fullPath).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            throw Reject("request-path-outside-root", "The request file is outside the fixed request directory.");
        }

        if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(fullPath), "N", out var filePlanId)
            || filePlanId == Guid.Empty)
        {
            throw Reject("request-file-name-invalid", "The request file name must be a non-empty plan GUID.");
        }

        string? claimedPath = null;
        try
        {
            EnsureLocalNonReparsePath(fullPath);
            claimedPath = ClaimRequest(fullPath, filePlanId);
            await using var stream = new FileStream(
                claimedPath,
                new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.None,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan
                });

            if (stream.Length is <= 0 or > MaximumRequestBytes)
            {
                throw Reject("request-size-invalid", "The request file size is invalid.");
            }

            using var reader = new StreamReader(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 16 * 1024,
                leaveOpen: false);
            var json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            var plan = FiveMCleanerJson.DeserializePlan(json);

            if (plan.PlanId != filePlanId)
            {
                throw Reject("request-file-name-mismatch", "The request file name does not match its plan ID.");
            }

            return plan;
        }
        catch (BrokerRequestException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or DecoderFallbackException)
        {
            throw Reject("request-read-failed", "The local plan request could not be read or parsed.", exception);
        }
        finally
        {
            TryDeleteClaim(claimedPath);
        }
    }

    private void EnsureLocalNonReparsePath(string fullPath)
    {
        if (!Directory.Exists(requestDirectory) || !File.Exists(fullPath))
        {
            throw Reject("request-not-found", "The local plan request was not found.");
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var productDirectory = Path.Combine(localAppData, ProductIdentity.Name);
        foreach (var directory in new[] { localAppData, productDirectory, requestDirectory })
        {
            if (!Directory.Exists(directory)
                || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            {
                throw Reject("request-directory-unsafe", "The fixed request directory is unavailable or unsafe.");
            }
        }

        if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw Reject("request-file-unsafe", "The request file cannot be a symbolic link or reparse point.");
        }
    }

    private string ClaimRequest(string fullPath, Guid planId)
    {
        var claimedPath = Path.Combine(
            requestDirectory,
            $".{planId:N}.{Environment.ProcessId}.processing");
        if (File.Exists(claimedPath))
        {
            throw Reject("request-already-claimed", "The plan request is already being processed.");
        }

        File.Move(fullPath, claimedPath, overwrite: false);
        try
        {
            if ((File.GetAttributes(claimedPath) & FileAttributes.ReparsePoint) != 0)
            {
                throw Reject("request-file-unsafe", "The claimed request cannot be a reparse point.");
            }

            return claimedPath;
        }
        catch
        {
            TryDeleteClaim(claimedPath);
            throw;
        }
    }

    private static void TryDeleteClaim(string? claimedPath)
    {
        if (string.IsNullOrWhiteSpace(claimedPath))
        {
            return;
        }

        try
        {
            File.Delete(claimedPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static BrokerRequestException Reject(
        string errorCode,
        string message,
        Exception? innerException = null)
    {
        return new BrokerRequestException(errorCode, message, innerException);
    }
}
