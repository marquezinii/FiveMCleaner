using FiveMCleaner.App.Services;
using Microsoft.Win32;
using Xunit;

namespace FiveMCleaner.Tests.App;

public sealed class StartupRegistrationServiceTests
{
    [Fact]
    public void WindowsStartupRegistration_QuotesExecutableAndUsesFixedArgument()
    {
        var service = new WindowsStartupRegistrationService(
            @"C:\Program Files\FiveMCleaner\FiveMCleaner.exe",
            () => throw new InvalidOperationException("Registry access was not expected."));

        Assert.Equal(
            "\"C:\\Program Files\\FiveMCleaner\\FiveMCleaner.exe\" --startup",
            service.BuildCommand());
    }

    [Theory]
    [InlineData(@"C:\FiveMCleaner\FiveMCleaner.dll")]
    [InlineData("FiveMCleaner.exe")]
    public void WindowsStartupRegistration_RejectsInvalidExecutablePath(string executablePath)
    {
        Assert.Throws<ArgumentException>(() =>
            new WindowsStartupRegistrationService(
                executablePath,
                () => Registry.CurrentUser));
    }

    [Fact]
    public void SessionStartupRegistration_RemainsInMemoryOnly()
    {
        var service = new SessionStartupRegistrationService();

        service.SetEnabled(true);
        Assert.True(service.IsEnabled());

        service.SetEnabled(false);
        Assert.False(service.IsEnabled());
    }
}
