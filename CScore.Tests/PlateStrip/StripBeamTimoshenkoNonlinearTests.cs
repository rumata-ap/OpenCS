using System.Linq;
using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

/// <summary>Срез 8b.3: нелинейная формулировка Тимошенко — кручение и сдвиг по текущему
/// состоянию слоёв.</summary>
public sealed class StripBeamTimoshenkoNonlinearTests
{
    const double Width = 2.0;
    const double Length = 6.0;
    const double E = 30_000.0;
    const double H = 0.3;
    const double Q = 0.24; // как у StripBeamNonlinearModelTests: заходит в пластику, но не вся

    static PlateSectionLiveResponse Live(double ry = 30.0, double hardening = 0.1)
    {
        MaterialChars Ch(CalcType ct) => new(ct)
        {
            E = E, Ry = ry, Ru = ry * (1.0 + hardening), Ft = ry, Fc = -ry,
            Ec2 = -0.05, Et2 = 0.05, Type = MatType.ReSteelF,
        };
        var m = new Material { Id = 1, E = E, Type = MatType.ReSteelF, Tag = "bilinear" };
        m.MaterialChars = [Ch(CalcType.C), Ch(CalcType.CL), Ch(CalcType.N), Ch(CalcType.NL)];
        var diagram = m.GetDiagramms(DiagrammType.L2)![CalcType.C];
        var section = new PlateSection { H = H, NLayers = 12, TensionConcrete = true, PlateModel = "layered" };
        return new PlateSectionLiveResponse(section, diagram, diagram);
    }

    static StripSectionSourceGrid Grid(IPlateSectionResponse source, int elements) =>
        StripSectionSourceGrid.Uniform([source, source], elements);

    static List<double> Stations(int elements) =>
        Enumerable.Range(0, elements + 1).Select(i => (double)i / elements).ToList();

    static StripLoadSet Uniform(double q) =>
        new([new StripLoad { SourceTag = "q", Kind = StripLoadKind.DistributedUniform, QzKnM = -q }]);

    static readonly StripBeamSupportScheme Pinned = StripBeamSupportScheme.SimplySupported;

    [Fact]
    public void LinearPlate_NonlinearLaw_MatchesLinearTimoshenkoModel()
    {
        var source = Live();
        var t = source.Tangent(ShellStrainState.Zero);
        var linear = new ConstantLinearPlateSectionResponse(t.A, t.B, t.D, t.As, "lin");
        var stations = Stations(4);

        var analogy = new PlateStripBeamAnalogy
        {
            Id = "s", SourceRegionId = 1, ExplicitWidthM = Width, Fingerprint = "f",
            Geometry = new PlateStripGeometry { LengthM = Length }
        };
        var section = EquivalentSectionCalculator.Build(
            analogy, linear, [linear, linear], ReductionPolicy.ConstitutiveIntegration, 2,
            StripBeamFormulation.Timoshenko).Section!;

        var reference = StripBeamTimoshenkoModel.SolveLinear(
            section.BeamTangent, section.TorsionalStiffness, section.ShearStiffnessGAy,
            section.ShearStiffnessGAz, Length, stations, Pinned, Uniform(10.0));
        var law = new NonlinearStripTimoshenkoSectionLaw(Grid(linear, 4), Width);
        var nonlinear = StripBeamTimoshenkoModel.Solve(law, Length, stations, Pinned, Uniform(10.0));

        Assert.True(nonlinear.IsCalculable);
        for (int i = 0; i < reference.Displacements.Length; i++)
            Assert.Equal(reference.Displacements[i], nonlinear.Displacements[i], 9);
    }

    [Fact]
    public void ThickPlate_ShearAddsDeflection_OverEulerBernoulliNonlinear()
    {
        var source = Live();
        var stations = Stations(6);

        var bernoulli = StripBeamNonlinearModel.Solve(
            Grid(source, 6), Width, Length, stations, Pinned, Uniform(Q));
        var law = new NonlinearStripTimoshenkoSectionLaw(Grid(source, 6), Width);
        var timoshenko = StripBeamTimoshenkoModel.Solve(law, Length, stations, Pinned, Uniform(Q));

        Assert.True(bernoulli.IsCalculable);
        Assert.True(timoshenko.IsCalculable);
        Assert.Equal(1.0, timoshenko.AchievedLoadFactor, 12);
        double wb = Math.Abs(bernoulli.Displacements[3 * 5 + 2]);
        double wt = Math.Abs(timoshenko.Displacements[3 * 6 + 2]);
        Assert.True(wt > wb, "Сдвиг обязан увеличить прогиб.");
        Assert.True(wt < 1.5 * wb, "Для L/h = 20 добавка сдвига умеренна.");
        // момент в середине — равновесный qL²/8, не зависит от формулировки
        Assert.Equal(Q * Length * Length / 8.0, Math.Abs(timoshenko.StationResultants[3][1]), 6);
    }

    [Fact]
    public void Torsion_EndTorque_IsMonotonicAndSolved()
    {
        var law = new NonlinearStripTimoshenkoSectionLaw(Grid(Live(), 3), Width);
        var stations = Stations(3);
        double previous = 0.0;
        foreach (double torque in new[] { 0.05, 0.1, 0.2 })
        {
            var r = StripBeamTimoshenkoModel.Solve(
                law, Length, stations, Pinned, new StripLoadSet([]),
                new KnownEndActions(EndT: torque), null, null, StripTorsionRestraint.StartOnly);
            Assert.True(r.IsCalculable);
            double twist = r.Displacements[3 * 6 + 3];
            Assert.True(twist > previous);
            Assert.All(r.StationResultants, s => Assert.Equal(torque, s[3], 6));
            previous = twist;
        }
    }

    [Fact]
    public void Law_TangentMatchesFiniteDifferenceOfResultants()
    {
        var source = Live();
        var law = new NonlinearStripTimoshenkoSectionLaw(Grid(source, 1), Width);
        double[] state = [0.0004, 0.02, 0.003, 0.002];

        var center = law.Evaluate(0, state[0], state[1], state[2], state[3]);
        for (int j = 0; j < 4; j++)
        {
            double h = 1e-7;
            var plus = (double[])state.Clone();
            var minus = (double[])state.Clone();
            plus[j] += h;
            minus[j] -= h;
            var qp = law.Evaluate(0, plus[0], plus[1], plus[2], plus[3]).Q;
            var qm = law.Evaluate(0, minus[0], minus[1], minus[2], minus[3]).Q;
            for (int i = 0; i < 4; i++)
            {
                double numeric = (qp[i] - qm[i]) / (2.0 * h);
                double scale = Math.Max(Math.Abs(center.K[i, i]), Math.Abs(center.K[j, j]));
                Assert.True(Math.Abs(numeric - center.K[i, j]) <= 1e-4 * scale,
                    $"K[{i},{j}]: аналитическая {center.K[i, j]}, численная {numeric}");
            }
        }
    }

    [Fact]
    public void ShearStiffness_DegradesWithMembraneDamage()
    {
        var law = new NonlinearStripTimoshenkoSectionLaw(Grid(Live(ry: 3.0, hardening: 0.0), 1), Width);

        var intact = law.Evaluate(0, 0, 0, 0, 0);
        var damaged = law.Evaluate(0, 0.0, 0.2, 0.0, 0.0);

        Assert.True(damaged.GAz <= intact.GAz);
    }
}
