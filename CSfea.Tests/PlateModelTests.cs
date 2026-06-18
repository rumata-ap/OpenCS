using CScore;

namespace CSfea.Tests;

/// <summary>Тесты нелинейных моделей пластины: слоистая и 1D по характерным точкам.</summary>
public static class PlateModelTests
{
    // Линейная диаграмма σ=E·ε (растяжение и сжатие), E в МПа.
    // ReSteelF L2: ветви (0,0)→(±Ft/E, ±Ft)→(±Et2, ±Ft). Предел текучести 600 МПа
    // → деформация текучести 0.02 ≫ тестовых ~1e-4, поэтому σ=E·ε строго линейна.
    static Diagramm LinearConcrete(double e_MPa)
    {
        MaterialChars Ch(CalcType ct) => new(ct)
        {
            E = e_MPa, Ry = 600, Ru = 600, Ft = 600, Fc = -600,
            Ec2 = -0.05, Et2 = 0.05, Type = MatType.ReSteelF,
        };
        var m = new Material { Id = 1, E = e_MPa, Type = MatType.ReSteelF, Tag = "lin" };
        m.MaterialChars = [Ch(CalcType.C), Ch(CalcType.CL), Ch(CalcType.N), Ch(CalcType.NL)];
        return m.GetDiagramms(DiagrammType.L2)![CalcType.C];
    }

    static PlateSection Plate(string model, double h, int nLayers) => new()
    {
        H = h, NLayers = nLayers, TensionConcrete = true,
        SofteningModel = "", PlateModel = model,
    };

    public static void RunAll()
    {
        TestHarness.Section("Пластина: слоистая модель — аналитика (линейная σ=Eε)");
        RunLayeredAnalytic();

        TestHarness.Section("Пластина: 1D по осям — аналитика и паритет со слоистой");
        RunAxial();

        TestHarness.Section("Пластина: 1D по главным — паритет со слоистой без сдвига");
        RunPrincipal();
    }

    static void RunLayeredAnalytic()
    {
        double e = 30_000, h = 0.2;
        var cd = LinearConcrete(e);
        var plate = Plate("layered", h, 50);

        // Чистое осевое растяжение ε0x: Nx = E·ε·h·1000 [кН/м]
        double eps = 1e-4;
        var s1 = new ShellStrainState(eps, 0, 0, 0, 0, 0);
        var r1 = plate.Compute(s1, cd, cd, null, false);
        double nxRef = e * eps * h * 1000.0;
        TestHarness.CheckRel("Nx осевое (layered)", r1.Nx, nxRef, 0.02);

        // Чистый изгиб κx: Mx = E·κ·h³/12·1000 [кН·м/м]
        double kx = 1e-3;
        var s2 = new ShellStrainState(0, 0, 0, kx, 0, 0);
        var r2 = plate.Compute(s2, cd, cd, null, false);
        double mxRef = e * kx * h * h * h / 12.0 * 1000.0;
        TestHarness.CheckRel("Mx изгиб (layered)", r2.Mx, mxRef, 0.02);
    }

    static void RunAxial()
    {
        double e = 30_000, h = 0.2;
        var cd = LinearConcrete(e);

        // Аналитика: осевое + изгиб
        double eps = 1e-4, kx = 1e-3;
        var sAx = new ShellStrainState(eps, 0, 0, 0, 0, 0);
        var sBe = new ShellStrainState(0, 0, 0, kx, 0, 0);

        var axial = Plate("char1d_axial", h, 1);  // NLayers не влияет на 1D
        var rAx = axial.Compute(sAx, cd, cd, null, false);
        var rBe = axial.Compute(sBe, cd, cd, null, false);
        TestHarness.CheckRel("Nx осевое (axial)", rAx.Nx, e * eps * h * 1000.0, 1e-6);
        TestHarness.CheckRel("Mx изгиб (axial)", rBe.Mx, e * kx * h * h * h / 12.0 * 1000.0, 1e-6);

        // Паритет со слоистой (softening off, без сдвига → совпадают)
        var layered = Plate("layered", h, 200);
        var rL = layered.Compute(sBe, cd, cd, null, false);
        TestHarness.CheckRel("Mx axial ≈ layered", rBe.Mx, rL.Mx, 0.01);

        // Независимость 1D от NLayers
        var axial2 = Plate("char1d_axial", h, 999);
        var rAx2 = axial2.Compute(sBe, cd, cd, null, false);
        TestHarness.CheckRel("axial независим от NLayers", rBe.Mx, rAx2.Mx, 1e-9);
    }

    static void RunPrincipal()
    {
        double e = 30_000, h = 0.2, kx = 1e-3;
        var cd = LinearConcrete(e);
        var sBe = new ShellStrainState(0, 0, 0, kx, 0, 0);

        // Без сдвига и softening: по главным == по осям == слоистая
        var prin = Plate("char1d_principal", h, 1);
        var rP = prin.Compute(sBe, cd, cd, null, false);
        TestHarness.CheckRel("Mx изгиб (principal)", rP.Mx, e * kx * h * h * h / 12.0 * 1000.0, 1e-6);

        var layered = Plate("layered", h, 200);
        var rL = layered.Compute(sBe, cd, cd, null, false);
        TestHarness.CheckRel("Mx principal ≈ layered", rP.Mx, rL.Mx, 0.01);

        var prin2 = Plate("char1d_principal", h, 999);
        var rP2 = prin2.Compute(sBe, cd, cd, null, false);
        TestHarness.CheckRel("principal независим от NLayers", rP.Mx, rP2.Mx, 1e-9);

        // Сдвиг присутствует — интегратор не падает, Mxy конечно
        var sSh = new ShellStrainState(1e-4, 0, 5e-4, kx, 0, 1e-3);
        var rS = prin.Compute(sSh, cd, cd, null, false);
        TestHarness.Check("principal со сдвигом конечен",
            !double.IsNaN(rS.Mxy) && !double.IsInfinity(rS.Mxy), $"Mxy={rS.Mxy:e3}");
    }
}
