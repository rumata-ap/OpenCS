using CScore;
using CSfea.CScoreBridge;
using CSfea.CScoreBridge.Structural;
using OpenCS.Services;
using Xunit;

namespace OpenCS.Tests.FemResult;

/// <summary>
/// Состояние слоистого сечения пластины по записанным деформациям CSfea (спека НДС сечений, срез 3): ЖБ-полоса 6 × 1 м
/// под равномерной нагрузкой (M в пролёте ≈ 2,5·Mcrc, как в RcSecantFieldsTests) — усилия интеграла слоёв против
/// записанных, доли трещин и ψs против записанных расчётом, поворот осей выдачи.
/// </summary>
public sealed class FemCsfeaPlateSectionStateTests
{
    const double B = 1.0, H = 0.2, A = 0.03, AsBot = 7.85e-4, AsTop = 2.5e-4, Span = 6.0;
    const int Nx = 20, Ny = 2;

    static MaterialChars Concrete(CalcType ct) => new()
    {
        Type = MatType.Concrete, TypeCalc = ct, Fc = -18500, Ft = 1550, E = 30_000_000,
        Ec0 = -0.002, Ec1 = -0.00029, Ec1Red = -0.0015, Ec2 = -0.0035,
        Et0 = 0.0001, Et1 = 0.000021, Et1Red = 0.00008, Et2 = 0.00015,
    };

    static MaterialChars Rebar(CalcType ct) => new()
    {
        Type = MatType.ReSteelF, TypeCalc = ct, Fc = -500000, Ft = 500000, E = 200_000_000, Ec2 = -0.0035, Et2 = 0.025,
    };

    static readonly CalcType[] Calcs = [CalcType.C, CalcType.CL, CalcType.N, CalcType.NL];

    static PlateSection Section() => new()
    {
        H = H, NLayers = 40, TensionConcrete = true, PlateModel = "layered", PoissonUncracked = 0.0,
        RebarLayers =
        [
            new PlateRebarLayer { Asx = AsBot, Asy = AsTop, Zsx = -(H / 2 - A), Zsy = -(H / 2 - A - 0.01) },
            new PlateRebarLayer { Asx = AsTop, Asy = AsTop, Zsx = H / 2 - A, Zsy = H / 2 - A - 0.01 },
        ],
    };

    static PlateSectionMaterials Materials() => new()
    {
        ConcreteDiagram = new Material { Type = MatType.Concrete, E = 30_000_000, MaterialChars = Calcs.Select(Concrete).ToList() }
            .GetDiagramms(DiagrammType.L3)![CalcType.N],
        RebarDiagram = new Material { Type = MatType.ReSteelF, E = 200_000_000, MaterialChars = Calcs.Select(Rebar).ToList() }
            .GetDiagramms(DiagrammType.L2)![CalcType.N],
        ConcreteE_MPa = 30000, Nu = 0.2,
    };

    /// <summary>Полоса, опёртая по коротким кромкам, 20 × 2 КЭ; ось x сечения — вдоль полосы.</summary>
    static RcStructuralModel Strip(PlateSection section, PlateSectionMaterials materials, double qKn)
    {
        var m = new RcStructuralModel();
        int Id(int i, int j) => i * (Ny + 1) + j;
        for (int i = 0; i <= Nx; i++)
            for (int j = 0; j <= Ny; j++)
                m.Nodes.Add(new RcNode(Id(i, j), Span * i / Nx, B * j / Ny, 0));
        var sec = new RcShellSection("полоса") { Plate = section, PlateMaterials = materials };
        int e = 0;
        for (int i = 0; i < Nx; i++)
            for (int j = 0; j < Ny; j++)
                m.Shells.Add(new RcShell(e++, [Id(i, j + 1), Id(i, j), Id(i + 1, j), Id(i + 1, j + 1)], sec, [1.0, 0, 0]));
        for (int j = 0; j <= Ny; j++)
        {
            m.Supports.Add(new RcSupport(Id(0, j), 0b000111));
            m.Supports.Add(new RcSupport(Id(Nx, j), 0b000100));
        }
        var lc = new RcLoadCase(1, "q");
        foreach (var sh in m.Shells) lc.Shells.Add(new RcShellLoad(sh.Id, qKn * 1e3, [0, 0, -1]));
        m.LoadCases.Add(lc);
        m.Stages.Add(new RcStage("q", [(1, 1.0)], 4));
        return m;
    }

    [Theory]
    [InlineData(PlateCrackRule.Layer, false, 0.0)]
    [InlineData(PlateCrackRule.Layer, true, 0.0)]
    [InlineData(PlateCrackRule.Section, true, 0.0)]
    [InlineData(PlateCrackRule.Layer, true, 30.0)]
    public void Strip_LayerForcesMatchRecorded(PlateCrackRule rule, bool psi, double angle)
    {
        double q = 8 * 30.0 / (Span * Span);
        var materials = Materials();
        var m = Strip(Section(), materials, q);
        var angles = m.Shells.ToDictionary(s => s.Id, _ => angle);
        var run = RcSecantAnalysis.Run(m, new RcSecantOptions
        {
            RecordSectionStrains = true, Psi = psi, PlateCrackRule = rule, ShellForceAngles = angles,
        });
        var f = run.LastConvergedFields();
        Assert.True(run.Result.Completed, run.Result.Message);
        Assert.NotNull(f);
        Assert.True(f!.HasStrains);

        int cracked = 0;
        for (int e = 0; e < f.ShellIds.Length; e++)
        {
            var s = FemCsfeaPlateSectionState.Compute(Section(), materials, psi, rule, angle, f, e);
            Assert.Equal(40, s.Layers.Count);
            Assert.Equal(4, s.Rebar.Count);
            // Трещины окна — те же, что в расчёте (по деформациям и записанным долям). ψs — в тех же КЭ; само значение
            // у слабо растянутых поперечных стержней зависит от соотношения усилий в момент трещины (не записано).
            Assert.Equal(s.RecordedCrackBottom, s.CrackBottom, 3);
            Assert.Equal(s.RecordedCrackTop, s.CrackTop, 3);
            if (psi) Assert.True(s.PsiMin < 1 == s.RecordedPsiMin < 1, $"КЭ {e}: ψs {s.PsiMin:0.###} / {s.RecordedPsiMin:0.###}");
            // Усилия интеграла слоёв = записанные (истинные усилия расчёта по тем же деформациям).
            Assert.True(s.Discrepancy < 0.02, $"КЭ {e}: Mx {s.Forces[3]:0.###} / {s.Recorded[3]:0.###}, Nx {s.Forces[0]:0.###} / {s.Recorded[0]:0.###}");
            if (s.Cracked) cracked++;
        }
        Assert.True(cracked > 0);

        // Середина пролёта: момент вдоль полосы — направление оси x сечения в осях выдачи (−угол), εs > 0, σs < Rs.
        var mid = FemCsfeaPlateSectionState.Compute(Section(), materials, psi, rule, angle, f, Ny * (Nx / 2 - 1));
        Assert.True(Math.Abs(mid.DirectionDeg + angle) < 0.5, $"направление {mid.DirectionDeg:0.##}°");
        Assert.True(mid.DirectionMoment < 0);   // M = ∫σ·z dz: растянут низ (z < 0)
        var bottom = mid.Rebar.Single(r => r.Layer == 1 && r.AlongX);
        Assert.True(bottom.Eps > 0 && bottom.SigmaMpa is > 0 and < 500);
        Assert.Equal(psi, bottom.HasEpsCrc);
        if (psi) Assert.True(bottom.Psi < 1);
        // Эпюры: низ растянут по направлению момента, верх сжат; треснувший нижний слой не несёт растяжения.
        var low = mid.Layers[0];
        Assert.True(low.EpsDir > 0 && low.Cracked && Math.Abs(low.SigDir) < 1e-6);
        Assert.True(mid.Layers[^1].EpsDir < 0 && mid.Layers[^1].SigDir < 0);
        // По направлению главного момента — как по оси сечения вдоль полосы: σ = σx сечения при любом угле выдачи.
        var top = mid.Layers[^1];
        double ra = angle * Math.PI / 180, c = Math.Cos(ra), sn = Math.Sin(ra);
        Assert.True(Math.Abs(top.SigDir - (top.SigX * c * c + top.SigY * sn * sn - 2 * top.TauXY * sn * c)) < 1e-6 * Math.Abs(top.SigDir));
    }

    [Fact]
    public void PrincipalMoment_LargestByModulus()
    {
        var (a, m) = FemCsfeaPlateSectionState.PrincipalMoment(10, 2, 0);
        Assert.Equal(0, a, 9);
        Assert.Equal(10, m, 9);
        (a, m) = FemCsfeaPlateSectionState.PrincipalMoment(2, -10, 0);
        Assert.Equal(90, a, 9);
        Assert.Equal(-10, m, 9);
        (a, m) = FemCsfeaPlateSectionState.PrincipalMoment(0, 0, 5);
        Assert.Equal(45, Math.Abs(a), 9);
        Assert.Equal(5, Math.Abs(m), 9);
    }
}
