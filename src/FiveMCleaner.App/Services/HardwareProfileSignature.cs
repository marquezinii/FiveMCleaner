using System.Security.Cryptography;
using System.Text;

namespace FiveMCleaner.App.Services;

/// <summary>
/// Computes a short, deterministic, local-only identifier for the current
/// machine's CPU/GPU/RAM combination, used to keep before/after comparisons
/// scoped to the same hardware instead of comparing unrelated machines. It
/// never leaves the device and never claims to identify a specific FiveM
/// server: there is no safe, documented way to read the currently connected
/// server without inspecting FiveM's own process/game state, which is out
/// of scope for this product.
/// </summary>
public static class HardwareProfileSignature
{
    public static string Compute(string cpuName, IReadOnlyList<string> gpuNames, double totalMemoryGiB)
    {
        ArgumentNullException.ThrowIfNull(cpuName);
        ArgumentNullException.ThrowIfNull(gpuNames);

        var roundedMemory = Math.Round(totalMemoryGiB / 2d) * 2d;
        var normalizedGpus = string.Join(
            "|",
            gpuNames.Select(name => name.Trim().ToUpperInvariant()).OrderBy(name => name, StringComparer.Ordinal));
        var material = $"{cpuName.Trim().ToUpperInvariant()}::{normalizedGpus}::{roundedMemory:0}GIB";

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(hash)[..12];
    }
}
