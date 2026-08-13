using FiveMCleaner.Contracts;
using FiveMCleaner.Core.Catalog;
using Xunit;

namespace FiveMCleaner.Tests.Core;

/// <summary>
/// Guards the single comparison the elevated broker and the Windows catalog
/// both rely on to reject a plan whose metadata drifted from the local catalog.
/// </summary>
public sealed class ActionMetadataContractTests
{
    [Fact]
    public void MatchesExactly_AcceptsSeparatelyBuiltButIdenticalMetadata()
    {
        var left = Metadata();
        var right = Metadata();

        Assert.True(left.MatchesExactly(right));
    }

    [Fact]
    public void RecordEquality_IsNotSufficient_WhichIsWhyMatchesExactlyExists()
    {
        // The list members compare by reference, so two structurally identical
        // instances are not equal. A boundary that trusted == would accept a
        // tampered plan or reject a legitimate one.
        Assert.NotEqual(Metadata(), Metadata());
        Assert.True(Metadata().MatchesExactly(Metadata()));
    }

    [Theory]
    [MemberData(nameof(DivergentMetadata))]
    public void MatchesExactly_RejectsAnyDivergentMember(ActionMetadataDto tampered)
    {
        Assert.False(tampered.MatchesExactly(Metadata()));
        Assert.False(Metadata().MatchesExactly(tampered));
    }

    [Fact]
    public void EveryCatalogAction_CarriesRiskReversibilityAndPrerequisitesAcrossSerialization()
    {
        foreach (var definition in ActionCatalog.Current.Actions)
        {
            var metadata = definition.ToMetadata();
            var plan = FiveMCleanerJson.SerializePlan(PlanWith(metadata));
            var restored = FiveMCleanerJson.DeserializePlan(plan).Actions[0].Metadata;

            Assert.True(
                metadata.MatchesExactly(restored),
                $"Metadata for '{definition.Id}' did not survive the plan boundary.");
            Assert.Equal(definition.Risk, restored.Risk);
            Assert.Equal(definition.Reversibility, restored.Reversibility);
            Assert.Equal(definition.Prerequisites, restored.Prerequisites);
        }
    }

    public static TheoryData<ActionMetadataDto> DivergentMetadata()
    {
        var baseline = Metadata();

        // Deliberately covers the members the broker used to ignore: a plan
        // could previously be tampered with in any of these and still pass.
        return
        [
            baseline with { Id = "another.action" },
            baseline with { Version = baseline.Version + 1 },
            baseline with { Name = baseline.Name + "!" },
            baseline with { Description = baseline.Description + "!" },
            baseline with { Category = ActionCategory.Storage },
            baseline with { SupportedProfiles = [OptimizationProfile.Light] },
            baseline with { Risk = ActionRisk.High },
            baseline with { Reversibility = ActionReversibility.Irreversible },
            baseline with { RequiredPrivilege = RequiredPrivilege.StandardUser },
            baseline with { RequiresFiveMStopped = !baseline.RequiresFiveMStopped },
            baseline with { RequiresAcPower = !baseline.RequiresAcPower },
            baseline with { RequiresRestart = !baseline.RequiresRestart },
            baseline with { ProgressWeight = baseline.ProgressWeight + 1 },
            baseline with { ExpectedImpact = baseline.ExpectedImpact + "!" },
            baseline with { Prerequisites = ["another.action"] },
            baseline with { IsCritical = !baseline.IsCritical },
            baseline with { AttemptWithoutElevationFirst = !baseline.AttemptWithoutElevationFirst },
            baseline with { SupportedWindows = SupportedWindowsVersions.Windows11 },
            baseline with { DetectionSummary = baseline.DetectionSummary + "!" },
            baseline with { ConfirmationSummary = baseline.ConfirmationSummary + "!" },
            baseline with { UndoSummary = baseline.UndoSummary + "!" },
            baseline with { RiskLimitations = baseline.RiskLimitations + "!" }
        ];
    }

    private static ActionMetadataDto Metadata()
    {
        return ActionCatalog.Current
            .GetRequired(OptimizationActionIds.EnableSessionPerformancePowerPlan)
            .ToMetadata();
    }

    private static OptimizationPlanDto PlanWith(ActionMetadataDto metadata)
    {
        return new OptimizationPlanDto
        {
            PlanId = Guid.NewGuid(),
            SchemaVersion = ProductIdentity.PlanSchemaVersion,
            CatalogVersion = ActionCatalog.CurrentVersion,
            ProductName = ProductIdentity.Name,
            ProductSubtitle = ProductIdentity.Subtitle,
            CreatedAtUtc = DateTimeOffset.UnixEpoch,
            Profile = OptimizationProfile.Balanced,
            Edition = FiveMEdition.Legacy,
            Options = new OptimizationOptionsDto(),
            IsExecutable = true,
            RequiresElevation = false,
            ContainsNonReversibleActions = false,
            MaximumRisk = metadata.Risk,
            Actions = [new PlannedActionDto { Sequence = 1, Metadata = metadata }],
            Blocks = [],
            Notices = []
        };
    }
}
