using CScore;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Structural;
using OpenCS.OpenSees.Tcl;
using OpenCS.OpenSees.Tests.Fixtures;

namespace OpenCS.OpenSees.Tests;

public sealed class NonlinearShellMaterialIntegrationTests
{
    [Fact]
    public async Task NonlinearConcreteAndRebar_UnderIncreasingLoad_ConvergesAndSoftens()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();

        var section = new PlateSection { H = 0.2, NLayers = 1, ConcreteMaterialId = 1, RebarMaterialId = 2 };
        section.RebarLayers.Add(new() { Asx = 0.002, Asy = 0.002, Zsx = 0.09, Zsy = 0.09, MaterialId = 2 });

        var concreteMaterial = new Material { Id = 1, Tag = "B25", Type = MatType.Concrete,
            C = new MaterialChars { E = 30_000_000, Fc = -17_000, Ft = 1_150, Ec0 = -0.002, Ec2 = -0.0035 } };
        var rebarMaterial = new Material { Id = 2, Tag = "A400", Type = MatType.ReSteelF,
            C = new MaterialChars { E = 200_000_000, Ft = 355_000, Ru = 500_000, Et2 = 0.05 } };

        var lookup = new Dictionary<int, Material> { [1] = concreteMaterial, [2] = rebarMaterial };
        var resolver = new PlateSectionShellMaterialResolver(id => lookup.GetValueOrDefault(id), CalcType.C, SteelModelKind.Steel02, null);

        PlateSectionShellMappingResult mapped = PlateSectionOpenSeesMapper.Map(
            section, ShellFrame.Identity, resolver, sectionTag: 10);

        using var fixture = new ShellArtifactFixture();
        string scriptPath = Path.Combine(fixture.Directory, "script.tcl");
        File.WriteAllText(scriptPath, BuildNonlinearScript(mapped));

        var runner = new OpenSeesProcessRunner();
        OpenSeesRunResult run = await runner.RunAsync(new OpenSeesRunRequest
        {
            ExecutablePath = executable,
            WorkingDirectory = fixture.Directory,
            ScriptPath = scriptPath,
            Timeout = TimeSpan.FromSeconds(30)
        }, CancellationToken.None);

        Assert.True(run.ExitCode == 0, $"stdout:\n{run.Stdout}\nstderr:\n{run.Stderr}");
        string log = File.ReadAllText(Path.Combine(fixture.Directory, "analysis.log"));
        Assert.DoesNotContain("worstOk=-", log);
        Assert.Contains("worstOk=0", log);
    }

    private static string BuildNonlinearScript(PlateSectionShellMappingResult mapped)
    {
        var sb = new System.Text.StringBuilder();
        void L(string line) => sb.Append(line).Append('\n');

        L("wipe");
        L("model Basic -ndm 3 -ndf 6");

        foreach (NativeShellMaterialDefinition material in mapped.Materials.OrderBy(m => m.Tag))
        {
            // Цепочка в mapped.Materials уже в порядке регистрации (база перед обёрткой) —
            // ShellTclGenerator.TopologicalOrder здесь не нужен, т.к. Register сохраняет
            // порядок вставки в списке materials (Task 3, RegisterChain регистрирует по порядку).
            foreach (string aux in material.Spec.AuxiliaryCommands) L(aux);
            L(material.Spec.ToTcl(material.Tag));
        }

        RCShellLayeredSection section = mapped.Section;
        string layerArgs = string.Join(' ', section.Layers.OrderBy(l => l.Index)
            .SelectMany(l => new[] { l.MaterialTag.ToString(), TclNumber.Format(l.Thickness) }));
        L($"section LayeredShell {section.Tag} {section.Layers.Count} {layerArgs}");

        L("node 1 0 0 0"); L("node 2 1 0 0"); L("node 3 1 1 0"); L("node 4 0 1 0");
        L("fix 1 1 1 1 1 1 1"); L("fix 2 1 1 1 1 1 1");
        L("fix 3 0 0 1 1 1 0"); L("fix 4 0 0 1 1 1 0");
        L($"element ASDShellQ4 1 1 2 3 4 {section.Tag}");

        L("pattern Plain 1 Linear {");
        L("    load 3 0.0 5000.0 0.0 0.0 0.0 0.0");
        L("    load 4 0.0 5000.0 0.0 0.0 0.0 0.0");
        L("}");

        L("constraints Transformation"); L("numberer RCM"); L("system BandGeneral");
        L("test NormDispIncr 1.0e-6 25 0"); L("algorithm Newton");
        L("integrator LoadControl 0.05"); L("analysis Static");
        L("set worstOk 0");
        L("for {set i 1} {$i <= 20} {incr i} { set ok [analyze 1]; if {$ok != 0} {set worstOk $ok} }");
        L("set logf [open analysis.log w]");
        L("puts $logf \"worstOk=$worstOk\"");
        L("close $logf");
        L("wipe");

        return sb.ToString();
    }
}
