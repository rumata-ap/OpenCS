using CScore;
using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Structural;
using OpenCS.OpenSees.Tcl;
using OpenCS.OpenSees.Tests.Fixtures;
using ShellResult = OpenCS.OpenSees.Structural.ShellResult;

namespace OpenCS.OpenSees.Tests;

public sealed class ShellReferenceValidationTests
{
    [Fact]
    public async Task Q4EndMoment_MatchesEquilibriumAndPlateSectionReference()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        var model = ShellReferenceFixtures.Q4EndMoment();
        await AssertEndMomentReferenceAsync(executable, model, 2 * ShellReferenceFixtures.TipMomentEach);
    }

    [Fact]
    public async Task T3FullEndMoment_MatchesEquilibrium()
    {
        // T3EndMoment — треугольник с ОДНОЙ свободной вершиной (не равномерной по ширине
        // свободной гранью, как у Q4): весь момент прикладывается в одном узле, поэтому
        // поточечное сравнение Mx с «appliedMoment/ширина» не имеет физического смысла
        // для этой геометрии (см. ShellReferenceFixtures — T3 не даёт uniform-width tip).
        // Глобальное равновесие реакций остаётся точной проверкой независимо от геометрии.
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        var model = ShellReferenceFixtures.T3EndMoment(ShellIntegrationPolicy.Full);
        await AssertEquilibriumOnlyAsync(executable, model, 2 * ShellReferenceFixtures.TipMomentEach);
    }

    [Fact]
    public async Task T3ReducedEndMoment_MatchesEquilibrium()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        var model = ShellReferenceFixtures.T3EndMoment(ShellIntegrationPolicy.Reduced);
        await AssertEquilibriumOnlyAsync(executable, model, 2 * ShellReferenceFixtures.TipMomentEach);
    }

    private static async Task<ShellResult> RunAsync(string executable, ShellOpenSeesModel model)
    {
        using var fixture = new ShellArtifactFixture();
        string scriptPath = Path.Combine(fixture.Directory, "script.tcl");
        File.WriteAllText(scriptPath, new ShellTclGenerator().Generate(model));

        OpenSeesRunResult run = await new OpenSeesProcessRunner().RunAsync(
            new OpenSeesRunRequest
            {
                ExecutablePath = executable,
                WorkingDirectory = fixture.Directory,
                ScriptPath = scriptPath,
                Timeout = TimeSpan.FromSeconds(30)
            }, CancellationToken.None);

        Assert.Equal(0, run.ExitCode);
        ShellResult result = new ShellResultParser().Parse(
            fixture.Directory, model.Elements.ToDictionary(e => e.Tag));
        Assert.Equal("completed", result.Status);
        return result;
    }

    private static void AssertGlobalEquilibrium(ShellResult result, double appliedMoment)
    {
        double reactionFx = result.Reactions.Sum(r => r.Fx);
        double reactionFy = result.Reactions.Sum(r => r.Fy);
        double reactionFz = result.Reactions.Sum(r => r.Fz);
        double reactionMomentY = result.Reactions.Sum(r => r.My);

        Assert.True(Math.Abs(reactionFx) < 1e-6, $"Fx reactions должны быть ~0, получено {reactionFx}");
        Assert.True(Math.Abs(reactionFy) < 1e-6, $"Fy reactions должны быть ~0, получено {reactionFy}");
        Assert.True(Math.Abs(reactionFz) < 1e-6, $"Fz reactions должны быть ~0 (чистый момент, без сдвига), получено {reactionFz}");
        Assert.True(Math.Abs(reactionMomentY + appliedMoment) < 1e-6 * appliedMoment,
            $"Равновесие моментов: reactionMomentY={reactionMomentY}, appliedMoment={appliedMoment}");
    }

    private static async Task AssertEquilibriumOnlyAsync(string executable, ShellOpenSeesModel model, double appliedMoment)
    {
        ShellResult result = await RunAsync(executable, model);
        AssertGlobalEquilibrium(result, appliedMoment);
    }

    private static async Task AssertEndMomentReferenceAsync(string executable, ShellOpenSeesModel model, double appliedMoment)
    {
        ShellResult result = await RunAsync(executable, model);
        AssertGlobalEquilibrium(result, appliedMoment);

        double expectedMx = appliedMoment / ShellReferenceFixtures.Width;
        // Постоянный изгибающий момент без сдвига — Q4 (uniform-width tip, момент поровну
        // на 2 узлах) воспроизводит его практически с машинной точностью (замерено ~5e-14).
        foreach (var section in result.SectionResultants)
            Assert.True(Math.Abs(section.Mx - expectedMx) < 1e-6 * Math.Abs(expectedMx),
                $"Mx элемента {section.ElementTag}, точка {section.IntegrationPoint}: {section.Mx}, ожидание {expectedMx}");

        double thetaYFree = result.Displacements
            .Where(d => model.Nodes.First(n => n.Tag == d.NodeTag).X > 0)
            .Average(d => d.Ry);
        double curvatureX = thetaYFree / ShellReferenceFixtures.Length;

        // Допуск 10% — грубая оценка кривизны через нодальный поворот/длину (не через
        // истинное assumed-strain поле элемента) даёт замеренное отклонение ~6.6%.
        double rawReferenceMx = ComputeReferenceMx(curvatureX);
        double referenceMxNewtonMeters = rawReferenceMx * 1000.0;
        Assert.True(Math.Abs(expectedMx - referenceMxNewtonMeters) < 0.1 * Math.Abs(expectedMx),
            $"Reference PlateSection Mx={referenceMxNewtonMeters} (raw kN*m/m={rawReferenceMx}), curvatureX={curvatureX}, thetaYFree={thetaYFree}, OpenSees ожидание={expectedMx}");
    }

    private static double ComputeReferenceMx(double curvatureX)
    {
        // PlateSection.Compute() интегрирует σ в кПа (см. комментарий в
        // CScore/PlateSection.cs:IntegrateConcreteLayered — «σ [кПа = кН/м²]»), а не в МПа,
        // как документирован generic Material.E (тот XML-doc относится к волоконному
        // методу CrossSection). Подтверждено эмпирически: E, поданный в кПа, воспроизводит
        // классическую формулу Mx = E·κ·h³/12 с точностью ~0.04% (NLayers=50).
        var plateSection = new PlateSection
        {
            H = ShellReferenceFixtures.Thickness, NLayers = 50, TensionConcrete = true
        };
        Diagramm linear = LinearElasticDiagram(ShellReferenceFixtures.E / 1000.0);
        var state = new ShellStrainState(0, 0, 0, curvatureX, 0, 0);
        return plateSection.Compute(state, linear, linear, null, computeStiffness: false).Mx;
    }

    private static Diagramm LinearElasticDiagram(double e_kPa)
    {
        MaterialChars Ch(CalcType ct) => new(ct)
        {
            E = e_kPa, Ry = e_kPa / 50, Ru = e_kPa / 50, Ft = e_kPa / 50, Fc = -e_kPa / 50,
            Ec2 = -0.05, Et2 = 0.05, Type = MatType.ReSteelF,
        };
        var m = new Material { Id = 1, E = e_kPa, Type = MatType.ReSteelF, Tag = "reference-linear" };
        m.MaterialChars = [Ch(CalcType.C), Ch(CalcType.CL), Ch(CalcType.N), Ch(CalcType.NL)];
        return m.GetDiagramms(DiagrammType.L2)![CalcType.C];
    }
}
