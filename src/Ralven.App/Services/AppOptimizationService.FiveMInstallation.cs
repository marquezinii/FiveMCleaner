using Ralven.Windows.Infrastructure;

namespace Ralven.App.Services;

public sealed partial class AppOptimizationService
{
    public async Task<FiveMInstallationSelectionResult> SetManualFiveMInstallationAsync(
        string? path,
        CancellationToken cancellationToken = default)
    {
        if (demoMode)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new FiveMInstallationSelectionResult(false, null);
        }

        string? root = null;
        if (!string.IsNullOrWhiteSpace(path))
        {
            if (!FiveMInstallationLocator.TryValidateLegacyCandidate(
                    path,
                    FiveMInstallationSource.Manual,
                    out var installation))
            {
                return new FiveMInstallationSelectionResult(false, null);
            }

            root = installation.Root;
        }

        var settings = await LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        await SaveSettingsAsync(
            settings with { ManualFiveMInstallationRoot = root },
            cancellationToken).ConfigureAwait(false);
        return new FiveMInstallationSelectionResult(true, root);
    }
}
