using Microsoft.Win32;

namespace FiveMCleaner.Windows.Infrastructure;

public interface IVendorLaptopSoftwareInspector
{
    /// <summary>Display names of known laptop-vendor performance/GPU-switch control software found installed.</summary>
    IReadOnlyList<string> DetectInstalledToolNames();
}

/// <summary>
/// Read-only, best-effort detector for well-known laptop manufacturer
/// utilities that expose GPU-switch (MUX) and performance-mode controls
/// (Armoury Crate, MSI Center, Lenovo Vantage, etc.). This product never
/// controls a MUX switch or BIOS setting itself through undocumented,
/// vendor-specific mechanisms (see docs/safety.md and
/// docs/graphics-optimizations-backlog.md, seção 12) -- detecting that the
/// controlling software is installed is the most this app can safely and
/// honestly report; it never assumes the switch itself is present or in
/// any particular state.
/// </summary>
public sealed class WindowsVendorLaptopSoftwareInspector : IVendorLaptopSoftwareInspector
{
    private const string UninstallRegistryPath =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    private static readonly (string Marker, string DisplayName)[] KnownTools =
    [
        ("Armoury Crate", "ASUS Armoury Crate"),
        ("MSI Center", "MSI Center"),
        ("Dragon Center", "MSI Dragon Center"),
        ("Lenovo Vantage", "Lenovo Vantage"),
        ("Dell Power Manager", "Dell Power Manager"),
        ("Alienware Command Center", "Alienware Command Center"),
        ("Alienware Control Center", "Alienware Command Center"),
        ("HP Omen Gaming Hub", "HP Omen Gaming Hub"),
        ("OMEN Command Center", "HP Omen Gaming Hub"),
        ("PredatorSense", "Acer PredatorSense"),
        ("Predator Sense", "Acer PredatorSense"),
        ("Gigabyte Control Center", "Gigabyte Control Center")
    ];

    public IReadOnlyList<string> DetectInstalledToolNames()
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        try
        {
            using var uninstall = Registry.LocalMachine.OpenSubKey(UninstallRegistryPath);
            if (uninstall is null)
            {
                return [];
            }

            foreach (var subKeyName in uninstall.GetSubKeyNames())
            {
                using var subKey = uninstall.OpenSubKey(subKeyName);
                var displayName = subKey?.GetValue("DisplayName") as string;
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    continue;
                }

                foreach (var (marker, name) in KnownTools)
                {
                    if (displayName.Contains(marker, StringComparison.OrdinalIgnoreCase))
                    {
                        found.Add(name);
                    }
                }
            }
        }
        catch (Exception exception) when (exception is System.Security.SecurityException
            or UnauthorizedAccessException)
        {
            return [];
        }

        return found.ToArray();
    }
}
