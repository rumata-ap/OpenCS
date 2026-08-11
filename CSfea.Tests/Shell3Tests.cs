using CSfea.Core;

namespace CSfea.Tests;

/// <summary>Регрессия на баг переставленных индексов invJ в Shell3.Geometry (найден при
/// разработке Task 7 ShellMeshPatchPostprocessor, Срез 3b) — dNdx считался с invJ[a,k] вместо
/// invJ[k,a], что было незаметно на осеориентированном каноническом треугольнике (invJ=I
/// симметрична), но давало неверный CST-отклик на скошенных/повёрнутых треугольниках.</summary>
public static class Shell3Tests
{
    public static void RunAll()
    {
        TestHarness.Section("Shell3.Geometry: полнота градиента dN/dx на скошенном треугольнике");
        SkewedTriangle_ReproducesLinearFieldGradientExactly();
        AxisAlignedCanonicalTriangle_StillCorrect();
    }

    static void SkewedTriangle_ReproducesLinearFieldGradientExactly()
    {
        // Треугольник (0,0),(1,0),(1,1) — не осеориентированный, J асимметрична.
        double[,] xy = { { 0, 0 }, { 1, 0 }, { 1, 1 } };
        var (dNdx, area, _) = Shell3.Geometry(xy);

        // Фундаментальное тождество изопараметрических производных: сумма dN_i/dx * x_i = 1,
        // сумма dN_i/dy * y_i = 1, перекрёстные суммы = 0 — для ЛЮБОГО невырожденного треугольника.
        double sumDxX = dNdx[0, 0] * xy[0, 0] + dNdx[1, 0] * xy[1, 0] + dNdx[2, 0] * xy[2, 0];
        double sumDyY = dNdx[0, 1] * xy[0, 1] + dNdx[1, 1] * xy[1, 1] + dNdx[2, 1] * xy[2, 1];
        double sumDxY = dNdx[0, 0] * xy[0, 1] + dNdx[1, 0] * xy[1, 1] + dNdx[2, 0] * xy[2, 1];
        double sumDyX = dNdx[0, 1] * xy[0, 0] + dNdx[1, 1] * xy[1, 0] + dNdx[2, 1] * xy[2, 0];

        TestHarness.Check("Σ dN/dx · x = 1", Math.Abs(sumDxX - 1.0) < 1e-12, $"value={sumDxX}");
        TestHarness.Check("Σ dN/dy · y = 1", Math.Abs(sumDyY - 1.0) < 1e-12, $"value={sumDyY}");
        TestHarness.Check("Σ dN/dx · y = 0", Math.Abs(sumDxY) < 1e-12, $"value={sumDxY}");
        TestHarness.Check("Σ dN/dy · x = 0", Math.Abs(sumDyX) < 1e-12, $"value={sumDyX}");
        TestHarness.Check("Area = 0.5", Math.Abs(area - 0.5) < 1e-12, $"area={area}");
    }

    static void AxisAlignedCanonicalTriangle_StillCorrect()
    {
        // Канонический треугольник (0,0),(1,0),(0,1) — J=I, симметрична, баг здесь был невидим;
        // фиксирует, что исправление не сломало этот случай.
        double[,] xy = { { 0, 0 }, { 1, 0 }, { 0, 1 } };
        var (dNdx, area, _) = Shell3.Geometry(xy);

        TestHarness.CheckRel("dN1/dx = -1", dNdx[0, 0], -1.0, 1e-12);
        TestHarness.CheckRel("dN1/dy = -1", dNdx[0, 1], -1.0, 1e-12);
        TestHarness.Check("dN2/dx = 1", Math.Abs(dNdx[1, 0] - 1.0) < 1e-12, $"value={dNdx[1, 0]}");
        TestHarness.Check("dN2/dy = 0", Math.Abs(dNdx[1, 1]) < 1e-12, $"value={dNdx[1, 1]}");
        TestHarness.Check("dN3/dx = 0", Math.Abs(dNdx[2, 0]) < 1e-12, $"value={dNdx[2, 0]}");
        TestHarness.Check("dN3/dy = 1", Math.Abs(dNdx[2, 1] - 1.0) < 1e-12, $"value={dNdx[2, 1]}");
        TestHarness.Check("Area = 0.5", Math.Abs(area - 0.5) < 1e-12, $"area={area}");
    }
}
