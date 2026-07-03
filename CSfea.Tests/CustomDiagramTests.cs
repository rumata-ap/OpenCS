using CScore;
using CSmath;
using System.Linq;

namespace CSfea.Tests;

/// <summary>Тесты ResolveCustomDiagramms и BuildSplines (LSpline).</summary>
public static class CustomDiagramTests
{
    public static void RunAll()
    {
        TestHarness.Section("CustomDiagram: ResolveCustomDiagramms + BuildSplines");
        ResolveCustomDiagramms_ReturnsCorrectDict();
        ResolveCustomDiagramms_ReturnsNull_WhenPoolEmpty();
        ResolveCustomDiagramms_SetsBaseType();
        BuildSplines_LSpline_Correct();
        GetCriticalStrains_Custom_AllKnots_BothBranches();
        GetCriticalStrains_Custom_ExplicitOverride();
        GetCriticalStrains_Standard_Unchanged();
    }

    static void GetCriticalStrains_Custom_AllKnots_BothBranches()
    {
        var ic = new LSpline(new[] { -0.003, -0.0015, 0.0 }, new[] { -30.0, -25.0, 0.0 });
        var it = new LSpline(new[] { 0.0, 0.0005, 0.001 }, new[] { 0.0, 10.0, 15.0 });
        var d  = new Diagramm(ic, it, DiagrammType.Custom, MatType.Concrete);

        var cs = d.GetCriticalStrains();

        // {0} ∪ внутренний узел Ic (-0.0015) ∪ внутренний узел It (0.0005)
        bool ok = cs.Length == 3
               && cs.Any(e => Math.Abs(e) < 1e-12)
               && cs.Any(e => Math.Abs(e - (-0.0015)) < 1e-12)
               && cs.Any(e => Math.Abs(e - 0.0005) < 1e-12);
        TestHarness.Check("GetCriticalStrains_Custom_AllKnots_BothBranches", ok,
            $"[{string.Join(", ", cs)}]");
    }

    static void GetCriticalStrains_Custom_ExplicitOverride()
    {
        var ic = new LSpline(new[] { -0.003, -0.0015, 0.0 }, new[] { -30.0, -25.0, 0.0 });
        var it = new LSpline(new[] { 0.0, 0.0005, 0.001 }, new[] { 0.0, 10.0, 15.0 });
        var d  = new Diagramm(ic, it, DiagrammType.Custom, MatType.Concrete);
        d.CharacteristicStrains = new List<double> { -0.0015 };   // помечена только одна точка

        var cs = d.GetCriticalStrains();

        // ровно {0, -0.0015}; внутренний узел It (0.0005) НЕ включён
        bool ok = cs.Length == 2
               && cs.Any(e => Math.Abs(e) < 1e-12)
               && cs.Any(e => Math.Abs(e - (-0.0015)) < 1e-12)
               && !cs.Any(e => Math.Abs(e - 0.0005) < 1e-12);
        TestHarness.Check("GetCriticalStrains_Custom_ExplicitOverride", ok,
            $"[{string.Join(", ", cs)}]");
    }

    static void GetCriticalStrains_Standard_Unchanged()
    {
        var ic = new LSpline(new[] { -0.003, -0.0015, 0.0 }, new[] { -30.0, -25.0, 0.0 });
        var it = new LSpline(new[] { 0.0, 0.001 }, new[] { 0.0, 15.0 });

        var dL2   = new Diagramm(ic, it, DiagrammType.L2,   MatType.Concrete);
        var dSp63 = new Diagramm(ic, it, DiagrammType.SP63, MatType.Concrete);

        bool okL2   = dL2.GetCriticalStrains().Any(e => Math.Abs(e - (-0.0015)) < 1e-12);   // L2 включает Ic
        bool okSp63 = !dSp63.GetCriticalStrains().Any(e => Math.Abs(e - (-0.0015)) < 1e-12); // SP63 не включает Ic

        TestHarness.Check("GetCriticalStrains_Standard_Unchanged", okL2 && okSp63,
            $"L2 incl Ic={okL2}, SP63 excl Ic={okSp63}");
    }

    static void ResolveCustomDiagramms_ReturnsCorrectDict()
    {
        var ic1 = new LSpline(new[] { -0.003, 0.0 }, new[] { -30.0, 0.0 });
        var it1 = new LSpline(new[] { 0.0, 0.001 }, new[] { 0.0, 15.0 });
        var d1  = new Diagramm(ic1, it1, DiagrammType.L2, MatType.Concrete) { Id = 1, CalcType = CalcType.C };

        var ic2 = new LSpline(new[] { -0.002, 0.0 }, new[] { -20.0, 0.0 });
        var it2 = new LSpline(new[] { 0.0, 0.002 }, new[] { 0.0, 10.0 });
        var d2  = new Diagramm(ic2, it2, DiagrammType.L2, MatType.Concrete) { Id = 2, CalcType = CalcType.N };

        var pool = new List<Diagramm> { d1, d2 };

        var mat = new Material
        {
            Type     = MatType.Custom,
            BaseType = MatType.Concrete,
            CustomDiagramIds = new Dictionary<CalcType, int>
            {
                { CalcType.C,  1 }, { CalcType.CL, 1 },
                { CalcType.N,  2 }, { CalcType.NL, 2 }
            }
        };

        var result = mat.ResolveCustomDiagramms(pool);

        bool ok = result != null
               && result.Count == 4
               && result[CalcType.C].Id  == 1
               && result[CalcType.N].Id  == 2
               && result[CalcType.NL].Id == 2;
        TestHarness.Check("ResolveCustomDiagramms_ReturnsCorrectDict", ok,
            $"count={result?.Count}");
    }

    static void ResolveCustomDiagramms_ReturnsNull_WhenPoolEmpty()
    {
        var mat = new Material
        {
            Type     = MatType.Custom,
            BaseType = MatType.Concrete,
            CustomDiagramIds = new Dictionary<CalcType, int> { { CalcType.C, 1 } }
        };
        var result = mat.ResolveCustomDiagramms(new List<Diagramm>());
        TestHarness.Check("ResolveCustomDiagramms_Null_EmptyPool", result == null, "");
    }

    static void ResolveCustomDiagramms_SetsBaseType()
    {
        var ic = new LSpline(new[] { -0.003, 0.0 }, new[] { -30.0, 0.0 });
        var it = new LSpline(new[] { 0.0, 0.001  }, new[] {   0.0, 15.0 });
        var d  = new Diagramm(ic, it, DiagrammType.L2, MatType.Concrete) { Id = 7 };

        var pool = new List<Diagramm> { d };
        var mat  = new Material
        {
            Type     = MatType.Custom,
            BaseType = MatType.ReSteelF,   // отличается от d.MaterialType
            CustomDiagramIds = new Dictionary<CalcType, int>
            {
                { CalcType.C, 7 }, { CalcType.CL, 7 },
                { CalcType.N, 7 }, { CalcType.NL, 7 }
            }
        };

        var result = mat.ResolveCustomDiagramms(pool);
        bool ok = result != null
               && result[CalcType.C].MaterialType == MatType.ReSteelF
               && d.MaterialType == MatType.Concrete;   // пул не мутирован
        TestHarness.Check("ResolveCustomDiagramms_SetsBaseType", ok,
            $"got={result?[CalcType.C].MaterialType}");
    }

    static void BuildSplines_LSpline_Correct()
    {
        // Ic: три точки (ε отрицательные + 0)
        var ic = new LSpline(new[] { -0.003, -0.002, 0.0 }, new[] { -30.0, -20.0, 0.0 });
        // It: три точки (0 + ε положительные)
        var it = new LSpline(new[] { 0.0, 0.001, 0.002 }, new[] { 0.0, 15.0, 15.0 });

        double sigAtMinus003 = ic.Interpolate(-0.003);
        double sigAt001      = it.Interpolate(0.001);

        TestHarness.Check("BuildSplines_Ic_Correct",
            Math.Abs(sigAtMinus003 - (-30.0)) < 1e-6,
            $"σ(-0.003)={sigAtMinus003:F4}");
        TestHarness.Check("BuildSplines_It_Correct",
            Math.Abs(sigAt001 - 15.0) < 1e-6,
            $"σ(0.001)={sigAt001:F4}");
    }
}
