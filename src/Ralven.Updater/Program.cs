using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Globalization;
using System.Resources;
using System.Windows.Forms;
using Ralven.UpdateRuntime;

namespace Ralven.Updater;

public static class Program
{
    private const int ParentExitTimeoutMilliseconds = 120_000;
    private const int InstallerTimeoutMilliseconds = 600_000;
    private static readonly ResourceManager Messages = new("Ralven.Updater.Resources.Strings", typeof(Program).Assembly);
    private static CultureInfo uiCulture = CultureInfo.CurrentUICulture;

    [STAThread]
    public static int Main(string[] args)
    {
        if (!UpdateHandoff.TryParse(args, out var handoff, out var error))
        {
            ShowFailure(T(error));
            return 2;
        }
        uiCulture = CultureInfo.GetCultureInfo(handoff.CultureName);

        try
        {
            WaitForParentExit(handoff.ParentProcessId, handoff.ParentStartTimeUtcFileTime);
            using var verifiedInstaller = VerifyInstaller(handoff);
            RunInstaller(handoff);
            return 0;
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            ShowFailure(DescribeFailure(exception));
            return 1;
        }
    }

    private static void WaitForParentExit(int parentProcessId, long parentStartTimeUtcFileTime) =>
        ParentProcessWait.WaitForExit(
            parentProcessId,
            parentStartTimeUtcFileTime,
            ParentExitTimeoutMilliseconds,
            "O Ralven não foi encerrado a tempo para instalar a atualização.");

    private static FileStream VerifyInstaller(UpdateHandoff handoff)
    {
        if (!File.Exists(handoff.InstallerPath)) throw new FileNotFoundException("O instalador baixado não foi encontrado.", handoff.InstallerPath);
        if (new FileInfo(handoff.InstallerPath).Length != handoff.InstallerSizeBytes)
        {
            throw new InvalidDataException("O tamanho do instalador baixado não confere com a atualização verificada.");
        }

        var stream = new FileStream(
            handoff.InstallerPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        if (stream.Length != handoff.InstallerSizeBytes)
        {
            stream.Dispose();
            throw new InvalidDataException("Installer size changed during verification.");
        }
        if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(handoff.InstallerSha256, StringComparison.OrdinalIgnoreCase))
        {
            stream.Dispose();
            throw new InvalidDataException("A verificação de integridade do instalador falhou.");
        }

        return stream;
    }

    private static void RunInstaller(UpdateHandoff handoff)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = handoff.InstallerPath,
            WorkingDirectory = Path.GetDirectoryName(handoff.InstallerPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in handoff.BuildInstallerArguments()) startInfo.ArgumentList.Add(argument);
        using var installer = Process.Start(startInfo) ?? throw new InvalidOperationException("O Windows não iniciou o instalador da atualização.");
        if (!installer.WaitForExit(InstallerTimeoutMilliseconds))
        {
            TryKill(installer);
            throw new TimeoutException($"O instalador da atualização não terminou a tempo e foi encerrado. {handoff.LogHint}");
        }
        if (installer.ExitCode != 0)
        {
            throw new InvalidOperationException($"A instalação da atualização foi encerrada com código {installer.ExitCode}. {handoff.LogHint}");
        }
    }

    private static void TryKill(Process process)
    {
        try { process.Kill(entireProcessTree: true); }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception) { }
    }

    private static void ShowFailure(string? detail) => MessageBox.Show(
        string.Format(uiCulture, T("Updater.Failure"), detail),
        T("Updater.Title"), MessageBoxButtons.OK, MessageBoxIcon.Error);

    private static string DescribeFailure(Exception exception) => exception switch
    {
        TimeoutException => T("Updater.Error.Timeout"),
        UnauthorizedAccessException => T("Updater.Error.AccessDenied"),
        CryptographicException or InvalidDataException => T("Updater.Error.Security"),
        FileNotFoundException => T("Updater.Error.MissingInstaller"),
        _ => T("Updater.Error.Unexpected")
    };

    internal static string T(string key) => Messages.GetString(key, uiCulture) ?? key;
}
