using CScore;
using CScore.Sp63.Normal;
using Xunit;

namespace CScore.Tests.Sp63Normal;

/// <summary>
/// Сверка альтернативного метода п. 8.1.16 с примером 26 Пособия к СП 63.13330.2018
/// (п. 3.2.45, формула (3.99)): колонна 400×400 мм, B25, A400, H = l0 = 6 м,
/// N = 2200 кН, M = 20 кН·м (e0 = 9 мм &lt; ea = h/30 = 13,3 мм), Nl = 1980 кН.
/// Пособие находит требуемую площадь As,tot = 571,3 мм² (кратковременное действие,
/// φ = 0,875) и 875 мм² (длительное, φ = 0,83, γb1·Rb = 0,9·14,5 МПа). При этих
/// площадях Nult по (8.17) должно совпасть с N.
/// </summary>
public sealed class Sp63AlternativeCompressionHandbookTests
{
    const double Rb = 14_500.0;   // кПа
    const double Rs = 340_000.0;  // кПа

    [Fact]
    public void Example26_ShortTerm_RequiredAreaGivesNultEqualN()
    {
        var detail = CheckExample(asTot: 571.3e-6, n: 2200.0, m: 20.0, CalcType.C);

        Assert.Equal(0.875, detail.Variables["phi"], 6);
        Assert.Equal(0.0, detail.Variables["phiLongTerm"]);
        Assert.Equal(2200.0, detail.Allowable, 0);
    }

    [Fact]
    public void Example26_LongTerm_RequiredAreaGivesNultEqualNl()
    {
        var detail = CheckExample(asTot: 875e-6, n: 1980.0, m: 0.0, CalcType.CL);

        Assert.Equal(0.83, detail.Variables["phi"], 6);
        Assert.Equal(25.0, detail.Variables["concreteClass"]);
        Assert.Equal(1980.0, detail.Allowable, 0);
    }

    [Fact]
    public void Example26_AcceptedReinforcement_4d18_Passes()
    {
        // Принято 4⌀18 = 1018 мм².
        var shortTerm = CheckExample(asTot: 1018e-6, n: 2200.0, m: 20.0, CalcType.C);
        var longTerm = CheckExample(asTot: 1018e-6, n: 1980.0, m: 0.0, CalcType.CL);

        Assert.True(shortTerm.Passed);
        Assert.True(longTerm.Passed);
    }

    static CheckDetail CheckExample(double asTot, double n, double m, CalcType calc)
    {
        var concrete = Sp63NormalFixtures.Concrete(rb: Rb);
        concrete.Tag = "B25";
        var concreteLong = concrete.C!;
        concreteLong.Fc = -0.9 * Rb;
        concrete.CL = concreteLong;
        concrete.C!.Fc = -Rb;

        var rebar = Sp63NormalFixtures.Rebar(2, rs: Rs, rsc: Rs);
        rebar.CL = rebar.C;

        var section = new CrossSection { Tag = "example-26" };
        section.Areas.Add(Sp63NormalFixtures.ConcreteRegion(concrete,
            [(-0.2, -0.2), (0.2, -0.2), (0.2, 0.2), (-0.2, 0.2)]));
        double bar = asTot / 4.0;
        foreach (double x in new[] { -0.15, 0.15 })
        foreach (double y in new[] { -0.16, 0.16 })
            Sp63NormalFixtures.AddBar(section, x, y, bar, rebar, diameter: 0.018);

        var options = new Sp63NormalOptions(
            Sp63NormalShapeKind.Rectangular,
            Sp63NormalAxis.Mx,
            new Sp63MemberContext(6.0, Sp63StructuralScheme.StaticallyIndeterminate,
                6.0, Sp63NormalStabilityMode.Member, 1.0));
        var result = Sp63NormalChecker.Check(section,
            new LoadItem { N = -n, Mx = -m }, calc, options);

        Assert.Equal(Sp63NormalStatus.Calculated, result.Status);
        var detail = Assert.Single(result.AlternativeChecks);
        Assert.Equal(15.0, detail.Variables["l0OverH"], 9);
        Assert.Equal(0.4 / 30.0, detail.Variables["e0"], 9);
        return detail;
    }
}
