using Ralven.Contracts;
using Ralven.Windows.Diagnostics;
using Xunit;

namespace Ralven.Tests.Windows;

public sealed class BugCodeClassifierTests
{
    [Fact]
    public void ClassifyException_UnrecognizedExceptionType_FallsBackToOptimizationOnlyWhenContextIsOptimization()
    {
        var result = BugCodeClassifier.ClassifyException(new FormatException("boom"), "optimization");

        Assert.Equal(BugCode.APP_OPT_ACTION_EXECUTION, result);
    }

    [Fact]
    public void ClassifyException_UnrecognizedContext_ReturnsUnknownRatherThanOptimization()
    {
        var result = BugCodeClassifier.ClassifyException(new FormatException("boom"), "some-unmapped-context");

        Assert.Equal(BugCode.Unknown, result);
    }

    [Fact]
    public void ClassifyException_AppInventoryContext_ReturnsAppInventoryScan()
    {
        var result = BugCodeClassifier.ClassifyException(new UnauthorizedAccessException(), "app-inventory");

        Assert.Equal(BugCode.APP_INV_SCAN, result);
    }

    [Fact]
    public void ClassifyException_SecurityHealthContext_ReturnsSecurityHealthQuery()
    {
        var result = BugCodeClassifier.ClassifyException(new DllNotFoundException(), "security-health");

        Assert.Equal(BugCode.SEC_HEALTH_QUERY, result);
    }

    [Fact]
    public void ClassifyOptimizationException_FiveMActionWithUnmatchedKeywordAndUntypedException_DoesNotReturnUnknown()
    {
        var result = BugCodeClassifier.ClassifyOptimizationException(
            new ArgumentException("boom"), "fivem.legacy.graphics.light.apply");

        Assert.Equal(BugCode.APP_OPT_ACTION_EXECUTION, result);
        Assert.NotEqual(BugCode.Unknown, result);
    }

    [Fact]
    public void ClassifyException_SettingsContext_ReturnsSettingsPersistenceRatherThanUnknown()
    {
        var result = BugCodeClassifier.ClassifyException(new ArgumentException("boom"), "settings");

        Assert.Equal(BugCode.APP_SETTINGS_PERSISTENCE, result);
    }

    [Fact]
    public void ClassifyException_UnauthorizedAccessWithSettingsContext_ReturnsSettingsPersistence()
    {
        var result = BugCodeClassifier.ClassifyException(new UnauthorizedAccessException(), "settings");

        Assert.Equal(BugCode.APP_SETTINGS_PERSISTENCE, result);
    }
}
