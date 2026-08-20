using FiveMCleaner.Windows.Infrastructure;

namespace FiveMCleaner.Windows.Actions;

internal sealed class GraphicsTargetProcessGuard
{
    private readonly string? gameRoot;
    private readonly GraphicsSettingsTarget target;
    private readonly IFiveMProcessInspector fiveMInspector;
    private readonly IGtaVProcessInspector gtaVInspector;

    public GraphicsTargetProcessGuard(
        GraphicsSettingsTarget target,
        string? gameRoot,
        IFiveMProcessInspector fiveMInspector,
        IGtaVProcessInspector gtaVInspector)
    {
        this.target = target;
        this.gameRoot = string.IsNullOrWhiteSpace(gameRoot) ? null : SafePath.Normalize(gameRoot);
        this.fiveMInspector = fiveMInspector ?? throw new ArgumentNullException(nameof(fiveMInspector));
        this.gtaVInspector = gtaVInspector ?? throw new ArgumentNullException(nameof(gtaVInspector));
    }

    public bool IsRunning()
    {
        return target == GraphicsSettingsTarget.FiveM
            ? fiveMInspector.IsRunningFrom(gameRoot!)
            : gtaVInspector.IsRunningFrom(gameRoot);
    }

    public void EnsureStopped(string fiveMMessage, string gtaVMessage)
    {
        if (IsRunning())
        {
            throw new InvalidOperationException(
                target == GraphicsSettingsTarget.FiveM ? fiveMMessage : gtaVMessage);
        }
    }
}
