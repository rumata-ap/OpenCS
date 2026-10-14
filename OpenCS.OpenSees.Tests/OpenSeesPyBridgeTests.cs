using Xunit.Sdk;
using System.Globalization;
using OpenCS.ExternalTools;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Tests.Fixtures;

namespace OpenCS.OpenSees.Tests;

/// <summary>
/// Мост Tcl → OpenSeesPy (<see cref="OpenSeesPyBridge"/>): тот же сценарий, что для OpenSees.exe, исполняется
/// пакетом openseespy. Без Python с openseespy и tkinter тесты пропускаются.
/// </summary>
public sealed class OpenSeesPyBridgeTests : IDisposable
{
    // Кириллица в пути: OpenSees.exe такой каталог не откроет, мост — должен.
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"opencs_мост_{Guid.NewGuid():N}");

    public OpenSeesPyBridgeTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    static string OpenSeesPyOrSkip()
    {
        string? python = PythonRuntime.FindWithModules(["openseespy", "tkinter"], out _);
        if (python == null) throw SkipException.ForSkip("Python с openseespy и tkinter не найден.");
        return new PythonToolCommand(OpenSeesPyBridge.ToolName, python).ToString();
    }

    async Task<OpenSeesRunResult> RunAsync(string executable, string script, string? dir = null)
    {
        dir ??= _dir;
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "script.tcl");
        await File.WriteAllTextAsync(path, script);
        return await new OpenSeesProcessRunner().RunAsync(new OpenSeesRunRequest
        {
            ExecutablePath = executable,
            WorkingDirectory = dir,
            ScriptPath = path,
            Timeout = TimeSpan.FromSeconds(60)
        }, CancellationToken.None);
    }

    const string Truss = """
        model basic -ndm 2 -ndf 2
        node 1 0 0
        node 2 1 0
        fix 1 1 1
        fix 2 0 1
        uniaxialMaterial Elastic 1 1000
        element truss 1 1 2 1.0 1
        pattern Plain 1 Linear { load 2 10 0 }
        system BandSPD
        numberer Plain
        constraints Plain
        integrator LoadControl 1
        algorithm Linear
        analysis Static
        set rc [analyze 1]
        puts "rc=$rc disp=[nodeDisp 2 1] all=[llength [nodeDisp 2]]"
        set f [open force.txt w]
        puts $f [eleResponse 1 axialForce]
        exit 2
        """;

    [Fact]
    public async Task Bridge_RunsTclScript_ReturnsValues_OneLineBlockBody_ExitCode_UnclosedChannel()
    {
        string executable = OpenSeesPyOrSkip();

        OpenSeesRunResult run = await RunAsync(executable, Truss);

        Assert.Equal(2, run.ExitCode);
        // Однострочное тело pattern { load … } применено (иначе перемещение 0); список — Tcl-список из двух.
        Assert.Contains("rc=0 disp=0.01 all=2", run.Stdout);
        // Канал, не закрытый сценарием до exit, дописан.
        Assert.Equal(10.0, double.Parse(File.ReadAllText(Path.Combine(_dir, "force.txt")).Trim(), CultureInfo.InvariantCulture), 9);
    }

    [Fact]
    public async Task Bridge_ScriptError_ExitCode1_WithMessage()
    {
        string executable = OpenSeesPyOrSkip();

        OpenSeesRunResult run = await RunAsync(executable, "model basic -ndm 2 -ndf 3\nelement noSuchElement 1 1 2\n");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("noSuchElement", run.Stderr);
    }

    [Fact]
    public async Task Bridge_Version()
    {
        string executable = OpenSeesPyOrSkip();

        OpenSeesRunResult run = await new OpenSeesProcessRunner().RunAsync(new OpenSeesRunRequest
        {
            ExecutablePath = executable, Arguments = ["-version"], WorkingDirectory = _dir, Timeout = TimeSpan.FromSeconds(30)
        }, CancellationToken.None);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("OpenSeesPy", run.Stdout);
    }

    /// <summary>Консоль 3D, fiber-сечение блоком, старый синтаксис стержня (как пишут генераторы OpenCS).</summary>
    static string Cantilever(string formulation) => $$"""
        model basic -ndm 3 -ndf 6
        node 1 0 0 0
        node 2 0 0 3
        fix 1 1 1 1 1 1 1
        uniaxialMaterial Concrete01 1 -30e6 -0.002 -6e6 -0.0035
        uniaxialMaterial Steel02 2 400e6 2e11 0.01 18 0.925 0.15
        section Fiber 1 -GJ 1e7 {
            patch rect 1 8 8 -0.2 -0.2 0.2 0.2
            fiber 0.15 0.15 3.14e-4 2
            fiber -0.15 0.15 3.14e-4 2
            fiber 0.15 -0.15 3.14e-4 2
            fiber -0.15 -0.15 3.14e-4 2
        }
        geomTransf PDelta 1 1 0 0
        element {{formulation}} 1 1 2 5 1 1
        pattern Plain 1 Linear {
            load 2 20000 0 -500000 0 0 0
        }
        system BandGeneral
        numberer RCM
        constraints Plain
        test NormDispIncr 1e-10 50
        algorithm Newton
        integrator LoadControl 0.1
        analysis Static
        set rc [analyze 10]
        set f [open tip.txt w]
        puts $f "$rc [nodeDisp 2 1] [nodeDisp 2 3] [nodeDisp 2 5]"
        close $f
        """;

    [Theory]
    [InlineData("forceBeamColumn")]
    [InlineData("dispBeamColumn")]
    public async Task Bridge_OldBeamSyntax_SameAsOpenSeesExe(string formulation)
    {
        string bridge = OpenSeesPyOrSkip();
        string exe = OpenSeesTestExecutable.ResolveOrSkip();
        string exeDir = Path.Combine(Path.GetTempPath(), $"opencs_exe_{Guid.NewGuid():N}");
        try
        {
            OpenSeesRunResult viaExe = await RunAsync(exe, Cantilever(formulation), exeDir);
            OpenSeesRunResult viaPy = await RunAsync(bridge, Cantilever(formulation));

            Assert.True(viaExe.ExitCode == 0, viaExe.Stderr);
            Assert.True(viaPy.ExitCode == 0, viaPy.Stderr);
            double[] a = Numbers(Path.Combine(exeDir, "tip.txt"));
            double[] b = Numbers(Path.Combine(_dir, "tip.txt"));
            Assert.Equal(0, a[0]);
            Assert.Equal(a.Length, b.Length);
            for (int i = 1; i < a.Length; i++)
                Assert.True(Math.Abs(a[i] - b[i]) <= 1e-9 * Math.Max(1e-12, Math.Abs(a[i])), $"{i}: exe {a[i]}, OpenSeesPy {b[i]}");
            Assert.NotEqual(0, a[1]);
        }
        finally
        {
            try { Directory.Delete(exeDir, recursive: true); } catch (IOException) { }
        }
    }

    static double[] Numbers(string path) =>
        File.ReadAllText(path).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();

    [Fact]
    public async Task Runner_PythonPackagesMode_ReroutesExePathToOpenSeesPy()
    {
        OpenSeesPyOrSkip();
        using var _ = ExternalToolPolicy.Use(ExternalToolSource.PythonPackages);

        // Путь к exe, которого нет: в строгом режиме он не запускается, сценарий исполняет OpenSeesPy.
        OpenSeesRunResult run = await RunAsync(Path.Combine(_dir, "OpenSees.exe"), Truss);

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("disp=0.01", run.Stdout);
    }
}
