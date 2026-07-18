using System.Collections.ObjectModel;
using FiveMCleaner.App.Services;
using FiveMCleaner.Contracts;
using FiveMCleaner.Core.Planning;

namespace FiveMCleaner.App.ViewModels;

public sealed class MainViewModel : BindableBase
{
    private readonly IAppOptimizationService service;
    private readonly IPlanBuilder planBuilder;
    private CancellationTokenSource? operationCancellation;
    private AppDiagnostic? diagnostic;
    private OptimizationPlanDto? currentPlan;
    private OptimizationProfile selectedProfile = OptimizationProfile.Balanced;
    private bool isBusy;
    private bool isInitializing = true;
    private double progressPercent;
    private string progressHeadline = "Pronto para otimizar";
    private string progressDetail = "Revise o plano e escolha Executar plano quando estiver pronto.";
    private string progressStateLabel = "AGUARDANDO";
    private string cpuName = "Analisando…";
    private string gpuName = "Analisando…";
    private string ramLabel = "Analisando…";
    private string diskLabel = "Analisando…";
    private string editionLabel = "PROCURANDO FIVEM";
    private string recommendationTitle = "Analisando o seu computador";
    private string recommendationText = "O diagnóstico é executado localmente e não envia dados.";
    private int readinessScore;
    private bool cleanTemporaryFiles = true;
    private bool removeOldDiagnostics = true;
    private bool smartCacheRepair;
    private bool enableGameMode = true;
    private bool disableBackgroundCapture = true;
    private bool usePerformancePowerPlan = true;

    public MainViewModel(IAppOptimizationService service, IPlanBuilder? planBuilder = null)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        this.planBuilder = planBuilder ?? new PlanBuilder();
        ActivityLog.Add(new ActivityLogItem(DateTime.Now.ToString("HH:mm:ss"), "FiveMCleaner iniciado sem privilégios elevados."));
    }

    public ObservableCollection<ActionDisplayItem> PlannedActions { get; } = [];

    public ObservableCollection<ActivityLogItem> ActivityLog { get; } = [];

    public ObservableCollection<HistoryDisplayItem> HistoryItems { get; } = [];

    public string CpuName { get => cpuName; private set => SetProperty(ref cpuName, value); }

    public string GpuName { get => gpuName; private set => SetProperty(ref gpuName, value); }

    public string RamLabel { get => ramLabel; private set => SetProperty(ref ramLabel, value); }

    public string DiskLabel { get => diskLabel; private set => SetProperty(ref diskLabel, value); }

    public string EditionLabel { get => editionLabel; private set => SetProperty(ref editionLabel, value); }

    public string RecommendationTitle { get => recommendationTitle; private set => SetProperty(ref recommendationTitle, value); }

    public string RecommendationText { get => recommendationText; private set => SetProperty(ref recommendationText, value); }

    public int ReadinessScore { get => readinessScore; private set => SetProperty(ref readinessScore, value); }

    public double ProgressPercent { get => progressPercent; private set => SetProperty(ref progressPercent, value); }

    public string ProgressHeadline { get => progressHeadline; private set => SetProperty(ref progressHeadline, value); }

    public string ProgressDetail { get => progressDetail; private set => SetProperty(ref progressDetail, value); }

    public string ProgressStateLabel { get => progressStateLabel; private set => SetProperty(ref progressStateLabel, value); }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                RaiseCommandState();
            }
        }
    }

    public bool CanRefresh => !IsBusy && !isInitializing;

    public bool CanStart => !IsBusy && !isInitializing && currentPlan?.IsExecutable == true && diagnostic?.IsFiveMRunning != true;

    public bool CanCancel => IsBusy && operationCancellation is not null;

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

    public bool CleanTemporaryFiles
    {
        get => cleanTemporaryFiles;
        set { if (SetProperty(ref cleanTemporaryFiles, value)) SettingsChanged(); }
    }

    public bool RemoveOldDiagnostics
    {
        get => removeOldDiagnostics;
        set { if (SetProperty(ref removeOldDiagnostics, value)) SettingsChanged(); }
    }

    public bool SmartCacheRepair
    {
        get => smartCacheRepair;
        set { if (SetProperty(ref smartCacheRepair, value)) SettingsChanged(); }
    }

    public bool EnableGameMode
    {
        get => enableGameMode;
        set { if (SetProperty(ref enableGameMode, value)) SettingsChanged(); }
    }

    public bool DisableBackgroundCapture
    {
        get => disableBackgroundCapture;
        set { if (SetProperty(ref disableBackgroundCapture, value)) SettingsChanged(); }
    }

    public bool UsePerformancePowerPlan
    {
        get => usePerformancePowerPlan;
        set { if (SetProperty(ref usePerformancePowerPlan, value)) SettingsChanged(); }
    }

    public int SelectedActionCount => currentPlan?.Actions.Count ?? 0;

    public string ElevationLabel => currentPlan?.RequiresElevation == true ? "UAC somente ao executar" : "sem elevação";

    public string PlanSummary => currentPlan?.ContainsNonReversibleActions == true
        ? "Limpezas permanentes aparecem identificadas no plano. Configurações possuem rollback."
        : "Todas as mudanças deste plano podem ser desfeitas.";

    public string PlanHeader => $"{SelectedActionCount} ações • catálogo v{currentPlan?.CatalogVersion ?? 1}";

    public string PlanNoticesText => currentPlan?.Notices.Count > 0
        ? string.Join("  •  ", currentPlan.Notices.Select(notice => notice.Message))
        : "Nenhum aviso adicional para este plano.";

    public string SelectedProfileLabel => selectedProfile switch
    {
        OptimizationProfile.Light => "LEVE",
        OptimizationProfile.Balanced => "MÉDIO • RECOMENDADO",
        OptimizationProfile.Aggressive => "AGRESSIVO",
        _ => "PERFIL"
    };

    public string SafetySummary => currentPlan?.RequiresElevation == true
        ? "O Windows exibirá um único pedido de UAC."
        : "Executado no usuário atual.";

    public string LogsDirectory => service.LogsDirectory;

    public async Task InitializeAsync()
    {
        isInitializing = true;
        RaiseCommandState();
        try
        {
            var settingsTask = service.LoadSettingsAsync();
            var diagnosticTask = service.DiagnoseAsync();
            var historyTask = service.LoadHistoryAsync();
            await Task.WhenAll(settingsTask, diagnosticTask, historyTask);

            ApplySettings(await settingsTask);
            ApplyDiagnostic(await diagnosticTask);
            ApplyHistory(await historyTask);
            AddLog("Diagnóstico local concluído.");
        }
        catch (Exception exception)
        {
            RecommendationTitle = "Diagnóstico parcial";
            RecommendationText = exception.Message;
            AddLog($"Aviso: {exception.Message}");
        }
        finally
        {
            isInitializing = false;
            RefreshPlan();
            RaiseCommandState();
        }
    }

    public async Task RefreshDiagnosticAsync()
    {
        if (!CanRefresh)
        {
            return;
        }

        isInitializing = true;
        RaiseCommandState();
        try
        {
            ApplyDiagnostic(await service.DiagnoseAsync());
            AddLog("PC reanalisado.");
        }
        finally
        {
            isInitializing = false;
            RefreshPlan();
            RaiseCommandState();
        }
    }

    public void SelectProfile(OptimizationProfile profile)
    {
        if (selectedProfile == profile)
        {
            return;
        }

        selectedProfile = profile;
        OnPropertyChanged(nameof(IsLightSelected));
        OnPropertyChanged(nameof(IsBalancedSelected));
        OnPropertyChanged(nameof(IsAggressiveSelected));
        OnPropertyChanged(nameof(SelectedProfileLabel));
        RefreshPlan();
    }

    public async Task StartOptimizationAsync()
    {
        // Recria o plano no clique para que o nonce e o timestamp aceitos pelo
        // broker elevado nunca fiquem antigos enquanto a janela permanece aberta.
        RefreshPlan();
        if (!CanStart || currentPlan is null)
        {
            ProgressHeadline = diagnostic?.IsFiveMRunning == true ? "Feche o FiveM primeiro" : "Plano indisponível";
            ProgressDetail = currentPlan?.Blocks.FirstOrDefault()?.Message ?? "Execute o diagnóstico novamente.";
            return;
        }

        operationCancellation = new CancellationTokenSource();
        IsBusy = true;
        ProgressPercent = 0;
        ProgressStateLabel = "PREPARANDO";
        ActivityLog.Clear();
        AddLog($"Iniciando perfil {SelectedProfileLabel.ToLowerInvariant()}.");
        foreach (var notice in currentPlan.Notices.Where(item =>
                     item.Severity == PlanNoticeSeverity.Warning))
        {
            AddLog($"Atenção: {notice.Message}");
        }

        var progress = new Progress<AppProgressUpdate>(ApplyProgress);
        try
        {
            var result = await service.ExecuteAsync(currentPlan, progress, operationCancellation.Token);
            ProgressPercent = result.Succeeded ? 100 : ProgressPercent;
            ProgressStateLabel = result.Succeeded ? "CONCLUÍDO" : result.WasCancelled ? "CANCELADO" : "COM AVISOS";
            ProgressHeadline = result.Succeeded ? "Otimização concluída" : result.Summary;
            ProgressDetail = result.BytesFreed > 0
                ? $"{result.CompletedActions} ações concluídas • {FormatBytes(result.BytesFreed)} liberados."
                : $"{result.CompletedActions} ações concluídas. {result.Summary}";
            AddLog(result.Summary);
            ApplyHistory(await service.LoadHistoryAsync());
        }
        catch (OperationCanceledException)
        {
            ProgressStateLabel = "CANCELADO";
            ProgressHeadline = "Operação cancelada com segurança";
            ProgressDetail = "O cancelamento ocorreu entre ações. Alterações incompletas foram revertidas.";
            AddLog("Cancelamento confirmado.");
        }
        catch (Exception exception)
        {
            ProgressStateLabel = "FALHA SEGURA";
            ProgressHeadline = "Não foi possível concluir";
            ProgressDetail = exception.Message;
            AddLog($"Erro: {exception.Message}");
        }
        finally
        {
            operationCancellation.Dispose();
            operationCancellation = null;
            IsBusy = false;
        }
    }

    public void CancelOptimization()
    {
        if (operationCancellation is null)
        {
            return;
        }

        ProgressStateLabel = "CANCELANDO";
        ProgressDetail = "A etapa atual terminará antes do cancelamento.";
        operationCancellation.Cancel();
        RaiseCommandState();
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
        var progress = new Progress<AppProgressUpdate>(ApplyProgress);
        try
        {
            var restored = await service.RollbackAsync(item.TransactionId, progress, operationCancellation.Token);
            ApplyHistory(await service.LoadHistoryAsync());
            AddLog(restored ? "Rollback concluído." : "Nenhuma alteração reversível encontrada.");
            return restored;
        }
        finally
        {
            operationCancellation.Dispose();
            operationCancellation = null;
            IsBusy = false;
        }
    }

    private void ApplyDiagnostic(AppDiagnostic value)
    {
        diagnostic = value;
        CpuName = value.CpuName;
        GpuName = value.GpuName;
        RamLabel = $"{value.TotalMemoryGiB:0.#} GB RAM";
        DiskLabel = $"{value.FreeDiskGiB:0.#} GB livres";
        ReadinessScore = value.ReadinessScore;
        EditionLabel = value.Edition switch
        {
            FiveMEdition.Legacy => "FIVEM LEGACY DETECTADO",
            FiveMEdition.Enhanced => "ENHANCED • AINDA BLOQUEADO",
            _ => "FIVEM LEGACY NÃO LOCALIZADO"
        };
        RecommendationTitle = value.IsFiveMRunning
            ? "Feche o FiveM para otimizar com segurança"
            : $"Perfil {ProfileName(value.RecommendedProfile)} recomendado para este PC";
        RecommendationText = value.Edition switch
        {
            FiveMEdition.Legacy => value.Notices.FirstOrDefault()
                ?? "O diagnóstico encontrou o layout Legacy conhecido e preparou ações compatíveis.",
            FiveMEdition.Enhanced => "A edição Enhanced usa um novo launcher e cache configurável. Esta versão não aplicará regras do Legacy nela.",
            _ => "Selecione uma instalação Legacy válida ou instale o FiveM para liberar a execução."
        };
    }

    private void ApplySettings(AppSettings settings)
    {
        cleanTemporaryFiles = settings.CleanTemporaryFiles;
        removeOldDiagnostics = settings.RemoveOldDiagnostics;
        smartCacheRepair = settings.SmartCacheRepair;
        enableGameMode = settings.EnableGameMode;
        disableBackgroundCapture = settings.DisableBackgroundCapture;
        usePerformancePowerPlan = settings.UsePerformancePowerPlan;
        OnPropertyChanged(nameof(CleanTemporaryFiles));
        OnPropertyChanged(nameof(RemoveOldDiagnostics));
        OnPropertyChanged(nameof(SmartCacheRepair));
        OnPropertyChanged(nameof(EnableGameMode));
        OnPropertyChanged(nameof(DisableBackgroundCapture));
        OnPropertyChanged(nameof(UsePerformancePowerPlan));
    }

    private void ApplyHistory(IReadOnlyList<AppHistoryRecord> records)
    {
        HistoryItems.Clear();
        foreach (var record in records.OrderByDescending(item => item.CreatedAt).Take(30))
        {
            HistoryItems.Add(new HistoryDisplayItem(
                record.TransactionId,
                $"Perfil {ProfileName(record.Profile)}",
                record.CreatedAt.LocalDateTime.ToString("dd/MM/yyyy • HH:mm"),
                $"{record.ChangedActions} ajustes • {record.State}",
                record.CanRollback));
        }

        if (HistoryItems.Count == 0)
        {
            HistoryItems.Add(new HistoryDisplayItem(
                Guid.Empty,
                "Nenhuma otimização executada",
                "Histórico local",
                "A primeira execução aparecerá aqui",
                false));
        }
    }

    private void RefreshPlan()
    {
        var edition = diagnostic?.Edition ?? FiveMEdition.Unknown;
        var options = new OptimizationOptionsDto
        {
            CleanUserTemporaryFiles = CleanTemporaryFiles,
            TemporaryFileMinimumAgeDays = selectedProfile switch
            {
                OptimizationProfile.Light => 14,
                OptimizationProfile.Balanced => 7,
                _ => 3
            },
            RemoveOldFiveMCrashDumps = RemoveOldDiagnostics,
            DiagnosticRetentionDays = selectedProfile == OptimizationProfile.Aggressive ? 7 : 14,
            ServerCacheRepair = SmartCacheRepair ? CacheRepairPolicy.WhenOversized : CacheRepairPolicy.Off,
            ServerCacheThresholdGiB = 8,
            EnableGameMode = EnableGameMode,
            PreferHighPerformanceGpu = true,
            DisableBackgroundCapture = DisableBackgroundCapture,
            UseSessionPerformancePowerPlan = UsePerformancePowerPlan,
            ApplyLegacyGraphicsPreset = selectedProfile != OptimizationProfile.Light,
            ReduceWindowsVisualEffects = selectedProfile == OptimizationProfile.Aggressive
        };

        currentPlan = planBuilder.Build(new OptimizationPlanRequestDto
        {
            Profile = selectedProfile,
            Edition = edition,
            Options = options
        });

        PlannedActions.Clear();
        foreach (var action in currentPlan.Actions)
        {
            PlannedActions.Add(ToDisplayItem(action.Metadata));
        }

        OnPropertyChanged(nameof(SelectedActionCount));
        OnPropertyChanged(nameof(ElevationLabel));
        OnPropertyChanged(nameof(PlanSummary));
        OnPropertyChanged(nameof(PlanHeader));
        OnPropertyChanged(nameof(PlanNoticesText));
        OnPropertyChanged(nameof(SafetySummary));
        RaiseCommandState();
    }

    private async void SettingsChanged()
    {
        RefreshPlan();
        try
        {
            await service.SaveSettingsAsync(new AppSettings
            {
                CleanTemporaryFiles = CleanTemporaryFiles,
                RemoveOldDiagnostics = RemoveOldDiagnostics,
                SmartCacheRepair = SmartCacheRepair,
                EnableGameMode = EnableGameMode,
                DisableBackgroundCapture = DisableBackgroundCapture,
                UsePerformancePowerPlan = UsePerformancePowerPlan
            });
        }
        catch
        {
            AddLog("Não foi possível salvar as configurações agora.");
        }
    }

    private void ApplyProgress(AppProgressUpdate update)
    {
        ProgressPercent = Math.Clamp(update.Percent, 0, 100);
        ProgressHeadline = update.Headline;
        ProgressDetail = update.Detail;
        ProgressStateLabel = update.Kind switch
        {
            AppProgressKind.Preparing => "PREPARANDO",
            AppProgressKind.Applying => "OTIMIZANDO",
            AppProgressKind.Verifying => "VERIFICANDO",
            AppProgressKind.RollingBack => "RESTAURANDO",
            AppProgressKind.Completed => "CONCLUÍDO",
            AppProgressKind.Warning => "ATENÇÃO",
            AppProgressKind.Failed => "FALHA SEGURA",
            _ => "EM ANDAMENTO"
        };
        AddLog(update.Detail);
    }

    private void AddLog(string message)
    {
        ActivityLog.Add(new ActivityLogItem(DateTime.Now.ToString("HH:mm:ss"), message));
        while (ActivityLog.Count > 100)
        {
            ActivityLog.RemoveAt(0);
        }
    }

    private void RaiseCommandState()
    {
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanCancel));
    }

    private static ActionDisplayItem ToDisplayItem(ActionMetadataDto action)
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
            ActionRisk.Informational => "INFORMATIVO",
            ActionRisk.Low => "RISCO BAIXO",
            ActionRisk.Moderate => "MODERADO",
            ActionRisk.High => "ALTO • REVERSÍVEL",
            _ => action.Risk.ToString().ToUpperInvariant()
        };
        var privilege = action.RequiredPrivilege == RequiredPrivilege.Administrator
            ? "Requer UAC"
            : action.Reversibility is ActionReversibility.Irreversible or ActionReversibility.RebuildableData
                ? "Limpeza permanente"
                : "Reversível";
        return new ActionDisplayItem(action.Id, action.Name, action.Description, icon, risk, privilege);
    }

    private static string ProfileName(OptimizationProfile profile) => profile switch
    {
        OptimizationProfile.Light => "Leve",
        OptimizationProfile.Balanced => "Médio",
        OptimizationProfile.Aggressive => "Agressivo",
        _ => profile.ToString()
    };

    private static string FormatBytes(long bytes)
    {
        const double giB = 1024d * 1024d * 1024d;
        const double miB = 1024d * 1024d;
        return bytes >= giB ? $"{bytes / giB:0.##} GB" : $"{bytes / miB:0.#} MB";
    }
}
