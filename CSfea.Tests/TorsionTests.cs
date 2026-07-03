using CSfea.Torsion;
using CScore;

using System.Diagnostics;

namespace CSfea.Tests;

public static class TorsionTests
{
    public static void SmokePropsConstruction()
    {
        TestHarness.Section("TorsionProps: конструктор и TauMax");
        var props = new TorsionProps { It = 1.5, TauUnitMax = 2.0 };
        TestHarness.CheckRel("It", props.It, 1.5, 1e-9);
        // τ_max = G·Θ·τ_unit = 1000·0.01·2.0 = 20
        TestHarness.CheckRel("TauMax", props.TauMax(1000.0, 0.01), 20.0, 1e-9);
    }

    public static void BoundaryFromMaterialArea()
    {
        TestHarness.Section("TorsionBoundary: из MaterialArea с отверстием");
        // Внешний контур 10×10 (CCW), отверстие 2×2 по центру (CW)
        var area = new MaterialArea();
        var hull = new Contour(
            new[] { new StressPoint(0.0, 0.0), new StressPoint(10.0, 0.0),
                    new StressPoint(10.0, 10.0), new StressPoint(0.0, 10.0) }, "hull");
        hull.Type = ContourType.Hull;
        area.Contours.Add(hull);
        var hole = new Contour(
            new[] { new StressPoint(4.0, 4.0), new StressPoint(6.0, 4.0),
                    new StressPoint(6.0, 6.0), new StressPoint(4.0, 6.0) }, "hole");
        hole.Type = ContourType.Hole;
        area.Contours.Add(hole);

        var b = area.FromMaterialArea();
        TestHarness.Check("Outer не null", b.OuterX != null && b.OuterX.Length == 4);
        TestHarness.Check("Holes есть", b.Holes != null && b.Holes.Count == 1);
        TestHarness.Check("Hole[0] размер", b.Holes![0].X.Length == 4);
    }

    public static void PrandtlTri3ElementMatrices()
    {
        TestHarness.Section("PrandtlTri3: матрицы K и Load прямоугольного tri");
        // Прямоугольный треугольник с катетами вдоль осей: (0,0),(2,0),(0,3). Площадь A=3.
        double[] coords = { 0, 0, 2, 0, 0, 3 };
        double[,] k = PrandtlTri3.ElementK(coords);
        // Сумма всех элементов K для скалярного Лапласа на T3 равна 0 (постоянный режим → нулевой поток)
        double kSum = 0;
        for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) kSum += k[i, j];
        TestHarness.Check("K sum == 0", Math.Abs(kSum) < 1e-12, $"kSum={kSum}");
        // K симметрична и положительно полуопределена: диагональ > 0
        TestHarness.Check("K diag > 0", k[0, 0] > 0 && k[1, 1] > 0 && k[2, 2] > 0);
        // Load: F_i = +2·(A/3) = +2·(3/3) = +2 для каждого (уравнение −∇²φ=2)
        double[] f = PrandtlTri3.LoadVector(coords);
        TestHarness.CheckRel("F_i = +2", f[0], 2.0, 1e-9);
        TestHarness.CheckRel("F_1 = +2", f[1], 2.0, 1e-9);
        // Mass integral: ∫N_i dA = A/3 = 1
        double[] m = PrandtlTri3.MassVector(coords);
        TestHarness.CheckRel("M_i = A/3 = 1", m[0], 1.0, 1e-9);
    }

    public static void MeshBuilderSquare()
    {
        TestHarness.Section("MeshBuilder: сетка квадрата 1×1 с границей");
        var boundary = new TorsionBoundary(
            new[] { 0.0, 1.0, 1.0, 0.0 },
            new[] { 0.0, 0.0, 1.0, 1.0 });
        var mesh = MeshBuilder.Build(boundary, maxElementSize: 0.5);
        TestHarness.Check("Есть узлы", mesh.NodesX.Length > 4);
        TestHarness.Check("Есть треугольники", mesh.Triangles.Length > 0);
        TestHarness.Check("FixedDofs непустой", mesh.FixedDofs.Length >= 4);
    }

    public static void MeshBuilderFromMaterialAreaMeters()
    {
        TestHarness.Section("MeshBuilder: прямоугольник 0.3×0.5 м из MaterialArea");
        double b = 0.3, h = 0.5;
        var area = new MaterialArea();
        var hull = new Contour(
            new[]
            {
                new StressPoint(-b / 2, -h / 2), new StressPoint(b / 2, -h / 2),
                new StressPoint(b / 2, h / 2), new StressPoint(-b / 2, h / 2)
            }, "hull");
        hull.Type = ContourType.Hull;
        area.Contours.Add(hull);

        var boundary = area.FromMaterialArea();
        var mesh = MeshBuilder.Build(boundary, maxElementSize: 0.05);
        TestHarness.Check(">20 треугольников", mesh.Triangles.Length > 20,
            $"tri={mesh.Triangles.Length}");
    }

    public static void MeshBuilderConcaveFrameFine()
    {
        TestHarness.Section("MeshBuilder: вогнутая рамка 30×15 см, h=0.01");
        var boundary = SampleConcaveFrameBoundary();
        var sw = Stopwatch.StartNew();
        var mesh = MeshBuilder.Build(boundary, maxElementSize: 0.01);
        sw.Stop();
        TestHarness.Check("время < 3 с", sw.ElapsedMilliseconds < 3000, $"ms={sw.ElapsedMilliseconds}");
        TestHarness.Check(">80 треугольников", mesh.Triangles.Length > 80, $"tri={mesh.Triangles.Length}");
    }

    /// <summary>Рамка 30×15 см (test_prj.db, контур 2) — один вогнутый контур.</summary>
    static TorsionBoundary SampleConcaveFrameBoundary()
    {
        double[] ox =
        [
            -0.0745, 0.0745, 0.0745, 0.01575, 0.012385352413667231, 0.00925, 0.00655761184457488,
            0.004491669750802297, 0.0031929642582421095, 0.00275, 0.00275, 0.003192964258242113,
            0.0044916697508022956, 0.006557611844574882, 0.009250000000000001, 0.012385352413667228,
            0.01575, 0.0745, 0.0745, 0.0745, -0.0745, -0.0745, -0.0745, -0.01575,
            -0.012385352413667231, -0.00925, -0.00655761184457488, -0.0044916697508022956,
            -0.0031929642582421112, -0.00275, -0.00275, -0.0031929642582421112,
            -0.0044916697508022956, -0.00655761184457488, -0.00925, -0.012385352413667231, -0.01575
        ];
        double[] oy =
        [
            -0.149, -0.149, -0.141, -0.141, -0.14055703574175787, -0.13925833024919768,
            -0.1371923881554251, -0.13449999999999998, -0.13136464758633273, -0.12799999999999997,
            0.12799999999999997, 0.13136464758633276, 0.13449999999999998, 0.1371923881554251,
            0.13925833024919768, 0.14055703574175787, 0.141, 0.141, 0.149, 0.149, 0.149, 0.141,
            0.141, 0.141, 0.14055703574175787, 0.13925833024919768, 0.1371923881554251,
            0.13449999999999998, 0.13136464758633273, 0.12799999999999997, -0.12799999999999997,
            -0.13136464758633273, -0.13449999999999998, -0.1371923881554251, -0.13925833024919768,
            -0.14055703574175787, -0.141
        ];
        return new TorsionBoundary(ox, oy);
    }

    public static void FemCircleItVsAnalytical()
    {
        TestHarness.Section("МКЭ: It круга vs π·r⁴/2");
        double r = 0.5; // м
        int n = 64;
        double[] ox = new double[n], oy = new double[n];
        for (int i = 0; i < n; i++)
        {
            double a = 2.0 * Math.PI * i / n;
            ox[i] = r * Math.Cos(a); oy[i] = r * Math.Sin(a);
        }
        var boundary = new TorsionBoundary(ox, oy);
        var props = TorsionFemSolver.Solve(boundary, maxElementSize: 0.1);
        double exact = Math.PI * Math.Pow(r, 4) / 2.0;
        TestHarness.CheckRel("It (МКЭ)", props.It, exact, 0.03); // ≤3%
        TestHarness.Check("τ_unit_max > 0", props.TauUnitMax > 0);
    }

    public static void BoundaryDiscretizeLoops()
    {
        TestHarness.Section("BoundaryDiscretizer: нарезка квадрата 10×10 с отверстием");
        var boundary = new TorsionBoundary(
            new[] { 0.0, 10.0, 10.0, 0.0 },
            new[] { 0.0, 0.0, 10.0, 10.0 },
            new List<(double[] X, double[] Y)>
            {
                (new[] { 4.0, 6.0, 6.0, 4.0 }, new[] { 4.0, 4.0, 6.0, 6.0 })
            });
        var d = BoundaryDiscretizer.Discretize(boundary, maxElementSize: 2.0);
        // Внешний контур 10×10: рёбра длины 10, ceil(10/2)=5 на ребро → 20 узлов.
        // Отверстие 2×2: рёбра длины 2, ceil(2/2)=1 на ребро → 4 узла.
        TestHarness.Check("LoopSizes [20,4]", d.LoopSizes[0] == 20 && d.LoopSizes[1] == 4, $"loops={d.LoopSizes[0]},{d.LoopSizes[1]}");
        TestHarness.Check("Сумма = N", d.X.Length == d.LoopSizes.Sum());
        TestHarness.Check("Длины согласованы", d.X.Length == d.Y.Length && d.X.Length == d.J1.Length);
        // Замыкание: последний узел внешнего контура (индекс 19) переходит в 0
        TestHarness.Check("J1 замыкание внешнего", d.J1[19] == 0, $"J1[19]={d.J1[19]}");
        // Замыкание отверстия: последний (индекс 23) переходит в начало отверстия (20)
        TestHarness.Check("J1 замыкание отверстия", d.J1[23] == 20, $"J1[23]={d.J1[23]}");
    }

    public static void BemKernelSlintcDiagonal()
    {
        TestHarness.Section("BemKernels: slintc (диагональ G = (l/2)(ln(l/2)−1)/π)");
        // Полудлина элемента sl = 1 → G_ii = 1·(ln(1) − 1)/π = (0 − 1)/π = −1/π
        double g = BemKernels.Slintc(halfLength: 1.0);
        TestHarness.CheckRel("G_ii для sl=1", g, -1.0 / Math.PI, 1e-9);
    }

    public static void BemCircleItVsAnalytical()
    {
        TestHarness.Section("МГЭ: It круга vs π·r⁴/2");
        double r = 0.5; // м
        int n = 64;
        double[] ox = new double[n], oy = new double[n];
        for (int i = 0; i < n; i++)
        {
            double a = 2.0 * Math.PI * i / n;
            ox[i] = r * Math.Cos(a); oy[i] = r * Math.Sin(a);
        }
        var boundary = new TorsionBoundary(ox, oy);
        var props = TorsionBemSolver.Solve(boundary, maxElementSize: 0.1);
        double exact = Math.PI * Math.Pow(r, 4) / 2.0;
        TestHarness.Check("Не сингулярна", !props.Singular);
        TestHarness.CheckRel("It (МГЭ)", props.It, exact, 0.03); // ≤3%
        TestHarness.Check("τ_unit_max > 0", props.TauUnitMax > 0);
        // Для круга центр кручения совпадает с геометрическим центром (0,0)
        TestHarness.Check("Центр кручения ≈ (0,0)", Math.Abs(props.ShearCenterX) < 0.05 && Math.Abs(props.ShearCenterY) < 0.05,
            $"sc=({props.ShearCenterX:F4},{props.ShearCenterY:F4})");
    }

    public static void CrossValidationBemVsFem()
    {
        TestHarness.Section("Перекрёстная сверка МГЭ↔МКЭ на прямоугольнике 0.3×0.5");
        double b = 0.3, h = 0.5;
        var boundary = new TorsionBoundary(
            new[] { -b / 2, b / 2, b / 2, -b / 2 },
            new[] { -h / 2, -h / 2, h / 2, h / 2 });
        var bem = TorsionSolver.Solve(boundary, TorsionMethod.Bem, 0.025);
        var fem = TorsionSolver.Solve(boundary, TorsionMethod.Fem, 0.025);
        TestHarness.Check("Bem не сингулярна", !bem.Singular);
        TestHarness.Check("It МГЭ > 0", bem.It > 0);
        TestHarness.Check("It МКЭ > 0", fem.It > 0);
        TestHarness.CheckRel("МГЭ vs МКЭ (≤2%)", bem.It, fem.It, 0.02);
    }

    public static void ConvergenceByElementSize()
    {
        TestHarness.Section("Сходимость МКЭ по измельчению сетки");
        double b = 0.2, h = 0.4;
        var boundary = new TorsionBoundary(
            new[] { -b / 2, b / 2, b / 2, -b / 2 },
            new[] { -h / 2, -h / 2, h / 2, h / 2 });
        double itFine = TorsionSolver.Solve(boundary, TorsionMethod.Fem, 0.02).It;
        double itCoarse = TorsionSolver.Solve(boundary, TorsionMethod.Fem, 0.10).It;
        // Эталон — формула Тимошенко для b≤h: b³h·(1/3 − 0.21·(b/h)·(1 − b⁴/(12h⁴)))
        double timo = b * b * b * h * (1.0 / 3.0 - 0.21 * (b / h) * (1.0 - Math.Pow(b / h, 4) / 12.0));
        TestHarness.CheckRel("It мелкая vs Тимошенко (≤5%)", itFine, timo, 0.05);
        TestHarness.CheckRel("It сходимость (fine vs coarse ≤30%)", itFine, itCoarse, 0.30);
    }

    public static void RectangleTimoshenko()
    {
        TestHarness.Section("Прямоугольник: It vs формула Тимошенко");
        // Несколько пропорций (b≤h). elementSize адаптируется к меньшей стороне.
        var cases = new[] { (b: 0.3, h: 0.3), (b: 0.2, h: 0.5), (b: 0.1, h: 0.6), (b: 0.15, h: 0.4) };
        foreach (var (b, h) in cases)
        {
            double es = Math.Min(0.04, b / 5.0); // не грубее ~5 элементов на меньшую сторону
            var boundary = new TorsionBoundary(
                new[] { -b / 2, b / 2, b / 2, -b / 2 },
                new[] { -h / 2, -h / 2, h / 2, h / 2 });
            var bem = TorsionSolver.Solve(boundary, TorsionMethod.Bem, es);
            var fem = TorsionSolver.Solve(boundary, TorsionMethod.Fem, es);
            double timo = b * b * b * h * (1.0 / 3.0 - 0.21 * (b / h) * (1.0 - Math.Pow(b / h, 4) / 12.0));
            TestHarness.CheckRel($"Bem {b}x{h} vs Тимошенко", bem.It, timo, 0.05);
            TestHarness.CheckRel($"Fem {b}x{h} vs Тимошенко", fem.It, timo, 0.05);
        }
    }

    public static void HollowBoxBredt()
    {
        TestHarness.Section("Полая коробка: It МКЭ (константы Прандтля) vs МГЭ");
        double B = 0.3, H = 0.5, t = 0.05;
        var boundary = new TorsionBoundary(
            new[] { -B / 2, B / 2, B / 2, -B / 2 },
            new[] { -H / 2, -H / 2, H / 2, H / 2 },
            new List<(double[] X, double[] Y)>
            {
                (new[] { -(B/2-t), (B/2-t), (B/2-t), -(B/2-t) },
                 new[] { -(H/2-t), -(H/2-t), (H/2-t), (H/2-t) })
            });
        var fem = TorsionSolver.Solve(boundary, TorsionMethod.Fem, 0.03);
        TestHarness.Check("It МКЭ > 0", fem.It > 0);
        TestHarness.Check("It МКЭ конечен", double.IsFinite(fem.It));
        // Бредт — ф-ла тонкой стенки; при t/B=17% корректный МКЭ (c_k≠0) даёт It > Бредт
        double Amid = (B - t) * (H - t);
        double Pmid = 2.0 * ((B - t) + (H - t));
        double bredt = 4.0 * Amid * Amid * t / Pmid;
        TestHarness.Check("It МКЭ > Бредт (корректная постановка)", fem.It > bredt);
        // Эталон Python FEM: ~1.921e-3 м⁴ ≈ Бредт×1.062; проверяем диапазон [0.9, 1.4]×Бредт
        double ratio = fem.It / bredt;
        TestHarness.Check("It МКЭ / Бредт ∈ [0.9, 1.4]", ratio >= 0.9 && ratio <= 1.4, $"ratio={ratio:F3}");
    }

    public static void FemHollowCircleItVsExact()
    {
        TestHarness.Section("МКЭ: полая труба r_out=0.1 r_in=0.06 vs π/2·(r⁴_out−r⁴_in)");
        double rOut = 0.1, rIn = 0.06;
        int nOut = 48, nIn = 36;
        var ox = new double[nOut]; var oy = new double[nOut];
        for (int i = 0; i < nOut; i++) { double a = 2 * Math.PI * i / nOut; ox[i] = rOut * Math.Cos(a); oy[i] = rOut * Math.Sin(a); }
        var hx = new double[nIn]; var hy = new double[nIn];
        for (int i = 0; i < nIn; i++) { double a = 2 * Math.PI * i / nIn; hx[i] = rIn * Math.Cos(a); hy[i] = rIn * Math.Sin(a); }
        var boundary = new TorsionBoundary(ox, oy,
            new List<(double[] X, double[] Y)> { (hx, hy) });
        var fem  = TorsionFemSolver.Solve(boundary, maxElementSize: 0.012);
        double exact = Math.PI / 2.0 * (Math.Pow(rOut, 4) - Math.Pow(rIn, 4));
        TestHarness.Check("It МКЭ > 0", fem.It > 0);
        TestHarness.CheckRel("It МКЭ (полая труба, ≤8%)", fem.It, exact, 0.08);
    }

    public static void BemHollowBoxBredt()
    {
        TestHarness.Section("МГЭ: полая коробка (многосвязная) vs формула Бредта");
        double B = 0.3, H = 0.5, t = 0.05;
        // Отверстие передаётся как CCW — TorsionBemSolver должен сам привести к CW
        var boundary = new TorsionBoundary(
            new[] { -B / 2, B / 2, B / 2, -B / 2 },
            new[] { -H / 2, -H / 2, H / 2, H / 2 },
            new List<(double[] X, double[] Y)>
            {
                (new[] { -(B/2-t), (B/2-t), (B/2-t), -(B/2-t) },
                 new[] { -(H/2-t), -(H/2-t), (H/2-t), (H/2-t) })
            });
        var bem = TorsionSolver.Solve(boundary, TorsionMethod.Bem, 0.025);
        TestHarness.Check("It МГЭ > 0", bem.It > 0);
        TestHarness.Check("It МГЭ конечен", double.IsFinite(bem.It));
        double Amid = (B - t) * (H - t);
        double Pmid = 2.0 * ((B - t) + (H - t));
        double bredt = 4.0 * Amid * Amid * t / Pmid;
        // Python МГЭ даёт ~1.955e-3 ≈ Бредт × 1.081; допуск [0.9, 1.4]
        double ratio = bem.It / bredt;
        TestHarness.Check("It МГЭ / Бредт ∈ [0.9, 1.4]", ratio >= 0.9 && ratio <= 1.4, $"ratio={ratio:F3}");
    }

    public static void BemHollowCircleItVsExact()
    {
        TestHarness.Section("МГЭ: полая труба r_out=0.1 r_in=0.06 vs π/2·(r⁴_out−r⁴_in)");
        double rOut = 0.1, rIn = 0.06;
        int nOut = 64, nIn = 48;
        var ox = new double[nOut]; var oy = new double[nOut];
        for (int i = 0; i < nOut; i++) { double a = 2 * Math.PI * i / nOut; ox[i] = rOut * Math.Cos(a); oy[i] = rOut * Math.Sin(a); }
        var hx = new double[nIn]; var hy = new double[nIn];
        for (int i = 0; i < nIn; i++) { double a = 2 * Math.PI * i / nIn; hx[i] = rIn * Math.Cos(a); hy[i] = rIn * Math.Sin(a); }
        var boundary = new TorsionBoundary(ox, oy,
            new List<(double[] X, double[] Y)> { (hx, hy) });
        var bem = TorsionBemSolver.Solve(boundary, maxElementSize: 0.012);
        double exact = Math.PI / 2.0 * (Math.Pow(rOut, 4) - Math.Pow(rIn, 4));
        TestHarness.Check("It МГЭ > 0", bem.It > 0);
        TestHarness.CheckRel("It МГЭ (полая труба, ≤8%)", bem.It, exact, 0.08);
    }
}

