using FiveMCleaner.Contracts;
using FiveMCleaner.Windows.Infrastructure;

namespace FiveMCleaner.Windows.Diagnosis;

public sealed record FiveMWindowsDiagnosis
{
    public required FiveMEdition Edition { get; init; }

    public required bool InstallationFound { get; init; }

    public required bool FiveMIsRunning { get; init; }

    public required bool LegacyGraphicsSettingsFound { get; init; }

    public required long ServerCacheBytes { get; init; }

    public required long DiagnosticBytes { get; init; }

    public required string InstallationRoot { get; init; }

    public required string AppRoot { get; init; }

    public required string ExecutablePath { get; init; }

    public required string GraphicsSettingsPath { get; init; }

    public required IReadOnlyList<string> Notices { get; init; }
}

public interface IFiveMWindowsDiagnosisService
{
    FiveMWindowsDiagnosis Diagnose();
}

public sealed class FiveMWindowsDiagnosisService : IFiveMWindowsDiagnosisService
{
    private readonly WindowsOptimizationEnvironment environment;
    private readonly IFiveMProcessInspector processInspector;
    private readonly SafeFileTree fileTree;

    public FiveMWindowsDiagnosisService()
        : this(
            WindowsOptimizationEnvironment.DetectDefault(),
            new WindowsFiveMProcessInspector(),
            new SafeFileTree())
    {
    }

    public FiveMWindowsDiagnosisService(
        WindowsOptimizationEnvironment environment,
        IFiveMProcessInspector processInspector,
        SafeFileTree fileTree)
    {
        this.environment = environment ?? throw new ArgumentNullException(nameof(environment));
        this.processInspector = processInspector
            ?? throw new ArgumentNullException(nameof(processInspector));
        this.fileTree = fileTree ?? throw new ArgumentNullException(nameof(fileTree));
    }

    public FiveMWindowsDiagnosis Diagnose()
    {
        var notices = new List<string>();
        var executableFound = File.Exists(environment.FiveMExecutablePath);
        var appRootFound = Directory.Exists(environment.FiveMAppRoot);
        var installationFound = executableFound && appRootFound;
        if (executableFound != appRootFound)
        {
            notices.Add(
                "A instalação padrão do FiveM está incompleta; nenhuma otimização deve ser executada.");
        }

        var edition = installationFound
            ? FiveMEdition.Legacy
            : FiveMEdition.Unknown;
        var cacheBytes = installationFound
            ? MeasureAllowlistedDirectories(
                environment.FiveMAppRoot,
                [
                    Path.Combine("data", "server-cache"),
                    Path.Combine("data", "server-cache-priv")
                ],
                notices)
            : 0;
        var diagnosticBytes = installationFound
            ? MeasureAllowlistedDirectories(
                environment.FiveMAppRoot,
                ["logs", "crashes"],
                notices)
            : 0;

        if (installationFound && !File.Exists(environment.LegacyGraphicsSettingsPath))
        {
            notices.Add(
                "O arquivo gta5_settings.xml ainda não existe; abra o FiveM uma vez antes de aplicar gráficos.");
        }

        return new FiveMWindowsDiagnosis
        {
            Edition = edition,
            InstallationFound = installationFound,
            FiveMIsRunning = installationFound
                && processInspector.IsRunningFrom(environment.FiveMInstallationRoot),
            LegacyGraphicsSettingsFound = File.Exists(environment.LegacyGraphicsSettingsPath),
            ServerCacheBytes = cacheBytes,
            DiagnosticBytes = diagnosticBytes,
            InstallationRoot = Path.GetFullPath(environment.FiveMInstallationRoot),
            AppRoot = Path.GetFullPath(environment.FiveMAppRoot),
            ExecutablePath = Path.GetFullPath(environment.FiveMExecutablePath),
            GraphicsSettingsPath = Path.GetFullPath(environment.LegacyGraphicsSettingsPath),
            Notices = notices
        };
    }

    private long MeasureAllowlistedDirectories(
        string root,
        IReadOnlyList<string> relativePaths,
        ICollection<string> notices)
    {
        long total = 0;
        foreach (var relativePath in relativePaths)
        {
            var directory = SafePath.EnsureDescendant(root, Path.Combine(root, relativePath));
            if (!Directory.Exists(directory))
            {
                continue;
            }

            try
            {
                var result = fileTree.EnumerateFiles(directory, _ => true);
                total = checked(total + result.Files.Sum(file => file.Length));
                if (result.SkippedReparsePoints.Count > 0)
                {
                    notices.Add(
                        $"{result.SkippedReparsePoints.Count} link(s) ou junction(s) foram ignorados em {relativePath}.");
                }

                if (result.SkippedInaccessiblePaths.Count > 0)
                {
                    notices.Add(
                        $"{result.SkippedInaccessiblePaths.Count} caminho(s) inacessível(is) foram preservados em {relativePath}.");
                }
            }
            catch (IOException exception)
            {
                notices.Add($"{relativePath} não foi medido com segurança: {exception.Message}");
            }
            catch (UnauthorizedAccessException exception)
            {
                notices.Add($"{relativePath} não foi medido por falta de acesso: {exception.Message}");
            }
        }

        return total;
    }
}
