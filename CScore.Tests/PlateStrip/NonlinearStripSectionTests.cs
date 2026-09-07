using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

/// <summary>Срез 7, Task 2: состояние-зависимая редукция полосы в стержневое сечение.</summary>
public sealed class NonlinearStripSectionTests
{
    const double E = 30_000.0;
    const double H = 0.3;

    [Fact]
    public void LinearSource_QEqualsKTimesState()
    {
        var source = Linear();
        foreach (var state in new[]
                 {
                     new BeamStrainState(0.001, 0.0, 0.0),
                     new BeamStrainState(0.0, 0.002, 0.0),
                     new BeamStrainState(0.0, 0.0, 0.003),
                     new BeamStrainState(-0.0005, 0.004, -0.002),
                 })
        {
            var response = NonlinearStripSection.Evaluate(2.0, [source, source], state);

            var vector = new[] { state.Eps0, state.KappaY, state.KappaZ };
            for (int i = 0; i < 3; i++)
            {
                double expected = 0.0;
                for (int j = 0; j < 3; j++)
                    expected += response.K[i, j] * vector[j];
                Assert.Equal(expected, response.Q[i], 9);
            }
        }
    }

    [Fact]
    public void Q_MatchesStripResultantIntegrator_TheReferenceFormula()
    {
        // StripResultantIntegrator (Срез 3a) остаётся точкой правды формулы Q = ∫Bᵀσ dA;
        // Evaluate считает то же за один проход и не имеет права с ней разойтись.
        var stiff = Linear(a00: 2000.0, d00: 300.0);
        var soft = Linear(a00: 200.0, d00: 30.0);
        var state = new BeamStrainState(0.0012, 0.0031, -0.0007);

        var reference = StripResultantIntegrator.Integrate(2.0, [stiff, soft], state);
        var response = NonlinearStripSection.Evaluate(2.0, [stiff, soft], state);

        Assert.Equal(reference[0], response.Q[0], 12);
        Assert.Equal(reference[1], response.Q[1], 12);
        Assert.Equal(reference[2], response.Q[2], 12);
    }

    [Fact]
    public void Q_MatchesReferenceFormula_OnNonlinearSource()
    {
        var source = Nonlinear();
        var state = new BeamStrainState(0.0004, 0.03, 0.0);

        var reference = StripResultantIntegrator.Integrate(2.0, [source, source], state);
        var response = NonlinearStripSection.Evaluate(2.0, [source, source], state);

        Assert.Equal(reference[0], response.Q[0], 9);
        Assert.Equal(reference[1], response.Q[1], 9);
        Assert.Equal(reference[2], response.Q[2], 9);
    }

    [Fact]
    public void NonlinearSource_TangentChangesWithState()
    {
        var source = Nonlinear();

        var atZero = NonlinearStripSection.Evaluate(2.0, [source, source], BeamStrainState.Zero);
        var beyondYield = NonlinearStripSection.Evaluate(
            2.0, [source, source], new BeamStrainState(0.0, 0.05, 0.0));

        Assert.NotEqual(atZero.K[1, 1], beyondYield.K[1, 1], 3);
        Assert.True(Math.Abs(beyondYield.K[1, 1]) < Math.Abs(atZero.K[1, 1]),
            "После излома изгибная жёсткость полосы обязана падать.");
    }

    [Fact]
    public void NonlinearSource_QIsNotLinearInState()
    {
        var source = Nonlinear();
        var single = NonlinearStripSection.Evaluate(2.0, [source, source], new BeamStrainState(0, 0.03, 0));
        var doubled = NonlinearStripSection.Evaluate(2.0, [source, source], new BeamStrainState(0, 0.06, 0));

        Assert.True(Math.Abs(doubled.Q[1]) < 2.0 * Math.Abs(single.Q[1]),
            "При удвоении кривизны за изломом момент обязан расти медленнее, чем вдвое.");
    }

    [Fact]
    public void LinearSource_TangentIsSymmetric()
    {
        var response = NonlinearStripSection.Evaluate(
            2.0, [Linear(), Linear()], new BeamStrainState(0.001, 0.002, 0.003));

        Assert.Equal(response.K[0, 1], response.K[1, 0], 12);
        Assert.Equal(response.K[0, 2], response.K[2, 0], 12);
        Assert.Equal(response.K[1, 2], response.K[2, 1], 12);
    }

    [Fact]
    public void ZeroState_LinearSource_GivesZeroForces()
    {
        var response = NonlinearStripSection.Evaluate(2.0, [Linear(), Linear()], BeamStrainState.Zero);

        Assert.Equal(0.0, response.Q[0], 12);
        Assert.Equal(0.0, response.Q[1], 12);
        Assert.Equal(0.0, response.Q[2], 12);
    }

    [Fact]
    public void Tangent_MatchesEquivalentSectionCalculator_AtZeroState()
    {
        // Прямая проверка, что вынос тела не изменил линейную сборку Среза 2.
        var source = Linear();
        var response = NonlinearStripSection.Evaluate(2.0, [source, source], BeamStrainState.Zero);
        var build = EquivalentSectionCalculator.Build(
            Analogy(), source, [source, source], ReductionPolicy.ConstitutiveIntegration, 2);

        Assert.True(build.IsCalculable);
        for (int i = 0; i < 3; i++)
        for (int j = 0; j < 3; j++)
            Assert.Equal(build.Section!.BeamTangent[i, j], response.K[i, j], 12);
    }

    [Fact]
    public void InvalidInputs_Throw()
    {
        var source = Linear();
        var state = new BeamStrainState(0.001, 0, 0);

        Assert.Throws<ArgumentNullException>(() => NonlinearStripSection.Evaluate(2.0, null!, state));
        Assert.Throws<ArgumentException>(() => NonlinearStripSection.Evaluate(2.0, [], state));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => NonlinearStripSection.Evaluate(0.0, [source, source], state));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => NonlinearStripSection.Evaluate(double.NaN, [source, source], state));
        Assert.Throws<ArgumentException>(() => NonlinearStripSection.Evaluate(
            2.0, [source, source], new BeamStrainState(double.NaN, 0, 0)));
    }

    static ConstantLinearPlateSectionResponse Linear(double a00 = 1000.0, double d00 = 300.0)
    {
        var a = new double[3, 3];
        var b = new double[3, 3];
        var d = new double[3, 3];
        var ass = new double[2, 2];
        a[0, 0] = a00;
        b[0, 0] = 20.0;
        d[0, 0] = d00;
        a[1, 1] = 500.0;
        d[1, 1] = 100.0;
        ass[0, 0] = ass[1, 1] = 400.0;
        return new ConstantLinearPlateSectionResponse(a, b, d, ass, "source-fp");
    }

    static PlateSectionLiveResponse Nonlinear()
    {
        MaterialChars Ch(CalcType ct) => new(ct)
        {
            E = E, Ry = 30.0, Ru = 30.0, Ft = 30.0, Fc = -30.0,
            Ec2 = -0.05, Et2 = 0.05, Type = MatType.ReSteelF,
        };
        var m = new Material { Id = 1, E = E, Type = MatType.ReSteelF, Tag = "bilinear" };
        m.MaterialChars = [Ch(CalcType.C), Ch(CalcType.CL), Ch(CalcType.N), Ch(CalcType.NL)];
        var diagram = m.GetDiagramms(DiagrammType.L2)![CalcType.C];
        var section = new PlateSection { H = H, NLayers = 20, TensionConcrete = true, PlateModel = "layered" };
        return new PlateSectionLiveResponse(section, diagram, diagram);
    }

    static PlateStripBeamAnalogy Analogy() => new()
    {
        Id = "strip-1",
        SourceRegionId = 10,
        ExplicitWidthM = 2.0,
        Fingerprint = "strip-fp",
        Geometry = new PlateStripGeometry { LengthM = 6.0 }
    };
}
