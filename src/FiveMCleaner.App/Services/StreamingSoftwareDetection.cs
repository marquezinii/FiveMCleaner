namespace FiveMCleaner.App.Services;

/// <summary>
/// Aplicativos de transmissão que o FiveMCleaner reconhece localmente.
/// A lista é fechada de propósito para evitar classificar processos desconhecidos.
/// </summary>
public enum StreamingSoftwareKind
{
    ObsStudio,
    StreamlabsDesktop,
    TikTokLiveStudio
}

/// <summary>
/// Resultado somente leitura para um aplicativo de transmissão conhecido.
/// Processo em execução não significa que existe uma transmissão ao vivo.
/// </summary>
public sealed record StreamingSoftwareStatus(
    StreamingSoftwareKind Kind,
    string DisplayName,
    bool IsInstalled,
    bool IsProcessRunning)
{
    public bool IsDetected => IsInstalled || IsProcessRunning;
}

/// <summary>
/// Fotografia local dos sinais permitidos de software de transmissão.
/// Os indicadores de completude permitem que a UI trate acesso negado como
/// resultado parcial, sem confundir isso com ausência confirmada.
/// </summary>
public sealed record StreamingSoftwareSnapshot(
    IReadOnlyList<StreamingSoftwareStatus> Applications,
    DateTimeOffset ObservedAtUtc,
    bool ProcessScanComplete,
    bool InstallationScanComplete)
{
    public bool HasKnownSoftwareInstalled => Applications.Any(item => item.IsInstalled);

    public bool HasKnownProcessRunning => Applications.Any(item => item.IsProcessRunning);

    public bool IsPartial => !ProcessScanComplete || !InstallationScanComplete;
}
