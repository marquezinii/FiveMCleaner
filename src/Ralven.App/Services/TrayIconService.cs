using Forms = System.Windows.Forms;
using Ralven.Contracts;

namespace Ralven.App.Services;

public sealed class TrayIconService : IDisposable
{
    private const int MaximumToolTipLength = 127;
    private readonly ILocalizationService localization;
    private readonly Forms.NotifyIcon notifyIcon;
    private readonly Forms.ContextMenuStrip nativeMenuBridge;
    private bool persistentVisible;
    private bool temporaryVisible;
    private int notificationGeneration;
    private bool disposed;

    public TrayIconService(ILocalizationService? localization = null)
    {
        this.localization = localization ?? LocalizationService.Current;
        nativeMenuBridge = new Forms.ContextMenuStrip();
        nativeMenuBridge.Opening += NativeMenuBridge_Opening;

        notifyIcon = new Forms.NotifyIcon
        {
            // Keep the native tray invocation path. Its Opening event is
            // cancelled and forwarded to the WPF menu, preserving the shell
            // integration without accepting the generic ToolStrip surface.
            ContextMenuStrip = nativeMenuBridge,
            Icon = LoadApplicationIcon(),
            Text = ProductIdentity.DisplayName,
            Visible = false
        };
        notifyIcon.MouseClick += (_, args) => RequestShow(args);
        notifyIcon.MouseDoubleClick += (_, args) => RequestShow(args);
        notifyIcon.BalloonTipClicked += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? ShowRequested;

    public event EventHandler? MenuRequested;

    public void Show(bool announce)
    {
        ThrowIfDisposed();
        var wasVisible = notifyIcon.Visible;
        persistentVisible = true;
        ApplyVisibility();
        if (announce)
        {
            _ = ShowBalloonAsync(
                localization.GetString("Tray.Title"),
                localization.GetString("Tray.Message"),
                3500,
                wasVisible);
        }
    }

    public void SetPersistentVisibility(bool visible)
    {
        ThrowIfDisposed();
        persistentVisible = visible;
        ApplyVisibility();
    }

    public void UpdateToolTip(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ThrowIfDisposed();
        notifyIcon.Text = NormalizeToolTip(text);
    }

    public void Hide()
    {
        if (!disposed)
        {
            notificationGeneration++;
            persistentVisible = false;
            temporaryVisible = false;
            ApplyVisibility();
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
        _ = ShowBalloonAsync(
            localization.GetString("Notification.UpdateAvailable.Title"),
            localization.Format("Notification.UpdateAvailable.Message", version),
            7000,
            notifyIcon.Visible);
    }

    public void ShowInformation(string title, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ThrowIfDisposed();
        _ = ShowBalloonAsync(title, message, 4500, notifyIcon.Visible);
    }

    private async Task ShowBalloonAsync(string title, string message, int timeout, bool wasVisible)
    {
        var generation = ++notificationGeneration;
        var ownsTemporaryVisibility = !persistentVisible;
        if (!wasVisible)
        {
            temporaryVisible = ownsTemporaryVisibility;
            ApplyVisibility();

            // Windows silently drops a balloon tip requested in the same
            // tick an icon first becomes visible (a well-documented
            // NotifyIcon/Shell_NotifyIcon quirk) — the tray host needs a
            // moment to register the icon before it will display a balloon
            // on it. Without this delay, the very first update notification
            // after the icon appears could be lost.
            await Task.Delay(TimeSpan.FromMilliseconds(300));
            if (disposed || generation != notificationGeneration)
            {
                return;
            }
        }

        notifyIcon.BalloonTipTitle = title;
        notifyIcon.BalloonTipText = message;
        notifyIcon.BalloonTipIcon = Forms.ToolTipIcon.Info;
        notifyIcon.ShowBalloonTip(timeout);

        if (ownsTemporaryVisibility)
        {
            // The icon was only made visible to carry this notification (the
            // user does not have "minimize to tray" active); hide it again
            // once the balloon has had time to display instead of leaving a
            // tray icon behind that the user never asked for.
            await Task.Delay(TimeSpan.FromMilliseconds(timeout + 1000));
            if (!disposed && generation == notificationGeneration)
            {
                temporaryVisible = false;
                ApplyVisibility();
            }
        }
    }

    private void NativeMenuBridge_Opening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        MenuRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RequestShow(Forms.MouseEventArgs args)
    {
        if (args.Button == Forms.MouseButtons.Left)
        {
            ShowRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        notifyIcon.Visible = false;
        notifyIcon.ContextMenuStrip?.Dispose();
        notifyIcon.Dispose();
    }

    private void ApplyVisibility() => notifyIcon.Visible = persistentVisible || temporaryVisible;

    internal static string NormalizeToolTip(string text)
    {
        if (text.Length <= MaximumToolTipLength)
        {
            return text;
        }

        var length = char.IsHighSurrogate(text[MaximumToolTipLength - 1])
            ? MaximumToolTipLength - 1
            : MaximumToolTipLength;
        return text[..length];
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
