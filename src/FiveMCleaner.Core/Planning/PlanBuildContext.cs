using FiveMCleaner.Contracts;
using FiveMCleaner.Core.Catalog;

namespace FiveMCleaner.Core.Planning;

/// <summary>
/// The inputs a plan needs that cannot be derived from the request itself:
/// its identity, its timestamp and the catalog it is planned against.
/// </summary>
/// <remarks>
/// Planning is a pure function of (request, context). Everything
/// non-deterministic — a fresh identifier, the current instant, the ambient
/// catalog — is resolved by the caller through <see cref="New"/> and handed in,
/// so <see cref="PlanBuilder.Build"/> stays reproducible and the validators
/// that rebuild a plan can pin these values instead of racing the clock.
/// </remarks>
public sealed record PlanBuildContext
{
    /// <summary>Identity of the plan being produced.</summary>
    public required Guid PlanId { get; init; }

    /// <summary>
    /// When the plan was produced. Expected to be UTC: the elevated broker
    /// rejects a plan whose timestamp carries a non-zero offset.
    /// </summary>
    public required DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>Catalog the plan is planned against.</summary>
    public ActionCatalog Catalog { get; init; } = ActionCatalog.Current;

    /// <summary>
    /// Resolves a context for a brand-new plan. This is the only place where
    /// planning touches a clock or generates an identifier.
    /// </summary>
    public static PlanBuildContext New(TimeProvider timeProvider, ActionCatalog? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        return new PlanBuildContext
        {
            PlanId = Guid.NewGuid(),
            CreatedAtUtc = timeProvider.GetUtcNow(),
            Catalog = catalog ?? ActionCatalog.Current
        };
    }

    /// <summary>
    /// Rebuilds the context of an existing plan so a validator can reproduce
    /// it deterministically instead of stamping it with a new identity.
    /// </summary>
    public static PlanBuildContext For(OptimizationPlanDto plan, ActionCatalog? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return new PlanBuildContext
        {
            PlanId = plan.PlanId,
            CreatedAtUtc = plan.CreatedAtUtc,
            Catalog = catalog ?? ActionCatalog.Current
        };
    }
}
