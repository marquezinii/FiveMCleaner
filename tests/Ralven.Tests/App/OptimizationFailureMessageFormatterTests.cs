using Ralven.App.Services;
using Ralven.Contracts;
using Xunit;

namespace Ralven.Tests.App;

public sealed class OptimizationFailureMessageFormatterTests
{
    [Fact]
    public void AppendCode_WithCode_AppendsFormattedSuffixAfterAnEmDash()
    {
        var result = OptimizationFailureMessageFormatter.AppendCode(
            "Access denied",
            BugCode.WIN_PRIVILEGE,
            code => $"Código do erro: {code}");

        Assert.Equal("Access denied — Código do erro: WIN_PRIVILEGE", result);
    }

    [Fact]
    public void AppendCode_NullCode_ReturnsMessageUnchanged()
    {
        var result = OptimizationFailureMessageFormatter.AppendCode(
            "Access denied",
            null,
            code => $"Código do erro: {code}");

        Assert.Equal("Access denied", result);
    }

    [Fact]
    public void AppendCode_NullMessage_ReturnsJustTheFormattedSuffix()
    {
        var result = OptimizationFailureMessageFormatter.AppendCode(
            null,
            BugCode.WIN_PRIVILEGE,
            code => $"Código do erro: {code}");

        Assert.Equal("Código do erro: WIN_PRIVILEGE", result);
    }

    [Fact]
    public void AppendCode_UnknownCode_ReturnsMessageUnchanged()
    {
        var result = OptimizationFailureMessageFormatter.AppendCode(
            "Access denied",
            BugCode.Unknown,
            code => $"Código do erro: {code}");

        Assert.Equal("Access denied", result);
    }
}
