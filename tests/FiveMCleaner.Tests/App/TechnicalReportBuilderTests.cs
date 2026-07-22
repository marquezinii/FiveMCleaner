using FiveMCleaner.App.Services;
using FiveMCleaner.Contracts;
using FiveMCleaner.Core.Catalog;
using Xunit;

namespace FiveMCleaner.Tests.App;

public sealed class ReportSanitizerTests
{
    [Fact]
    public void Sanitize_RemovesGenericUsersPathRegardlessOfAccountName()
    {
        var text = @"Falha ao gravar C:\Users\felipe.marquezini\Documents\Rockstar Games\GTA V\settings.xml";

        var sanitized = ReportSanitizer.Sanitize(text);

        Assert.DoesNotContain("felipe.marquezini", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("%USERPROFILE%", sanitized);
    }

    [Fact]
    public void Sanitize_ReplacesCurrentUserKnownFolders()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var text = $@"{localAppData}\FiveMCleaner\Transactions\abc.json";

        var sanitized = ReportSanitizer.Sanitize(text);

        Assert.Contains("%LOCALAPPDATA%", sanitized);
        Assert.DoesNotContain(localAppData, sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void Sanitize_HandlesNullOrEmptyWithoutThrowing()
    {
        Assert.Equal(string.Empty, ReportSanitizer.Sanitize(null));
        Assert.Equal(string.Empty, ReportSanitizer.Sanitize(string.Empty));
    }
}

public sealed class TechnicalReportBuilderTests
{
    [Fact]
    public void Build_NeverIncludesUserNameFromTemplatedPaths()
    {
        var localization = new LocalizationService(
            System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
        var report = new OptimizationReportDto
        {
            TransactionId = Guid.NewGuid(),
            Profile = OptimizationProfile.Balanced,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            VerifiedCount = 1,
            ChangedCount = 1,
            SkippedCount = 0,
            WarningCount = 0,
            FailedCount = 1,
            RollbackFailedCount = 0,
            NotRunCount = 0,
            RequiresRestart = false,
            RestorePossible = true,
            Succeeded = false,
            Lines =
            [
                new OptimizationReportLineDto
                {
                    Sequence = 1,
                    ActionId = OptimizationActionIds.CleanUserTemporaryFiles,
                    ActionName = "Limpar temporários antigos",
                    Category = ActionCategory.Storage,
                    Outcome = ActionExecutionOutcome.Failed,
                    Reason = @"Acesso negado em C:\Users\joao.silva\AppData\Local\Temp\arquivo.tmp"
                }
            ]
        };

        var text = TechnicalReportBuilder.Build(report, diagnostic: null, localization);

        Assert.DoesNotContain("joao.silva", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(report.TransactionId.ToString("N"), text);
        Assert.Contains("1", text); // contagens presentes
    }

    [Fact]
    public void Build_NeverClaimsSuccessWordingWhenReportFailed()
    {
        var localization = new LocalizationService(
            System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
        var report = new OptimizationReportDto
        {
            TransactionId = Guid.NewGuid(),
            Profile = OptimizationProfile.Light,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            VerifiedCount = 0,
            ChangedCount = 0,
            SkippedCount = 0,
            WarningCount = 0,
            FailedCount = 1,
            RollbackFailedCount = 0,
            NotRunCount = 0,
            RequiresRestart = false,
            RestorePossible = false,
            Succeeded = false,
            Lines =
            [
                new OptimizationReportLineDto
                {
                    Sequence = 1,
                    ActionId = OptimizationActionIds.EnableGameMode,
                    ActionName = "Ativar Modo de Jogo",
                    Category = ActionCategory.WindowsGaming,
                    Outcome = ActionExecutionOutcome.Failed
                }
            ]
        };

        var text = TechnicalReportBuilder.Build(report, diagnostic: null, localization);

        Assert.Contains("Falhou", text);
    }
}
