using System.Diagnostics;
using Ralven.App.Services;
using Xunit;

namespace Ralven.Tests.App;

public sealed class ErrorExperienceTests
{
    [Fact]
    public void Parse_EnablesOnlyKnownDeveloperPreviewModes()
    {
        Assert.Equal(ErrorExperienceTestMode.Recoverable,
            ErrorExperienceTest.Parse(["--error-experience-test=recoverable"], AppRuntimeEnvironment.Development));
        Assert.Equal(ErrorExperienceTestMode.Fatal,
            ErrorExperienceTest.Parse(["--error-experience-test=fatal"], AppRuntimeEnvironment.Development));
        Assert.Equal(ErrorExperienceTestMode.None,
            ErrorExperienceTest.Parse(["--other"], AppRuntimeEnvironment.Development));
        Assert.Equal(ErrorExperienceTestMode.None,
            ErrorExperienceTest.Parse(["--error-experience-test=fatal"], AppRuntimeEnvironment.Production));
    }

    [Fact]
    public void Format_RemovesUserPathsFromTechnicalDetails()
    {
        var details = ErrorDetailsFormatter.Format(
            new IOException(@"Could not open C:\Users\private-user\AppData\Local\Ralven\settings.json"),
            LocalizationService.Current);

        Assert.DoesNotContain("private-user", details, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("%USERPROFILE%", details, StringComparison.Ordinal);
    }

    [Fact]
    public void TryStart_UsesAQualifiedExecutableWithoutThrowing()
    {
        ProcessStartInfo? received = null;
        using var currentProcess = Process.GetCurrentProcess();

        var started = AppRestart.TryStart(@"C:\Program Files\Ralven\Ralven.exe", info =>
        {
            received = info;
            return currentProcess;
        });

        Assert.True(started);
        Assert.Equal(@"C:\Program Files\Ralven\Ralven.exe", received?.FileName);
        Assert.True(received is { UseShellExecute: true });
    }

    [Fact]
    public void TryStart_RejectsUnavailableOrRelativeExecutables()
    {
        Assert.False(AppRestart.TryStart(null));
        Assert.False(AppRestart.TryStart("Ralven.exe"));
    }
}
