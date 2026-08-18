using System.ComponentModel;
using System.Diagnostics;

namespace FiveMCleaner.UpdateRuntime;

/// <summary>
/// Waits for a previous FiveMCleaner process to exit before its files are
/// replaced. Shared by FiveMCleaner.Updater and FiveMCleaner.Launcher so the
/// "process already gone" race between GetProcessById and the StartTime/
/// HasExited read is handled identically in both.
/// </summary>
public static class ParentProcessWait
{
    public static void WaitForExit(
        int parentProcessId, long parentStartTimeUtcFileTime, int timeoutMilliseconds, string timeoutMessage)
    {
        try
        {
            using var parent = Process.GetProcessById(parentProcessId);
            if (!parent.HasExited
                && parent.StartTime.ToUniversalTime().ToFileTimeUtc() == parentStartTimeUtcFileTime
                && !parent.WaitForExit(timeoutMilliseconds))
            {
                throw new TimeoutException(timeoutMessage);
            }
        }
        // O processo pai pode sair entre GetProcessById e a leitura de HasExited/
        // StartTime: nesse caso o Windows recusa o acesso ao processo já encerrado
        // (Win32Exception) ou nega a propriedade (InvalidOperationException), o
        // mesmo caso "já se foi" que ArgumentException já tratava como inofensivo.
        catch (ArgumentException) { }
        catch (Win32Exception) { }
        catch (InvalidOperationException) { }
    }
}
