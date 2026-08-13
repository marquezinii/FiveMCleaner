using System.Diagnostics;

namespace FiveMCleaner.Windows.Infrastructure;

internal static class ProcessInspection
{
    public static string GetNameOrEmpty(Process process)
    {
        try
        {
            return process.ProcessName;
        }
        catch (Exception exception) when (IsInspectionFailure(exception))
        {
            return string.Empty;
        }
    }

    public static bool IsExecutableWithinRoot(Process process, string normalizedRoot)
    {
        try
        {
            var fileName = process.MainModule?.FileName;
            if (fileName is null)
            {
                return false;
            }

            var processPath = Path.GetFullPath(fileName);
            return processPath.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
                || processPath.StartsWith(
                    normalizedRoot + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (IsInspectionFailure(exception))
        {
            return false;
        }
    }

    public static bool IsNotResponding(Process process)
    {
        try
        {
            return !process.Responding;
        }
        catch (Exception exception) when (IsInspectionFailure(exception))
        {
            return false;
        }
    }

    private static bool IsInspectionFailure(Exception exception)
    {
        return exception is InvalidOperationException
            or System.ComponentModel.Win32Exception
            or NotSupportedException;
    }
}
