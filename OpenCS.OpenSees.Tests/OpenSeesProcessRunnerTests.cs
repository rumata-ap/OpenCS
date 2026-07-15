using System.Text.Json;
using OpenCS.OpenSees.Artifacts;
using OpenCS.OpenSees.Runtime;

namespace OpenCS.OpenSees.Tests;

public sealed class OpenSeesProcessRunnerTests
{
    [Fact]
    public async Task Runner_captures_stdout_and_stderr()
    {
        OpenSeesRunResult result = await RunCmdAsync("echo stdout & echo stderr 1>&2");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("stdout", result.Stdout);
        Assert.Contains("stderr", result.Stderr);
        Assert.False(result.TimedOut);
        Assert.False(result.Cancelled);
    }

    [Fact]
    public async Task Runner_returns_nonzero_exit_code()
    {
        OpenSeesRunResult result = await RunCmdAsync("exit /b 7");

        Assert.Equal(7, result.ExitCode);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public async Task Runner_kills_process_tree_on_timeout()
    {
        OpenSeesRunResult result = await RunCmdAsync("ping -n 10 127.0.0.1 > nul", TimeSpan.FromMilliseconds(100));

        Assert.True(result.TimedOut);
        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public async Task Runner_kills_process_tree_on_cancellation()
    {
        using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(100));

        OpenSeesRunResult result = await RunCmdAsync(
            "ping -n 10 127.0.0.1 > nul",
            TimeSpan.FromSeconds(10),
            cancellation.Token);

        Assert.True(result.Cancelled);
        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public void Artifact_store_uses_unique_folders_and_preserves_failure_files()
    {
        string root = Path.Combine(Path.GetTempPath(), "opencs-opensees-tests", Guid.NewGuid().ToString("N"));
        try
        {
            OpenSeesArtifactStore store = new(root);
            OpenSeesArtifact first = store.Create();
            OpenSeesArtifact second = store.Create();

            Assert.NotEqual(first.DirectoryPath, second.DirectoryPath);

            OpenSeesManifest manifest = new()
            {
                Status = "not_converged",
                ExitCode = 2,
                Diagnostics = ["solver failed"]
            };
            store.WriteManifest(first, manifest);
            store.WriteScript(first, "wipe\n");
            store.WriteRunResult(first, new OpenSeesRunResult
            {
                ExitCode = 2,
                Stdout = "out",
                Stderr = "err",
                Duration = TimeSpan.FromMilliseconds(12),
                TimedOut = false,
                Cancelled = false
            });

            Assert.True(File.Exists(first.ManifestPath));
            Assert.True(File.Exists(first.ScriptPath));
            Assert.True(File.Exists(first.StdoutPath));
            Assert.True(File.Exists(first.StderrPath));
            Assert.True(File.Exists(first.ExitPath));
            Assert.Equal("out", File.ReadAllText(first.StdoutPath));
            Assert.Equal("err", File.ReadAllText(first.StderrPath));
            Assert.Equal("not_converged", JsonSerializer.Deserialize<OpenSeesManifest>(
                File.ReadAllText(first.ManifestPath))!.Status);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Resolver_prefers_explicit_path_then_bundled_path()
    {
        string executable = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
        string bundled = Path.Combine(Path.GetTempPath(), "bundled-opensees.exe");
        string? oldEnvironment = Environment.GetEnvironmentVariable("OPENSEES_EXE");
        File.Copy(executable, bundled, overwrite: true);
        try
        {
            Environment.SetEnvironmentVariable("OPENSEES_EXE", null);
            OpenSeesExecutableResolver resolver = new(bundled);

            OpenSeesExecutableInfo explicitInfo = resolver.Resolve(executable);
            OpenSeesExecutableInfo bundledInfo = resolver.Resolve(null);

            Assert.Equal(Path.GetFullPath(executable), explicitInfo.Path);
            Assert.Equal("explicit", explicitInfo.Source);
            Assert.Equal(Path.GetFullPath(bundled), bundledInfo.Path);
            Assert.Equal("bundled", bundledInfo.Source);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENSEES_EXE", oldEnvironment);
            File.Delete(bundled);
        }
    }

    [Fact]
    public void Resolver_reports_missing_executable_clearly()
    {
        string? oldEnvironment = Environment.GetEnvironmentVariable("OPENSEES_EXE");
        try
        {
            Environment.SetEnvironmentVariable("OPENSEES_EXE", null);
            OpenSeesExecutableResolver resolver = new(Path.Combine(Path.GetTempPath(), "missing-opensees.exe"));

            FileNotFoundException exception = Assert.Throws<FileNotFoundException>(() =>
                resolver.Resolve(Path.Combine(Path.GetTempPath(), "also-missing-opensees.exe")));

            Assert.Contains("OpenSees", exception.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENSEES_EXE", oldEnvironment);
        }
    }

    private static async Task<OpenSeesRunResult> RunCmdAsync(
        string command,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        string executable = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
        OpenSeesRunRequest request = new()
        {
            ExecutablePath = executable,
            Arguments = ["/d", "/s", "/c", command],
            WorkingDirectory = Path.GetTempPath(),
            Timeout = timeout ?? TimeSpan.FromSeconds(5)
        };

        return await new OpenSeesProcessRunner().RunAsync(request, cancellationToken);
    }
}
