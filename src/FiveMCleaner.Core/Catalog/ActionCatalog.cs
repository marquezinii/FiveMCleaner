using System.Collections.ObjectModel;
using FiveMCleaner.Contracts;

namespace FiveMCleaner.Core.Catalog;

public sealed class ActionCatalog
{
    public const int CurrentVersion = 1;

    private static readonly OptimizationProfile[] AllProfiles =
    [
        OptimizationProfile.Light,
        OptimizationProfile.Balanced,
        OptimizationProfile.Aggressive
    ];

    private static readonly OptimizationProfile[] BalancedAndAggressive =
    [
        OptimizationProfile.Balanced,
        OptimizationProfile.Aggressive
    ];

    private readonly IReadOnlyDictionary<string, OptimizationActionDefinition> _byId;

    private ActionCatalog(IReadOnlyList<OptimizationActionDefinition> actions)
    {
        Actions = new ReadOnlyCollection<OptimizationActionDefinition>(actions.ToArray());
        _byId = new ReadOnlyDictionary<string, OptimizationActionDefinition>(
            actions.ToDictionary(action => action.Id, StringComparer.Ordinal));
    }

    public static ActionCatalog Current { get; } = new(CreateActions());

    public IReadOnlyList<OptimizationActionDefinition> Actions { get; }

    public bool TryGet(string actionId, out OptimizationActionDefinition? definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        return _byId.TryGetValue(actionId, out definition);
    }

    public OptimizationActionDefinition GetRequired(string actionId)
    {
        return TryGet(actionId, out var definition)
            ? definition!
            : throw new KeyNotFoundException($"Unknown optimization action ID '{actionId}'.");
    }

    private static IReadOnlyList<OptimizationActionDefinition> CreateActions()
    {
        return
        [
            Define(
                OptimizationActionIds.VerifyFiveMIsStopped,
                "Verificar estado do FiveM",
                "Confirma que os processos do FiveM estão encerrados antes de qualquer alteração em cache ou gráficos.",
                ActionCategory.Safety,
                ActionRisk.Informational,
                ActionReversibility.ReadOnly,
                RequiredPrivilege.StandardUser,
                AllProfiles,
                requiresFiveMStopped: false,
                progressWeight: 2,
                expectedImpact: "Protege a integridade dos arquivos do FiveM.",
                ActionOptionGate.Always),
            Define(
                OptimizationActionIds.CleanUserTemporaryFiles,
                "Limpar temporários antigos",
                "Remove somente arquivos temporários do usuário que ultrapassaram o período de retenção configurado.",
                ActionCategory.Storage,
                ActionRisk.Low,
                ActionReversibility.Irreversible,
                RequiredPrivilege.StandardUser,
                AllProfiles,
                requiresFiveMStopped: false,
                progressWeight: 18,
                expectedImpact: "Libera espaço; não promete aumento direto de FPS.",
                ActionOptionGate.CleanUserTemporaryFiles),
            Define(
                OptimizationActionIds.PruneLegacyCrashDumps,
                "Remover diagnósticos antigos",
                "Remove dumps e logs antigos do FiveM Legacy, preservando os arquivos dentro do período de retenção.",
                ActionCategory.Storage,
                ActionRisk.Low,
                ActionReversibility.Irreversible,
                RequiredPrivilege.StandardUser,
                AllProfiles,
                requiresFiveMStopped: true,
                progressWeight: 12,
                expectedImpact: "Libera espaço ocupado por diagnósticos antigos.",
                ActionOptionGate.RemoveOldFiveMCrashDumps),
            Define(
                OptimizationActionIds.RepairLegacyServerCache,
                "Reparar cache de servidores",
                "Remove somente caches regeneráveis de conteúdo de servidores do FiveM Legacy.",
                ActionCategory.Storage,
                ActionRisk.Moderate,
                ActionReversibility.RebuildableData,
                RequiredPrivilege.StandardUser,
                AllProfiles,
                requiresFiveMStopped: true,
                progressWeight: 30,
                expectedImpact: "Pode corrigir corrupção ou liberar espaço; o primeiro carregamento ficará mais lento.",
                ActionOptionGate.RepairLegacyServerCache),
            Define(
                OptimizationActionIds.EnableGameMode,
                "Ativar Modo de Jogo",
                "Ativa o recurso de jogos do Windows para priorização quando jogos estão em execução.",
                ActionCategory.WindowsGaming,
                ActionRisk.Low,
                ActionReversibility.FullyReversible,
                RequiredPrivilege.StandardUser,
                AllProfiles,
                requiresFiveMStopped: false,
                progressWeight: 5,
                expectedImpact: "Pode melhorar a consistência da sessão em sistemas compatíveis.",
                ActionOptionGate.EnableGameMode),
            Define(
                OptimizationActionIds.PreferHighPerformanceGpu,
                "Preferir GPU de alto desempenho",
                "Define a preferência gráfica do Windows para o executável detectado do FiveM Legacy.",
                ActionCategory.WindowsGaming,
                ActionRisk.Low,
                ActionReversibility.FullyReversible,
                RequiredPrivilege.StandardUser,
                AllProfiles,
                requiresFiveMStopped: true,
                progressWeight: 5,
                expectedImpact: "Evita o uso acidental da GPU econômica em computadores com múltiplas GPUs.",
                ActionOptionGate.PreferHighPerformanceGpu),
            Define(
                OptimizationActionIds.DisableBackgroundCapture,
                "Desativar captura em segundo plano",
                "Desativa a gravação contínua em segundo plano do Windows sem remover o Game Bar.",
                ActionCategory.WindowsGaming,
                ActionRisk.Low,
                ActionReversibility.FullyReversible,
                RequiredPrivilege.StandardUser,
                BalancedAndAggressive,
                requiresFiveMStopped: false,
                progressWeight: 4,
                expectedImpact: "Reduz atividade de captura quando ela estava habilitada.",
                ActionOptionGate.DisableBackgroundCapture),
            Define(
                OptimizationActionIds.EnableSessionPerformancePowerPlan,
                "Ativar plano de energia de alto desempenho",
                "Ativa um plano de energia de desempenho na tomada e registra o estado anterior para rollback.",
                ActionCategory.Power,
                ActionRisk.Moderate,
                ActionReversibility.FullyReversible,
                RequiredPrivilege.Administrator,
                BalancedAndAggressive,
                requiresFiveMStopped: false,
                progressWeight: 7,
                expectedImpact: "Reduz limitação de energia; aumenta consumo e temperatura até o rollback.",
                ActionOptionGate.UseSessionPerformancePowerPlan,
                requiresAcPower: true),
            Define(
                OptimizationActionIds.ApplyBalancedLegacyGraphics,
                "Aplicar gráficos equilibrados",
                "Ajusta somente opções existentes do arquivo gráfico do FiveM Legacy e cria backup antes da gravação.",
                ActionCategory.FiveMGraphics,
                ActionRisk.Moderate,
                ActionReversibility.FullyReversible,
                RequiredPrivilege.StandardUser,
                [OptimizationProfile.Balanced],
                requiresFiveMStopped: true,
                progressWeight: 12,
                expectedImpact: "Equilibra qualidade visual, uso de GPU e estabilidade de quadros.",
                ActionOptionGate.ApplyLegacyGraphicsPreset),
            Define(
                OptimizationActionIds.ApplyAggressiveLegacyGraphics,
                "Aplicar gráficos agressivos",
                "Reduz opções gráficas de maior custo no FiveM Legacy e cria backup antes da gravação.",
                ActionCategory.FiveMGraphics,
                ActionRisk.High,
                ActionReversibility.FullyReversible,
                RequiredPrivilege.StandardUser,
                [OptimizationProfile.Aggressive],
                requiresFiveMStopped: true,
                progressWeight: 14,
                expectedImpact: "Prioriza FPS e responsividade em vez de qualidade visual.",
                ActionOptionGate.ApplyLegacyGraphicsPreset),
            Define(
                OptimizationActionIds.ReduceWindowsVisualEffects,
                "Reduzir efeitos visuais do Windows",
                "Reduz animações e transparências preservando legibilidade e suavização de fontes.",
                ActionCategory.Appearance,
                ActionRisk.Moderate,
                ActionReversibility.FullyReversible,
                RequiredPrivilege.StandardUser,
                [OptimizationProfile.Aggressive],
                requiresFiveMStopped: false,
                progressWeight: 6,
                expectedImpact: "Reduz trabalho visual do desktop em computadores limitados.",
                ActionOptionGate.ReduceWindowsVisualEffects)
        ];
    }

    private static OptimizationActionDefinition Define(
        string id,
        string name,
        string description,
        ActionCategory category,
        ActionRisk risk,
        ActionReversibility reversibility,
        RequiredPrivilege requiredPrivilege,
        IReadOnlyList<OptimizationProfile> supportedProfiles,
        bool requiresFiveMStopped,
        int progressWeight,
        string expectedImpact,
        ActionOptionGate optionGate,
        bool requiresAcPower = false,
        bool requiresRestart = false)
    {
        return new OptimizationActionDefinition(
            id,
            version: 1,
            name,
            description,
            category,
            risk,
            reversibility,
            requiredPrivilege,
            supportedProfiles.ToArray(),
            requiresFiveMStopped,
            requiresAcPower,
            requiresRestart,
            progressWeight,
            expectedImpact,
            optionGate);
    }
}
