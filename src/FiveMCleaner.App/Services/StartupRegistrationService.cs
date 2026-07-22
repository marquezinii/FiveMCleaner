using System.IO;
using Microsoft.Win32;
using FiveMCleaner.Contracts;

namespace FiveMCleaner.App.Services;

public interface IStartupRegistrationService
{
    bool IsEnabled();

    void SetEnabled(bool enabled);
}

public sealed class WindowsStartupRegistrationService : IStartupRegistrationService
{
    internal const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly string executablePath;
    private readonly Func<RegistryKey> currentUserFactory;

    public WindowsStartupRegistrationService(string? executablePath = null)
        : this(
            executablePath ?? Environment.ProcessPath
                ?? throw new InvalidOperationException("O caminho do FiveMCleaner não está disponível."),
            () => Registry.CurrentUser)
    {
    }

    internal WindowsStartupRegistrationService(
        string executablePath,
        Func<RegistryKey> currentUserFactory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (!Path.IsPathFullyQualified(executablePath)
            || !Path.GetExtension(executablePath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("O executável de inicialização precisa ser um caminho absoluto .exe.", nameof(executablePath));
        }

        this.executablePath = Path.GetFullPath(executablePath);

        this.currentUserFactory = currentUserFactory
            ?? throw new ArgumentNullException(nameof(currentUserFactory));
    }

    public bool IsEnabled()
    {
        using var currentUser = currentUserFactory();
        using var runKey = currentUser.OpenSubKey(RunSubKey, writable: false);
        return runKey?.GetValue(ProductIdentity.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)
            is string value
            && value.Equals(BuildCommand(), StringComparison.OrdinalIgnoreCase);
    }

    public void SetEnabled(bool enabled)
    {
        using var currentUser = currentUserFactory();
        if (enabled)
        {
            using var runKey = currentUser.CreateSubKey(RunSubKey, writable: true)
                ?? throw new IOException("Não foi possível abrir a inicialização do usuário atual.");
            runKey.SetValue(ProductIdentity.Name, BuildCommand(), RegistryValueKind.String);
            return;
        }

        using var existing = currentUser.OpenSubKey(RunSubKey, writable: true);
        existing?.DeleteValue(ProductIdentity.Name, throwOnMissingValue: false);
    }

    internal string BuildCommand() => $"\"{executablePath}\" --startup";
}

public sealed class SessionStartupRegistrationService : IStartupRegistrationService
{
    private bool enabled;

    public bool IsEnabled() => enabled;

    public void SetEnabled(bool enabled) => this.enabled = enabled;
}
