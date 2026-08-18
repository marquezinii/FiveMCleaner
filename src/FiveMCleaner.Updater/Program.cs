using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Windows.Forms;
using FiveMCleaner.UpdateRuntime;

namespace FiveMCleaner.Updater;

public static class Program
{
    private const int ParentExitTimeoutMilliseconds = 120_000;
    private const int InstallerTimeoutMilliseconds = 600_000;

    [STAThread]
    public static int Main(string[] args)
    {
        if (!UpdateHandoff.TryParse(args, out var handoff, out var error))
        {
            ShowFailure(error);
            return 2;
        }

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
            "O FiveMCleaner não foi encerrado a tempo para instalar a atualização.");

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
        $"Não foi possível concluir a atualização do FiveMCleaner.\n\n{detail}\n\nAbra o aplicativo novamente e tente outra vez.",
        "Atualização do FiveMCleaner", MessageBoxButtons.OK, MessageBoxIcon.Error);

    private static string DescribeFailure(Exception exception) => exception switch
    {
        TimeoutException => "A atualização demorou mais que o esperado e foi interrompida. Abra o FiveMCleaner novamente para tentar outra vez.",
        UnauthorizedAccessException => "O Windows não permitiu concluir a atualização. Verifique a permissão e tente novamente.",
        CryptographicException or InvalidDataException => "A verificação de segurança do instalador falhou. Nada foi alterado.",
        FileNotFoundException => "O instalador verificado não está mais disponível. Abra o FiveMCleaner e baixe a atualização novamente.",
        _ => "O atualizador encontrou um problema inesperado. Abra o FiveMCleaner novamente e tente outra vez."
    };
}
