using CScore;
using CScore.Fem;

namespace CSfea.Tests;

public static class ShellLayeredCrackWidthTests
{
    static PlateSection MakeSection(double asx, double zsx, double asy, double zsy)
    {
        var section = new PlateSection { H = 0.200 };
        section.RebarLayers.Add(new PlateRebarLayer
        {
            Name = zsx > 0.0 || zsy > 0.0 ? "верх" : "низ", Asx = asx, Zsx = zsx, Asy = asy, Zsy = zsy,
            InputMode = "direct",
        });
        return section;
    }

    static (MaterialChars c, MaterialChars r) MakeChars()
    {
        var c = new MaterialChars { E = 32_500_000.0, Fc = -22_000.0, Ft = 1_750.0 };
        var r = new MaterialChars { E = 200_000_000.0, Fc = -500_000.0, Ft = 500_000.0 };
        return (c, r);
    }

    /// <summary>Изгиб по X (растяжение снизу по X) → трещина перпендикулярна X → угол 90°.</summary>
    public static void RunAngleBendingX()
    {
        TestHarness.Section("ShellLayeredCrackWidth: угол трещины — изгиб по X → 90°");
        var section = MakeSection(0.001, 0.0875, 0.0, 0.0);
        var (c, r) = MakeChars();
        var shell = new ShellLoadItem { Mx = 50.0 };
        var st = new ShellStrainState(0, 0, 0, kx: 0.01, ky: 0, kxy: 0);

        var strips = ShellLayeredCrackWidth.ComputeAll(section, shell, st, c, r, 1.0, 0.5,
            SigmaSCrcMethod.ReleasedConcrete8137, WplGammaMethod.Sp63);

        TestHarness.CheckRel("одна полоса (X)", strips.Count, 1, 0.001);
        TestHarness.CheckRel("угол трещины ≈ 90°", strips[0].CrackAngleDeg, 90.0, 0.01);
        TestHarness.CheckRel("z > 0 → верхняя грань", strips[0].IsTop ? 1 : 0, 1, 0.001);
    }

    /// <summary>Изгиб по Y → трещина перпендикулярна Y → угол 0°.</summary>
    public static void RunAngleBendingY()
    {
        TestHarness.Section("ShellLayeredCrackWidth: угол трещины — изгиб по Y → 0°");
        var section = MakeSection(0.0, 0.0, 0.001, 0.0875);
        var (c, r) = MakeChars();
        var shell = new ShellLoadItem { My = 50.0 };
        var st = new ShellStrainState(0, 0, 0, kx: 0, ky: 0.01, kxy: 0);

        var strips = ShellLayeredCrackWidth.ComputeAll(section, shell, st, c, r, 1.0, 0.5,
            SigmaSCrcMethod.ReleasedConcrete8137, WplGammaMethod.Sp63);

        TestHarness.CheckRel("угол трещины ≈ 0°", strips[0].CrackAngleDeg, 0.0, 0.01);
    }

    /// <summary>Чистое кручение → косые трещины ≈ ±45° (оба знака допустимы, не фиксируем один).</summary>
    public static void RunAngleTorsion()
    {
        TestHarness.Section("ShellLayeredCrackWidth: угол трещины — чистое кручение → |угол|≈45°");
        var section = MakeSection(0.001, 0.0875, 0.001, 0.0875);
        var (c, r) = MakeChars();
        var shell = new ShellLoadItem { Mx = 1.0, My = 1.0, Mxy = 10.0 };
        var st = new ShellStrainState(0, 0, 0, kx: 0, ky: 0, kxy: 0.02);

        var strips = ShellLayeredCrackWidth.ComputeAll(section, shell, st, c, r, 1.0, 0.5,
            SigmaSCrcMethod.ReleasedConcrete8137, WplGammaMethod.Sp63);

        foreach (var strip in strips)
            TestHarness.CheckRel($"|угол| ≈ 45° ({strip.Direction})", Math.Abs(strip.CrackAngleDeg), 45.0, 0.02);
    }

    /// <summary>Mcrc/Cracked заполнены даже когда полоса не растрескалась (M &lt;= Mcrc) —
    /// контракт A.1 спеки: Mcrc не зависит от eps_s, считается всегда.</summary>
    public static void RunMcrcAlwaysPopulated()
    {
        TestHarness.Section("ShellLayeredCrackWidth: Mcrc/Cracked заполнены и при отсутствии трещин");
        var section = MakeSection(0.001, 0.0875, 0.0, 0.0);
        var (c, r) = MakeChars();
        var shell = new ShellLoadItem { Mx = 0.01 };
        var st = new ShellStrainState(0, 0, 0, kx: 0.0001, ky: 0, kxy: 0);

        var strips = ShellLayeredCrackWidth.ComputeAll(section, shell, st, c, r, 1.0, 0.5,
            SigmaSCrcMethod.ReleasedConcrete8137, WplGammaMethod.Sp63);

        TestHarness.CheckRel("Mcrc > 0", strips[0].Mcrc > 0.0 ? 1 : 0, 1, 0.001);
        TestHarness.CheckRel("Cracked == false", strips[0].Cracked ? 1 : 0, 0, 0.001);
        TestHarness.CheckRel("AcrcMm == 0 (не растрескалась)", strips[0].AcrcMm, 0.0, 0.001);
    }

    /// <summary>ComputeWorst совпадает с ComputeAll().Where(Cracked).MaxBy(AcrcMm).</summary>
    public static void RunComputeWorstMatchesComputeAll()
    {
        TestHarness.Section("ShellLayeredCrackWidth: ComputeWorst == ComputeAll().Where(Cracked).MaxBy(AcrcMm)");
        var section = MakeSection(0.001, 0.0875, 0.0015, 0.0875);
        var (c, r) = MakeChars();
        var shell = new ShellLoadItem { Mx = 60.0, My = 40.0 };
        var st = new ShellStrainState(0, 0, 0, kx: 0.012, ky: 0.008, kxy: 0);

        var all = ShellLayeredCrackWidth.ComputeAll(section, shell, st, c, r, 1.0, 0.5,
            SigmaSCrcMethod.ReleasedConcrete8137, WplGammaMethod.Sp63);
        var worst = ShellLayeredCrackWidth.ComputeWorst(section, shell, st, c, r, 1.0, 0.5,
            SigmaSCrcMethod.ReleasedConcrete8137, WplGammaMethod.Sp63);
        var expected = all.Where(s => s.Cracked).MaxBy(s => s.AcrcMm);

        TestHarness.CheckRel("worst не null", worst != null ? 1 : 0, expected != null ? 1 : 0, 0.001);
        if (worst != null && expected != null)
            TestHarness.CheckRel("worst.AcrcMm == expected.AcrcMm", worst.AcrcMm, expected.AcrcMm, 0.0001);
    }

    /// <summary>Пустой RebarLayers → ComputeAll пуст, ComputeWorst == null.</summary>
    public static void RunEmptyRebarLayers()
    {
        TestHarness.Section("ShellLayeredCrackWidth: пустой RebarLayers");
        var section = new PlateSection { H = 0.200 };
        var (c, r) = MakeChars();
        var shell = new ShellLoadItem { Mx = 60.0 };
        var st = new ShellStrainState(0, 0, 0, kx: 0.012, ky: 0, kxy: 0);

        var all = ShellLayeredCrackWidth.ComputeAll(section, shell, st, c, r, 1.0, 0.5,
            SigmaSCrcMethod.ReleasedConcrete8137, WplGammaMethod.Sp63);
        var worst = ShellLayeredCrackWidth.ComputeWorst(section, shell, st, c, r, 1.0, 0.5,
            SigmaSCrcMethod.ReleasedConcrete8137, WplGammaMethod.Sp63);

        TestHarness.CheckRel("ComputeAll пуст", all.Count, 0, 0.001);
        TestHarness.CheckRel("ComputeWorst == null", worst == null ? 1 : 0, 1, 0.001);
    }
}
