using Ralven.App.Services;
using Ralven.Contracts;
using Ralven.Windows.Diagnostics;
using Ralven.Windows.Infrastructure;

namespace Ralven.App.ViewModels;

public sealed partial class MainViewModel
{
    private readonly WindowsGamingControlsService windowsGamingControls;
    private readonly IWindowsSystemHealthInspector windowsSystemHealthInspector;
    private WindowsGamingSettingsDto windowsGamingSettings = new(
        WindowsGamingSettingState.Unknown,
        WindowsGamingSettingState.Unknown);
    private bool isWindowsGamingBusy;
    private Guid? windowsGamingTransactionId;
    private WindowsGamingControlsBlockReason windowsGamingBlockReason =
        WindowsGamingControlsBlockReason.ProcessInspectionUnavailable;
    private string windowsGamingStatusKey = "System.Gaming.Status.Ready";
    private WindowsSystemHealthSnapshot? windowsSystemHealth;
    private bool isWindowsSystemHealthBusy;
    private string windowsSystemHealthStatusKey = "System.Health.Status.Loading";
    private BugCode? windowsSystemHealthBugCode;

    public bool IsWindowsGamingBusy => isWindowsGamingBusy;

    public bool IsWindowsSystemHealthBusy => isWindowsSystemHealthBusy;

    public string WindowsGameModeStateLabel => DescribeWindowsGamingState(
        windowsGamingSettings.GameMode);

    public string WindowsBackgroundCaptureStateLabel => DescribeWindowsGamingState(
        windowsGamingSettings.BackgroundCapture);

    public string WindowsGamingStatusMessage => localization.GetString(windowsGamingStatusKey);

    public string WindowsAntivirusHealthLabel => DescribeWindowsSecurityHealth(
        windowsSystemHealth?.Antivirus.State);

    public string WindowsFirewallHealthLabel => DescribeWindowsSecurityHealth(
        windowsSystemHealth?.Firewall.State);

    public string WindowsAutomaticUpdatesHealthLabel => DescribeWindowsSecurityHealth(
        windowsSystemHealth?.AutomaticUpdates.State);

    public string WindowsSystemHealthStatusMessage => OptimizationFailureMessageFormatter.AppendCode(
        localization.GetString(windowsSystemHealthStatusKey),
        windowsSystemHealthStatusKey != "System.Health.Status.Unavailable"
            ? null
            : windowsSystemHealthBugCode
                ?? windowsSystemHealth?.Antivirus.BugCode
                ?? windowsSystemHealth?.Firewall.BugCode
                ?? windowsSystemHealth?.AutomaticUpdates.BugCode,
        code => localization.Format("Report.ErrorCodeSuffix", code))!;

    public string WindowsSystemHealthUpdatedLabel => windowsSystemHealth is null
        ? localization.GetString("System.Health.Updated.Pending")
        : localization.Format(
            "System.Health.UpdatedAt",
            windowsSystemHealth.ObservedAtUtc.ToLocalTime().ToString(
                "HH:mm",
                localization.CurrentCulture));

    public bool CanRefreshWindowsGamingSettings => !IsBusy
        && !isPersonalBusy
        && !isInitializing
        && !isWindowsGamingBusy
        && !IsUpdateDownloading
        && !IsInstallingUpdate
        && !IsGtaVBenchmarkRunning;

    public bool CanRefreshWindowsSystemHealth => !IsBusy
        && !isInitializing
        && !isWindowsSystemHealthBusy
        && !IsUpdateDownloading
        && !IsInstallingUpdate
        && !IsGtaVBenchmarkRunning;

    public bool CanApplyWindowsGamingSettings => CanRefreshWindowsGamingSettings
        && windowsGamingBlockReason == WindowsGamingControlsBlockReason.None
        && CanChangeWindowsGamingSettings(windowsGamingSettings)
        && !IsDesiredWindowsGamingState(windowsGamingSettings);

    public bool CanRestoreWindowsGamingSettings => CanRefreshWindowsGamingSettings
        && windowsGamingBlockReason == WindowsGamingControlsBlockReason.None
        && windowsGamingTransactionId.HasValue;

    public async Task RefreshWindowsGamingSettingsAsync()
    {
        if (!CanRefreshWindowsGamingSettings)
        {
            return;
        }

        SetWindowsGamingBusy(true);
        windowsGamingStatusKey = "System.Gaming.Status.Reading";
        RefreshWindowsGamingPresentation();
        try
        {
            windowsGamingSettings = await windowsGamingControls.ReadAsync();
            windowsGamingBlockReason = windowsGamingControls.GetMutationBlockReason();
            windowsGamingStatusKey = GetWindowsGamingStatusKey();
        }
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            windowsGamingSettings = new WindowsGamingSettingsDto(
                WindowsGamingSettingState.Unavailable,
                WindowsGamingSettingState.Unavailable);
            windowsGamingBlockReason =
                WindowsGamingControlsBlockReason.ProcessInspectionUnavailable;
            windowsGamingStatusKey = "System.Gaming.Status.Unavailable";
        }
        finally
        {
            SetWindowsGamingBusy(false);
            RefreshWindowsGamingPresentation();
        }
    }

    public async Task RefreshWindowsSystemHealthAsync()
    {
        if (!CanRefreshWindowsSystemHealth)
        {
            return;
        }

        SetWindowsSystemHealthBusy(true);
        windowsSystemHealthStatusKey = "System.Health.Status.Reading";
        RefreshWindowsSystemHealthPresentation();
        try
        {
            windowsSystemHealth = await windowsSystemHealthInspector.InspectAsync();
            windowsSystemHealthBugCode = null;
            windowsSystemHealthStatusKey = !windowsSystemHealth.Antivirus.IsAvailable
                && !windowsSystemHealth.Firewall.IsAvailable
                && !windowsSystemHealth.AutomaticUpdates.IsAvailable
                    ? "System.Health.Status.Unavailable"
                    : windowsSystemHealth.IsPartial
                        ? "System.Health.Status.Partial"
                        : "System.Health.Status.Ready";
        }
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            windowsSystemHealth = null;
            windowsSystemHealthBugCode = BugCodeClassifier.ClassifyException(exception, "security-health");
            windowsSystemHealthStatusKey = "System.Health.Status.Unavailable";
        }
        finally
        {
            SetWindowsSystemHealthBusy(false);
            RefreshWindowsSystemHealthPresentation();
        }
    }

    public async Task ApplyWindowsGamingSettingsAsync()
    {
        if (!CanApplyWindowsGamingSettings)
        {
            return;
        }

        SetWindowsGamingBusy(true);
        windowsGamingStatusKey = "System.Gaming.Status.Applying";
        RefreshWindowsGamingPresentation();
        try
        {
            var result = await windowsGamingControls.ApplyAsync();
            windowsGamingSettings = result.Settings;
            windowsGamingBlockReason = result.BlockReason;
            if (result.Succeeded)
            {
                windowsGamingTransactionId = result.Changed
                    ? result.TransactionId
                    : null;
                windowsGamingStatusKey = result.Changed
                    ? "System.Gaming.Status.Applied"
                    : "System.Gaming.Status.Configured";
            }
            else if (result.BlockReason != WindowsGamingControlsBlockReason.None)
            {
                windowsGamingStatusKey = GetWindowsGamingBlockStatusKey(result.BlockReason);
            }
            else
            {
                windowsGamingStatusKey = "System.Gaming.Status.Failed";
            }
        }
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            windowsGamingStatusKey = "System.Gaming.Status.Failed";
        }
        finally
        {
            await RefreshHistoryAfterWindowsGamingOperationAsync();
            SetWindowsGamingBusy(false);
            RefreshWindowsGamingPresentation();
        }
    }

    public async Task RestoreWindowsGamingSettingsAsync()
    {
        if (!CanRestoreWindowsGamingSettings || windowsGamingTransactionId is not { } transactionId)
        {
            return;
        }

        SetWindowsGamingBusy(true);
        windowsGamingStatusKey = "System.Gaming.Status.Restoring";
        RefreshWindowsGamingPresentation();
        try
        {
            var result = await windowsGamingControls.RestoreAsync(transactionId);
            windowsGamingBlockReason = result.BlockReason;
            if (result.Succeeded)
            {
                windowsGamingTransactionId = null;
            }

            windowsGamingSettings = await windowsGamingControls.ReadAsync();
            windowsGamingStatusKey = result.BlockReason != WindowsGamingControlsBlockReason.None
                ? GetWindowsGamingBlockStatusKey(result.BlockReason)
                : result.Succeeded
                    ? "System.Gaming.Status.Restored"
                    : "System.Gaming.Status.RestoreFailed";
        }
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            windowsGamingStatusKey = "System.Gaming.Status.RestoreFailed";
        }
        finally
        {
            await RefreshHistoryAfterWindowsGamingOperationAsync();
            SetWindowsGamingBusy(false);
            RefreshWindowsGamingPresentation();
        }
    }

    private async Task RefreshHistoryAfterWindowsGamingOperationAsync()
    {
        try
        {
            ApplyHistory(await service.LoadHistoryAsync());
        }
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            // The committed journal remains the source of truth; a later refresh can reload it.
        }
    }

    private void SetWindowsGamingBusy(bool value)
    {
        if (isWindowsGamingBusy != value)
        {
            isWindowsGamingBusy = value;
            OnPropertyChanged(nameof(IsWindowsGamingBusy));
            RaiseCommandState();
        }
    }

    private void SetWindowsSystemHealthBusy(bool value)
    {
        if (isWindowsSystemHealthBusy != value)
        {
            isWindowsSystemHealthBusy = value;
            OnPropertyChanged(nameof(IsWindowsSystemHealthBusy));
            RaiseCommandState();
        }
    }

    private void RefreshWindowsGamingPresentation()
    {
        OnPropertyChanged(nameof(WindowsGameModeStateLabel));
        OnPropertyChanged(nameof(WindowsBackgroundCaptureStateLabel));
        OnPropertyChanged(nameof(WindowsGamingStatusMessage));
        RaiseCommandState();
    }

    private void RefreshWindowsSystemHealthPresentation()
    {
        OnPropertyChanged(nameof(WindowsAntivirusHealthLabel));
        OnPropertyChanged(nameof(WindowsFirewallHealthLabel));
        OnPropertyChanged(nameof(WindowsAutomaticUpdatesHealthLabel));
        OnPropertyChanged(nameof(WindowsSystemHealthStatusMessage));
        OnPropertyChanged(nameof(WindowsSystemHealthUpdatedLabel));
        RaiseCommandState();
    }

    private string DescribeWindowsSecurityHealth(WindowsSecurityHealthState? state)
    {
        if (state is null
            && windowsSystemHealthStatusKey == "System.Health.Status.Unavailable")
        {
            return localization.GetString("System.Health.State.Unavailable");
        }

        return localization.GetString(state switch
        {
            WindowsSecurityHealthState.Good => "System.Health.State.Good",
            WindowsSecurityHealthState.NotMonitored => "System.Health.State.NotMonitored",
            WindowsSecurityHealthState.Poor => "System.Health.State.Attention",
            WindowsSecurityHealthState.Snoozed => "System.Health.State.Snoozed",
            WindowsSecurityHealthState.Unavailable => "System.Health.State.Unavailable",
            _ => "System.Health.State.Waiting"
        });
    }

    private string DescribeWindowsGamingState(WindowsGamingSettingState state)
    {
        return localization.GetString(state switch
        {
            WindowsGamingSettingState.Enabled => "System.Gaming.State.Enabled",
            WindowsGamingSettingState.Disabled => "System.Gaming.State.Disabled",
            WindowsGamingSettingState.NotConfigured => "System.Gaming.State.NotConfigured",
            WindowsGamingSettingState.Unavailable => "System.Gaming.State.Unavailable",
            _ => "System.Gaming.State.Unknown"
        });
    }

    private static bool CanChangeWindowsGamingSettings(WindowsGamingSettingsDto settings)
    {
        return settings.GameMode != WindowsGamingSettingState.Unavailable
            && settings.BackgroundCapture != WindowsGamingSettingState.Unavailable
            && settings.GameMode != WindowsGamingSettingState.Unknown
            && settings.BackgroundCapture != WindowsGamingSettingState.Unknown;
    }

    private static bool IsDesiredWindowsGamingState(WindowsGamingSettingsDto settings)
    {
        return settings.GameMode == WindowsGamingSettingState.Enabled
            && settings.BackgroundCapture == WindowsGamingSettingState.Disabled;
    }

    private string GetWindowsGamingStatusKey()
    {
        if (windowsGamingBlockReason != WindowsGamingControlsBlockReason.None)
        {
            return GetWindowsGamingBlockStatusKey(windowsGamingBlockReason);
        }

        return !CanChangeWindowsGamingSettings(windowsGamingSettings)
            ? "System.Gaming.Status.Unavailable"
            : IsDesiredWindowsGamingState(windowsGamingSettings)
                ? "System.Gaming.Status.Configured"
                : "System.Gaming.Status.Ready";
    }

    private static string GetWindowsGamingBlockStatusKey(
        WindowsGamingControlsBlockReason blockReason)
    {
        return blockReason switch
        {
            WindowsGamingControlsBlockReason.FiveMRunning =>
                "System.Gaming.Status.FiveMRunning",
            WindowsGamingControlsBlockReason.ProcessInspectionUnavailable =>
                "System.Gaming.Status.ProcessCheckFailed",
            _ => "System.Gaming.Status.Ready"
        };
    }
}

internal sealed class SyntheticWindowsSystemHealthInspector : IWindowsSystemHealthInspector
{
    public Task<WindowsSystemHealthSnapshot> InspectAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var good = new WindowsSecurityProviderHealth(WindowsSecurityHealthState.Good, 0);
        return Task.FromResult(new WindowsSystemHealthSnapshot(
            good,
            good,
            good,
            DateTimeOffset.UtcNow));
    }
}
