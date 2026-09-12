using Ralven.Broker;
using Ralven.Contracts;
using Ralven.Core.Planning;
using Xunit;

namespace Ralven.Tests.Broker;

public sealed class PlanValidatorScopeTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(FiveMEdition.Unknown)]
    [InlineData(FiveMEdition.Legacy)]
    [InlineData(FiveMEdition.Enhanced)]
    public void Validate_AcceptsCanonicalGeneralWindowsAdministratorPlan(FiveMEdition edition)
    {
        var plan = BuildGeneralPlan(edition);

        var validated = new PlanValidator(new FixedTimeProvider(Now)).Validate(plan);

        Assert.Equal(plan, validated.Plan);
        Assert.NotEmpty(validated.AdministratorActions);
    }

    [Fact]
    public void Validate_RejectsTamperedScope()
    {
        var plan = BuildGeneralPlan(FiveMEdition.Legacy);

        var exception = Assert.Throws<BrokerRequestException>(() =>
            new PlanValidator(new FixedTimeProvider(Now)).Validate(
                plan with { Scope = OptimizationScope.FiveMLegacy }));

        Assert.Equal("plan-actions-mismatch", exception.ErrorCode);
    }

    [Fact]
    public void Validate_RejectsFiveMLegacyScopeForEnhancedEdition()
    {
        var plan = PlanBuilder.Build(
            new OptimizationPlanRequestDto
            {
                Scope = OptimizationScope.FiveMLegacy,
                Profile = OptimizationProfile.Balanced,
                Edition = FiveMEdition.Legacy
            },
            PlanBuildContext.New(new FixedTimeProvider(Now)));

        var exception = Assert.Throws<BrokerRequestException>(() =>
            new PlanValidator(new FixedTimeProvider(Now)).Validate(
                plan with { Edition = FiveMEdition.Enhanced }));

        Assert.Equal("plan-scope-unsupported", exception.ErrorCode);
    }

    [Fact]
    public void Validate_RebuildsPersonalOptionsAtThePrivilegedBoundary()
    {
        var plan = PlanBuilder.Build(new OptimizationPlanRequestDto
        {
            Scope = OptimizationScope.GeneralWindows,
            Profile = OptimizationProfile.Aggressive,
            Edition = FiveMEdition.Unknown,
            PersonalPreferences = new() { AllowPerformancePower = true }
        }, PlanBuildContext.New(new FixedTimeProvider(Now)));
        var validator = new PlanValidator(new FixedTimeProvider(Now));

        Assert.Equal(plan, validator.Validate(plan).Plan);
        Assert.Throws<BrokerRequestException>(() => validator.Validate(plan with
        {
            Options = plan.Options with { TemporaryFileMinimumAgeDays = 29 }
        }));
    }

    private static OptimizationPlanDto BuildGeneralPlan(FiveMEdition edition) =>
        PlanBuilder.Build(
            new OptimizationPlanRequestDto
            {
                Scope = OptimizationScope.GeneralWindows,
                Profile = OptimizationProfile.Balanced,
                Edition = edition
            },
            PlanBuildContext.New(new FixedTimeProvider(Now)));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
