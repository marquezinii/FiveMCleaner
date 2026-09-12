using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Ralven.App.Services;
using Ralven.Contracts;
using Microsoft.Win32;

namespace Ralven.App.Views;

public partial class BugReportWindow : Ralven.App.Controls.DialogWindow
{
    private readonly IBugReportService service;
    private readonly string appVersion;
    private readonly string profile;
    private readonly string edition;
    private readonly ILocalizationService localization;
    private CancellationTokenSource? sendCancellation;
    private string? category;
    private BugCode selectedBugCode = BugCode.Unknown;
    private bool sending;
    private bool delivered;

    public BugReportWindow(
        IBugReportService service,
        string appVersion,
        string profile,
        string edition,
        string? initialLogText = null)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        this.appVersion = appVersion;
        this.profile = profile;
        this.edition = edition;
        localization = LocalizationService.Current;
        InitializeComponent();
        LogTextBox.Text = initialLogText ?? string.Empty;
        PopulateBugCodeComboBox();
        Closing += BugReportWindow_Closing;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Category_Checked(object sender, RoutedEventArgs e)
    {
        category = (sender as FrameworkElement)?.Tag as string;
    }

    private void PopulateBugCodeComboBox()
    {
        BugCodeComboBox.ItemsSource = new[]
        {
            new BugCodeOption(BugCode.APP_OPT_ACTION_EXECUTION, T("BugReport.Reason.Optimization")),
            new BugCodeOption(BugCode.APP_OPT_ACTION_ROLLBACK, T("BugReport.Reason.Rollback")),
            new BugCodeOption(BugCode.APP_DIAG_FIVEM_DETECTION, T("BugReport.Reason.GameDetection")),
            new BugCodeOption(BugCode.APP_UI_RENDER, T("BugReport.Reason.Interface")),
            new BugCodeOption(BugCode.APP_AUTH_FLOW, T("BugReport.Reason.Account")),
            new BugCodeOption(BugCode.APP_SETTINGS_PERSISTENCE, T("BugReport.Reason.Settings")),
            new BugCodeOption(BugCode.UPD_INSTALLER_EXECUTION, T("BugReport.Reason.Update")),
            new BugCodeOption(BugCode.WIN_PRIVILEGE, T("BugReport.Reason.Windows")),
            new BugCodeOption(BugCode.APP_LIFECYCLE, T("BugReport.Reason.Crash"))
        };
        BugCodeComboBox.SelectedIndex = 0;
    }

    private void BugCode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BugCodeComboBox.SelectedItem is BugCodeOption option)
        {
            selectedBugCode = option.Code;
        }
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
        if (email.Length > 0 && !AccountValidation.IsValidEmail(email))
        {
            ShowStatus(T("BugReport.Validation.Email"), success: false);
            EmailTextBox.Focus();
            return false;
        }

        if (selectedBugCode == BugCode.Unknown)
        {
            ShowStatus(T("BugReport.Validation.BugCode"), success: false);
            BugCodeComboBox.Focus();
            return false;
        }

        var logText = LogTextBox.Text.Trim();
        if (Encoding.UTF8.GetByteCount(logText) > BugReportSubmission.MaxLogTextBytes)
        {
            ShowStatus(T("BugReport.Validation.Log"), success: false);
            LogTextBox.Focus();
            return false;
        }

        submission = new BugReportSubmission
        {
            ReportId = Guid.NewGuid(),
            Category = category,
            BugCode = selectedBugCode,
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

    private void SetFormEnabled(bool enabled)
    {
        SummaryTextBox.IsEnabled = enabled;
        DescriptionTextBox.IsEnabled = enabled;
        CategoryPanel.IsEnabled = enabled;
        BugCodeComboBox.IsEnabled = enabled;
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
            success ? "SuccessBaseBrush" : "DangerBaseBrush");
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
        return string.IsNullOrWhiteSpace(result.Message)
            ? T(result.Accepted ? "BugReport.Send.Accepted" : "BugReport.Send.NotConfirmed")
            : result.Message;
    }

    private string FormatForClipboard(BugReportSubmission submission)
    {
        var builder = new StringBuilder();
        builder.AppendLine(T("BugReport.Clipboard.Title"));
        builder.AppendLine(F("BugReport.Clipboard.Id", submission.ReportId.ToString("D")));
        builder.AppendLine(F("BugReport.Clipboard.Category", LocalizeCategory(submission.Category)));
        var categoryResourceKey = BugCodeCatalog.GetCategoryResourceKey(submission.BugCode)
            ?? "BugCode.Category.Unknown";
        builder.AppendLine(F(
            "BugReport.Clipboard.BugCode",
            submission.BugCode.ToString(),
            T(categoryResourceKey)));
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

    private string LocalizeCategory(string categoryId) => categoryId switch
    {
        "optimization" => T("BugReport.Category.Optimization"),
        "games" => T("BugReport.Category.Games"),
        "windows" => T("BugReport.Category.Windows"),
        "interface" => T("BugReport.Category.Interface"),
        "crash" => T("BugReport.Category.Crash"),
        _ => T("BugReport.Category.Other")
    };

    private sealed record BugCodeOption(BugCode Code, string Label);

}
