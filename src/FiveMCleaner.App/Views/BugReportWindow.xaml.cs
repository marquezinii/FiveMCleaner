using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FiveMCleaner.App.Services;
using Microsoft.Win32;

namespace FiveMCleaner.App.Views;

public partial class BugReportWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly IBugReportService service;
    private readonly string appVersion;
    private readonly string profile;
    private readonly string edition;
    private readonly ILocalizationService localization;
    private CancellationTokenSource? sendCancellation;
    private string? category;
    private bool sending;
    private bool delivered;

    public BugReportWindow(
        IBugReportService service,
        string appVersion,
        string profile,
        string edition)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        this.appVersion = appVersion;
        this.profile = profile;
        this.edition = edition;
        localization = LocalizationService.Current;
        InitializeComponent();
        ConstrainToWorkArea();
        Closing += BugReportWindow_Closing;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Category_Checked(object sender, RoutedEventArgs e)
    {
        category = (sender as FrameworkElement)?.Tag as string;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (!TryCreateSubmission(out var submission))
        {
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(FormatForClipboard(submission));
            ShowStatus(T("BugReport.Copy.Success"), success: true);
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException
            or InvalidOperationException)
        {
            ShowStatus(F("BugReport.Copy.Failed", localization.DescribeException(exception)), success: false);
        }
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        if (sending || !TryCreateSubmission(out var submission))
        {
            return;
        }

        sending = true;
        SetFormEnabled(false);
        SendButton.IsEnabled = false;
        SendButton.Content = T("BugReport.Sending");
        sendCancellation = new CancellationTokenSource();
        try
        {
            var result = await service.SendAsync(submission, sendCancellation.Token);
            ShowStatus(LocalizeSendResult(result), result.Accepted);
            if (result.Accepted)
            {
                delivered = true;
                SendButton.Content = T("BugReport.Sent");
                CopyButton.IsEnabled = true;
                return;
            }
        }
        catch (OperationCanceledException)
        {
            ShowStatus(T("BugReport.Send.Cancelled"), success: false);
        }
        catch (Exception exception) when (exception is HttpRequestException
            or IOException
            or ArgumentException)
        {
            ShowStatus(F("BugReport.Send.Unconfirmed", localization.DescribeException(exception)), success: false);
        }
        finally
        {
            sendCancellation?.Dispose();
            sendCancellation = null;
            sending = false;
            if (!delivered)
            {
                SendButton.Content = T("BugReport.TryAgain");
                SendButton.IsEnabled = true;
                SetFormEnabled(true);
            }
        }
    }

    private bool TryCreateSubmission(out BugReportSubmission submission)
    {
        submission = null!;
        var summary = SummaryTextBox.Text.Trim();
        var description = DescriptionTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(category))
        {
            ShowStatus(T("BugReport.Validation.Category"), success: false);
            return false;
        }

        if (summary.Length is < 5 or > 120)
        {
            ShowStatus(T("BugReport.Validation.Summary"), success: false);
            SummaryTextBox.Focus();
            return false;
        }

        if (description.Length is < 20 or > 8000)
        {
            ShowStatus(T("BugReport.Validation.Description"), success: false);
            DescriptionTextBox.Focus();
            return false;
        }

        var email = EmailTextBox.Text.Trim();
        if (email.Length > 0 && !IsValidEmail(email))
        {
            ShowStatus(T("BugReport.Validation.Email"), success: false);
            EmailTextBox.Focus();
            return false;
        }

        var logText = LogTextBox.Text.Trim();
        if (Encoding.UTF8.GetByteCount(logText) > MaxLogTextBytes)
        {
            ShowStatus(T("BugReport.Validation.Log"), success: false);
            LogTextBox.Focus();
            return false;
        }

        submission = new BugReportSubmission
        {
            ReportId = Guid.NewGuid(),
            Category = category,
            Summary = summary,
            Description = description,
            AppVersion = appVersion,
            Profile = profile,
            TechnicalSummary = IncludeTechnicalInfoCheckBox.IsChecked == true
                ? F("BugReport.Technical.Summary", RuntimeInformation.OSDescription, edition, profile)
                : null,
            Email = email.Length > 0 ? email : null,
            LogText = logText.Length > 0 ? logText : null
        };
        return true;
    }

    private const int MaxLogTextBytes = 100 * 1024;

    private static bool IsValidEmail(string value) =>
        value.Length <= 254 && System.Text.RegularExpressions.Regex.IsMatch(
            value,
            @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
            System.Text.RegularExpressions.RegexOptions.None,
            TimeSpan.FromMilliseconds(100));

    private void SetFormEnabled(bool enabled)
    {
        SummaryTextBox.IsEnabled = enabled;
        DescriptionTextBox.IsEnabled = enabled;
        CategoryPanel.IsEnabled = enabled;
        IncludeTechnicalInfoCheckBox.IsEnabled = enabled;
        LogTextBox.IsEnabled = enabled;
        EmailTextBox.IsEnabled = enabled;
        CopyButton.IsEnabled = enabled;
    }

    private void ShowStatus(string message, bool success)
    {
        StatusBorder.Visibility = Visibility.Visible;
        StatusText.Text = message;
        StatusText.SetResourceReference(
            TextBlock.ForegroundProperty,
            success ? "GreenBrush" : "RedBrush");
    }

    private void BugReportWindow_Closing(object? sender, CancelEventArgs e)
    {
        sendCancellation?.Cancel();
    }

    private string T(string key) => localization.GetString(key);

    private string F(string key, params object?[] arguments) =>
        localization.Format(key, arguments);

    private string LocalizeSendResult(BugReportSendResult result)
    {
        if (result.Accepted)
        {
            return T("BugReport.Send.Accepted");
        }

        if (result.Message.StartsWith("O serviço recebeu muitos", StringComparison.Ordinal))
        {
            return T("BugReport.Send.RateLimited");
        }

        if (result.Message.StartsWith("O canal de relatos aguarda", StringComparison.Ordinal))
        {
            return T("BugReport.Send.ActivationRequired");
        }

        if (result.Message.StartsWith("O serviço respondeu em um formato inesperado", StringComparison.Ordinal))
        {
            return T("BugReport.Send.UnexpectedResponse");
        }

        return T("BugReport.Send.NotConfirmed");
    }

    private string FormatForClipboard(BugReportSubmission submission)
    {
        var builder = new StringBuilder();
        builder.AppendLine(T("BugReport.Clipboard.Title"));
        builder.AppendLine(F("BugReport.Clipboard.Id", submission.ReportId.ToString("D")));
        builder.AppendLine(F("BugReport.Clipboard.Category", submission.Category));
        builder.AppendLine(F("BugReport.Clipboard.Summary", submission.Summary.Trim()));
        builder.AppendLine(F("BugReport.Clipboard.Version", submission.AppVersion));
        builder.AppendLine(F("BugReport.Clipboard.Profile", submission.Profile));
        if (!string.IsNullOrWhiteSpace(submission.TechnicalSummary))
        {
            builder.AppendLine(F("BugReport.Clipboard.Technical", submission.TechnicalSummary));
        }

        if (!string.IsNullOrWhiteSpace(submission.Email))
        {
            builder.AppendLine(F("BugReport.Clipboard.Email", submission.Email));
        }

        builder.AppendLine();
        builder.AppendLine(T("BugReport.Clipboard.Description"));
        builder.AppendLine(submission.Description.Trim());

        if (!string.IsNullOrWhiteSpace(submission.LogText))
        {
            builder.AppendLine();
            builder.AppendLine(T("BugReport.Clipboard.Log"));
            builder.AppendLine(submission.LogText.Trim());
        }

        return builder.ToString();
    }

    private void ConstrainToWorkArea()
    {
        const double outerMargin = 24;
        var workArea = SystemParameters.WorkArea;
        var availableWidth = Math.Max(320, workArea.Width - outerMargin);
        var availableHeight = Math.Max(320, workArea.Height - outerMargin);
        MinWidth = Math.Min(MinWidth, availableWidth);
        MinHeight = Math.Min(MinHeight, availableHeight);
        MaxWidth = availableWidth;
        MaxHeight = availableHeight;
        Width = Math.Min(Width, availableWidth);
        Height = Math.Min(Height, availableHeight);
    }
}
