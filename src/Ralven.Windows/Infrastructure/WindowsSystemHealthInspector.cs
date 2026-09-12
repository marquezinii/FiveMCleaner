using System.Runtime.InteropServices;
using Ralven.Contracts;
using Ralven.Windows.Diagnostics;

namespace Ralven.Windows.Infrastructure;

public enum WindowsSecurityHealthState
{
    Good,
    NotMonitored,
    Poor,
    Snoozed,
    Unavailable
}

public sealed record WindowsSecurityProviderHealth(
    WindowsSecurityHealthState State,
    int HResult,
    BugCode? BugCode = null)
{
    public bool IsAvailable => State != WindowsSecurityHealthState.Unavailable;
}

public sealed record WindowsSystemHealthSnapshot(
    WindowsSecurityProviderHealth Antivirus,
    WindowsSecurityProviderHealth Firewall,
    WindowsSecurityProviderHealth AutomaticUpdates,
    DateTimeOffset ObservedAtUtc)
{
    public bool IsPartial => !Antivirus.IsAvailable
        || !Firewall.IsAvailable
        || !AutomaticUpdates.IsAvailable;
}

public interface IWindowsSystemHealthInspector
{
    Task<WindowsSystemHealthSnapshot> InspectAsync(
        CancellationToken cancellationToken = default);
}

public sealed class WindowsSystemHealthInspector : IWindowsSystemHealthInspector
{
    private const int S_OK = 0;
    private readonly SecurityProviderHealthReader readHealth;

    public WindowsSystemHealthInspector()
        : this(NativeMethods.ReadHealth)
    {
    }

    internal WindowsSystemHealthInspector(SecurityProviderHealthReader readHealth)
    {
        this.readHealth = readHealth;
    }

    public Task<WindowsSystemHealthSnapshot> InspectAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => Inspect(cancellationToken), cancellationToken);
    }

    private WindowsSystemHealthSnapshot Inspect(CancellationToken cancellationToken)
    {
        var antivirus = Read(SecurityProvider.Antivirus, cancellationToken);
        var firewall = Read(SecurityProvider.Firewall, cancellationToken);
        var automaticUpdates = Read(SecurityProvider.AutoUpdateSettings, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return new WindowsSystemHealthSnapshot(
            antivirus,
            firewall,
            automaticUpdates,
            DateTimeOffset.UtcNow);
    }

    private WindowsSecurityProviderHealth Read(
        SecurityProvider provider,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var hResult = readHealth(provider, out var nativeHealth);
            if (hResult != S_OK)
            {
                return new WindowsSecurityProviderHealth(
                    WindowsSecurityHealthState.Unavailable,
                    hResult);
            }

            return new WindowsSecurityProviderHealth(Map(nativeHealth), hResult);
        }
        catch (Exception exception) when (exception is DllNotFoundException
            or EntryPointNotFoundException
            or BadImageFormatException)
        {
            return new WindowsSecurityProviderHealth(
                WindowsSecurityHealthState.Unavailable,
                exception.HResult,
                BugCodeClassifier.ClassifyException(exception, "security-health"));
        }
    }

    private static WindowsSecurityHealthState Map(NativeSecurityProviderHealth health) => health switch
    {
        NativeSecurityProviderHealth.Good => WindowsSecurityHealthState.Good,
        NativeSecurityProviderHealth.NotMonitored => WindowsSecurityHealthState.NotMonitored,
        NativeSecurityProviderHealth.Poor => WindowsSecurityHealthState.Poor,
        NativeSecurityProviderHealth.Snooze => WindowsSecurityHealthState.Snoozed,
        _ => WindowsSecurityHealthState.Unavailable
    };

    internal delegate int SecurityProviderHealthReader(
        SecurityProvider provider,
        out NativeSecurityProviderHealth health);

    internal enum SecurityProvider : uint
    {
        Firewall = 0x1,
        AutoUpdateSettings = 0x2,
        Antivirus = 0x4
    }

    internal enum NativeSecurityProviderHealth
    {
        Good = 0,
        NotMonitored = 1,
        Poor = 2,
        Snooze = 3
    }

    private static class NativeMethods
    {
        internal static int ReadHealth(
            SecurityProvider provider,
            out NativeSecurityProviderHealth health) =>
            WscGetSecurityProviderHealth(provider, out health);

        [DllImport("wscapi.dll", ExactSpelling = true)]
        private static extern int WscGetSecurityProviderHealth(
            SecurityProvider providers,
            out NativeSecurityProviderHealth health);
    }
}
