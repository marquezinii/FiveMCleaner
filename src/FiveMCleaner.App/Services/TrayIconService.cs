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
