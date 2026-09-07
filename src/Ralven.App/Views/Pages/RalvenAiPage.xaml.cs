using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Ralven.App.Services;
using Ralven.App.ViewModels;
using Ralven.Contracts;
using UserControl = System.Windows.Controls.UserControl;

namespace Ralven.App.Views.Pages;

public partial class RalvenAiPage : UserControl, IDisposable
{
    private readonly RalvenAiService? service;
    private readonly Func<CancellationToken, Task<string?>> getIdToken;
    private readonly bool demoMode;
    private readonly ObservableCollection<ChatMessage> messages = [];
    private readonly CancellationTokenSource lifetime = new();
    private OptimizationProfile? recommendedProfile;
    private bool sending;

    public RalvenAiPage(
        RalvenAiService? service,
        Func<CancellationToken, Task<string?>> getIdToken,
        bool demoMode)
    {
        this.service = service;
        this.getIdToken = getIdToken ?? throw new ArgumentNullException(nameof(getIdToken));
        this.demoMode = demoMode;
        InitializeComponent();
        ConversationItems.ItemsSource = messages;
    }

    private async void Send_Click(object sender, RoutedEventArgs e) => await SendAsync();

    private async void MessageTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            await SendAsync();
        }
    }

    private async Task SendAsync()
    {
        if (sending || DataContext is not MainViewModel { HasProAccess: true } viewModel)
        {
            return;
        }

        var text = MessageTextBox.Text.Trim();
        if (text.Length == 0)
        {
            return;
        }
        var context = viewModel.CreateRalvenAiContext();
        if (context is null)
        {
            StatusText.Text = Localize("RalvenAi.Error.DiagnosticUnavailable");
            return;
        }

        var history = messages.TakeLast(6)
            .Select(item => new RalvenAiConversationTurn(item.IsUser ? "user" : "assistant", item.Text))
            .ToArray();
        messages.Add(new ChatMessage(Localize("RalvenAi.You"), text, true));
        EmptyState.Visibility = Visibility.Collapsed;
        MessageTextBox.Clear();
        SetSending(true);

        try
        {
            RalvenAiReply reply;
            if (demoMode)
            {
                await Task.Yield();
                reply = new RalvenAiReply(
                    Localize("RalvenAi.DemoReply"),
                    OptimizationProfile.Balanced);
            }
            else
            {
                var token = await getIdToken(lifetime.Token);
                if (string.IsNullOrWhiteSpace(token) || service is null)
                {
                    throw new RalvenAiException(RalvenAiError.Unauthorized);
                }
                reply = await service.AskAsync(
                    token,
                    text,
                    LocalizationService.Current.CurrentCulture.Name,
                    context,
                    history,
                    lifetime.Token);
            }

            messages.Add(new ChatMessage(Localize("RalvenAi.Assistant"), reply.Answer, false));
            recommendedProfile = reply.RecommendedProfile;
            RecommendationPanel.Visibility = recommendedProfile is null
                ? Visibility.Collapsed
                : Visibility.Visible;
            if (recommendedProfile is { } profile)
            {
                RecommendationProfileText.Text = Localize($"Profiles.{profile}.Name");
            }
            StatusText.Text = string.Empty;
            ConversationScrollViewer.ScrollToEnd();
        }
        catch (RalvenAiException exception)
        {
            StatusText.Text = Localize($"RalvenAi.Error.{exception.Error}");
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            SetSending(false);
        }
    }

    private void ReviewPlan_Click(object sender, RoutedEventArgs e)
    {
        if (recommendedProfile is not { } profile || Window.GetWindow(this) is not MainWindow shell)
        {
            return;
        }
        shell.RequestReviewRalvenAiPlan(profile);
    }

    private void SetSending(bool value)
    {
        sending = value;
        MessageTextBox.IsEnabled = !value;
        SendButton.IsEnabled = !value;
        if (value)
        {
            StatusText.Text = Localize("RalvenAi.Sending");
        }
    }

    private static string Localize(string key) => LocalizationService.Current.GetString(key);

    public void Dispose() => lifetime.Cancel();

    private sealed record ChatMessage(string Author, string Text, bool IsUser);
}
