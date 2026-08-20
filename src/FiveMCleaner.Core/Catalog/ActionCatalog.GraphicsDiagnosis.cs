using FiveMCleaner.Contracts;

namespace FiveMCleaner.Core.Catalog;

public sealed partial class ActionCatalog
{
    private static IReadOnlyList<OptimizationActionDefinition> CreateGraphicsDiagnosisActions()
    {
        return
        [
            Define(
                OptimizationActionIds.RecommendGraphicsPreset,
                "Recomendar preset gráfico",
                "Analisa GPU, VRAM, CPU, RAM e a taxa de atualização do monitor para sugerir qual preset gráfico (FPS, Equilibrado ou Qualidade) combina melhor com o hardware, sem aplicar nada sozinho.",
                ActionCategory.FiveMGraphics,
                ActionRisk.Informational,
                ActionReversibility.ReadOnly,
                RequiredPrivilege.StandardUser,
                AllProfiles,
                requiresFiveMStopped: false,
                progressWeight: 3,
                expectedImpact: "Ajuda a escolher entre os presets gráficos existentes sem precisar testar cada um manualmente.",
                ActionOptionGate.Always,
                detectionSummary: "Combina os diagnósticos de GPU/VRAM, CPU, RAM e monitor já existentes em uma recomendação simples de preset.",
                confirmationSummary: "Sempre é concluída com uma mensagem informativa; nunca falha por si só.",
                undoSummary: "Somente leitura: apenas recomenda; o usuário decide qual preset aplicar manualmente.",
                riskLimitations: "É uma heurística baseada em hardware local; não considera o servidor utilizado nem resultado de benchmark ainda não executado."),
            Define(
                OptimizationActionIds.DiagnoseTextureVramFit,
                "Diagnosticar textura configurada vs. VRAM disponível",
                "Compara a qualidade de textura configurada no FiveM/GTA V com a VRAM detectada da GPU e avisa quando a configuração provavelmente excede a VRAM disponível.",
                ActionCategory.FiveMGraphics,
                ActionRisk.Informational,
                ActionReversibility.ReadOnly,
                RequiredPrivilege.StandardUser,
                AllProfiles,
                requiresFiveMStopped: false,
                progressWeight: 2,
                expectedImpact: "Ajuda a identificar stutter e falta de memória de vídeo causados por textura acima do que a GPU comporta.",
                ActionOptionGate.Always,
                detectionSummary: "Lê TextureQuality do arquivo gráfico existente e a VRAM já lida pelo diagnóstico de GPU, aplicando um limiar conservador documentado.",
                confirmationSummary: "Sempre é concluída com uma mensagem informativa; nunca falha por si só.",
                undoSummary: "Somente leitura: não altera nenhuma configuração gráfica.",
                riskLimitations: "É uma estimativa por limiar de VRAM, não uma medição real de uso de memória durante o jogo."),
            Define(
                OptimizationActionIds.DiagnoseGtaVLaunchParameters,
                "Diagnosticar parâmetros de inicialização do GTA V",
                "Lê o commandline.txt do GTA V Legacy standalone (nunca do FiveM, que ignora esse arquivo) e avisa quando parâmetros de reparo ficaram ativos permanentemente.",
                ActionCategory.FiveMGraphics,
                ActionRisk.Informational,
                ActionReversibility.ReadOnly,
                RequiredPrivilege.StandardUser,
                AllProfiles,
                requiresFiveMStopped: false,
                progressWeight: 2,
                expectedImpact: "Evita esquecer um parâmetro de reparo (-safemode, -useMinimumSettings) ligado além do necessário.",
                ActionOptionGate.Always,
                detectionSummary: "Lê o commandline.txt na pasta de instalação do GTA V standalone, se existir.",
                confirmationSummary: "Sempre é concluída com uma mensagem informativa; nunca falha por si só.",
                undoSummary: "Somente leitura: não altera o arquivo.",
                riskLimitations: "O FiveM ignora commandline.txt do GTA (bloqueio documentado do próprio FiveM); este diagnóstico é relevante apenas para quem também joga o GTA V standalone.")
        ];
    }
}
