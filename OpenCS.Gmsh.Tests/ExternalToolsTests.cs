using Xunit.Sdk;
using OpenCS.ExternalTools;
using OpenCS.Gmsh.Runtime;

namespace OpenCS.Gmsh.Tests;

/// <summary>Поиск внешних программ и Python-пакетов (OpenCS.ExternalTools), запуск gmsh через Python-пакет.</summary>
public sealed class ExternalToolsTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"opencs_tools_{Guid.NewGuid():N}");

    public ExternalToolsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    string MakeExecutable(string subdir, string name)
    {
        var dir = Path.Combine(_dir, subdir);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, "");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    [Fact]
    public void FindInPath_FirstMatchingDirectory_SkipsEmptyAndRelativeEntries()
    {
        var name = ExecutableSearch.PlatformFileName("tool");
        var second = MakeExecutable("b", name);
        MakeExecutable("c", name);
        var path = string.Join(Path.PathSeparator,
            "", "relative-dir", Path.Combine(_dir, "a"), Path.Combine(_dir, "b"), Path.Combine(_dir, "c"));

        Assert.Equal(second, ExecutableSearch.FindInPath(path, [name]));
        Assert.Null(ExecutableSearch.FindInPath(path, ["missing-tool"]));
        Assert.Equal(Path.Combine(_dir, "c", name),
            ExecutableSearch.FindInPath(path, [name], candidate => !candidate.Contains($"{Path.DirectorySeparatorChar}b{Path.DirectorySeparatorChar}")));
    }

    [Fact]
    public void FindInPath_Unix_RequiresExecuteBit()
    {
        if (OperatingSystem.IsWindows()) return;
        var path = MakeExecutable("x", "tool");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        Assert.Null(ExecutableSearch.FindInPath(Path.Combine(_dir, "x"), ["tool"]));
    }

    [Fact]
    public void IsUsablePath_PathOfOtherOs_NotUsable()
    {
        Assert.False(ExecutableSearch.IsUsablePath(null));
        Assert.False(ExecutableSearch.IsUsablePath(" "));
        Assert.False(ExecutableSearch.IsUsablePath("relative/dir"));
        Assert.True(ExecutableSearch.IsUsablePath(_dir));
        // Путь из файла проекта, сохранённого на другой ОС.
        Assert.False(ExecutableSearch.IsUsablePath(OperatingSystem.IsWindows() ? "/opt/opencs" : @"C:\Tools\gmsh.exe"));
    }

    [Fact]
    public void PlatformFileName_AddsExeOnWindowsOnly()
    {
        Assert.Equal(OperatingSystem.IsWindows() ? "gmsh.exe" : "gmsh", ExecutableSearch.PlatformFileName("gmsh"));
        Assert.Equal("gmsh.exe", ExecutableSearch.PlatformFileName("gmsh.exe"));
    }

    [Fact]
    public void PythonToolCommand_RoundTrip_AndPlainPathIsNotCommand()
    {
        var command = new PythonToolCommand("gmsh", @"C:\Python 3\python.exe");

        Assert.True(PythonToolCommand.TryParse(command.ToString(), out var parsed));
        Assert.Equal(command, parsed);
        Assert.False(PythonToolCommand.TryParse(@"C:\Tools\gmsh.exe", out _));
        Assert.False(PythonToolCommand.TryParse("python:gmsh|", out _));
        Assert.False(PythonToolCommand.TryParse(null, out _));
    }

    [Fact]
    public async Task Policy_ScopedToExecutionFlow_AndRestored()
    {
        using (ExternalToolPolicy.Use(ExternalToolSource.Auto))
        {
            using (ExternalToolPolicy.Use(ExternalToolSource.PythonPackages))
            {
                Assert.Equal(ExternalToolSource.PythonPackages, ExternalToolPolicy.Current);
                // Значение переходит в задачи, запущенные из этого потока выполнения.
                Assert.Equal(ExternalToolSource.PythonPackages, await Task.Run(() => ExternalToolPolicy.Current));
            }
            Assert.Equal(ExternalToolSource.Auto, ExternalToolPolicy.Current);
        }
    }

    [Fact]
    public void MaterializeScript_SameContentSameFile_DifferentContentDifferentFile()
    {
        var a = PythonRuntime.MaterializeScript("opencs_test.py", "print('a')\n");
        var a2 = PythonRuntime.MaterializeScript("opencs_test.py", "print('a')\n");
        var b = PythonRuntime.MaterializeScript("opencs_test.py", "print('b')\n");

        Assert.Equal(a, a2);
        Assert.NotEqual(a, b);
        Assert.Equal("print('b')\n", File.ReadAllText(b));
    }

    static string PythonWithGmshOrSkip() =>
        PythonRuntime.FindWithModules(["gmsh"], out _) ?? throw SkipException.ForSkip("Python с пакетом gmsh не найден.");

    [Fact]
    public async Task PythonPackagesMode_ResolvesGmshPackage_AndReadsVersion()
    {
        var python = PythonWithGmshOrSkip();
        using var _ = ExternalToolPolicy.Use(ExternalToolSource.PythonPackages);

        // Явный путь к exe в режиме Python-пакетов не используется.
        var info = new GmshExecutableResolver().Resolve(@"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe");
        var version = await GmshProcessRunner.ReadVersionAsync(info.Path, TimeSpan.FromSeconds(60), CancellationToken.None);

        Assert.Equal("python", info.Source);
        Assert.Equal(new PythonToolCommand(GmshExecutableResolver.PythonToolName, python).ToString(), info.Path);
        Assert.Matches(@"^\d+\.\d+", version);
    }
}
