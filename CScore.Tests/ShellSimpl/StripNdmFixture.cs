using CScore.ShellSimpl;

namespace CScore.Tests.ShellSimpl;

// Полоса плиты h = 200 мм, B25, A500, Ø12 шаг 200 у каждой грани, центр арматуры в 30 мм от грани —
// те же числа, что в ShellSimplCapriDirectionTests.
internal static class StripNdmFixture
{
    public const double H = 0.2;
    public const double Z = 0.07;
    public const double Ds = 0.012;
    public const double AsPerMeter = Math.PI * Ds * Ds / 4.0 / 0.2; // м²/м

    static MaterialChars ConcreteChars(CalcType ct, double rb, double rbt) => new(ct)
    {
        Type = MatType.Concrete, TypeCalc = ct, E = 30_000_000.0, Fc = -rb, Ft = rbt,
        Ec0 = -0.002, Ec1 = -0.6 * rb / 30_000_000.0, Ec2 = -0.0035, Ec1Red = -0.0015,
        Et0 = 0.0001, Et1 = 0.6 * rbt / 30_000_000.0, Et2 = 0.00015, Et1Red = 0.00008,
    };

    static MaterialChars RebarChars(CalcType ct, double rs) => new(ct)
    {
        Type = MatType.ReSteelF, TypeCalc = ct, E = 200_000_000.0, Fc = -rs, Ft = rs, Ec2 = -0.025, Et2 = 0.025,
    };

    public static Material Concrete()
    {
        var m = new Material { Id = 1, Tag = "B25", Type = MatType.Concrete, E = 30_000_000.0 };
        m.MaterialChars =
        [
            ConcreteChars(CalcType.C, 14_500.0, 1_050.0), ConcreteChars(CalcType.CL, 14_500.0, 1_050.0),
            ConcreteChars(CalcType.N, 18_500.0, 1_550.0), ConcreteChars(CalcType.NL, 18_500.0, 1_550.0),
        ];
        return m;
    }

    public static Material Rebar()
    {
        var m = new Material { Id = 2, Tag = "A500", Type = MatType.ReSteelF, E = 200_000_000.0 };
        m.MaterialChars =
        [
            RebarChars(CalcType.C, 435_000.0), RebarChars(CalcType.CL, 435_000.0),
            RebarChars(CalcType.N, 500_000.0), RebarChars(CalcType.NL, 500_000.0),
        ];
        return m;
    }

    public static Diagramm ConcreteDiagram(CalcType ct, DiagrammType type = DiagrammType.L3) =>
        Concrete().GetDiagramms(type)![ct];

    public static Diagramm RebarDiagram(CalcType ct) => Rebar().GetDiagramms(DiagrammType.L2)![ct];

    public static StripNdmSection Strip(CalcType ct, DiagrammType concreteType = DiagrammType.L3,
        double asTop = AsPerMeter, double asBot = AsPerMeter) =>
        new(H, [new StripNdmRow(Z, asTop), new StripNdmRow(-Z, asBot)], ConcreteDiagram(ct, concreteType), RebarDiagram(ct));

    public static StripNdmLimits Limits(CalcType ct = CalcType.C) =>
        StripNdmLimits.From(Concrete().chars[ct], MatType.ReSteelF);

    public static PlateSection Plate(int layers = 40) => new()
    {
        H = H, NLayers = layers, PlateModel = "layered", ConcreteDiagramType = DiagrammType.L3,
        TensionConcrete = false,
        RebarLayers =
        [
            Layer("низ", -Z),
            Layer("верх", Z),
        ],
    };

    static PlateRebarLayer Layer(string name, double z) => new()
    {
        Name = name, InputMode = "direct", Asx = AsPerMeter, Asy = AsPerMeter,
        Zsx = z, Zsy = z, DiameterX = Ds, DiameterY = Ds,
    };
}
