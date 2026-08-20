namespace FiveMCleaner.Windows.Infrastructure;

/// <summary>
/// Single place where a GPU driver description is turned into a vendor name and
/// an integrated-vs-discrete guess. Both are name heuristics over the
/// <c>DriverDesc</c> value this product already reads from
/// <c>SYSTEM\CurrentControlSet\Control\Video</c>, never a vendor SDK query, so an
/// unrecognized name degrades to <see cref="UnknownVendor"/>/
/// <see cref="GpuKindGuess.Unknown"/> instead of a plausible-looking guess.
/// </summary>
internal static class GpuVendorClassifier
{
    public const string UnknownVendor = "Desconhecido";

    // Both the "Intel(R) ..." (driver) and "Intel ..." (marketing) spellings
    // show up in DriverDesc, so both are listed.
    private static readonly string[] IntegratedMarkers =
    [
        "Intel(R) UHD", "Intel UHD", "Intel(R) HD", "Intel HD", "Intel(R) Iris", "Intel Iris",
        "AMD Radeon(TM) Graphics", "AMD Radeon Graphics", "Radeon(TM) Vega"
    ];

    private static readonly string[] DiscreteMarkers = ["NVIDIA", "Radeon RX", "Arc"];

    public static string VendorOf(string driverDescription)
    {
        if (Matches(driverDescription, "NVIDIA"))
        {
            return "NVIDIA";
        }

        if (Matches(driverDescription, "AMD") || Matches(driverDescription, "Radeon"))
        {
            return "AMD";
        }

        return Matches(driverDescription, "Intel") ? "Intel" : UnknownVendor;
    }

    public static bool IsIntegrated(string driverDescription)
    {
        return IntegratedMarkers.Any(marker => Matches(driverDescription, marker));
    }

    public static GpuKindGuess GuessKind(string driverDescription)
    {
        if (IsIntegrated(driverDescription))
        {
            return GpuKindGuess.LikelyIntegrated;
        }

        return DiscreteMarkers.Any(marker => Matches(driverDescription, marker))
            ? GpuKindGuess.LikelyDiscrete
            : GpuKindGuess.Unknown;
    }

    private static bool Matches(string driverDescription, string marker)
    {
        return driverDescription.Contains(marker, StringComparison.OrdinalIgnoreCase);
    }
}
