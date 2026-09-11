using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;

namespace Ralven.App.Services;

/// <summary>
/// Outcome of an interactive Google sign-in. Exactly one of
/// <see cref="IdToken"/> and <see cref="Error"/> is set; <see cref="Error"/>
/// is already localized for the current app language.
/// </summary>
public sealed record GoogleSignInTicket(string? IdToken, string? Error)
{
    public static GoogleSignInTicket Fail(string message) => new(null, message);
}

public interface IGoogleOAuthClient
{
    /// <summary>
    /// False when no OAuth client id is configured for this build. The
    /// account window hides the Google button entirely in that case rather
    /// than offering a button that could only ever fail.
    /// </summary>
    bool IsConfigured { get; }

    Task<GoogleSignInTicket> AuthenticateAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Interactive Google sign-in using the OAuth 2.0 authorization code flow
/// with PKCE and a loopback redirect — the flow Google documents for
/// installed/desktop apps
/// (https://developers.google.com/identity/protocols/oauth2/native-app).
///
/// The user authenticates in their own system browser, on accounts.google.com,
/// against the real Google login page: this app never sees the Google
/// password and never renders an embedded web view, which is exactly why
/// Google requires this shape. What comes back here is a short-lived
/// authorization code, exchanged for a Google OpenID Connect id_token that
/// <see cref="FirebaseAuthService.SignInWithGoogleAsync"/> then trades for a
/// Firebase session.
///
/// Notes on the two things that usually go wrong in this flow:
/// <list type="bullet">
///   <item>A raw <see cref="TcpListener"/> is used instead of
///   <c>HttpListener</c> because binding an HttpListener prefix can require a
///   URL ACL (an elevation prompt) on some Windows configurations. A plain
///   loopback socket never does, and the "HTTP server" here only ever has to
///   answer one GET.</item>
///   <item>The <c>state</c> value is compared before the code is used, so a
///   different page that happens to hit the loopback port cannot inject a
///   code from another authorization.</item>
/// </list>
/// </summary>
public sealed class GoogleOAuthClient : IGoogleOAuthClient
{
    private const string AuthorizeEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string Scope = "openid email profile";
    private static readonly Lazy<string?> AppIconDataUri = new(TryCreateAppIconDataUri);

    /// <summary>
    /// How long the loopback listener waits for the browser round trip. Long
    /// enough for a password + 2FA prompt, short enough that an abandoned
    /// attempt does not hold a socket open for the rest of the session.
    /// </summary>
    private static readonly TimeSpan BrowserTimeout = TimeSpan.FromMinutes(4);

    private readonly HttpClient client;
    private readonly string? clientId;
    private readonly string? clientSecret;
    private readonly ILocalizationService localization;

    public GoogleOAuthClient(string? clientId, string? clientSecret = null, ILocalizationService? localization = null)
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(20) }, clientId, clientSecret, localization)
    {
    }

    internal GoogleOAuthClient(HttpClient client, string? clientId, string? clientSecret, ILocalizationService? localization = null)
    {
        this.client = client;
        this.clientId = string.IsNullOrWhiteSpace(clientId) ? null : clientId.Trim();
        this.clientSecret = string.IsNullOrWhiteSpace(clientSecret) ? null : clientSecret.Trim();
        this.localization = localization ?? LocalizationService.Current;
    }

    public bool IsConfigured => clientId is not null;

    public async Task<GoogleSignInTicket> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        if (clientId is null)
        {
            return GoogleSignInTicket.Fail(T("Account.Google.NotConfigured"));
        }

        var verifier = CreateRandomToken();
        var state = CreateRandomToken();
        var nonce = CreateRandomToken();

        var listener = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            listener.Start();
        }
        catch (SocketException)
        {
            return GoogleSignInTicket.Fail(T("Account.Google.LoopbackUnavailable"));
        }

        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var redirectUri = $"http://127.0.0.1:{port}/";

            if (!TryOpenBrowser(BuildAuthorizeUrl(redirectUri, verifier, state, nonce)))
            {
                return GoogleSignInTicket.Fail(T("Account.Google.BrowserOpenFailed"));
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(BrowserTimeout);

            var callback = await WaitForCallbackAsync(listener, state, timeout.Token, cancellationToken).ConfigureAwait(false);
            if (callback.Error is not null)
            {
                return GoogleSignInTicket.Fail(callback.Error);
            }

            return await ExchangeCodeAsync(callback.Code!, verifier, redirectUri, nonce, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            listener.Stop();
        }
    }

    internal string BuildAuthorizeUrl(string redirectUri, string verifier, string state, string nonce)
    {
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var query = new Dictionary<string, string>
        {
            ["client_id"] = clientId!,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = Scope,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state,
            ["nonce"] = nonce,
            // Always let the user pick which Google account to use: silently
            // reusing whichever one the browser happens to be signed into is
            // a common source of "it created the wrong account" reports.
            ["prompt"] = "select_account",
        };

        return $"{AuthorizeEndpoint}?{string.Join('&', query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"))}";
    }

    private static bool TryOpenBrowser(string url)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Accepts loopback connections until one carries the authorization
    /// response. Browsers routinely open extra connections to the same port
    /// (favicon, connection pre-warming), so a single accept is not enough.
    /// </summary>
    private async Task<CallbackResult> WaitForCallbackAsync(
        TcpListener listener,
        string expectedState,
        CancellationToken timeoutToken,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            TcpClient connection;
            try
            {
                connection = await listener.AcceptTcpClientAsync(timeoutToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return CallbackResult.Failed(T("Account.Google.Cancelled"));
            }
            catch (OperationCanceledException)
            {
                return CallbackResult.Failed(T("Account.Google.TimedOut"));
            }
            catch (SocketException)
            {
                return CallbackResult.Failed(T("Account.Google.LocalConnectionInterrupted"));
            }

            using (connection)
            {
                var requestTarget = await ReadRequestTargetAsync(connection, timeoutToken).ConfigureAwait(false);
                if (requestTarget is null)
                {
                    continue;
                }

                if (!TryParseCallback(requestTarget, expectedState, out var code, out var error))
                {
                    // Ignore favicon/pre-warming requests and callbacks with
                    // the wrong state. A local process that discovers the
                    // ephemeral port must not be able to terminate the real
                    // authorization attempt with a forged callback.
                    await WriteResponseAsync(
                        connection,
                        "404 Not Found",
                        $"<section class=\"result result--neutral\"><h1>{H("Account.Google.Callback.NothingTitle")}</h1></section>",
                        timeoutToken).ConfigureAwait(false);
                    continue;
                }

                var succeeded = code is not null;
                await WriteResponseAsync(
                    connection,
                    "200 OK",
                    succeeded
                        ? $"<section class=\"result result--success\"><span class=\"status-mark\" aria-hidden=\"true\"></span><h1>{H("Account.Google.Callback.SuccessTitle")}</h1><p>{H("Account.Google.Callback.SuccessDetail")}</p></section>"
                        : $"<section class=\"result result--error\"><span class=\"status-mark\" aria-hidden=\"true\"></span><h1>{H("Account.Google.Callback.FailureTitle")}</h1><p>{H("Account.Google.Callback.FailureDetail")}</p></section>",
                    timeoutToken).ConfigureAwait(false);

                return succeeded
                    ? new CallbackResult(code, null)
                    : CallbackResult.Failed(error == "access_denied"
                        ? T("Account.Google.ProviderCancelled")
                        : T("Account.Google.ProviderFailed"));
            }
        }
    }

    internal static bool TryParseCallback(
        string requestTarget,
        string expectedState,
        out string? code,
        out string? error)
    {
        code = error = null;
        var query = HttpUtility.ParseQueryString(
            requestTarget.Contains('?', StringComparison.Ordinal)
                ? requestTarget[(requestTarget.IndexOf('?', StringComparison.Ordinal) + 1)..]
                : string.Empty);
        code = query["code"];
        error = query["error"];
        if ((code is null) == (error is null)
            || !FixedTimeEquals(query["state"], expectedState))
        {
            code = error = null;
            return false;
        }

        return true;
    }

    private static async Task<string?> ReadRequestTargetAsync(TcpClient connection, CancellationToken cancellationToken)
    {
        try
        {
            using var reader = new StreamReader(connection.GetStream(), Encoding.ASCII, leaveOpen: true);
            var requestLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(requestLine)) return null;

            // "GET /?code=...&state=... HTTP/1.1"
            var parts = requestLine.Split(' ');
            return parts.Length >= 2 && parts[0] == "GET" ? parts[1] : null;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException)
        {
            return null;
        }
    }

    private async Task WriteResponseAsync(TcpClient connection, string status, string body, CancellationToken cancellationToken)
    {
        var iconMarkup = AppIconDataUri.Value is { } icon
            ? $"<img class=\"brand-mark\" src=\"{icon}\" alt=\"\">"
            : string.Empty;
        var html = $$"""
            <!doctype html>
            <html lang="{{localization.CurrentCulture.Name}}">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <meta name="color-scheme" content="dark">
              <title>Ralven</title>
              <style>
                :root {
                  color-scheme: dark;
                  --canvas: #0A0A0B;
                  --canvas-deep: #111214;
                  --text: #FFFFFF;
                  --text-muted: #A6A7AC;
                  --accent: #FFFFFF;
                  --success: #32D583;
                  --success-surface: #10271F;
                  --danger: #F04438;
                  --danger-surface: #2B1518;
                }

                * { box-sizing: border-box; }

                html, body { min-height: 100%; }

                body {
                  min-height: 100svh;
                  margin: 0;
                  display: grid;
                  place-items: center;
                  padding: 32px 24px;
                  overflow: hidden;
                  background:
                    radial-gradient(circle at 10% 100%, rgba(255, 255, 255, .12) 0, rgba(166, 167, 172, .06) 28%, transparent 58%),
                    linear-gradient(145deg, var(--canvas) 0, var(--canvas-deep) 72%);
                  color: var(--text);
                  font-family: "Inter", "Segoe UI Variable Text", "Segoe UI", sans-serif;
                  text-align: center;
                }

                .brand {
                  position: fixed;
                  top: 32px;
                  left: 32px;
                  display: flex;
                  align-items: center;
                  gap: 11px;
                  color: var(--text);
                  font-size: 14px;
                  font-weight: 600;
                  letter-spacing: -.01em;
                }

                .brand-mark {
                  width: 36px;
                  height: 36px;
                  border-radius: 10px;
                  box-shadow: 0 8px 20px rgba(3, 5, 10, .40);
                }

                .brand-reg {
                  position: relative;
                  top: -.45em;
                  margin-left: 2px;
                  color: var(--text-muted);
                  font-size: 8px;
                  line-height: 1;
                }

                main { width: min(100%, 520px); }

                .result { padding: 28px 12px; }

                .status-mark {
                  position: relative;
                  display: block;
                  width: 60px;
                  height: 60px;
                  margin: 0 auto 26px;
                  border-radius: 16px;
                }

                .result--success .status-mark { background: var(--success-surface); }
                .result--error .status-mark { background: var(--danger-surface); }

                .result--success .status-mark::after {
                  content: "";
                  position: absolute;
                  top: 21px;
                  left: 18px;
                  width: 24px;
                  height: 18px;
                  background: var(--success);
                  clip-path: polygon(0 42%, 12% 31%, 40% 62%, 88% 9%, 100% 22%, 40% 100%);
                }

                .result--error .status-mark::before,
                .result--error .status-mark::after {
                  content: "";
                  position: absolute;
                  top: 28px;
                  left: 18px;
                  width: 24px;
                  height: 3px;
                  border-radius: 2px;
                  background: var(--danger);
                }

                .result--error .status-mark::before { transform: rotate(45deg); }
                .result--error .status-mark::after { transform: rotate(-45deg); }

                h1 {
                  margin: 0;
                  font-size: clamp(28px, 4vw, 34px);
                  line-height: 1.18;
                  font-weight: 650;
                  letter-spacing: -.03em;
                }

                p {
                  max-width: 38ch;
                  margin: 13px auto 0;
                  color: var(--text-muted);
                  font-size: 15px;
                  line-height: 1.6;
                }

                ::selection {
                  background: var(--accent);
                  color: #0A0A0B;
                }

                @media (max-width: 520px) {
                  body { padding: 88px 20px 28px; }
                  .brand { top: 24px; left: 24px; }
                  .result { padding-inline: 0; }
                }
              </style>
            </head>
            <body>
              <div class="brand" aria-label="Ralven">
                {{iconMarkup}}
                <span>Ralven</span>
              </div>
              <main>{{body}}</main>
            </body>
            </html>
            """;
        var payload = Encoding.UTF8.GetBytes(html);
        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");

        try
        {
            var stream = connection.GetStream();
            await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // The browser closing first must not fail an otherwise complete
            // sign-in: the code is already in hand.
        }
    }

    private static string? TryCreateAppIconDataUri()
    {
        try
        {
            if (Environment.ProcessPath is not { } executablePath) return null;

            using var icon = Icon.ExtractAssociatedIcon(executablePath);
            if (icon is null) return null;

            using var bitmap = icon.ToBitmap();
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return $"data:image/png;base64,{Convert.ToBase64String(stream.ToArray())}";
        }
        catch (Exception exception) when (exception is ArgumentException or ExternalException or IOException)
        {
            // Branding must never turn a successful OAuth callback into a failed sign-in.
            return null;
        }
    }

    internal async Task<GoogleSignInTicket> ExchangeCodeAsync(
        string code,
        string verifier,
        string redirectUri,
        string nonce,
        CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = clientId!,
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = redirectUri,
        };

        // Google's desktop client type still issues a "secret". It is not
        // treated as one (it ships inside the installed app), and PKCE is
        // what actually protects the exchange -- but the endpoint rejects the
        // request without it when the client was registered with one.
        if (clientSecret is not null)
        {
            form["client_secret"] = clientSecret;
        }

        try
        {
            using var response = await client
                .PostAsync(TokenEndpoint, new FormUrlEncodedContent(form), cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return GoogleSignInTicket.Fail(T("Account.Google.ExchangeRejected"));
            }

            var payload = await response.Content
                .ReadFromJsonAsync<GoogleTokenResponse>(cancellationToken)
                .ConfigureAwait(false);

            return string.IsNullOrWhiteSpace(payload?.IdToken)
                ? GoogleSignInTicket.Fail(T("Account.Google.MissingAccountInfo"))
                : HasExpectedIdTokenClaims(payload.IdToken, clientId!, nonce, DateTimeOffset.UtcNow)
                    ? new GoogleSignInTicket(payload.IdToken, null)
                    : GoogleSignInTicket.Fail(T("Account.Google.InvalidResponse"));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return GoogleSignInTicket.Fail(T("Account.Google.ExchangeTimedOut"));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or IOException)
        {
            return GoogleSignInTicket.Fail(T("Account.Google.NetworkFailed"));
        }
    }

    private static string CreateRandomToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

    internal static bool HasExpectedIdTokenClaims(
        string idToken,
        string expectedAudience,
        string expectedNonce,
        DateTimeOffset now)
    {
        if (idToken.Length is 0 or > 16 * 1024)
        {
            return false;
        }

        var parts = idToken.Split('.');
        if (parts.Length != 3 || !TryDecodeBase64Url(parts[1], out var payloadBytes))
        {
            return false;
        }

        try
        {
            using var payload = JsonDocument.Parse(payloadBytes);
            var root = payload.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("iss", out var issuer)
                || issuer.ValueKind != JsonValueKind.String
                || issuer.GetString() is not ("https://accounts.google.com" or "accounts.google.com")
                || !root.TryGetProperty("aud", out var audience)
                || !AudienceContains(audience, expectedAudience)
                || !root.TryGetProperty("exp", out var expiry)
                || !expiry.TryGetInt64(out var expirySeconds)
                || expirySeconds <= now.ToUnixTimeSeconds()
                || !root.TryGetProperty("sub", out var subject)
                || subject.ValueKind != JsonValueKind.String
                || !IsValidSubject(subject.GetString())
                || !root.TryGetProperty("nonce", out var nonce)
                || nonce.ValueKind != JsonValueKind.String
                || !FixedTimeEquals(nonce.GetString(), expectedNonce))
            {
                return false;
            }

            // This is a defensive OIDC correlation check only. The token is
            // not authenticated until Firebase validates Google's signature.
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payloadBytes);
        }
    }

    private static bool AudienceContains(JsonElement audience, string expectedAudience) =>
        audience.ValueKind == JsonValueKind.String
            ? string.Equals(audience.GetString(), expectedAudience, StringComparison.Ordinal)
            : audience.ValueKind == JsonValueKind.Array
              && audience.EnumerateArray().Any(value =>
                  value.ValueKind == JsonValueKind.String
                  && string.Equals(value.GetString(), expectedAudience, StringComparison.Ordinal));

    private static bool IsValidSubject(string? subject) =>
        !string.IsNullOrWhiteSpace(subject) && subject.Length <= 255;

    private static bool TryDecodeBase64Url(string value, out byte[] bytes)
    {
        try
        {
            var padded = value.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            bytes = Convert.FromBase64String(padded);
            return true;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }

    private static bool FixedTimeEquals(string? actual, string expected)
    {
        if (actual is null)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(actual),
            Encoding.UTF8.GetBytes(expected));
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private string T(string key) => localization.GetString(key);

    private string H(string key) => WebUtility.HtmlEncode(T(key));

    private sealed record CallbackResult(string? Code, string? Error)
    {
        public static CallbackResult Failed(string message) => new(null, message);
    }

    private sealed record GoogleTokenResponse([property: JsonPropertyName("id_token")] string? IdToken);
}
