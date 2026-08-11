using Xunit;

namespace FiveMCleaner.Tests.App;

public sealed class PublicExposureHardeningTests
{
    [Fact]
    public void StableRelease_RequiresExactOriginMainBeforeSecretBearingSteps()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "release.yml"));

        var provenanceGuard = workflow.IndexOf(
            "Stable release tag must point to the current origin/main commit.",
            StringComparison.Ordinal);
        var firstSecretReference = workflow.IndexOf("${{ secrets.", StringComparison.Ordinal);

        Assert.True(provenanceGuard >= 0, "Stable releases must validate the trusted origin/main commit.");
        Assert.True(
            firstSecretReference > provenanceGuard,
            "The provenance guard must run before any workflow secret is referenced.");
        Assert.Contains("refs/remotes/origin/main", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Startup_DoesNotWriteRawExceptionsToDeveloperSpecificPath()
    {
        var root = FindRepositoryRoot();
        var appSource = File.ReadAllText(Path.Combine(root, "src", "FiveMCleaner.App", "App.xaml.cs"));

        Assert.DoesNotContain(@"C:\Projetos\fivemcleaner-debug.log", appSource, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FiveMCleaner.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
