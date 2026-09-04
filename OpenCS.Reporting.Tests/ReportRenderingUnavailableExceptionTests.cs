using OpenCS.Reporting;
using Xunit;

namespace OpenCS.Reporting.Tests;

/// <summary>Контракт ошибки рендеринга: причина и URL типизированы, сообщение —
/// техническое (для логов), а не локализованный текст для пользователя.</summary>
public sealed class ReportRenderingUnavailableExceptionTests
{
    [Fact]
    public void Ctor_KeepsReasonAndDownloadUrl()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new ReportRenderingUnavailableException(
            ReportRenderingFailureReason.RuntimeMissing,
            "WebView2 Runtime not found.", "https://example.invalid/bootstrapper", inner);

        Assert.Equal(ReportRenderingFailureReason.RuntimeMissing, ex.Reason);
        Assert.Equal("https://example.invalid/bootstrapper", ex.RuntimeDownloadUrl);
        Assert.Same(inner, ex.InnerException);
        Assert.Equal("WebView2 Runtime not found.", ex.Message);
    }

    [Fact]
    public void Ctor_LeavesDownloadUrlNull_ForOtherReasons()
    {
        var ex = new ReportRenderingUnavailableException(
            ReportRenderingFailureReason.TimedOut, "Print timed out after 60 s.");

        Assert.Null(ex.RuntimeDownloadUrl);
        Assert.Equal(ReportRenderingFailureReason.TimedOut, ex.Reason);
    }

    [Fact]
    public void Exception_IsNotOperationCanceled()
        => Assert.IsNotType<OperationCanceledException>(
            new ReportRenderingUnavailableException(ReportRenderingFailureReason.TimedOut, "x"));

    [Fact]
    public void FailureReason_HasExactlyFourMembers()
        => Assert.Equal(4, Enum.GetValues<ReportRenderingFailureReason>().Length);
}
