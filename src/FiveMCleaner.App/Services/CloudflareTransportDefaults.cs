using System.Net;
using System.Net.Http;

namespace FiveMCleaner.App.Services;

/// <summary>
/// HTTPS-only endpoint validation and a redirect-less, compressed
/// <see cref="HttpClient"/> shared by the Cloudflare Worker transports
/// (telemetry and bug reports) so both enforce the same transport
/// invariants instead of drifting independently.
/// </summary>
internal static class CloudflareTransportDefaults
{
    public static Uri ValidateHttpsEndpoint(Uri value, string errorMessage)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(errorMessage, nameof(value));
        }

        return value;
    }

    public static HttpClient CreateClient(TimeSpan timeout)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        return new HttpClient(handler) { Timeout = timeout };
    }
}
