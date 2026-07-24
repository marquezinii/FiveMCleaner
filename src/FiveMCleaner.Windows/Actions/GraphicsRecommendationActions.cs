using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using FiveMCleaner.Contracts;
using FiveMCleaner.Core.Catalog;
using FiveMCleaner.Windows.Infrastructure;

namespace FiveMCleaner.Windows.Actions;

/// <summary>
/// Read-only recommendation combining already-existing hardware diagnostics
/// into a single suggestion of which graphics preset (FPS, Equilibrado or
/// Qualidade) fits the detected hardware best. Never applies anything by
/// itself — the user still picks and applies a preset manually, matching the
/// product's rule that only the mode/preset choice belongs to the user.
/// </summary>
public sealed class GraphicsPresetRecommendationAction : WindowsOptimizationAction
{
    private const long GiB = 1024L * 1024L * 1024L;
    private readonly IGpuDetailsInspector gpuDetails;
    private readonly ICpuInspector cpu;
    private readonly IRamDetailsInspector ramDetails;
    private readonly IDisplayConfigurationInspector displayConfiguration;

    public GraphicsPresetRecommendationAction(
        IGpuDetailsInspector gpuDetails,
        ICpuInspector cpu,
        IRamDetailsInspector ramDetails,
        IDisplayConfigurationInspector displayConfiguration)
    {
        this.gpuDetails = gpuDetails ?? throw new ArgumentNullException(nameof(gpuDetails));
        this.cpu = cpu ?? throw new ArgumentNullException(nameof(cpu));
        this.ramDetails = ramDetails ?? throw new ArgumentNullException(nameof(ramDetails));
        this.displayConfiguration = displayConfiguration
            ?? throw new ArgumentNullException(nameof(displayConfiguration));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.RecommendGraphicsPreset);

    public override Task<WindowsActionApplyResult> ApplyAsync(
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var gpus = gpuDetails.GetSnapshot();
        var cpuSnapshot = cpu.GetSnapshot();
        var ram = ramDetails.GetSnapshot();
        var display = displayConfiguration.GetSnapshot();

        if (gpus.Count == 0 || cpuSnapshot is null || ram.Modules.Count == 0)
        {
            return Task.FromResult(WindowsActionApplyResult.NoChange(
                "Não foi possível ler hardware suficiente para recomendar um preset agora; use os diagnósticos de GPU/CPU/RAM individualmente."));
        }

        return Task.FromResult(WindowsActionApplyResult.NoChange(
            Recommend(gpus, cpuSnapshot, ram, display)));
    }

    public override Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    internal static string Recommend(
        IReadOnlyList<GpuAdapterDetails> gpus,
        CpuSnapshot cpuSnapshot,
        RamDetailsSnapshot ram,
        DisplayConfigurationSnapshot? display)
    {
        var bestVramBytes = gpus.Max(gpu => gpu.VramBytes ?? 0);
        var hasDedicatedGpu = gpus.Any(gpu => gpu.KindGuess == GpuKindGuess.LikelyDiscrete);
        var vramGiB = bestVramBytes / (double)GiB;
        var ramGiB = ram.Modules.Sum(module => module.CapacityBytes) / (double)GiB;
        var refreshHz = display?.CurrentRefreshHz ?? 60;

        var isWeak = !hasDedicatedGpu
            || vramGiB < 4
            || cpuSnapshot.LogicalThreads <= 4
            || ramGiB < 8;
        if (isWeak)
        {
            return $"Recomendação: preset FPS. Hardware detectado (GPU {(hasDedicatedGpu ? "dedicada" : "integrada")}, "
                + $"~{vramGiB:0.#} GB de VRAM, {cpuSnapshot.LogicalThreads} threads lógicas, ~{ramGiB:0.#} GB de RAM) "
                + "sugere priorizar FPS e responsividade em vez de qualidade visual.";
        }

        var isStrong = vramGiB >= 8 && cpuSnapshot.LogicalThreads >= 8 && ramGiB >= 16;
        if (isStrong && refreshHz <= 75)
        {
            return $"Recomendação: preset Qualidade. Hardware com boa folga (GPU dedicada, ~{vramGiB:0.#} GB de VRAM, "
                + $"{cpuSnapshot.LogicalThreads} threads lógicas, ~{ramGiB:0.#} GB de RAM) e monitor de {refreshHz} Hz "
                + "comportam elevar a qualidade visual sem comprometer o FPS de forma perceptível.";
        }

        return $"Recomendação: preset Equilibrado. Hardware (GPU dedicada, ~{vramGiB:0.#} GB de VRAM, "
            + $"{cpuSnapshot.LogicalThreads} threads lógicas, ~{ramGiB:0.#} GB de RAM, monitor de {refreshHz} Hz) "
            + "sugere equilibrar qualidade visual com estabilidade de quadros. Esta é uma heurística com base no "
            + "hardware local; não considera o servidor utilizado nem um benchmark ainda não executado.";
    }
}

/// <summary>
/// Compares the texture quality already configured in the FiveM graphics
/// file with the GPU's detected VRAM, using a conservative, documented
/// threshold table. Never reads real in-game VRAM usage (that data is not
/// available without hooking the game).
/// </summary>
public sealed class TextureVramFitDiagnosisAction : WindowsOptimizationAction
{
    private const long GiB = 1024L * 1024L * 1024L;
    private readonly string settingsPath;
    private readonly IGpuDetailsInspector gpuDetails;

    public TextureVramFitDiagnosisAction(string settingsPath, IGpuDetailsInspector gpuDetails)
    {
        this.settingsPath = Path.GetFullPath(settingsPath);
        this.gpuDetails = gpuDetails ?? throw new ArgumentNullException(nameof(gpuDetails));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnoseTextureVramFit);

    public override Task<WindowsActionApplyResult> ApplyAsync(
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(settingsPath))
        {
            return Task.FromResult(WindowsActionApplyResult.NoChange(
                "O arquivo gráfico ainda não existe; nada para comparar com a VRAM."));
        }

        int? textureQuality;
        try
        {
            textureQuality = ReadTextureQuality(settingsPath);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException or XmlException)
        {
            return Task.FromResult(WindowsActionApplyResult.NoChange(
                $"Não foi possível ler a qualidade de textura configurada ({exception.Message})."));
        }

        if (textureQuality is null)
        {
            return Task.FromResult(WindowsActionApplyResult.NoChange(
                "A opção de qualidade de textura não foi encontrada no arquivo gráfico."));
        }

        var gpus = gpuDetails.GetSnapshot();
        var bestVramBytes = gpus.Count == 0 ? (long?)null : gpus.Max(gpu => gpu.VramBytes ?? 0);
        if (bestVramBytes is null or 0)
        {
            return Task.FromResult(WindowsActionApplyResult.NoChange(
                "Não foi possível ler a VRAM da GPU para comparar com a qualidade de textura configurada."));
        }

        var vramGiB = bestVramBytes.Value / (double)GiB;
        var requiredGiB = textureQuality.Value switch
        {
            <= 0 => 0d,
            1 => 2d,
            2 => 4d,
            3 => 6d,
            _ => 8d
        };

        var message = vramGiB < requiredGiB
            ? $"A qualidade de textura configurada (nível {textureQuality}) costuma exigir por volta de {requiredGiB:0} GB de VRAM, "
                + $"acima dos ~{vramGiB:0.#} GB detectados na GPU. Isso é uma estimativa por limiar, não uma medição real "
                + "de uso durante o jogo, mas pode explicar stutter ou quedas de textura."
            : $"A qualidade de textura configurada (nível {textureQuality}) é compatível com os ~{vramGiB:0.#} GB de VRAM detectados.";
        return Task.FromResult(WindowsActionApplyResult.NoChange(message));
    }

    public override Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private static int? ReadTextureQuality(string path)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 4 * 1024 * 1024
        };
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > 4 * 1024 * 1024)
        {
            throw new InvalidDataException("O arquivo gráfico excede o limite seguro de 4 MB.");
        }

        using var reader = XmlReader.Create(stream, settings);
        var document = XDocument.Load(reader);
        var node = document
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName.Equals("TextureQuality", StringComparison.Ordinal));
        var value = node?.Attribute("value")?.Value;
        return value is not null
            && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
    }
}
