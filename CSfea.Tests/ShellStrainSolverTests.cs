using CScore;

namespace CSfea.Tests;

/// <summary>Тесты обратной задачи пластины: солвер, клон, выборка по толщине.</summary>
public static class ShellStrainSolverTests
{
    // Линейная диаграмма σ=E·ε (как в PlateModelTests).
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
        TestHarness.Section("Пластина: глубокий клон CloneForCalc");
        RunClone();
    }

    static void RunClone()
    {
        var p = Plate("layered", 0.25, 12);
        p.Id = 7; p.Num = 3; p.Tag = "orig";
        p.ConcreteMaterialId = 11; p.RebarMaterialId = 22;
        p.SofteningModel = "vecchio_collins"; p.SofteningEpsC2 = 0.0021;
        p.RebarLayers.Add(new PlateRebarLayer { Name = "низ", Asx = 5e-4, Asy = 4e-4,
            Zsx = -0.1, Zsy = -0.09, InputMode = "direct", MaterialId = 33 });

        var c = p.CloneForCalc();
        // Идентичность значений
        TestHarness.Check("клон: H совпадает", c.H == p.H, $"{c.H}");
        TestHarness.Check("клон: PlateModel совпадает", c.PlateModel == p.PlateModel);
        TestHarness.Check("клон: слой скопирован", c.RebarLayers.Count == 1 && c.RebarLayers[0].Asx == 5e-4);
        // Независимость
        c.H = 0.99; c.RebarLayers[0].Asx = 1e-9; c.RebarLayers.Add(new PlateRebarLayer());
        TestHarness.Check("клон: H независим", p.H == 0.25, $"orig={p.H}");
        TestHarness.Check("клон: слой независим", p.RebarLayers[0].Asx == 5e-4 && p.RebarLayers.Count == 1);
    }
}
