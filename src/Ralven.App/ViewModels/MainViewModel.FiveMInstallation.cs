namespace Ralven.App.ViewModels;

public sealed partial class MainViewModel
{
    private string fiveMInstallationSelectionMessage = string.Empty;

    public bool HasManualFiveMInstallation => !string.IsNullOrWhiteSpace(manualFiveMInstallationRoot);

    public string FiveMInstallationSelectionMessage
    {
        get => fiveMInstallationSelectionMessage;
        private set => SetProperty(ref fiveMInstallationSelectionMessage, value);
    }

    public async Task SelectFiveMInstallationAsync(string? path)
    {
        if (!CanRefresh)
        {
            return;
        }

        var result = await service.SetManualFiveMInstallationAsync(path);
        if (!result.Succeeded)
        {
            FiveMInstallationSelectionMessage = localization.GetString("FiveMHub.Detection.Invalid");
            return;
        }

        manualFiveMInstallationRoot = result.Root;
        OnPropertyChanged(nameof(HasManualFiveMInstallation));
        FiveMInstallationSelectionMessage = result.Root is null
            ? localization.GetString("FiveMHub.Detection.Automatic")
            : localization.Format("FiveMHub.Detection.Manual", result.Root);
        await RefreshDiagnosticAsync();
    }
}
