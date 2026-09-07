using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

/// <summary>Срез 7, Task 1: нелинейный (не замороженный) источник плитного отклика.
/// Контраст с PlateSectionTangentSnapshot, который по контракту заморожен в нулевом состоянии.</summary>
public sealed class PlateSectionLiveResponseTests
{
    // Билинейный материал с ранним изломом: eps_y = Ry/E = 30/30000 = 0.001.
    // Такой предел достигается уже при умеренной кривизне, поэтому нелинейность
    // проявляется на состояниях, безопасных для квадратуры по толщине.
    const double E = 30_000.0;
    const double Ry = 30.0;
    const double H = 0.3;

    [Fact]
    public void Tangent_NonlinearDiagram_ChangesWithState()
    {
        var source = Live(Nonlinear());

        var atZero = source.Tangent(ShellStrainState.Zero);
        var beyondYield = source.Tangent(new ShellStrainState(0, 0, 0, 0.05, 0, 0));

        Assert.NotEqual(atZero.D[0, 0], beyondYield.D[0, 0], 6);
        Assert.True(Math.Abs(beyondYield.D[0, 0]) < Math.Abs(atZero.D[0, 0]),
            "После излома изгибная жёсткость обязана падать, а не расти.");
    }

    [Fact]
    public void Tangent_LinearDiagram_DoesNotChangeWithState()
    {
        var source = Live(Linear());

        var atZero = source.Tangent(ShellStrainState.Zero);
        var strained = source.Tangent(new ShellStrainState(0.0002, 0, 0, 0.01, 0, 0));

        Assert.Equal(atZero.A[0, 0], strained.A[0, 0], 3);
        Assert.Equal(atZero.D[0, 0], strained.D[0, 0], 3);
    }

    [Fact]
    public void Forces_MatchesPlateSectionComputeWithoutStiffness()
    {
        var section = Section();
        var diagram = Nonlinear();
        var source = new PlateSectionLiveResponse(section, diagram, diagram);
        var state = new ShellStrainState(0.0003, 0, 0, 0.02, 0, 0);

        var forces = source.Forces(state);
        var expected = section.Compute(state, diagram, diagram, null, computeStiffness: false);

        Assert.Equal(expected.Nx, forces.Nx, 9);
        Assert.Equal(expected.Ny, forces.Ny, 9);
        Assert.Equal(expected.Nxy, forces.Nxy, 9);
        Assert.Equal(expected.Mx, forces.Mx, 9);
        Assert.Equal(expected.My, forces.My, 9);
        Assert.Equal(expected.Mxy, forces.Mxy, 9);
    }

    [Fact]
    public void Tangent_CarriesSameForcesAsForces()
    {
        // A.2 опирается на это тождество: Evaluate берёт Q и K из одного вызова Tangent,
        // не делая второго прогона через Forces.
        var source = Live(Nonlinear());
        var state = new ShellStrainState(0.0003, 0, 0, 0.02, 0, 0);

        var forces = source.Forces(state);
        var tangent = source.Tangent(state);

        Assert.Equal(forces.Nx, tangent.Nx, 9);
        Assert.Equal(forces.Mx, tangent.Mx, 9);
        Assert.Equal(forces.My, tangent.My, 9);
    }

    [Fact]
    public void BeforeCracking_MatchesFrozenSnapshot_AfterCracking_IsSofter()
    {
        var diagram = Nonlinear();
        var live = Live(diagram);
        var frozen = PlateSectionTangentSnapshot.Create(Section(), diagram, diagram);
        Assert.True(frozen.IsCalculable && frozen.Source != null);

        var small = new ShellStrainState(0, 0, 0, 1e-5, 0, 0);
        Assert.Equal(frozen.Source!.Forces(small).Mx, live.Forces(small).Mx, 6);

        var large = new ShellStrainState(0, 0, 0, 0.05, 0, 0);
        double frozenMx = Math.Abs(frozen.Source.Forces(large).Mx);
        double liveMx = Math.Abs(live.Forces(large).Mx);
        Assert.True(liveMx < frozenMx,
            $"Нелинейный источник обязан быть мягче замороженного: {liveMx} vs {frozenMx}.");
    }

    [Fact]
    public void SourceKind_IsPlateSectionLive()
    {
        Assert.Equal(EquivalentSectionSourceKind.PlateSectionLive, Live(Nonlinear()).SourceKind);
    }

    [Fact]
    public void Fingerprint_IsDeterministic_AndIndependentOfEvaluatedState()
    {
        var diagram = Nonlinear();
        var a = Live(diagram);
        var b = Live(diagram);

        string before = a.Fingerprint;
        a.Forces(new ShellStrainState(0, 0, 0, 0.05, 0, 0));

        Assert.Equal(before, a.Fingerprint);
        Assert.Equal(b.Fingerprint, a.Fingerprint);
    }

    [Fact]
    public void Fingerprint_DiffersOnSectionChange()
    {
        var diagram = Nonlinear();
        var thin = new PlateSectionLiveResponse(Section(h: 0.2), diagram, diagram);
        var thick = new PlateSectionLiveResponse(Section(h: 0.4), diagram, diagram);

        Assert.NotEqual(thin.Fingerprint, thick.Fingerprint);
    }

    [Fact]
    public void Constructor_NullArguments_Throw()
    {
        var diagram = Nonlinear();
        Assert.Throws<ArgumentNullException>(() => new PlateSectionLiveResponse(null!, diagram, diagram));
        Assert.Throws<ArgumentNullException>(() => new PlateSectionLiveResponse(Section(), null!, diagram));
        Assert.Throws<ArgumentNullException>(() => new PlateSectionLiveResponse(Section(), diagram, null!));
    }

    [Fact]
    public void NonFiniteState_Throws()
    {
        var source = Live(Nonlinear());
        var bad = new ShellStrainState(double.NaN, 0, 0, 0, 0, 0);

        Assert.Throws<ArgumentException>(() => source.Forces(bad));
        Assert.Throws<ArgumentException>(() => source.Tangent(bad));
    }

    static PlateSectionLiveResponse Live(Diagramm diagram) =>
        new(Section(), diagram, diagram);

    static PlateSection Section(double h = H) =>
        new() { H = h, NLayers = 20, TensionConcrete = true, PlateModel = "layered" };

    /// <summary>Билинейная диаграмма с площадкой текучести — касательная меняется на изломе.</summary>
    static Diagramm Nonlinear() => Build(Ry);

    /// <summary>Тот же материал с недостижимо высоким пределом — отклик линеен во всём
    /// рабочем диапазоне теста.</summary>
    static Diagramm Linear() => Build(600.0);

    static Diagramm Build(double ry)
    {
        MaterialChars Ch(CalcType ct) => new(ct)
        {
            E = E, Ry = ry, Ru = ry, Ft = ry, Fc = -ry,
            Ec2 = -0.05, Et2 = 0.05, Type = MatType.ReSteelF,
        };
        var m = new Material { Id = 1, E = E, Type = MatType.ReSteelF, Tag = "bilinear" };
        m.MaterialChars = [Ch(CalcType.C), Ch(CalcType.CL), Ch(CalcType.N), Ch(CalcType.NL)];
        return m.GetDiagramms(DiagrammType.L2)![CalcType.C];
    }
}
