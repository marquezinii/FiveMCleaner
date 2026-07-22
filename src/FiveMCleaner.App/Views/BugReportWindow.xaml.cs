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

public partial class BugReportWindow : Window
{
    private readonly IBugReportService service;
    private readonly string appVersion;
    private readonly string profile;
    private readonly string edition;
    private readonly ILocalizationService localization;
    private CancellationTokenSource? sendCancellation;
    private BugReportAttachment? attachment;
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

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Category_Checked(object sender, RoutedEventArgs e)
    {
        category = (sender as FrameworkElement)?.Tag as string;
    }

    private void SelectAttachment_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = T("BugReport.Attachment.DialogTitle"),
            Filter = T("BugReport.Attachment.Filter"),
            Multiselect = false,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            attachment = BugReportImageProcessor.LoadSanitizedImage(dialog.FileName);
            AttachmentLabel.Text = F(
                "BugReport.Attachment.Ready",
                FormatBytes(attachment.Content.Length));
            RemoveAttachmentButton.Visibility = Visibility.Visible;
            ShowStatus(T("BugReport.Attachment.Sanitized"), success: true);
        }
        catch (Exception exception) when (exception is IOException
            or NotSupportedException
            or OverflowException)
        {
            attachment = null;
            RemoveAttachmentButton.Visibility = Visibility.Collapsed;
            ShowStatus(T("BugReport.Attachment.Invalid"), success: false);
        }
    }

    private void RemoveAttachment_Click(object sender, RoutedEventArgs e)
    {
        attachment = null;
        AttachmentLabel.Text = T("BugReport.Attachment.Help");
        RemoveAttachmentButton.Visibility = Visibility.Collapsed;
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
            ShowStatus(F("BugReport.Copy.Failed", exception.Message), success: false);
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
            ShowStatus(F("BugReport.Send.Unconfirmed", exception.Message), success: false);
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
            Attachment = attachment
        };
        return true;
    }

    private void SetFormEnabled(bool enabled)
    {
        SummaryTextBox.IsEnabled = enabled;
        DescriptionTextBox.IsEnabled = enabled;
        CategoryPanel.IsEnabled = enabled;
        IncludeTechnicalInfoCheckBox.IsEnabled = enabled;
        SelectAttachmentButton.IsEnabled = enabled;
        RemoveAttachmentButton.IsEnabled = enabled;
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

    private string FormatBytes(int bytes)
    {
        var value = bytes >= 1024 * 1024
            ? bytes / 1024d / 1024d
            : bytes / 1024d;
        var format = bytes >= 1024 * 1024 ? "0.##" : "0.#";
        var suffix = bytes >= 1024 * 1024 ? "MB" : "KB";
        return $"{value.ToString(format, localization.CurrentCulture)} {suffix}";
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

        if (result.Message.StartsWith("A imagem ultrapassou", StringComparison.Ordinal))
        {
            return T("BugReport.Send.ImageTooLarge");
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

        builder.AppendLine();
        builder.AppendLine(T("BugReport.Clipboard.Description"));
        builder.AppendLine(submission.Description.Trim());
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
