using FiveMCleaner.Contracts;

namespace FiveMCleaner.Core.Planning;

public interface IPlanBuilder
{
    OptimizationPlanDto Build(OptimizationPlanRequestDto request);
}
