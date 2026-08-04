using System.IO;
using System.Text.Json;
using FiveMCleaner.UpdateRuntime;
using Xunit;

namespace FiveMCleaner.Tests.UpdateRuntime;

public sealed class UpdaterDiagnosticsAuthTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "FiveMCleanerDiagAuth", Guid.NewGuid().ToString("N"));

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    private string SettingsPath => Path.Combine(root, "settings.json");

    [Fact]
    public void MissingSettingsFile_ReturnsFalse()
    {
        var authorized = UpdaterDiagnostics.IsTelemetryAuthorized(root);
        Assert.False(authorized);
    }

    [Fact]
    public void EmptyFile_ReturnsFalse()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(SettingsPath, "");
        Assert.False(UpdaterDiagnostics.IsTelemetryAuthorized(root));
    }

    [Fact]
    public void CorruptJson_ReturnsFalse()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(SettingsPath, "{share");
        Assert.False(UpdaterDiagnostics.IsTelemetryAuthorized(root));
    }

    [Fact]
    public void NotJsonAtAll_ReturnsFalse()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(SettingsPath, "not-json-content");
        Assert.False(UpdaterDiagnostics.IsTelemetryAuthorized(root));
    }

    [Fact]
    public void ValidJsonMissingTelemetryProperty_ReturnsFalse()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(SettingsPath, "{}");
        Assert.False(UpdaterDiagnostics.IsTelemetryAuthorized(root));
    }

    [Fact]
    public void TelemetryDisabled_ReturnsFalse()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(SettingsPath,
            """{"shareAnonymousTelemetry":false,"privacyConsentVersion":3}""");
        Assert.False(UpdaterDiagnostics.IsTelemetryAuthorized(root));
    }

    [Fact]
    public void ConsentVersionTooLow_ReturnsFalse()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(SettingsPath,
            """{"shareAnonymousTelemetry":true,"privacyConsentVersion":2}""");
        Assert.False(UpdaterDiagnostics.IsTelemetryAuthorized(root));
    }

    [Fact]
    public void NoConsentVersion_ReturnsFalse()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(SettingsPath,
            """{"shareAnonymousTelemetry":true}""");
        Assert.False(UpdaterDiagnostics.IsTelemetryAuthorized(root));
    }

    [Fact]
    public void ConsentVersionNull_ReturnsFalse()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(SettingsPath,
            """{"shareAnonymousTelemetry":true,"privacyConsentVersion":null}""");
        Assert.False(UpdaterDiagnostics.IsTelemetryAuthorized(root));
    }

    [Fact]
    public void FullyAuthorized_ReturnsTrue()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(SettingsPath,
            """{"shareAnonymousTelemetry":true,"privacyConsentVersion":3}""");
        Assert.True(UpdaterDiagnostics.IsTelemetryAuthorized(root));
    }

    [Fact]
    public void ConsentVersionHigherThanMinimum_ReturnsTrue()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(SettingsPath,
            """{"shareAnonymousTelemetry":true,"privacyConsentVersion":99}""");
        Assert.True(UpdaterDiagnostics.IsTelemetryAuthorized(root));
    }

    [Fact]
    public void LockedFile_ReturnsFalse()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(SettingsPath,
            """{"shareAnonymousTelemetry":true,"privacyConsentVersion":3}""");
        using (new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(UpdaterDiagnostics.IsTelemetryAuthorized(root));
        }
    }

    [Fact]
    public void TelemetryBooleanIsStringNotBool_ReturnsFalse()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(SettingsPath,
            """{"shareAnonymousTelemetry":"true","privacyConsentVersion":3}""");
        Assert.False(UpdaterDiagnostics.IsTelemetryAuthorized(root));
    }

    [Fact]
    public void ConsentVersionIsString_ReturnsFalse()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(SettingsPath,
            """{"shareAnonymousTelemetry":true,"privacyConsentVersion":"3"}""");
        Assert.False(UpdaterDiagnostics.IsTelemetryAuthorized(root));
    }
}
