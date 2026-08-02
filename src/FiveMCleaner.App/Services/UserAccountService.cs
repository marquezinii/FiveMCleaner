using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO;
using FiveMCleaner.Contracts;

namespace FiveMCleaner.App.Services;

public static class AccountTerms
{
    public const string CurrentVersion = "2026-08-02";
}

public sealed record UserProfile(string FirstName, string LastName, string Username, string Email)
{
    public string DisplayName => $"{FirstName} {LastName}";

    public string Initials => string.Concat(FirstName.Take(1), LastName.Take(1)).ToUpperInvariant();
}

public sealed record UserAccountResult(UserProfile? Profile, string? Error)
{
    public bool Succeeded => Profile is not null && Error is null;
}

public interface IUserAccountService
{
    UserProfile? CurrentProfile { get; }
    Task<UserAccountResult> RestoreSessionAsync(CancellationToken cancellationToken = default);
    Task<UserAccountResult> RegisterAsync(string firstName, string lastName, string username, string email, string password, CancellationToken cancellationToken = default);
    Task<UserAccountResult> LoginAsync(string email, string password, CancellationToken cancellationToken = default);
    Task LogoutAsync(CancellationToken cancellationToken = default);
}

public sealed class CloudflareUserAccountService : IUserAccountService, IDisposable
{
    private readonly HttpClient client;
    private readonly string sessionPath;
    private string? token;

    public CloudflareUserAccountService(Uri endpoint)
        : this(
            new HttpClient { BaseAddress = endpoint },
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                ProductIdentity.Name,
                "account.session"))
    {
    }

    internal CloudflareUserAccountService(HttpClient client, string sessionPath)
    {
        this.client = client;
        this.sessionPath = sessionPath;
    }

    public UserProfile? CurrentProfile { get; private set; }

    public async Task<UserAccountResult> RestoreSessionAsync(CancellationToken cancellationToken = default)
    {
        token = await ReadTokenAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token)) return new UserAccountResult(null, null);
        var result = await SendAsync(HttpMethod.Get, "session", null, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded) await ClearTokenAsync().ConfigureAwait(false);
        return result;
    }

    public Task<UserAccountResult> RegisterAsync(string firstName, string lastName, string username, string email, string password, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, "register", new
        {
            firstName,
            lastName,
            username,
            email,
            password,
            termsAccepted = true,
            termsVersion = AccountTerms.CurrentVersion
        }, cancellationToken);

    public Task<UserAccountResult> LoginAsync(string email, string password, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, "login", new { email, password }, cancellationToken);

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(token)) await SendAsync(HttpMethod.Post, "logout", new { }, cancellationToken).ConfigureAwait(false);
        await ClearTokenAsync().ConfigureAwait(false);
        CurrentProfile = null;
    }

    private async Task<UserAccountResult> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(method, path);
            if (!string.IsNullOrWhiteSpace(token)) request.Headers.Authorization = new("Bearer", token);
            if (body is not null) request.Content = JsonContent.Create(body);
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await response.Content.ReadFromJsonAsync<AccountResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || payload?.Profile is null)
                return new UserAccountResult(null, ErrorFor(response.StatusCode, payload?.Error));
            CurrentProfile = new UserProfile(payload.Profile.FirstName, payload.Profile.LastName, payload.Profile.Username, payload.Profile.Email);
            if (!string.IsNullOrWhiteSpace(payload.Token))
            {
                token = payload.Token;
                await WriteTokenAsync(token, cancellationToken).ConfigureAwait(false);
            }
            return new UserAccountResult(CurrentProfile, null);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or IOException)
        {
            return new UserAccountResult(null, "Não foi possível conectar à sua conta. Verifique a internet e tente novamente.");
        }
    }

    private static string ErrorFor(HttpStatusCode status, string? error) => error switch
    {
        "email-in-use" => "Este e-mail já possui uma conta. Faça login para continuar.",
        "username-in-use" => "Este nome de usuário já está em uso. Escolha outro.",
        "invalid-credentials" => "E-mail ou senha incorretos.",
        "too-many-attempts" => "Muitas tentativas. Aguarde alguns minutos antes de tentar novamente.",
        "invalid-request" => "Confira os dados informados e tente novamente.",
        _ when status == HttpStatusCode.Unauthorized => "Sua sessão expirou. Entre novamente.",
        _ => "Não foi possível concluir agora. Tente novamente em alguns instantes."
    };

    private async Task<string?> ReadTokenAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(sessionPath)) return null;
            var protectedBytes = await File.ReadAllBytesAsync(sessionPath, cancellationToken).ConfigureAwait(false);
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser));
        }
        catch (Exception exception) when (exception is CryptographicException or IOException or UnauthorizedAccessException) { return null; }
    }

    private async Task WriteTokenAsync(string value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(sessionPath)!);
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(sessionPath, bytes, cancellationToken).ConfigureAwait(false);
    }

    private Task ClearTokenAsync()
    {
        try { if (File.Exists(sessionPath)) File.Delete(sessionPath); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        token = null;
        return Task.CompletedTask;
    }

    public void Dispose() => client.Dispose();

    private sealed record AccountResponse(string? Token, AccountProfile? Profile, string? Error);
    private sealed record AccountProfile(string FirstName, string LastName, string Username, string Email);
}
