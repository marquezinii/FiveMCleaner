using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Globalization;
using System.Windows.Threading;
using FiveMCleaner.App.Services;
using FiveMCleaner.Contracts;
using FiveMCleaner.Core.Catalog;
using FiveMCleaner.Core.Planning;

namespace FiveMCleaner.App.ViewModels;

public sealed partial class MainViewModel
{
    /// <summary>
    /// The dismissible title-bar banner for an admin-broadcast live alert.
    /// False once the user closes it for this specific alert id (persisted
    /// in <see cref="AppSettings.DismissedLiveAlertId"/>), even though the
    /// alert may still be active -- see <see cref="IsLiveAlertIconVisible"/>.
    /// </summary>
    public bool IsLiveAlertBannerVisible
    {
        get => isLiveAlertBannerVisible;
        private set => SetProperty(ref isLiveAlertBannerVisible, value);
    }

    /// <summary>
    /// The persistent warning-triangle icon. Stays visible for as long as
    /// the alert is active on the server, independent of whether the banner
    /// was dismissed.
    /// </summary>
    public bool IsLiveAlertIconVisible
    {
        get => isLiveAlertIconVisible;
        private set => SetProperty(ref isLiveAlertIconVisible, value);
    }

    public string LiveAlertMessage
    {
        get => liveAlertMessage;
        private set => SetProperty(ref liveAlertMessage, value);
    }

    /// <summary>
    /// Polls the current admin-broadcast live alert. Called at startup and
    /// then hourly by <see cref="liveAlertTimer"/> -- see
    /// docs/superpowers/specs/2026-08-17-live-alerts-design.md. A network
    /// failure or malformed response leaves the current banner/icon state
    /// untouched instead of flickering it away.
    /// </summary>
    public async Task CheckLiveAlertAsync()
    {
        if (liveAlertService is null)
        {
            return;
        }

        try
        {
            var snapshot = await liveAlertService.GetCurrentAsync();
            if (snapshot is not null)
            {
                ApplyLiveAlert(snapshot);
            }
        }
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            // Aviso ao vivo é best-effort; uma falha de rede não altera o estado atual.
        }
    }

    private void ApplyLiveAlert(LiveAlertSnapshot snapshot)
    {
        liveAlertId = snapshot.Active ? snapshot.Id : null;
        LiveAlertMessage = snapshot.Active ? snapshot.Message : string.Empty;
        IsLiveAlertIconVisible = snapshot.Active;
        IsLiveAlertBannerVisible = snapshot.Active
            && !string.IsNullOrEmpty(liveAlertId)
            && !string.Equals(liveAlertId, dismissedLiveAlertId, StringComparison.Ordinal);
    }

    /// <summary>
    /// Closes the live-alert banner for the currently shown alert only; the
    /// warning icon keeps showing while the alert stays active on the
    /// server. Persisted so the banner does not reappear on the next launch.
    /// </summary>
    public void DismissLiveAlert()
    {
        if (!IsLiveAlertBannerVisible)
        {
            return;
        }

        IsLiveAlertBannerVisible = false;
        dismissedLiveAlertId = liveAlertId;
        SettingsChanged(refreshPlan: false);
    }
}
