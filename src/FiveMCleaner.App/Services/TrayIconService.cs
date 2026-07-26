using System.Drawing;
using Forms = System.Windows.Forms;

namespace FiveMCleaner.App.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly ILocalizationService localization;
    private readonly Forms.NotifyIcon notifyIcon;
    private readonly Forms.ToolStripMenuItem openItem;
    private readonly Forms.ToolStripMenuItem exitItem;
    private bool disposed;

    public TrayIconService(ILocalizationService? localization = null)
    {
        this.localization = localization ?? LocalizationService.Current;
        openItem = new Forms.ToolStripMenuItem();
        exitItem = new Forms.ToolStripMenuItem();
        openItem.Click += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(openItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        notifyIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = LoadApplicationIcon(),
            Text = "FiveMCleaner",
            Visible = false
        };
        notifyIcon.DoubleClick += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);
        notifyIcon.BalloonTipClicked += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);
        this.localization.LanguageChanged += OnLanguageChanged;
        UpdateText();
    }

    public event EventHandler? ShowRequested;

    public event EventHandler? ExitRequested;

    public bool IsVisible => notifyIcon.Visible;

    public void Show(bool announce)
    {
        ThrowIfDisposed();
        notifyIcon.Visible = true;
        if (announce)
        {
            notifyIcon.BalloonTipTitle = localization.GetString("Tray.Title");
            notifyIcon.BalloonTipText = localization.GetString("Tray.Message");
            notifyIcon.BalloonTipIcon = Forms.ToolTipIcon.Info;
            notifyIcon.ShowBalloonTip(3500);
        }
    }

    public void Hide()
    {
        if (!disposed)
        {
            notifyIcon.Visible = false;
        }
    }

    /// <summary>
    /// Shows the native Windows notification (Action Center toast, using the
    /// app's own tray icon and name) announcing that a new version is
    /// available, whether the main window is currently in the foreground or
    /// minimized to the tray. Clicking the notification raises
    /// <see cref="ShowRequested"/>, the same event used to restore the
    /// window from the tray.
    /// </summary>
    public void ShowUpdateAvailable(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ThrowIfDisposed();
        _ = ShowUpdateAvailableAsync(version);
    }

    private async Task ShowUpdateAvailableAsync(string version)
    {
        var wasVisible = notifyIcon.Visible;
        if (!wasVisible)
        {
            notifyIcon.Visible = true;

            // Windows silently drops a balloon tip requested in the same
            // tick an icon first becomes visible (a well-documented
            // NotifyIcon/Shell_NotifyIcon quirk) — the tray host needs a
            // moment to register the icon before it will display a balloon
            // on it. Without this delay, the very first update notification
            // after the icon appears could be lost.
            await Task.Delay(TimeSpan.FromMilliseconds(300));
            if (disposed)
            {
                return;
            }
        }

        notifyIcon.BalloonTipTitle = localization.GetString("Notification.UpdateAvailable.Title");
        notifyIcon.BalloonTipText = localization.Format("Notification.UpdateAvailable.Message", version);
        notifyIcon.BalloonTipIcon = Forms.ToolTipIcon.Info;
        notifyIcon.ShowBalloonTip(7000);

        if (!wasVisible)
        {
            // The icon was only made visible to carry this notification (the
            // user does not have "minimize to tray" active); hide it again
            // once the balloon has had time to display instead of leaving a
            // tray icon behind that the user never asked for.
            _ = HideAfterDelayAsync(TimeSpan.FromSeconds(8));
        }
    }

    private async Task HideAfterDelayAsync(TimeSpan delay)
    {
        await Task.Delay(delay);
        if (!disposed)
        {
            notifyIcon.Visible = false;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        localization.LanguageChanged -= OnLanguageChanged;
        notifyIcon.Visible = false;
        notifyIcon.ContextMenuStrip?.Dispose();
        notifyIcon.Dispose();
    }

    private void OnLanguageChanged(object? sender, AppLanguageChangedEventArgs e) => UpdateText();

    private void UpdateText()
    {
        openItem.Text = localization.GetString("Tray.Open");
        exitItem.Text = localization.GetString("Tray.Exit");
    }

    private static Icon LoadApplicationIcon()
    {
        var executablePath = Environment.ProcessPath;
        return !string.IsNullOrWhiteSpace(executablePath)
            ? Icon.ExtractAssociatedIcon(executablePath) ?? SystemIcons.Application
            : SystemIcons.Application;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}
