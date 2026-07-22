using System.Collections.ObjectModel;
using FiveMCleaner.Contracts;

namespace FiveMCleaner.Core.Catalog;

public sealed class ActionCatalog
{
    public const int CurrentVersion = 3;

    private static readonly string[] NoPrerequisites = [];
    private static readonly string[] RequiresFiveMStoppedFirst = [OptimizationActionIds.VerifyFiveMIsStopped];
    private static readonly string[] RequiresGtaVStoppedFirst = [OptimizationActionIds.VerifyGtaVIsStopped];

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
                ActionOptionGate.Always,
                isCritical: true,
                detectionSummary: "Inspeciona os processos ativos cuja imagem pertence à pasta do FiveM.",
                confirmationSummary: "A verificação passa quando nenhum processo do FiveM está em execução.",
                undoSummary: "Somente leitura: não há nada para desfazer.",
                riskLimitations: "Se o FiveM estiver aberto, as ações dependentes são ignoradas com segurança."),
            Define(
                OptimizationActionIds.VerifyGtaVIsStopped,
                "Verificar estado do GTA V",
                "Confirma que o GTA V Legacy está fechado antes de qualquer alteração no settings.xml dele.",
                ActionCategory.Safety,
                ActionRisk.Informational,
                ActionReversibility.ReadOnly,
                RequiredPrivilege.StandardUser,
                AllProfiles,
                requiresFiveMStopped: false,
                progressWeight: 2,
                expectedImpact: "Evita uma execução parcial quando a otimização do GTA V está habilitada.",
                ActionOptionGate.ApplyGtaVGraphicsPreset,
                isCritical: true,
                detectionSummary: "Inspeciona os processos ativos cuja imagem pertence à pasta do GTA V.",
                confirmationSummary: "A verificação passa quando o GTA V não está em execução.",
                undoSummary: "Somente leitura: não há nada para desfazer.",
                riskLimitations: "Se o GTA V estiver aberto, os ajustes gráficos do GTA V são ignorados com segurança."),
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
                ActionOptionGate.CleanUserTemporaryFiles,
                detectionSummary: "Percorre a pasta Temp do usuário buscando arquivos além do período de retenção.",
                confirmationSummary: "Confirma que os arquivos antigos selecionados não existem mais após a limpeza.",
                undoSummary: "Irreversível: arquivos temporários antigos não são preservados em quarentena.",
                riskLimitations: "Remove apenas arquivos antigos do próprio usuário; nunca toca em pastas do sistema."),
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
                ActionOptionGate.RemoveOldFiveMCrashDumps,
                prerequisites: RequiresFiveMStoppedFirst,
                detectionSummary: "Lista dumps e logs do FiveM mais antigos que o período de retenção.",
                confirmationSummary: "Confirma que os diagnósticos antigos foram removidos e os recentes preservados.",
                undoSummary: "Irreversível: diagnósticos antigos não são copiados para o journal.",
                riskLimitations: "Dumps podem ser úteis para suporte; apenas os fora do período de retenção são removidos."),
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
                ActionOptionGate.RepairLegacyServerCache,
                prerequisites: RequiresFiveMStoppedFirst,
                detectionSummary: "Mede o tamanho de server-cache e server-cache-priv sob a instalação do FiveM.",
                confirmationSummary: "Confirma que apenas os caches regeneráveis foram removidos.",
                undoSummary: "Dados regeneráveis: o FiveM baixa o conteúdo novamente no próximo acesso ao servidor.",
                riskLimitations: "O primeiro carregamento fica mais lento; clipes antigos do Rockstar Editor podem deixar de funcionar."),
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
                ActionOptionGate.EnableGameMode,
                detectionSummary: "Lê o valor AutoGameModeEnabled do registro do usuário atual.",
                confirmationSummary: "Confirma que o Modo de Jogo ficou habilitado após a gravação.",
                undoSummary: "Totalmente reversível: o valor anterior do registro é restaurado no rollback.",
                riskLimitations: "O ganho depende do hardware e da versão do Windows; pode não ter efeito perceptível."),
            Define(
                OptimizationActionIds.PreferHighPerformanceGpu,
                "Preferir GPU de alto desempenho",
                "Define a preferência gráfica do Windows para o launcher e os renderizadores detectados do FiveM Legacy.",
                ActionCategory.WindowsGaming,
                ActionRisk.Low,
                ActionReversibility.FullyReversible,
                RequiredPrivilege.StandardUser,
                AllProfiles,
                requiresFiveMStopped: true,
                progressWeight: 5,
                expectedImpact: "Evita o uso acidental da GPU econômica em computadores com múltiplas GPUs.",
                ActionOptionGate.PreferHighPerformanceGpu,
                prerequisites: RequiresFiveMStoppedFirst,
                detectionSummary: "Lê a preferência gráfica registrada para o executável do FiveM.",
                confirmationSummary: "Confirma que a preferência de alto desempenho ficou registrada.",
                undoSummary: "Totalmente reversível: a preferência anterior é restaurada no rollback.",
                riskLimitations: "Só faz diferença em computadores com mais de uma GPU."),
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
                ActionOptionGate.DisableBackgroundCapture,
                detectionSummary: "Lê o valor de gravação em segundo plano (Game DVR) do registro do usuário.",
                confirmationSummary: "Confirma que a captura em segundo plano ficou desabilitada.",
                undoSummary: "Totalmente reversível: o valor anterior é restaurado no rollback.",
                riskLimitations: "Não remove o Game Bar nem afeta gravações manuais que você iniciar."),
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
                requiresAcPower: true,
                detectionSummary: "Lê o plano de energia ativo e verifica se o computador está na tomada.",
                confirmationSummary: "Confirma que o plano de desempenho ficou ativo.",
                undoSummary: "Totalmente reversível: o plano anterior é restaurado no rollback.",
                riskLimitations: "Só é aplicado na tomada; aumenta consumo e temperatura enquanto ativo."),
            Define(
                OptimizationActionIds.ApplyLightLegacyGraphics,
                "Ajustar gráficos leves do FiveM",
                "Desliga apenas antialiasing de maior custo no arquivo existente do FiveM, com backup.",
                ActionCategory.FiveMGraphics,
                ActionRisk.Low,
                ActionReversibility.FullyReversible,
                RequiredPrivilege.StandardUser,
                [OptimizationProfile.Light],
                requiresFiveMStopped: true,
                progressWeight: 7,
                expectedImpact: "Remove custos altos preservando texturas, resolução e a maior parte da qualidade visual.",
                ActionOptionGate.ApplyLegacyGraphicsPreset,
                prerequisites: RequiresFiveMStoppedFirst,
                detectionSummary: "Lê as opções existentes de gta5_settings.xml do FiveM.",
                confirmationSummary: "Confirma que apenas as opções caras foram ajustadas e o arquivo permanece válido.",
                undoSummary: "Totalmente reversível: o backup do arquivo é restaurado no rollback.",
                riskLimitations: "Só altera opções já presentes; não distribui um XML genérico."),
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
                ActionOptionGate.ApplyLegacyGraphicsPreset,
                prerequisites: RequiresFiveMStoppedFirst,
                detectionSummary: "Lê as opções existentes de gta5_settings.xml do FiveM.",
                confirmationSummary: "Confirma que os limites equilibrados foram aplicados e o arquivo permanece válido.",
                undoSummary: "Totalmente reversível: o backup do arquivo é restaurado no rollback.",
                riskLimitations: "Resultados variam conforme o hardware; só altera opções já presentes."),
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
                ActionOptionGate.ApplyLegacyGraphicsPreset,
                prerequisites: RequiresFiveMStoppedFirst,
                detectionSummary: "Lê as opções existentes de gta5_settings.xml do FiveM.",
                confirmationSummary: "Confirma que as opções pesadas foram reduzidas e o arquivo permanece válido.",
                undoSummary: "Totalmente reversível: o backup do arquivo é restaurado no rollback.",
                riskLimitations: "Reduz qualidade visual de forma perceptível; não força resolução mínima."),
            Define(
                OptimizationActionIds.ApplyLightGtaVGraphics,
                "Ajustar gráficos leves do GTA V",
                "Detecta o settings.xml do GTA V Legacy, reduz somente opções caras já existentes e cria backup.",
                ActionCategory.FiveMGraphics,
                ActionRisk.Low,
                ActionReversibility.FullyReversible,
                RequiredPrivilege.StandardUser,
                [OptimizationProfile.Light],
                requiresFiveMStopped: true,
                progressWeight: 7,
                expectedImpact: "Preserva resolução e qualidade geral enquanto reduz antialiasing caro.",
                ActionOptionGate.ApplyGtaVGraphicsPreset,
                prerequisites: RequiresGtaVStoppedFirst,
                detectionSummary: "Lê as opções existentes do settings.xml do GTA V Legacy.",
                confirmationSummary: "Confirma que apenas as opções caras foram ajustadas e o arquivo permanece válido.",
                undoSummary: "Totalmente reversível: o backup do arquivo é restaurado no rollback.",
                riskLimitations: "Só altera opções já presentes; não altera resolução, tela ou adaptador."),
            Define(
                OptimizationActionIds.ApplyBalancedGtaVGraphics,
                "Equilibrar gráficos do GTA V",
                "Aplica limites equilibrados somente em opções existentes do settings.xml do GTA V Legacy, com backup.",
                ActionCategory.FiveMGraphics,
                ActionRisk.Moderate,
                ActionReversibility.FullyReversible,
                RequiredPrivilege.StandardUser,
                [OptimizationProfile.Balanced],
                requiresFiveMStopped: true,
                progressWeight: 11,
                expectedImpact: "Equilibra qualidade, uso de GPU e estabilidade de quadros também no GTA V base.",
                ActionOptionGate.ApplyGtaVGraphicsPreset,
                prerequisites: RequiresGtaVStoppedFirst,
                detectionSummary: "Lê as opções existentes do settings.xml do GTA V Legacy.",
                confirmationSummary: "Confirma que os limites equilibrados foram aplicados e o arquivo permanece válido.",
                undoSummary: "Totalmente reversível: o backup do arquivo é restaurado no rollback.",
                riskLimitations: "Resultados variam conforme o hardware; só altera opções já presentes."),
            Define(
                OptimizationActionIds.ApplyAggressiveGtaVGraphics,
                "Priorizar FPS no GTA V",
                "Reduz opções gráficas pesadas do settings.xml do GTA V Legacy sem alterar resolução, tela ou adaptador.",
                ActionCategory.FiveMGraphics,
                ActionRisk.High,
                ActionReversibility.FullyReversible,
                RequiredPrivilege.StandardUser,
                [OptimizationProfile.Aggressive],
                requiresFiveMStopped: true,
                progressWeight: 13,
                expectedImpact: "Prioriza FPS sem forçar resolução, taxa de atualização, DirectX ou textura mínima.",
                ActionOptionGate.ApplyGtaVGraphicsPreset,
                prerequisites: RequiresGtaVStoppedFirst,
                detectionSummary: "Lê as opções existentes do settings.xml do GTA V Legacy.",
                confirmationSummary: "Confirma que as opções pesadas foram reduzidas e o arquivo permanece válido.",
                undoSummary: "Totalmente reversível: o backup do arquivo é restaurado no rollback.",
                riskLimitations: "Reduz qualidade visual; não força resolução, taxa de atualização ou textura mínima."),
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
                ActionOptionGate.ReduceWindowsVisualEffects,
                detectionSummary: "Lê o estado atual de animações e transparências do Windows.",
                confirmationSummary: "Confirma que os efeitos foram reduzidos preservando a suavização de fontes.",
                undoSummary: "Totalmente reversível: o estado anterior dos efeitos é restaurado no rollback.",
                riskLimitations: "Muda a aparência do desktop; preserva legibilidade e suavização de fontes.")
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
        bool requiresRestart = false,
        IReadOnlyList<string>? prerequisites = null,
        bool isCritical = false,
        SupportedWindowsVersions supportedWindows = SupportedWindowsVersions.All,
        string detectionSummary = "",
        string confirmationSummary = "",
        string undoSummary = "",
        string riskLimitations = "")
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
            optionGate,
            prerequisites ?? NoPrerequisites,
            isCritical,
            supportedWindows,
            detectionSummary,
            confirmationSummary,
            undoSummary,
            riskLimitations);
    }
}
