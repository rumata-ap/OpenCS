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
}
