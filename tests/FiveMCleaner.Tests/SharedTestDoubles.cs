using System.Net;
using System.Net.Http;
using System.Text;

namespace FiveMCleaner.Tests;

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "FiveMCleaner.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts)
    {
        return parts.Aggregate(Path, System.IO.Path.Combine);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}

internal sealed class RecordingHandler : HttpMessageHandler
{
    public RecordingHandler(HttpStatusCode statusCode = HttpStatusCode.Accepted)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }

    public int CallCount { get; private set; }

    public HttpMethod? Method { get; private set; }

    public Uri? RequestUri { get; private set; }

    public string ContentType { get; private set; } = string.Empty;

    public string Body { get; private set; } = string.Empty;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        CallCount++;
        Method = request.Method;
        RequestUri = request.RequestUri;
        ContentType = request.Content?.Headers.ContentType?.ToString() ?? string.Empty;
        if (request.Content is not null)
        {
            Body = Encoding.UTF8.GetString(await request.Content.ReadAsByteArrayAsync(cancellationToken));
        }

        return new HttpResponseMessage(StatusCode)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
    }
}

internal sealed class ThrowingHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        throw new HttpRequestException("simulated network failure");
}
