using System.IO;
using Ralven.Windows.Infrastructure;

namespace Ralven.App.ViewModels;

public sealed partial class MainViewModel
{
    private bool isCacheOperationRunning;
    private int cleanableCacheFileCount;
    private string cacheStorageLabel = string.Empty;

    public bool IsCacheOperationRunning
    {
        get => isCacheOperationRunning;
        private set
        {
            if (SetProperty(ref isCacheOperationRunning, value))
            {
                RaiseCommandState();
            }
        }
    }

    public bool CanClearRalvenCache => cleanableCacheFileCount > 0
        && !IsCacheOperationRunning
        && !IsBusy
        && !IsUpdateDownloading
        && !IsInstallingUpdate;

    public string CacheStorageLabel
    {
        get => cacheStorageLabel;
        private set => SetProperty(ref cacheStorageLabel, value);
    }

    public void ShowCacheDemoState()
    {
        cleanableCacheFileCount = 0;
        CacheStorageLabel = localization.GetString("Settings.Cache.DemoUnavailable");
        RaiseCommandState();
    }

    public async Task RefreshCacheStorageAsync(CancellationToken cancellationToken = default)
    {
        if (IsCacheOperationRunning)
        {
            return;
        }

        IsCacheOperationRunning = true;
        CacheStorageLabel = localization.GetString("Settings.Cache.Calculating");
        try
        {
            var snapshot = await ralvenCacheService.InspectAsync(cancellationToken);
            SetCacheSnapshot(snapshot);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            CacheStorageLabel = localization.GetString("Settings.Cache.Cancelled");
        }
        catch (Exception exception) when (IsExpectedCacheFailure(exception))
        {
            cleanableCacheFileCount = 0;
            CacheStorageLabel = localization.GetString("Settings.Cache.Error");
        }
        finally
        {
            IsCacheOperationRunning = false;
            RaiseCommandState();
        }
    }

    public async Task CleanRalvenCacheAsync(CancellationToken cancellationToken = default)
    {
        if (!CanClearRalvenCache)
        {
            return;
        }

        IsCacheOperationRunning = true;
        CacheStorageLabel = localization.GetString("Settings.Cache.Cleaning");
        try
        {
            var result = await ralvenCacheService.CleanAsync(cancellationToken);
            cleanableCacheFileCount = result.After.FileCount;
            CacheStorageLabel = result.IsComplete
                ? localization.Format("Settings.Cache.Cleaned", FormatStorageSize(result.DeletedBytes))
                : localization.Format(
                    "Settings.Cache.Partial",
                    FormatStorageSize(result.DeletedBytes),
                    FormatStorageSize(result.After.TotalSizeBytes),
                    result.FailedFileCount + result.After.SkippedPathCount);

            if (result.Before.Categories.Any(category =>
                    category.Category == RalvenCacheCategory.UpdateDownloads && category.FileCount > 0)
                && updatePresentationState == UpdatePresentationState.Ready)
            {
                updatePresentationState = UpdatePresentationState.Available;
                RefreshUpdatePresentation();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            CacheStorageLabel = localization.GetString("Settings.Cache.Cancelled");
        }
        catch (Exception exception) when (IsExpectedCacheFailure(exception))
        {
            CacheStorageLabel = localization.GetString("Settings.Cache.Error");
        }
        finally
        {
            IsCacheOperationRunning = false;
            RaiseCommandState();
        }
    }

    private void SetCacheSnapshot(RalvenCacheSnapshot snapshot)
    {
        cleanableCacheFileCount = snapshot.FileCount;
        CacheStorageLabel = snapshot.SkippedPathCount > 0
            ? localization.Format(
                "Settings.Cache.SizePartial",
                FormatStorageSize(snapshot.TotalSizeBytes),
                snapshot.FileCount,
                snapshot.SkippedPathCount)
            : snapshot.FileCount == 0
                ? localization.GetString("Settings.Cache.Empty")
                : localization.Format(
                    "Settings.Cache.Size",
                    FormatStorageSize(snapshot.TotalSizeBytes),
                    snapshot.FileCount);
        RaiseCommandState();
    }

    private string FormatStorageSize(long bytes)
    {
        var culture = localization.CurrentCulture;
        return bytes switch
        {
            >= 1024L * 1024 * 1024 => $"{(bytes / (1024d * 1024 * 1024)).ToString("0.##", culture)} GB",
            >= 1024L * 1024 => $"{(bytes / (1024d * 1024)).ToString("0.##", culture)} MB",
            >= 1024 => $"{(bytes / 1024d).ToString("0.##", culture)} KB",
            _ => $"{bytes.ToString(culture)} B"
        };
    }

    private static bool IsExpectedCacheFailure(Exception exception) => exception is
        IOException
        or UnauthorizedAccessException
        or NotSupportedException
        or System.Security.SecurityException
        or InvalidOperationException;
}
