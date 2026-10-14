using System.Runtime.InteropServices;
using OpenCS.ExternalTools;
using OpenCS.Gmsh.Runtime;
using OpenCS.OpenSees.Runtime;
using OpenCS.Utilites;

namespace OpenCS.Cli;

sealed partial class CliApp
{
    /// <summary>Внешние программы для этой машины: интерпретаторы Python с пакетами, найденные OpenSees и gmsh.</summary>
    async Task<int> ToolsAsync(CliCall call)
    {
        var candidates = PythonRuntime.Candidates().Select(PythonRuntime.Probe).ToList();
        var openSees = await ProbeOpenSeesAsync(call.Token);
        var gmsh = await ProbeGmshAsync(call.Token);
        string mode = ExternalToolPolicy.Current == ExternalToolSource.PythonPackages ? "python-packages" : "auto";
        string os = $"{RuntimeInformation.OSDescription} {RuntimeInformation.OSArchitecture}";
        string dotnet = RuntimeInformation.FrameworkDescription;

        if (call.Json)
        {
            CliOutput.WriteJson(call.Out, new
            {
                Mode = mode, Os = os, Dotnet = dotnet,
                Python = candidates.Select(p => new
                {
                    Path = p.PythonPath, Version = p.PythonVersion, p.Error,
                    Modules = p.Modules.Values.Select(m => new { m.Name, m.Available, m.Version, m.Error })
                }),
                OpenSees = openSees, Gmsh = gmsh
            });
            return CliExitCode.Ok;
        }

        CliOutput.WritePairs(call.Out,
        [
            (Loc.S("CliToolsMode"), mode == "auto" ? Loc.S("CliToolsModeAuto") : Loc.S("CliToolsModePython")),
            (Loc.S("CliToolsOs"), os),
            (".NET", dotnet),
            ("OpenSees", Describe(openSees)),
            ("Gmsh", Describe(gmsh)),
            (Loc.S("CliToolsArtifacts"), Loc.S("CliToolsArtifactsSession")),
        ]);
        call.Out.WriteLine();
        if (candidates.Count == 0)
        {
            call.Out.WriteLine(string.Format(Loc.S("CliToolsNoPython"), PythonRuntime.PythonEnvironmentVariable));
            return CliExitCode.Ok;
        }
        CliOutput.WriteTable(call.Out,
            ["Python", Loc.S("CliToolsVersion"), .. PythonRuntime.KnownModules],
            candidates.Select(p => (string[])
            [
                p.PythonPath, p.PythonVersion ?? p.Error ?? Loc.S("CliNone"),
                .. PythonRuntime.KnownModules.Select(name => p.Modules.TryGetValue(name, out var m) && m.Available
                    ? m.Version ?? "+"
                    : Loc.S("CliToolsMissing"))
            ]));
        return CliExitCode.Ok;
    }

    sealed record ToolStatus(bool Found, string? Path, string? Source, string? Version, string? Error);

    static string Describe(ToolStatus s) => s.Found
        ? $"{s.Path} ({s.Source}){(s.Version != null ? ", " + s.Version : "")}"
        : string.Format(Loc.S("CliToolsNotFound"), s.Error);

    static async Task<ToolStatus> ProbeOpenSeesAsync(CancellationToken ct)
    {
        try
        {
            var info = await new OpenSeesExecutableResolver().ResolveAndProbeAsync(null, new OpenSeesProcessRunner(), ct);
            return new ToolStatus(true, info.Path, info.Source, FirstLine(info.RawVersionOutput), null);
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or InvalidOperationException
                                       or System.ComponentModel.Win32Exception)
        {
            return new ToolStatus(false, null, null, null, ex.Message);
        }
    }

    static async Task<ToolStatus> ProbeGmshAsync(CancellationToken ct)
    {
        try
        {
            var info = new GmshExecutableResolver().Resolve(null);
            string? version = null;
            try { version = await GmshProcessRunner.ReadVersionAsync(info.Path, TimeSpan.FromSeconds(30), ct); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or TimeoutException) { version = ex.Message; }
            return new ToolStatus(true, info.Path, info.Source, version, null);
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new ToolStatus(false, null, null, null, ex.Message);
        }
    }

    static string? FirstLine(string text) =>
        text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
}
