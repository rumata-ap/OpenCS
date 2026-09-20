using OpenCS.Reporting.Pandoc;
using Xunit;

namespace OpenCS.Reporting.Tests;

/// <summary>Проверяет безопасный запуск внешних инструментов отчётности.</summary>
public sealed class PandocProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_captures_output_and_exit_code()
    {
        string command = Environment.GetEnvironmentVariable("ComSpec")
            ?? throw new InvalidOperationException("ComSpec is missing.");
        PandocProcessResult result = await new PandocProcessRunner().RunAsync(
            command,
            ["/c", "echo runner-ok"],
            AppContext.BaseDirectory,
            TimeSpan.FromSeconds(10));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("runner-ok", result.StandardOutput);
    }

    [Fact]
    public async Task RunAsync_returns_nonzero_exit_and_stderr()
    {
        string command = Environment.GetEnvironmentVariable("ComSpec")
            ?? throw new InvalidOperationException("ComSpec is missing.");
        PandocProcessResult result = await new PandocProcessRunner().RunAsync(
            command,
            ["/c", "echo runner-error 1>&2 & exit /b 7"],
            AppContext.BaseDirectory,
            TimeSpan.FromSeconds(10));

        Assert.Equal(7, result.ExitCode);
        Assert.Contains("runner-error", result.StandardError);
    }

    [Fact]
    public async Task RunAsync_reports_timeout()
    {
        string command = Environment.GetEnvironmentVariable("ComSpec")
            ?? throw new InvalidOperationException("ComSpec is missing.");
        PandocExportException error = await Assert.ThrowsAsync<PandocExportException>(() =>
            new PandocProcessRunner().RunAsync(
                command,
                ["/c", "ping -n 8 127.0.0.1 > nul"],
                AppContext.BaseDirectory,
                TimeSpan.FromMilliseconds(100)));

        Assert.Equal(PandocExportFailureReason.TimedOut, error.Reason);
    }

    [Fact]
    public async Task RunAsync_reports_start_failure()
    {
        PandocExportException error = await Assert.ThrowsAsync<PandocExportException>(() =>
            new PandocProcessRunner().RunAsync(
                Path.Combine(AppContext.BaseDirectory, "missing-pandoc.exe"),
                [],
                AppContext.BaseDirectory,
                TimeSpan.FromSeconds(1)));

        Assert.Equal(PandocExportFailureReason.ProcessStartFailed, error.Reason);
    }
}
