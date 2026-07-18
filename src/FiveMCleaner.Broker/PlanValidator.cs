using System.Diagnostics.CodeAnalysis;
using FiveMCleaner.Contracts;
using FiveMCleaner.Core.Catalog;
using FiveMCleaner.Core.Planning;

namespace FiveMCleaner.Broker;

internal sealed record ValidatedPlan(
    OptimizationPlanDto Plan,
    IReadOnlyList<PlannedActionDto> AdministratorActions);

internal sealed class PlanValidator
{
    private static readonly TimeSpan MaximumPlanAge = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan MaximumFutureSkew = TimeSpan.FromMinutes(5);
    private readonly TimeProvider timeProvider;

    public PlanValidator()
        : this(TimeProvider.System)
    {
    }

    internal PlanValidator(TimeProvider timeProvider)
    {
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public ValidatedPlan Validate(OptimizationPlanDto plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        Require(plan.PlanId != Guid.Empty, "plan-id-invalid", "The plan ID cannot be empty.");
        Require(
            plan.SchemaVersion == ProductIdentity.PlanSchemaVersion,
            "plan-schema-unsupported",
            "The plan schema version is not supported.");
        Require(
            plan.CatalogVersion == ActionCatalog.CurrentVersion,
            "plan-catalog-unsupported",
            "The action catalog version is not supported.");
        Require(
            string.Equals(plan.ProductName, ProductIdentity.Name, StringComparison.Ordinal)
                && string.Equals(plan.ProductSubtitle, ProductIdentity.Subtitle, StringComparison.Ordinal),
            "plan-product-mismatch",
            "The plan product identity is invalid.");
        Require(
            plan.Edition == FiveMEdition.Legacy,
            "plan-edition-unsupported",
            "Only FiveM Legacy plans are supported by this broker.");
        Require(plan.IsExecutable, "plan-not-executable", "The plan is not executable.");
        Require(plan.Blocks is { Count: 0 }, "plan-is-blocked", "A blocked plan cannot be executed.");
        Require(plan.Actions is not null, "plan-actions-missing", "The plan action list is missing.");
        Require(plan.Notices is not null, "plan-notices-missing", "The plan notice list is missing.");
        Require(plan.Options is not null, "plan-options-missing", "The plan options are missing.");

        var now = timeProvider.GetUtcNow();
        Require(
            plan.CreatedAtUtc.Offset == TimeSpan.Zero
                && plan.CreatedAtUtc >= now - MaximumPlanAge
                && plan.CreatedAtUtc <= now + MaximumFutureSkew,
            "plan-expired",
            "The plan timestamp is outside the accepted execution window.");

        OptimizationPlanDto expected;
        try
        {
            expected = new PlanBuilder(ActionCatalog.Current, timeProvider).Build(
                new OptimizationPlanRequestDto
                {
                    Profile = plan.Profile,
                    Edition = plan.Edition,
                    Options = plan.Options
                });
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw new BrokerRequestException(
                "plan-options-invalid",
                "The plan options are invalid.",
                exception);
        }

        Require(expected.IsExecutable, "plan-rebuild-failed", "The plan cannot be rebuilt safely.");
        Require(
            plan.RequiresElevation == expected.RequiresElevation
                && plan.ContainsNonReversibleActions == expected.ContainsNonReversibleActions
                && plan.MaximumRisk == expected.MaximumRisk,
            "plan-summary-mismatch",
            "The plan summary does not match the current catalog.");
        Require(
            ActionsMatch(plan.Actions, expected.Actions),
            "plan-actions-mismatch",
            "The plan actions or metadata do not match the current catalog.");
        Require(
            NoticesMatch(plan.Notices, expected.Notices),
            "plan-notices-mismatch",
            "The plan notices do not match the current catalog.");

        var administratorActions = plan.Actions
            .Where(action => action.Metadata.RequiredPrivilege == RequiredPrivilege.Administrator)
            .ToArray();
        Require(
            administratorActions.Length > 0 && plan.RequiresElevation,
            "plan-has-no-administrator-actions",
            "The plan does not contain an administrator action.");

        return new ValidatedPlan(plan, administratorActions);
    }

    private static bool ActionsMatch(
        IReadOnlyList<PlannedActionDto> actual,
        IReadOnlyList<PlannedActionDto> expected)
    {
        if (actual.Count != expected.Count)
        {
            return false;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < actual.Count; index++)
        {
            var actualAction = actual[index];
            var expectedAction = expected[index];
            if (actualAction is null
                || actualAction.Metadata is null
                || actualAction.Sequence != index + 1
                || expectedAction.Sequence != actualAction.Sequence
                || !ids.Add(actualAction.Metadata.Id)
                || !MetadataMatches(actualAction.Metadata, expectedAction.Metadata))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MetadataMatches(ActionMetadataDto actual, ActionMetadataDto expected)
    {
        return string.Equals(actual.Id, expected.Id, StringComparison.Ordinal)
            && actual.Version == expected.Version
            && string.Equals(actual.Name, expected.Name, StringComparison.Ordinal)
            && string.Equals(actual.Description, expected.Description, StringComparison.Ordinal)
            && actual.Category == expected.Category
            && actual.SupportedProfiles is not null
            && actual.SupportedProfiles.SequenceEqual(expected.SupportedProfiles)
            && actual.Risk == expected.Risk
            && actual.Reversibility == expected.Reversibility
            && actual.RequiredPrivilege == expected.RequiredPrivilege
            && actual.RequiresFiveMStopped == expected.RequiresFiveMStopped
            && actual.RequiresAcPower == expected.RequiresAcPower
            && actual.RequiresRestart == expected.RequiresRestart
            && actual.ProgressWeight == expected.ProgressWeight
            && string.Equals(actual.ExpectedImpact, expected.ExpectedImpact, StringComparison.Ordinal);
    }

    private static bool NoticesMatch(
        IReadOnlyList<PlanNoticeDto> actual,
        IReadOnlyList<PlanNoticeDto> expected)
    {
        if (actual.Count != expected.Count)
        {
            return false;
        }

        for (var index = 0; index < actual.Count; index++)
        {
            if (actual[index] is null
                || !string.Equals(actual[index].Code, expected[index].Code, StringComparison.Ordinal)
                || actual[index].Severity != expected[index].Severity
                || !string.Equals(actual[index].Message, expected[index].Message, StringComparison.Ordinal)
                || !string.Equals(actual[index].ActionId, expected[index].ActionId, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static void Require(
        [DoesNotReturnIf(false)] bool condition,
        string errorCode,
        string message)
    {
        if (!condition)
        {
            throw new BrokerRequestException(errorCode, message);
        }
    }
}
