using CScore.Fem;
using CScore.Import;
using Xunit;

namespace CScore.Tests;

/// <summary>Свойства КЭ ЛИРЫ по строкам жёсткостей «Мирной» (т, м; сечения в см).</summary>
public sealed class LiraElementStiffnessSourceTests
{
    const double T = 9806.65, Cm = 0.01;

    const string Wall = "Ro:2.5 E:3.06e+006 V:0.2 H:25 E2:0 V2:0 G2:0 fL:0 WLKE:1 WLKG:1 PLKE:0.6 PLKG:0.6 NZ:10 UseSixDOF:0 PLATE_END";
    const string Column = "Ro:2.5 E:3.06e+006 B:25 H:60 NGrndPar:-1 GF:0 nFlags:0 Length:0 B_:0 H_:0 NEL:0 UseRbNel:0 BAR_END " +
                          "Mu:0.2 EF:459000 EIy:8262 EIz:1434.37 GIk:3005.99 GFz:159375 GFy:159375 IsSavedAsKoef:1 STD_END";
    const string BarAnalog = "Ro:0 E:0 B:25 H:47.4 NGrndPar:-1 BAR_END Mu:0.2 EF:0 EIy:0 EIz:0 GIk:0 STD_END";
    const string Numeric = "Ro:0.01 EF:1e+006 EIy:2e+006 EIz:3e+006 GIk:4e+006 GFy:0 GFz:0 Y1:0.57735 DD10_END";
    const string Channel = "Section = Channel  MatId = STL  File  = |shv-parl.profiles.srt|  Shape = |20П|   NEL:0 Iter:0";

    static readonly LiraElementStiffnessSource Source = new(new Dictionary<int, LiraStiffnessRecord>
    {
        [1] = new(1, 36, "Железобетон стен", Wall, Cm),
        [2] = new(2, 0, "Стержневой аналог", BarAnalog, Cm),
        [3] = new(3, 0, "Железобетон колонн", Column, Cm),
        [6] = new(6, 14, "Связь", Numeric, Cm),
        [7] = new(7, 1003, "", Channel, Cm),
    }, LiraUnits.Default(9.80665));

    static FemElement E(int stiffness, string type = "beam", double? h = null) =>
        new() { ElemTag = "1", ElemType = type, StiffnessNum = stiffness, ThicknessM = h };

    [Fact]
    public void Plate_EnuH_InDocumentUnits()
    {
        var s = Source.Shell(E(1, "shell"))!;
        Assert.Equal(3.06e6 * T, s.E, 3);
        Assert.Equal(0.2, s.Nu, 12);
        Assert.Equal(0.25, s.H, 12);
        Assert.Equal(0.3, Source.Shell(E(1, "shell", h: 0.3))!.H, 12);
        Assert.Equal(2.5 * T, Source.UnitWeight(E(1, "shell"))!.Value, 6);
        Assert.Null(Source.Bar(E(1)));
    }

    [Fact]
    public void BarRect_GeometryAndMaterialModulus_NotReducedNumericEi()
    {
        var bar = Source.Bar(E(3))!;
        Assert.Equal(3.06e6 * T, bar.E, 3);
        Assert.Equal(bar.E / 2.4, bar.G, 3);
        Assert.Equal(0.15, bar.A, 12);
        Assert.Equal(0.25 * Math.Pow(0.6, 3) / 12, bar.Iy, 12); // H ‖ Z1 — изгиб вокруг Y1
        Assert.Equal(0.6 * Math.Pow(0.25, 3) / 12, bar.Iz, 12);
        // EIy ЛИРЫ = 0,6·EI (коэффициент к жёсткости), EF — полный
        Assert.Equal(459000 * T, bar.E * bar.A, 0);
        Assert.Equal(1, bar.E * bar.Iy / (8262 / 0.6 * T), 6);
        // GIk ЛИРЫ — Сен-Венан без коэффициента; приближение Роарка в пределах 3 %
        Assert.InRange(bar.G * bar.J / (3005.99 * T), 0.97, 1.03);
        Assert.Equal(2.5 * T, Source.UnitWeight(E(3))!.Value, 6);
        Assert.Equal(0.15, Source.BarArea(E(3))!.Value, 12);
    }

    [Fact]
    public void BarAnalog_WithoutStiffness_HasNoProperties()
    {
        Assert.Null(Source.Bar(E(2)));
        Assert.Null(Source.UnitWeight(E(2)));
    }

    [Fact]
    public void NumericStiffness_RigiditiesKept_RoIsPerLength()
    {
        var bar = Source.Bar(E(6))!;
        Assert.Equal(1e6 * T, bar.E * bar.A, 0);
        Assert.Equal(2e6 * T, bar.E * bar.Iy, 0);
        Assert.Equal(3e6 * T, bar.E * bar.Iz, 0);
        Assert.Equal(4e6 * T, bar.G * bar.J, 0);
        Assert.Equal(0.01 * T, Source.UnitWeight(E(6))!.Value * Source.BarArea(E(6))!.Value, 9);
    }

    [Fact]
    public void SteelProfile_WithoutSortament_HasNoProperties_ButIsSteel()
    {
        Assert.Null(Source.Bar(E(7)));
        Assert.True(LiraSteelProfiles.IsSteel(new LiraStiffnessRecord(7, 1003, "", Channel, Cm)));
        Assert.False(LiraSteelProfiles.IsSteel(new LiraStiffnessRecord(3, 0, "", Column, Cm)));
    }

    [Fact]
    public void RectTorsion_SquareAndThinStrip()
    {
        Assert.InRange(LiraElementStiffnessSource.RectTorsion(0.4, 0.4) / (0.1406 * Math.Pow(0.4, 4)), 0.995, 1.005); // точное 0,1406·a⁴
        Assert.InRange(LiraElementStiffnessSource.RectTorsion(1.0, 0.01) / (1.0 * 1e-6 / 3), 0.99, 1.0);
    }

    [Fact]
    public void Units_FromCodes_AndJsonRoundTrip()
    {
        var u = LiraUnits.FromCodes(4, 1, 9.81); // кН, см
        Assert.Equal(1000, u.ForceUnitN, 9);
        Assert.Equal(0.01, u.LengthUnitM, 12);
        Assert.Equal(1e7, u.Stress(1), 3);
        Assert.Equal(u, LiraUnits.FromJson(u.ToJson()));
        Assert.Equal(LiraUnits.Default(9.81), LiraUnits.FromCodes(-1, -1, 9.81));
        Assert.Throws<InvalidDataException>(() => LiraUnits.FromJson("{\"ForceUnitN\":0,\"LengthUnitM\":1}"));
        Assert.Throws<InvalidDataException>(() => LiraUnits.FromJson("не json"));
    }
}
