using CScore.History;
using Xunit;

namespace CScore.Tests.History;

public class SofteningDiagrammTests
{
    static Diagramm ConcreteC() => StagedTestSections.SinglePart().Parts[0].Areas[0].Diagramms[CalcType.C];

    [Fact]
    public void InsideLimit_SameAsInner()
    {
        var inner = ConcreteC();
        var d = new SofteningDiagramm(inner, -0.0035, 0.0005);
        Assert.Equal(inner.SigValue(-0.002), d.SigValue(-0.002), 9);
    }

    [Fact]
    public void BeyondLimit_LinearDropToZero()
    {
        var inner = ConcreteC();
        var d = new SofteningDiagramm(inner, -0.0035, 0.0005);
        double atLimit = inner.SigValue(-0.0035);
        Assert.Equal(atLimit * 0.5, d.SigValue(-0.00375), 6);
        Assert.Equal(0.0, d.SigValue(-0.004), 12);
        Assert.Equal(0.0, d.SigValue(-0.01), 12);
    }

    [Fact]
    public void Tangent_IsConsistentWithStress()
    {
        var d = new SofteningDiagramm(ConcreteC(), -0.0035, 0.0005);
        double e = -0.0037, h = 1e-8;
        d.Sig(e, out double e2, true, true);
        double num = (d.SigValue(e + h) - d.SigValue(e - h)) / (2 * h);
        Assert.Equal(num, e2, 0); // кПа, порядок 1e6–1e7
    }

    [Fact]
    public void CriticalStrains_IncludeSofteningKinks()
    {
        var d = new SofteningDiagramm(ConcreteC(), -0.0035, 0.0005);
        var crit = d.GetCriticalStrains();
        Assert.Contains(-0.0035, crit);
        Assert.Contains(-0.004, crit, new ToleranceComparer(1e-12));
    }

    [Fact]
    public void RebarRupture_TensionSide()
    {
        var rebar = StagedTestSections.SinglePart().Parts[0].Areas[1].Diagramms[CalcType.C];
        var d = new SofteningDiagramm(rebar, 0.025, 0.0005);
        Assert.Equal(rebar.SigValue(0.01), d.SigValue(0.01), 9);
        Assert.Equal(0.0, d.SigValue(0.026), 12);
        Assert.Equal(rebar.SigValue(-0.002), d.SigValue(-0.002), 9); // сжатие не трогаем
    }

    sealed class ToleranceComparer(double tol) : IEqualityComparer<double>
    {
        public bool Equals(double a, double b) => Math.Abs(a - b) <= tol;
        public int GetHashCode(double v) => 0;
    }
}
