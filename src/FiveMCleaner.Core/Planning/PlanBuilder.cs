using FiveMCleaner.Contracts;
using FiveMCleaner.Core.Catalog;

namespace FiveMCleaner.Core.Planning;

public sealed class PlanBuilder : IPlanBuilder
{
    private readonly ActionCatalog _catalog;
    private readonly TimeProvider _timeProvider;

    public PlanBuilder()
        : this(ActionCatalog.Current, TimeProvider.System)
    {
    }

    public PlanBuilder(ActionCatalog catalog, TimeProvider timeProvider)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public OptimizationPlanDto Build(OptimizationPlanRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        var blocks = CreateBlocks(request.Edition);
        if (blocks.Count > 0)
        {
            return CreatePlan(request, [], blocks, []);
        }

        var selectedDefinitions = _catalog.Actions
            .Where(action => action.Supports(request.Profile))
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
        return CreatePlan(request, plannedActions, [], notices);
    }

    private OptimizationPlanDto CreatePlan(
        OptimizationPlanRequestDto request,
        IReadOnlyList<PlannedActionDto> actions,
        IReadOnlyList<PlanBlockDto> blocks,
        IReadOnlyList<PlanNoticeDto> notices)
    {
        var metadata = actions.Select(action => action.Metadata).ToArray();

        return new OptimizationPlanDto
        {
            PlanId = Guid.NewGuid(),
            SchemaVersion = ProductIdentity.PlanSchemaVersion,
            CatalogVersion = ActionCatalog.CurrentVersion,
            ProductName = ProductIdentity.Name,
            ProductSubtitle = ProductIdentity.Subtitle,
            CreatedAtUtc = _timeProvider.GetUtcNow(),
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

        if (request.Profile == OptimizationProfile.Aggressive)
        {
            notices.Add(new PlanNoticeDto
            {
                Code = "aggressive-prioritizes-performance",
                Severity = PlanNoticeSeverity.Warning,
                Message = "O perfil agressivo prioriza FPS e responsividade, reduzindo a qualidade visual."
            });
        }

        return notices;
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
