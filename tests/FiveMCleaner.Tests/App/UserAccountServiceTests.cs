using System.Net;
using System.Text.Json;
using System.Windows;
using FiveMCleaner.App.Views;
using FiveMCleaner.App.Services;
using Xunit;

namespace FiveMCleaner.Tests.App;

public sealed class UserAccountServiceTests
{
    [Fact]
    public async Task RegisterAsync_SendsUsernameAndVersionedTermsAcceptance()
    {
        string? requestJson = null;
        using var client = new HttpClient(new StubHandler(async request =>
        {
            requestJson = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("""
                    {"profile":{"firstName":"João","lastName":"Silva","username":"joao.silva","email":"joao@example.com"}}
                    """, System.Text.Encoding.UTF8, "application/json")
            };
        })) { BaseAddress = new Uri("https://example.test/account/") };
        using var service = new CloudflareUserAccountService(client, Path.Combine(Path.GetTempPath(), $"account-{Guid.NewGuid():N}.session"));

        var result = await service.RegisterAsync("João", "Silva", "joao.silva", "joao@example.com", "uma senha segura");

        Assert.True(result.Succeeded);
        Assert.Equal("joao.silva", result.Profile!.Username);
        using var document = JsonDocument.Parse(requestJson!);
        var root = document.RootElement;
        Assert.Equal("joao.silva", root.GetProperty("username").GetString());
        Assert.True(root.GetProperty("termsAccepted").GetBoolean());
        Assert.Equal(AccountTerms.CurrentVersion, root.GetProperty("termsVersion").GetString());
    }

    [Fact]
    public void AccountAndTermsWindows_CanOpenWithoutAWindowBackdropCrash()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var application = new FiveMCleaner.App.App();
                application.InitializeComponent();
                var account = new AccountWindow(new StubAccountService());
                account.Show();
                var terms = new TermsOfUseWindow { Owner = account };
                terms.Show();
                terms.Close();
                account.Close();
                application.Shutdown();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);

        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "A janela de conta não concluiu o smoke test.");
        Assert.Null(failure);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }

    private sealed class StubAccountService : IUserAccountService
    {
        public UserProfile? CurrentProfile => null;
        public Task<UserAccountResult> RestoreSessionAsync(CancellationToken cancellationToken = default) => Task.FromResult(new UserAccountResult(null, null));
        public Task<UserAccountResult> RegisterAsync(string firstName, string lastName, string username, string email, string password, CancellationToken cancellationToken = default) => Task.FromResult(new UserAccountResult(null, null));
        public Task<UserAccountResult> LoginAsync(string email, string password, CancellationToken cancellationToken = default) => Task.FromResult(new UserAccountResult(null, null));
        public Task LogoutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
