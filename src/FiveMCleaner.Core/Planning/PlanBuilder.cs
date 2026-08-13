using FiveMCleaner.Contracts;
using FiveMCleaner.Core.Catalog;

namespace FiveMCleaner.Core.Planning;

/// <summary>
/// Turns an optimization request into a plan. Pure: the same request and
/// context always produce the same plan, and planning never reads the clock,
/// the file system, the registry or any ambient state.
/// </summary>
public static class PlanBuilder
{
    public static OptimizationPlanDto Build(
        OptimizationPlanRequestDto request,
        PlanBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        ValidateRequest(request);

        var blocks = CreateBlocks(request.Edition);
        if (blocks.Count > 0)
        {
            return CreatePlan(request, context, [], blocks, []);
        }

        var selectedDefinitions = context.Catalog.Actions
            .Where(action => action.Supports(request.Profile))
            .Where(action => action.SupportsWindows(request.DetectedWindows))
            .Where(action => IsEnabled(action.OptionGate, request.Options))
            .ToArray();

        var plannedActions = selectedDefinitions
            .Select((definition, index) => new PlannedActionDto
            {
                Sequence = index + 1,
                Metadata = definition.ToMetadata()
            })
            .ToArray();

        var notices = CreateNotices(request, selectedDefinitions);
        return CreatePlan(request, context, plannedActions, [], notices);
    }

    /// <summary>
    /// Rebuilds the request that must reproduce <paramref name="plan"/>. Both
    /// the elevated broker and the Windows runtime re-plan a submitted plan and
    /// reject it when the result differs, so the reconstruction lives here
    /// rather than being restated at each boundary.
    /// </summary>
    /// <remarks>
    /// <see cref="OptimizationPlanRequestDto.DetectedWindows"/> is deliberately
    /// left at its default: the plan does not carry the detected Windows
    /// version, so a validator cannot know it. Every catalog action is
    /// currently eligible on every supported version, which keeps the
    /// reconstruction exact. If an action ever becomes version-gated, the
    /// detected version has to travel with the plan or validation will reject
    /// legitimate plans.
    /// </remarks>
    public static OptimizationPlanRequestDto CanonicalRequestFor(OptimizationPlanDto plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(plan.Options);

        return new OptimizationPlanRequestDto
        {
            Profile = plan.Profile,
            Edition = plan.Edition,
            Options = plan.Options with { }
        };
    }

    private static OptimizationPlanDto CreatePlan(
        OptimizationPlanRequestDto request,
        PlanBuildContext context,
        IReadOnlyList<PlannedActionDto> actions,
        IReadOnlyList<PlanBlockDto> blocks,
        IReadOnlyList<PlanNoticeDto> notices)
    {
        var metadata = actions.Select(action => action.Metadata).ToArray();

        return new OptimizationPlanDto
        {
            PlanId = context.PlanId,
            SchemaVersion = ProductIdentity.PlanSchemaVersion,
            CatalogVersion = ActionCatalog.CurrentVersion,
            ProductName = ProductIdentity.Name,
            ProductSubtitle = ProductIdentity.Subtitle,
            CreatedAtUtc = context.CreatedAtUtc,
            Profile = request.Profile,
            Edition = request.Edition,
            Options = request.Options with { },
            IsExecutable = blocks.Count == 0 && actions.Count > 0,
            RequiresElevation = metadata.Any(action => action.RequiredPrivilege == RequiredPrivilege.Administrator),
            ContainsNonReversibleActions = metadata.Any(action =>
                action.Reversibility is ActionReversibility.RebuildableData or ActionReversibility.Irreversible),
            MaximumRisk = metadata.Length == 0
                ? ActionRisk.Informational
                : metadata.Max(action => action.Risk),
            Actions = actions.ToArray(),
            Blocks = blocks.ToArray(),
            Notices = notices.ToArray()
        };
    }

    private static IReadOnlyList<PlanBlockDto> CreateBlocks(FiveMEdition edition)
    {
        return edition switch
        {
            FiveMEdition.Legacy => [],
            FiveMEdition.Unknown =>
            [
                new PlanBlockDto
                {
                    Code = PlanBlockCode.EditionNotDetected,
                    Message = "Nenhuma instalação compatível do FiveM Legacy foi detectada."
                }
            ],
            FiveMEdition.Enhanced =>
            [
                new PlanBlockDto
                {
                    Code = PlanBlockCode.EnhancedNotSupported,
                    Message = "FiveM Enhanced ainda não é suportado. Nenhuma ação do Legacy será executada nessa edição."
                }
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(edition), edition, "Unknown FiveM edition value.")
        };
    }

    private static IReadOnlyList<PlanNoticeDto> CreateNotices(
        OptimizationPlanRequestDto request,
        IReadOnlyList<OptimizationActionDefinition> actions)
    {
        return [.. CreateRemovalNotices(request, actions),
            .. CreatePowerAndProcessNotices(actions),
            .. CreateRepairNotices(actions),
            .. CreateGraphicsNotices(actions),
            .. CreateProfileNotices(request)];
    }

    private static IReadOnlyList<PlanNoticeDto> CreateRemovalNotices(
        OptimizationPlanRequestDto request,
        IReadOnlyList<OptimizationActionDefinition> actions)
    {
        var notices = new List<PlanNoticeDto>();

        if (actions.Any(action => action.Id == OptimizationActionIds.PruneLegacyCrashDumps))
        {
            notices.Add(new PlanNoticeDto
            {
                Code = "diagnostics-removal-is-permanent",
                Severity = PlanNoticeSeverity.Information,
                ActionId = OptimizationActionIds.PruneLegacyCrashDumps,
                Message = $"Diagnósticos com mais de {request.Options.DiagnosticRetentionDays} dias serão removidos permanentemente."
            });
        }

        if (actions.Any(action => action.Id == OptimizationActionIds.RepairLegacyServerCache))
        {
            notices.Add(new PlanNoticeDto
            {
                Code = "server-cache-will-be-rebuilt",
                Severity = PlanNoticeSeverity.Warning,
                ActionId = OptimizationActionIds.RepairLegacyServerCache,
                Message = "O cache de servidores será recriado e o primeiro carregamento poderá ficar mais lento; limpar server-cache-priv também pode tornar clipes antigos do Rockstar Editor indisponíveis."
            });
        }

        return notices;
    }

    private static IReadOnlyList<PlanNoticeDto> CreatePowerAndProcessNotices(
        IReadOnlyList<OptimizationActionDefinition> actions)
    {
        var notices = new List<PlanNoticeDto>();

        if (actions.Any(action => action.Id == OptimizationActionIds.EnableSessionPerformancePowerPlan))
        {
            notices.Add(new PlanNoticeDto
            {
                Code = "performance-power-requires-ac",
                Severity = PlanNoticeSeverity.Information,
                ActionId = OptimizationActionIds.EnableSessionPerformancePowerPlan,
                Message = "O modo de energia de desempenho só será aplicado com o computador ligado à tomada."
            });
        }

        if (actions.Any(action => action.Id == OptimizationActionIds.TerminateStuckFiveMProcess))
        {
            notices.Add(new PlanNoticeDto
            {
                Code = "stuck-process-termination-loses-unsaved-state",
                Severity = PlanNoticeSeverity.Warning,
                ActionId = OptimizationActionIds.TerminateStuckFiveMProcess,
                Message = "Só encerra um processo do FiveM comprovadamente travado (não responde); qualquer estado não salvo nele será perdido."
            });
        }

        return notices;
    }

    private static IReadOnlyList<PlanNoticeDto> CreateRepairNotices(
        IReadOnlyList<OptimizationActionDefinition> actions)
    {
        var notices = new List<PlanNoticeDto>();

        if (actions.Any(action => action.Id == OptimizationActionIds.RecreateFiveMLocalData))
        {
            notices.Add(new PlanNoticeDto
            {
                Code = "local-data-recreation-is-a-repair-not-daily-optimization",
                Severity = PlanNoticeSeverity.Warning,
                ActionId = OptimizationActionIds.RecreateFiveMLocalData,
                Message = "Recria as pastas de dados regeneráveis do FiveM; use apenas para reparar uma instalação com problema, não como otimização diária."
            });
        }

        if (actions.Any(action => action.Id == OptimizationActionIds.RepairStaleAuthData))
        {
            notices.Add(new PlanNoticeDto
            {
                Code = "auth-data-repair-requires-detected-error-pattern",
                Severity = PlanNoticeSeverity.Warning,
                ActionId = OptimizationActionIds.RepairStaleAuthData,
                Message = "Só remove ros_id.dat e entitlements quando um padrão de erro específico é detectado no log; exigirá novo login no próximo início do FiveM."
            });
        }

        return notices;
    }

    private static IReadOnlyList<PlanNoticeDto> CreateGraphicsNotices(
        IReadOnlyList<OptimizationActionDefinition> actions)
    {
        var notices = new List<PlanNoticeDto>();

        if (actions.Any(action => action.Id == OptimizationActionIds.ApplyQualityLegacyGraphics
            || action.Id == OptimizationActionIds.ApplyQualityGtaVGraphics))
        {
            notices.Add(new PlanNoticeDto
            {
                Code = "quality-preset-may-reduce-fps",
                Severity = PlanNoticeSeverity.Warning,
                Message = "O preset de qualidade aumenta opções gráficas até um teto seguro; isso pode reduzir o FPS em comparação com os presets Equilibrado ou FPS."
            });
        }

        if (actions.Any(action => action.Id == OptimizationActionIds.ApplyLegacyDisplayPreferences
            || action.Id == OptimizationActionIds.ApplyGtaVDisplayPreferences))
        {
            notices.Add(new PlanNoticeDto
            {
                Code = "display-preferences-do-not-change-resolution",
                Severity = PlanNoticeSeverity.Information,
                Message = "Este ajuste altera apenas janela e VSync; resolução, taxa de atualização, adaptador de vídeo e proporção de tela não são alterados automaticamente."
            });
        }

        if (actions.Any(action => action.Id == OptimizationActionIds.ApplyGtaVRepairLaunchParameters))
        {
            notices.Add(new PlanNoticeDto
            {
                Code = "gtav-repair-launch-parameters-are-temporary",
                Severity = PlanNoticeSeverity.Warning,
                ActionId = OptimizationActionIds.ApplyGtaVRepairLaunchParameters,
                Message = "Parâmetros de reparo do GTA V (-safemode/-useMinimumSettings/-UseAutoSettings) são temporários; reverta esta otimização assim que terminar de diagnosticar o problema."
            });
        }

        if (actions.Any(action => action.Id == OptimizationActionIds.ApplyGtaVGraphicsLaunchParameters
            || action.Id == OptimizationActionIds.ApplyGtaVDisplayLaunchParameters
            || action.Id == OptimizationActionIds.ApplyGtaVRepairLaunchParameters))
        {
            notices.Add(new PlanNoticeDto
            {
                Code = "gtav-launch-parameters-do-not-affect-fivem",
                Severity = PlanNoticeSeverity.Information,
                Message = "Parâmetros de inicialização em commandline.txt só têm efeito no GTA V standalone; o FiveM ignora esse arquivo."
            });
        }

        return notices;
    }

    private static IReadOnlyList<PlanNoticeDto> CreateProfileNotices(OptimizationPlanRequestDto request)
    {
        if (request.Profile != OptimizationProfile.Aggressive)
        {
            return [];
        }

        return
        [
            new PlanNoticeDto
            {
                Code = "aggressive-prioritizes-performance",
                Severity = PlanNoticeSeverity.Warning,
                Message = "O perfil agressivo prioriza FPS e responsividade, reduzindo a qualidade visual."
            }
        ];
    }

    private static bool IsEnabled(ActionOptionGate gate, OptimizationOptionsDto options)
    {
        return gate switch
        {
            ActionOptionGate.Always => true,
            ActionOptionGate.CleanUserTemporaryFiles => options.CleanUserTemporaryFiles,
            ActionOptionGate.RemoveOldFiveMCrashDumps => options.RemoveOldFiveMCrashDumps,
            ActionOptionGate.RepairLegacyServerCache => options.ServerCacheRepair != CacheRepairPolicy.Off,
            ActionOptionGate.EnableGameMode => options.EnableGameMode,
            ActionOptionGate.PreferHighPerformanceGpu => options.PreferHighPerformanceGpu,
            ActionOptionGate.DisableBackgroundCapture => options.DisableBackgroundCapture,
            ActionOptionGate.UseSessionPerformancePowerPlan => options.UseSessionPerformancePowerPlan,
            ActionOptionGate.ApplyLegacyGraphicsPreset => options.ApplyLegacyGraphicsPreset,
            ActionOptionGate.ApplyGtaVGraphicsPreset => options.ApplyGtaVGraphicsPreset,
            ActionOptionGate.ReduceWindowsVisualEffects => options.ReduceWindowsVisualEffects,
            ActionOptionGate.TerminateStuckFiveMProcess => options.TerminateStuckFiveMProcess,
            ActionOptionGate.RecreateFiveMLocalData => options.RecreateFiveMLocalData,
            ActionOptionGate.RepairStaleAuthData => options.RepairStaleAuthData,
            ActionOptionGate.ApplyQualityGraphicsPreset => options.ApplyQualityGraphicsPreset,
            ActionOptionGate.ApplyDisplayPreferences => options.ApplyDisplayPreferences,
            ActionOptionGate.ApplyGtaVDisplayPreferences => options.ApplyGtaVDisplayPreferences,
            ActionOptionGate.ApplyGtaVGraphicsLaunchParameters => options.ApplyGtaVGraphicsLaunchParameters,
            ActionOptionGate.ApplyGtaVDisplayLaunchParameters => options.ApplyGtaVDisplayLaunchParameters,
            ActionOptionGate.ApplyGtaVRepairLaunchParameters => options.ApplyGtaVRepairLaunchParameters,
            ActionOptionGate.ToggleFullscreenOptimizations => options.ToggleFullscreenOptimizationsExperiment,
            ActionOptionGate.ToggleHags => options.ToggleHagsExperiment,
            ActionOptionGate.GuideDriverReinstall => options.GuideDriverReinstall,
            ActionOptionGate.AdjustPciExpressPowerManagement => options.AdjustPciExpressPowerManagement,
            _ => throw new ArgumentOutOfRangeException(nameof(gate), gate, "Unknown option gate value.")
        };
    }

    private static void ValidateRequest(OptimizationPlanRequestDto request)
    {
        if (!Enum.IsDefined(request.Profile))
        {
            throw new ArgumentOutOfRangeException(nameof(request.Profile), request.Profile, "Unknown optimization profile value.");
        }

        if (!Enum.IsDefined(request.Edition))
        {
            throw new ArgumentOutOfRangeException(nameof(request.Edition), request.Edition, "Unknown FiveM edition value.");
        }

        ArgumentNullException.ThrowIfNull(request.Options);

        if (!Enum.IsDefined(request.Options.ServerCacheRepair))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.Options.ServerCacheRepair),
                request.Options.ServerCacheRepair,
                "Unknown cache repair policy value.");
        }

        ValidateRange(
            request.Options.TemporaryFileMinimumAgeDays,
            minimum: 1,
            maximum: 30,
            nameof(request.Options.TemporaryFileMinimumAgeDays));
        ValidateRange(
            request.Options.DiagnosticRetentionDays,
            minimum: 1,
            maximum: 365,
            nameof(request.Options.DiagnosticRetentionDays));
        ValidateRange(
            request.Options.ServerCacheThresholdGiB,
            minimum: 1,
            maximum: 256,
            nameof(request.Options.ServerCacheThresholdGiB));
    }

    private static void ValidateRange(int value, int minimum, int maximum, string parameterName)
    {
        if (value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"Value must be between {minimum} and {maximum}.");
        }
    }
}
