namespace FiveMCleaner.App.Services;

/// <summary>
/// Separates the developer's own machine from an end user's installation for
/// remote reporting purposes (Sentry environment tag) — never affects
/// product behavior, safety invariants, or the optimization engine.
/// </summary>
public enum AppRuntimeEnvironment
{
    Development,
    Production
}

/// <summary>
/// Resolves which runtime environment this process is running in. Pure and
/// dependency-free (the environment variable reader is injectable for
/// testing) so it never touches the network, disk beyond what the caller
/// passes in, or any remote service.
/// </summary>
public static class AppEnvironment
{
    public const string EnvironmentVariableName = "FIVEMCLEANER_ENVIRONMENT";

    /// <summary>
    /// Resolution order: an explicit <c>FIVEMCLEANER_ENVIRONMENT</c>
    /// environment variable ("Development" or "Production", case
    /// insensitive) always wins — this is what
    /// <c>scripts/Start-DevelopmentApp.ps1</c> sets so the development
    /// shortcut always reports as Development. Without it, a Debug build
    /// resolves to Development and a Release build resolves to Production —
    /// a safe default, since an installed end-user copy is always Release
    /// and never sets the variable.
    /// </summary>
    public static AppRuntimeEnvironment Resolve(Func<string, string?>? environmentVariableReader = null)
    {
        var reader = environmentVariableReader ?? Environment.GetEnvironmentVariable;
        var value = reader(EnvironmentVariableName);
        if (string.Equals(value, "Development", StringComparison.OrdinalIgnoreCase))
        {
            return AppRuntimeEnvironment.Development;
        }

        if (string.Equals(value, "Production", StringComparison.OrdinalIgnoreCase))
        {
            return AppRuntimeEnvironment.Production;
        }

#if DEBUG
        return AppRuntimeEnvironment.Development;
#else
        return AppRuntimeEnvironment.Production;
#endif
    }
}
