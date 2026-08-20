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
    public bool CanRevertLastOptimization => ComparisonRegressionSuspected
        && !IsBusy
        && lastTransactionId is { } id
        && HistoryItems.Any(item => item.TransactionId == id && item.CanRollback);

    public bool IsGtaVBenchmarkRunning { get => isGtaVBenchmarkRunning; private set => SetProperty(ref isGtaVBenchmarkRunning, value); }

    public string GtaVBenchmarkStatusLabel { get => gtaVBenchmarkStatusLabel; private set => SetProperty(ref gtaVBenchmarkStatusLabel, value); }

    public bool CanRunGtaVBenchmark => !IsBusy && !IsGtaVBenchmarkRunning;

    public string ProfilePresentationBenefits { get => profilePresentationBenefits; private set => SetProperty(ref profilePresentationBenefits, value); }

    public string ProfilePresentationImpact { get => profilePresentationImpact; private set => SetProperty(ref profilePresentationImpact, value); }

    public string ProfilePresentationCategories { get => profilePresentationCategories; private set => SetProperty(ref profilePresentationCategories, value); }

    public bool IsLightSelected
    {
        get => selectedProfile == OptimizationProfile.Light;
        set { if (value) SelectProfile(OptimizationProfile.Light); }
    }

    public bool IsBalancedSelected
    {
        get => selectedProfile == OptimizationProfile.Balanced;
        set { if (value) SelectProfile(OptimizationProfile.Balanced); }
    }

    public bool IsAggressiveSelected
    {
        get => selectedProfile == OptimizationProfile.Aggressive;
        set { if (value) SelectProfile(OptimizationProfile.Aggressive); }
    }

    private OptimizationProfile RecommendedProfile =>
        diagnostic?.RecommendedProfile ?? OptimizationProfile.Balanced;

    public bool IsLightRecommended => RecommendedProfile == OptimizationProfile.Light;

    public bool IsBalancedRecommended => RecommendedProfile == OptimizationProfile.Balanced;

    public bool IsAggressiveRecommended => RecommendedProfile == OptimizationProfile.Aggressive;

    public int SelectedActionCount => currentPlan?.Actions.Count ?? 0;

    public bool HasPlannedActions => SelectedActionCount > 0;

    public string ElevationLabel => localization.GetString(
        currentPlan?.RequiresElevation == true
            ? "Plan.Elevation.UacAtRun"
            : "Plan.Elevation.None");

    public string PlanSummary => !HasPlannedActions
        ? localization.GetString("Plan.Empty.Summary")
        : currentPlan?.ContainsNonReversibleActions == true
        ? localization.GetString("Plan.Safety.Mixed")
        : localization.GetString("Plan.Safety.Reversible");

    public string PlanHeader => localization.Format(
        "Plan.ActionsCatalog",
        SelectedActionCount,
        currentPlan?.CatalogVersion ?? 1);

    public string PlanNoticesText => !HasPlannedActions
        ? string.Empty
        : currentPlan?.Notices.Count > 0
        ? string.Join("  •  ", currentPlan.Notices.Select(LocalizeNotice))
        : localization.GetString("Plan.NoAdditionalWarnings");

    public string EmptyPlanMessage => diagnostic?.Edition == FiveMEdition.Legacy
        ? localization.GetString("Plan.Empty.NoSafeActions")
        : localization.GetString("Plan.Empty.LegacyRequired");

    public string SelectedProfileLabel
    {
        get
        {
            var upper = SelectedProfileName.ToUpper(localization.CurrentCulture);
            return selectedProfile == RecommendedProfile
                ? $"{upper} • {localization.GetString("Profiles.RecommendedBadge")}"
                : upper;
        }
    }

    /// <summary>
    /// True when the "Recomendado" mark should render as its own badge next
    /// to <see cref="SelectedProfileName"/>, instead of text concatenated
    /// into the all-caps <see cref="SelectedProfileLabel"/> heading.
    /// </summary>
    public bool IsSelectedProfileRecommended => selectedProfile == RecommendedProfile;

    /// <summary>
    /// Posição do perfil selecionado na escala Leve → Médio → Agressivo, de 0 a 1.
    /// Não é uma estimativa de ganho nem uma medida de FPS: é só o nível
    /// escolhido, exposto para que a cena 3D e o anel do Otimizador reajam de
    /// forma visível quando o usuário troca de perfil.
    /// </summary>
    public double ProfileIntensity => selectedProfile switch
    {
        OptimizationProfile.Light => 0.34,
        OptimizationProfile.Aggressive => 1,
        _ => 0.67
    };

    public double ProfileIntensityPercent => ProfileIntensity * 100;

    public string SafetySummary => currentPlan?.RequiresElevation == true
        ? localization.GetString("Plan.Elevation.OnePrompt")
        : localization.GetString("Plan.Elevation.CurrentUser");

    public string SelectedProfileName => ProfileName(selectedProfile);

    public void SelectProfile(OptimizationProfile profile)
    {
        if (selectedProfile == profile)
        {
            return;
        }

        profileInitializedFromDiagnostic = true;
        selectedProfile = profile;
        OnPropertyChanged(nameof(IsLightSelected));
        OnPropertyChanged(nameof(IsBalancedSelected));
        OnPropertyChanged(nameof(IsAggressiveSelected));
        OnPropertyChanged(nameof(SelectedProfileLabel));
        OnPropertyChanged(nameof(SelectedProfileName));
        OnPropertyChanged(nameof(IsSelectedProfileRecommended));
        OnPropertyChanged(nameof(ProfileIntensity));
        OnPropertyChanged(nameof(ProfileIntensityPercent));
        RefreshPlan();
    }

    public async Task StartOptimizationAsync()
    {
        if (!TryPrepareOptimizationRun())
        {
            return;
        }

        operationCancellation = new CancellationTokenSource();
        var progress = new Progress<AppProgressUpdate>(ApplyProgress);
        var completedSuccessfully = false;
        var telemetryEventName = "optimization-failed";
        string? telemetryErrorCategory = null;
        BugCode? telemetryBugCode = null;
        try
        {
            // currentPlan é garantido não-nulo aqui: TryPrepareOptimizationRun
            // só retorna true quando CanStart é true (e CanStart exige plano).
            var result = await service.ExecuteAsync(currentPlan!, progress, operationCancellation.Token);
            completedSuccessfully = result.Succeeded;
            telemetryEventName = result.Succeeded ? "optimization-completed" : "optimization-failed";
            if (!result.Succeeded && result.Report is not null)
            {
                // Use the first failed action's ID for bug classification
                var failedActionId = result.Report.Lines
                    .Where(l => l.Outcome is ActionExecutionOutcome.Failed or ActionExecutionOutcome.RollbackFailed)
                    .Select(l => l.ActionId)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(failedActionId))
                {
                    telemetryBugCode = BugCodeClassifier.ClassifyOptimizationException(new InvalidOperationException(), failedActionId);
                }
            }
            await HandleOptimizationResultAsync(result);
        }
        catch (OperationCanceledException)
        {
            telemetryEventName = "optimization-cancelled";
            telemetryErrorCategory = "cancelled";
            telemetryBugCode = BugCode.APP_OPT_CANCELLED;
            HandleOptimizationCancelled();
        }
        catch (Exception exception)
        {
            telemetryEventName = "optimization-failed";
            telemetryErrorCategory = TelemetryErrorClassifier.ClassifyException(exception);
            telemetryBugCode = BugCodeClassifier.ClassifyException(exception, "optimization");
            HandleOptimizationFailed();
        }
        finally
        {
            FinalizeOptimizationRun(completedSuccessfully, telemetryEventName, telemetryErrorCategory, telemetryBugCode);
        }
    }

    private bool TryPrepareOptimizationRun()
    {
        // Recria o plano no clique para que o nonce e o timestamp aceitos pelo
        // broker elevado nunca fiquem antigos enquanto a janela permanece aberta.
        RefreshPlan();
        if (!CanStart || currentPlan is null)
        {
            ProgressHeadline = diagnostic?.IsFiveMRunning == true
                ? localization.GetString("Plan.CloseFiveM")
                : diagnostic?.GtaVIsRunning == true
                    ? localization.GetString("Plan.CloseGtaV")
                    : localization.GetString("Plan.Unavailable");
            return false;
        }

        IsBusy = true;
        ProgressPercent = 0;
        ClearProgressHistory();
        StartOperationTiming();
        StepLedger.Clear();
        ApplyReport(null);
        ApplyComparison(null);
        lastTransactionId = null;
        return true;
    }

    private async Task HandleOptimizationResultAsync(AppOptimizationResult result)
    {
        ProgressPercent = result.Succeeded ? 100 : ProgressPercent;
        FinalizeHeadline(result.Succeeded
            ? localization.GetString("Status.OptimizationCompleted")
            : result.Summary);
        ApplyReport(result.Report);
        lastTransactionId = result.TransactionId;
        ApplyComparison(result.Comparison);
        ApplyHistory(await service.LoadHistoryAsync());
    }

    private void HandleOptimizationCancelled()
    {
        FinalizeHeadline(localization.GetString("Status.SafeCancellation.Headline"));
    }

    private void HandleOptimizationFailed()
    {
        FinalizeHeadline(localization.GetString("Status.CouldNotComplete"));
    }

    private void FinalizeOptimizationRun(
        bool completedSuccessfully,
        string telemetryEventName,
        string? telemetryErrorCategory,
        BugCode? bugCode = null)
    {
        var executionTime = operationStopwatch?.Elapsed ?? TimeSpan.Zero;
        StopOperationTiming(completedSuccessfully);
        TrackOptimizationTelemetry(telemetryEventName, executionTime, telemetryErrorCategory, bugCode);
        // operationCancellation foi atribuído antes do try em StartOptimizationAsync.
        operationCancellation!.Dispose();
        operationCancellation = null;
        IsBusy = false;
    }

    public void CancelOptimization()
    {
        if (operationCancellation is null)
        {
            return;
        }

        operationCancellation.Cancel();
        RaiseCommandState();
    }

    public async Task RunGtaVBenchmarkAsync()
    {
        if (!CanRunGtaVBenchmark)
        {
            return;
        }

        IsGtaVBenchmarkRunning = true;
        GtaVBenchmarkStatusLabel = localization.GetString("GtaVBenchmark.Running");
        RaiseCommandState();
        try
        {
            var result = await service.RunGtaVBenchmarkAsync(3);
            GtaVBenchmarkStatusLabel = DescribeGtaVBenchmarkResult(result);
        }
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            GtaVBenchmarkStatusLabel = localization.Format("GtaVBenchmark.Error", localization.DescribeException(exception));
        }
        finally
        {
            IsGtaVBenchmarkRunning = false;
            RaiseCommandState();
        }
    }

    private string DescribeGtaVBenchmarkResult(AppGtaVBenchmarkResult result)
    {
        if (!result.Succeeded || result.Median is null)
        {
            var reasonKey = result.FailureReason switch
            {
                "gtav-not-detected" => "GtaVBenchmark.Failure.NotDetected",
                "gtav-still-running" => "GtaVBenchmark.Failure.StillRunning",
                "gta-executable-not-found" => "GtaVBenchmark.Failure.NotDetected",
                "profile-folder-not-found" => "GtaVBenchmark.Failure.OutputNotFound",
                "benchmark-output-file-not-found" => "GtaVBenchmark.Failure.OutputNotFound",
                "benchmark-output-file-not-recognized" => "GtaVBenchmark.Failure.OutputNotRecognized",
                "benchmark-did-not-exit-in-time" => "GtaVBenchmark.Failure.Timeout",
                _ => "GtaVBenchmark.Failure.Generic"
            };
            return localization.GetString(reasonKey);
        }

        return localization.Format(
            "GtaVBenchmark.Result",
            result.Median.AverageFps,
            result.Median.MinimumFps,
            result.Median.OnePercentLowFps,
            result.Median.PointOnePercentLowFps,
            result.Iterations.Count);
    }

    public async Task<bool> RevertLastOptimizationAsync()
    {
        if (lastTransactionId is not { } id)
        {
            return false;
        }

        var item = HistoryItems.FirstOrDefault(candidate => candidate.TransactionId == id);
        return item is not null && await RollbackAsync(item);
    }

    public async Task<bool> RollbackAsync(HistoryDisplayItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (IsBusy || !item.CanRollback)
        {
            return false;
        }

        operationCancellation = new CancellationTokenSource();
        IsBusy = true;
        ProgressPercent = 0;
        ClearProgressHistory();
        StartOperationTiming();
        var progress = new Progress<AppProgressUpdate>(ApplyProgress);
        var completedSuccessfully = false;
        try
        {
            var restored = await service.RollbackAsync(item.TransactionId, progress, operationCancellation.Token);
            completedSuccessfully = restored;
            ApplyHistory(await service.LoadHistoryAsync());
            return restored;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception)
        {
            FinalizeHeadline(localization.GetString("Status.CouldNotRestore"));
            return false;
        }
        finally
        {
            StopOperationTiming(completedSuccessfully);
            operationCancellation.Dispose();
            operationCancellation = null;
            IsBusy = false;
        }
    }

    private void RefreshPlan()
    {
        var edition = diagnostic?.Edition ?? FiveMEdition.Unknown;
        var options = new OptimizationOptionsDto
        {
            CleanUserTemporaryFiles = true,
            TemporaryFileMinimumAgeDays = selectedProfile switch
            {
                OptimizationProfile.Light => 30,
                OptimizationProfile.Balanced => 14,
                _ => 7
            },
            RemoveOldFiveMCrashDumps = true,
            DiagnosticRetentionDays = selectedProfile == OptimizationProfile.Aggressive ? 7 : 14,
            ServerCacheRepair = selectedProfile == OptimizationProfile.Light
                ? CacheRepairPolicy.Off
                : CacheRepairPolicy.WhenOversized,
            ServerCacheThresholdGiB = 8,
            EnableGameMode = true,
            PreferHighPerformanceGpu = true,
            DisableBackgroundCapture = true,
            UseSessionPerformancePowerPlan = selectedProfile != OptimizationProfile.Light,
            ApplyLegacyGraphicsPreset = true,
            ApplyGtaVGraphicsPreset = diagnostic?.GtaVDetected == true,
            ReduceWindowsVisualEffects = selectedProfile == OptimizationProfile.Aggressive
        };

        currentPlan = PlanBuilder.Build(
            new OptimizationPlanRequestDto
            {
                Profile = selectedProfile,
                Edition = edition,
                Options = options
            },
            PlanBuildContext.New(TimeProvider.System));

        PlannedActions.Clear();
        foreach (var action in currentPlan.Actions)
        {
            PlannedActions.Add(ToDisplayItem(action.Metadata));
        }

        OnPropertyChanged(nameof(SelectedActionCount));
        OnPropertyChanged(nameof(HasPlannedActions));
        OnPropertyChanged(nameof(ElevationLabel));
        OnPropertyChanged(nameof(PlanSummary));
        OnPropertyChanged(nameof(PlanHeader));
        OnPropertyChanged(nameof(PlanNoticesText));
        OnPropertyChanged(nameof(EmptyPlanMessage));
        OnPropertyChanged(nameof(SafetySummary));
        OnPropertyChanged(nameof(AboutVersionDeveloper));
        RefreshProfilePresentation();
        RaiseCommandState();
    }

    private void RefreshProfilePresentation()
    {
        var presentation = ProfilePresentationProvider.For(selectedProfile);
        ProfilePresentationBenefits = localization.GetString($"Profiles.Presentation.{selectedProfile}.Benefits");
        ProfilePresentationImpact = localization.GetString($"Profiles.Presentation.Impact.{presentation.ImpactLevel}");
        ProfilePresentationCategories = string.Join(
            "  •  ",
            presentation.AnalyzedCategories.Select(category =>
                localization.GetString($"Category.{category}")));
    }

    private ActionDisplayItem ToDisplayItem(ActionMetadataDto action)
    {
        var icon = action.Category switch
        {
            ActionCategory.Safety => "\uEA18",
            ActionCategory.Storage => "\uE958",
            ActionCategory.WindowsGaming => "\uE7FC",
            ActionCategory.Power => "\uE945",
            ActionCategory.Appearance => "\uE790",
            ActionCategory.FiveMGraphics => "\uE7F8",
            _ => "\uE946"
        };
        var risk = action.Risk switch
        {
            ActionRisk.Informational => localization.GetString("Risk.Informational"),
            ActionRisk.Low => localization.GetString("Risk.Low"),
            ActionRisk.Moderate => localization.GetString("Risk.Moderate"),
            ActionRisk.High => localization.GetString("Risk.HighReversible"),
            _ => action.Risk.ToString().ToUpperInvariant()
        };
        var riskBrushKey = action.Risk switch
        {
            ActionRisk.Informational => "TextTertiaryBrush",
            ActionRisk.Low => "InfoBaseBrush",
            ActionRisk.Moderate => "WarningBaseBrush",
            ActionRisk.High => "DangerBaseBrush",
            _ => "TextTertiaryBrush"
        };
        var requiresElevation = action.RequiredPrivilege == RequiredPrivilege.Administrator;
        var privilege = requiresElevation
            ? localization.GetString("Privilege.RequiresUac")
            : action.Reversibility is ActionReversibility.Irreversible or ActionReversibility.RebuildableData
                ? localization.GetString("Privilege.PermanentCleanup")
                : localization.GetString("Privilege.Reversible");
        var categoryLabel = action.Category switch
        {
            ActionCategory.Safety => localization.GetString("Category.Safety"),
            ActionCategory.Storage => localization.GetString("Category.Storage"),
            ActionCategory.WindowsGaming => localization.GetString("Category.WindowsGaming"),
            ActionCategory.Power => localization.GetString("Category.Power"),
            ActionCategory.Appearance => localization.GetString("Category.Appearance"),
            ActionCategory.FiveMGraphics => localization.GetString("Category.FiveMGraphics"),
            _ => action.Category.ToString()
        };
        var nameKey = $"Actions.{action.Id}.Name";
        var descriptionKey = $"Actions.{action.Id}.Description";
        var localizedName = localization.GetString(nameKey);
        var localizedDescription = localization.GetString(descriptionKey);
        return new ActionDisplayItem(
            action.Id,
            localizedName == nameKey ? action.Name : localizedName,
            localizedDescription == descriptionKey ? action.Description : localizedDescription,
            icon,
            risk,
            riskBrushKey,
            privilege,
            requiresElevation,
            categoryLabel);
    }

    private string LocalizeNotice(PlanNoticeDto notice) => notice.Code switch
    {
        "diagnostics-removal-is-permanent" => localization.Format(
            "Plan.Notice.DiagnosticsRetention",
            currentPlan?.Options.DiagnosticRetentionDays ?? 14),
        "server-cache-will-be-rebuilt" => localization.GetString("Plan.Notice.ServerCacheRepair"),
        "performance-power-requires-ac" => localization.GetString("Plan.Notice.AcPower"),
        "aggressive-prioritizes-performance" => localization.GetString("Plan.Notice.AggressiveVisual"),
        _ => notice.Message
    };

    private string ProfileName(OptimizationProfile profile) => profile switch
    {
        OptimizationProfile.Light => localization.GetString("Profiles.Light.Name"),
        OptimizationProfile.Balanced => localization.GetString("Profiles.Balanced.Name"),
        OptimizationProfile.Aggressive => localization.GetString("Profiles.Aggressive.Name"),
        _ => profile.ToString()
    };
}
