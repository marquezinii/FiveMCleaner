using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;

namespace FiveMCleaner.App.Services;

/// <summary>
/// Outcome of an interactive Google sign-in. Exactly one of
/// <see cref="IdToken"/> and <see cref="Error"/> is set; <see cref="Error"/>
/// is already a user-facing pt-BR sentence.
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

    /// <summary>
    /// How long the loopback listener waits for the browser round trip. Long
    /// enough for a password + 2FA prompt, short enough that an abandoned
    /// attempt does not hold a socket open for the rest of the session.
    /// </summary>
    private static readonly TimeSpan BrowserTimeout = TimeSpan.FromMinutes(4);

    private readonly HttpClient client;
    private readonly string? clientId;
    private readonly string? clientSecret;

    public GoogleOAuthClient(string? clientId, string? clientSecret = null)
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(20) }, clientId, clientSecret)
    {
    }

    internal GoogleOAuthClient(HttpClient client, string? clientId, string? clientSecret)
    {
        this.client = client;
        this.clientId = string.IsNullOrWhiteSpace(clientId) ? null : clientId.Trim();
        this.clientSecret = string.IsNullOrWhiteSpace(clientSecret) ? null : clientSecret.Trim();
    }

    public bool IsConfigured => clientId is not null;

    public async Task<GoogleSignInTicket> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        if (clientId is null)
        {
            return GoogleSignInTicket.Fail("O login com o Google não está configurado nesta versão.");
        }

        var verifier = CreateRandomToken();
        var state = CreateRandomToken();

        var listener = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            listener.Start();
        }
        catch (SocketException)
        {
            return GoogleSignInTicket.Fail("Não foi possível abrir a porta local necessária para o login com o Google.");
        }

        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var redirectUri = $"http://127.0.0.1:{port}/";

            if (!TryOpenBrowser(BuildAuthorizeUrl(redirectUri, verifier, state)))
            {
                return GoogleSignInTicket.Fail("Não foi possível abrir seu navegador para concluir o login com o Google.");
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(BrowserTimeout);

            var callback = await WaitForCallbackAsync(listener, timeout.Token, cancellationToken).ConfigureAwait(false);
            if (callback.Error is not null)
            {
                return GoogleSignInTicket.Fail(callback.Error);
            }

            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(callback.State ?? string.Empty),
                    Encoding.UTF8.GetBytes(state)))
            {
                return GoogleSignInTicket.Fail("A resposta do Google não pôde ser validada. Tente entrar novamente.");
            }

            return await ExchangeCodeAsync(callback.Code!, verifier, redirectUri, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            listener.Stop();
        }
    }

    private string BuildAuthorizeUrl(string redirectUri, string verifier, string state)
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
    private static async Task<CallbackResult> WaitForCallbackAsync(
        TcpListener listener,
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
                return CallbackResult.Failed("Login com o Google cancelado.");
            }
            catch (OperationCanceledException)
            {
                return CallbackResult.Failed("O login com o Google demorou demais e foi cancelado.");
            }
            catch (SocketException)
            {
                return CallbackResult.Failed("A conexão local do login com o Google foi interrompida.");
            }

            using (connection)
            {
                var requestTarget = await ReadRequestTargetAsync(connection, timeoutToken).ConfigureAwait(false);
                if (requestTarget is null)
                {
                    continue;
                }

                var query = HttpUtility.ParseQueryString(
                    requestTarget.Contains('?', StringComparison.Ordinal)
                        ? requestTarget[(requestTarget.IndexOf('?', StringComparison.Ordinal) + 1)..]
                        : string.Empty);

                var code = query["code"];
                var error = query["error"];
                if (code is null && error is null)
                {
                    // Not the redirect (favicon and friends): answer 404 and
                    // keep waiting for the real one.
                    await WriteResponseAsync(connection, "404 Not Found", "<p>Nada aqui.</p>", timeoutToken).ConfigureAwait(false);
                    continue;
                }

                var succeeded = code is not null;
                await WriteResponseAsync(
                    connection,
                    "200 OK",
                    succeeded
                        ? "<h1>Tudo certo!</h1><p>Você já pode voltar para o FiveMCleaner.</p>"
                        : "<h1>Login não concluído</h1><p>Volte ao FiveMCleaner e tente novamente.</p>",
                    timeoutToken).ConfigureAwait(false);

                return succeeded
                    ? new CallbackResult(code, query["state"], null)
                    : CallbackResult.Failed(error == "access_denied"
                        ? "Você cancelou o login com o Google."
                        : "O Google não concluiu o login. Tente novamente.");
            }
        }
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

    private static async Task WriteResponseAsync(TcpClient connection, string status, string body, CancellationToken cancellationToken)
    {
        var html = $$"""
            <!doctype html><html lang="pt-BR"><head><meta charset="utf-8">
            <title>FiveMCleaner</title>
            <style>body{background:#0A0B0D;color:#F4F5F7;font-family:'Segoe UI',sans-serif;display:flex;align-items:center;justify-content:center;height:100vh;margin:0;text-align:center}h1{color:#FF7A18;font-size:22px}p{color:#9A9FA9}</style>
            </head><body><div>{{body}}</div></body></html>
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

    private async Task<GoogleSignInTicket> ExchangeCodeAsync(
        string code,
        string verifier,
        string redirectUri,
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
                return GoogleSignInTicket.Fail("O Google recusou a conclusão do login. Tente novamente.");
            }

            var payload = await response.Content
                .ReadFromJsonAsync<GoogleTokenResponse>(cancellationToken)
                .ConfigureAwait(false);

            return string.IsNullOrWhiteSpace(payload?.IdToken)
                ? GoogleSignInTicket.Fail("O Google não devolveu as informações da sua conta. Tente novamente.")
                : new GoogleSignInTicket(payload!.IdToken, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return GoogleSignInTicket.Fail("A conexão com o Google demorou demais. Verifique sua internet.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or IOException)
        {
            return GoogleSignInTicket.Fail("Não foi possível falar com o Google. Verifique sua conexão.");
        }
    }

    private static string CreateRandomToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record CallbackResult(string? Code, string? State, string? Error)
    {
        public static CallbackResult Failed(string message) => new(null, null, message);
    }

    private sealed record GoogleTokenResponse([property: JsonPropertyName("id_token")] string? IdToken);
}
