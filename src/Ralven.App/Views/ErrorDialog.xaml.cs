using System.Runtime.InteropServices;
using System.Windows;
using Ralven.App.Services;

namespace Ralven.App.Views;

internal enum ErrorDialogKind
{
    Recoverable,
    Fatal,
    TestRecoverable,
    TestFatal
}

public partial class ErrorDialog : Ralven.App.Controls.DialogWindow
{
    private readonly ErrorDialogKind kind;
    private readonly Action<string>? reportRequested;
    private readonly ILocalizationService localization;
    private readonly string details;

    private ErrorDialog(
        Window? owner,
        Exception exception,
        ErrorDialogKind kind,
        Action<string>? reportRequested = null)
    {
        this.kind = kind;
        this.reportRequested = reportRequested;
        localization = LocalizationService.Current;
        details = ErrorDetailsFormatter.Format(exception, localization);

        Owner = owner;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        InitializeComponent();
        ConfigurePresentation();
    }

    internal static void ShowFatal(Window? owner, Exception exception) =>
        new ErrorDialog(owner, exception, ErrorDialogKind.Fatal).ShowDialog();

    internal static void ShowRecoverable(Window? owner, Exception exception, Action<string>? reportRequested) =>
        new ErrorDialog(owner, exception, ErrorDialogKind.Recoverable, reportRequested).ShowDialog();

    internal static void ShowTest(Window? owner, ErrorExperienceTestMode mode)
    {
        var kind = mode == ErrorExperienceTestMode.Fatal ? ErrorDialogKind.TestFatal : ErrorDialogKind.TestRecoverable;
        new ErrorDialog(owner, new InvalidOperationException("Controlled error presentation test."), kind).ShowDialog();
    }

    internal bool RestartRequested { get; private set; }

    private bool IsFatal => kind is ErrorDialogKind.Fatal or ErrorDialogKind.TestFatal;

    private bool IsTest => kind is ErrorDialogKind.TestFatal or ErrorDialogKind.TestRecoverable;

    private void ConfigurePresentation()
    {
        Title = T(IsTest ? "ErrorDialog.Test.WindowTitle" : "ErrorDialog.WindowTitle");
        HeadlineText.Text = T(IsTest
            ? "ErrorDialog.Test.Headline"
            : IsFatal ? "ErrorDialog.Fatal.Headline" : "ErrorDialog.Recoverable.Headline");
        MessageText.Text = T(IsTest
            ? "ErrorDialog.Test.Message"
            : IsFatal ? "ErrorDialog.Fatal.Message" : "ErrorDialog.Recoverable.Message");
        GuidanceText.Text = T(IsTest
            ? "ErrorDialog.Test.Guidance"
            : IsFatal ? "ErrorDialog.Fatal.Guidance" : "ErrorDialog.Recoverable.Guidance");
        DetailsTitleText.Text = T("ErrorDialog.Details.Title");
        DetailsHelpText.Text = T("ErrorDialog.Details.Help");
        DetailsTextBox.Text = details;
        CopyButton.Content = T("ErrorDialog.Copy");
        CloseButton.Content = T(IsTest ? "ErrorDialog.ClosePreview" : IsFatal ? "ErrorDialog.Exit" : "Dialog.Close");

        RestartButton.Visibility = IsFatal && !IsTest ? Visibility.Visible : Visibility.Collapsed;
        RestartButton.Content = T("ErrorDialog.Restart");
        ReportButton.Visibility = reportRequested is null ? Visibility.Collapsed : Visibility.Visible;
        ReportButton.Content = T("ErrorDialog.Report");
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(details);
            ShowStatus(T("ErrorDialog.Copy.Success"));
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException)
        {
            ShowStatus(F("ErrorDialog.Copy.Failed", localization.DescribeException(exception)));
        }
    }

    private void Report_Click(object sender, RoutedEventArgs e)
    {
        Close();
        reportRequested?.Invoke(details);
    }

    private void Restart_Click(object sender, RoutedEventArgs e)
    {
        if (!AppRestart.TryStart(Environment.ProcessPath))
        {
            ShowStatus(T("ErrorDialog.Restart.Failed"));
            return;
        }

        RestartRequested = true;
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ShowStatus(string text)
    {
        StatusText.Text = text;
        StatusText.Visibility = Visibility.Visible;
    }

    private string T(string key) => localization.GetString(key);

    private string F(string key, params object?[] arguments) => localization.Format(key, arguments);
}
