using System.Security.Principal;

namespace FiveMCleaner.Broker;

internal sealed class BrokerNotElevatedException : UnauthorizedAccessException
{
    public BrokerNotElevatedException()
        : base("The broker requires an elevated administrator token.")
    {
    }
}

internal static class ElevationGuard
{
    public static void EnsureElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
        {
            throw new BrokerNotElevatedException();
        }
    }
}
