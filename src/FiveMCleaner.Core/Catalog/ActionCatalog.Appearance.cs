using FiveMCleaner.Contracts;

namespace FiveMCleaner.Core.Catalog;

public sealed partial class ActionCatalog
{
    private static IReadOnlyList<OptimizationActionDefinition> CreateAppearanceActions()
    {
        return
        [
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
}
