using System.Management;

namespace FiveMCleaner.Windows.Infrastructure;

public sealed record ThermalSnapshot(bool IsAvailable, double? HighestCelsius);

public interface IThermalInspector
{
    ThermalSnapshot GetSnapshot();
}

/// <summary>
/// Best-effort read of the ACPI thermal zone exposed by WMI
/// (root\WMI, MSAcpi_ThermalZoneTemperature). Most consumer motherboards do
/// not expose a working ACPI thermal zone without vendor tooling, so this
/// almost always returns <c>IsAvailable = false</c>; callers must present
/// that honestly instead of guessing a value. Follows the same
/// try/graceful-null WMI pattern already used for RAM module layout in
/// <c>AppOptimizationService</c>.
/// </summary>
public sealed class WindowsThermalInspector : IThermalInspector
{
    // MSAcpi_ThermalZoneTemperature reports tenths of Kelvin. Anything outside
    // a plausible operating range (-40C to 130C) is treated as unavailable
    // rather than reported, since bogus zero/placeholder values are common.
    private const double MinPlausibleCelsius = -40d;
    private const double MaxPlausibleCelsius = 130d;

    public ThermalSnapshot GetSnapshot()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI",
                "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            var readings = searcher.Get()
                .Cast<ManagementObject>()
                .Select(zone => zone["CurrentTemperature"])
                .OfType<uint>()
                .Select(tenthsKelvin => (tenthsKelvin / 10d) - 273.15d)
                .Where(celsius => celsius is >= MinPlausibleCelsius and <= MaxPlausibleCelsius)
                .ToArray();

            return readings.Length == 0
                ? new ThermalSnapshot(false, null)
                : new ThermalSnapshot(true, readings.Max());
        }
        catch (ManagementException)
        {
            return new ThermalSnapshot(false, null);
        }
        catch (UnauthorizedAccessException)
        {
            return new ThermalSnapshot(false, null);
        }
    }
}
