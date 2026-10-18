using CSfea.CScoreBridge.Structural;
using CSfea.Core;
using CSfea.Sparse;

namespace CSfea.Tests;

/// <summary>
/// Линейная задача устойчивости совместной сетки (<see cref="StructuralMesh.SolveBuckling"/>): случаи Эйлера, шарниры
/// концов (согласованная K_σ), две плоскости, рама Бритвина–Айзенбергера (2025, рис. 5).
/// </summary>
[HarnessChecks]
public class StructuralBucklingTests
{
    private const double E = 2.0594e11;

    // Местные DOF конца: My, Mz.
    private const int My = 1 << 4, Mz = 1 << 5;

    // Маски опор: X Y Z RX RY RZ — биты 0–5.
    private const int Pin = 0b000111, Fixed = 0b111111, XY = 0b000011;

    [Fact]
    public static void RunAll()
    {
        RunJacobi();
        RunEulerCases();
        RunReleasedEnds();
        RunTwoPlanes();
        RunBritvinFrame();
        RunNoCompression();
    }

    private static RcBeamSection Section(double a, double iy, double iz) =>
        new($"A{a} Iy{iy} Iz{iz}") { Elastic = new BeamSection(E, a, iy, iz) };

    /// <summary>Вертикальная стойка длиной <paramref name="l"/> из <paramref name="n"/> КЭ, сила −1 Н по z вверху.</summary>
    private static RcStructuralModel Column(double l, int n, RcBeamSection s, int bottom, int top)
    {
        var m = new RcStructuralModel();
        for (int i = 0; i <= n; i++) m.Nodes.Add(new RcNode(i + 1, 0, 0, l * i / n));
        for (int i = 0; i < n; i++) m.Beams.Add(new RcBeam(101 + i, i + 1, i + 2, s, new[] { 1.0, 0.0, 0.0 }));
        // Кручение закреплено внизу всегда: иначе механизм вокруг оси стойки.
        m.Supports.Add(new RcSupport(1, bottom | (1 << 5)));
        if (top != 0) m.Supports.Add(new RcSupport(n + 1, top));
        var lc = new RcLoadCase(1, "P");
        lc.Nodal.Add(new RcNodalLoad(n + 1, new[] { 0.0, 0, -1, 0, 0, 0 }));
        m.LoadCases.Add(lc);
        return m;
    }

    private static StructuralBucklingResult Buckle(RcStructuralModel m, int modes = 1)
    {
        var build = RcStructuralMeshBuilder.Build(m, new LinearRcSectionFactory());
        var u = build.Mesh.SolveLinear(build.LoadCases[1], build.Bc);
        return build.Mesh.SolveBuckling(u, build.Bc, modes);
    }

    private static void RunJacobi()
    {
        TestHarness.Section("Якоби: собственные значения симметричной матрицы");
        var a = new[,] { { 4.0, 1, -2, 2 }, { 1, 2, 0, 1 }, { -2, 0, 3, -2 }, { 2, 1, -2, -1 } };
        var (values, v) = SymmetricEigen.Jacobi(a);
        double worst = 0;
        for (int k = 0; k < 4; k++)
        {
            var x = new double[4];
            for (int i = 0; i < 4; i++) x[i] = v[i, k];
            var ax = Dense.MatVec(a, x);
            for (int i = 0; i < 4; i++) worst = Math.Max(worst, Math.Abs(ax[i] - values[k] * x[i]));
        }
        TestHarness.Check("A·v = λ·v", worst < 1e-12, $"max {worst:e2}");
        TestHarness.CheckRel("след сохраняется", values.Sum(), 8.0, 1e-12);
    }

    private static void RunEulerCases()
    {
        TestHarness.Section("Устойчивость: случаи Эйлера (8 КЭ на стойку)");
        const double l = 4.0, i = 4.8711e-4;
        var s = Section(2.3695e-2, i, i);
        double pe = Math.PI * Math.PI * E * i / (l * l);
        foreach (var (name, bottom, top, mu) in new[]
        {
            ("шарнир — шарнир", Pin, XY, 1.0),
            ("консоль", Fixed, 0, 2.0),
            ("заделка — заделка (верх скользит по z)", Fixed, 0b111011, 0.5),
            ("заделка — шарнир", Fixed, XY, 0.6992),
        })
        {
            var r = Buckle(Column(l, 8, s, bottom, top));
            TestHarness.Check($"{name}: сошлось", r.Converged && r.Lambda.Length == 1, $"итераций {r.Iterations}");
            double muFe = Math.Sqrt(pe / r.Lambda[0]);
            TestHarness.CheckRel($"{name}: μ", muFe, mu, 2e-3);
        }
    }

    /// <summary>
    /// Шарниры моделями: узлы концов закреплены полностью, концевые КЭ освобождены по My, Mz — как шарнир–шарнир.
    /// Проверяет согласованную с конденсацией K_σ = Pᵀ·K_σ·P.
    /// </summary>
    private static void RunReleasedEnds()
    {
        TestHarness.Section("Устойчивость: шарниры концов КЭ");
        const double l = 5.0, i = 2.1959e-4;
        var s = Section(1.6521e-2, i, i);
        double pe = Math.PI * Math.PI * E * i / (l * l);
        foreach (int n in new[] { 2, 6 })
        {
            var m = Column(l, n, s, Fixed, 0b111011);
            m.Beams[0] = m.Beams[0] with { ReleaseI = My | Mz };
            m.Beams[n - 1] = m.Beams[n - 1] with { ReleaseJ = m.Beams[n - 1].ReleaseJ | My | Mz };
            var free = Buckle(Column(l, n, s, Pin, XY));
            var r = Buckle(m);
            // Освобождённый поворот исключается статически (по K без K_σ) — форма концевого КЭ беднее, чем при
            // свободном повороте узла; разница уходит с дроблением.
            TestHarness.CheckRel($"{n} КЭ: как шарнирные узлы", r.Lambda[0], free.Lambda[0], n == 2 ? 1e-2 : 1e-5);
            TestHarness.CheckRel($"{n} КЭ: P_cr ≈ π²EI/l²", r.Lambda[0], pe, n == 2 ? 2e-2 : 1e-3);
        }
    }

    private static void RunTwoPlanes()
    {
        TestHarness.Section("Устойчивость: две плоскости изгиба");
        const double l = 6.0, iy = 6.755e-5, iz = 2.041e-4;
        var r = Buckle(Column(l, 8, Section(1e-2, iy, iz), Pin, XY), modes: 2);
        double k = Math.PI * Math.PI * E / (l * l);
        TestHarness.Check("две формы", r.Lambda.Length == 2, $"{r.Lambda.Length}");
        TestHarness.CheckRel("первая — по меньшему I", r.Lambda[0], k * iy, 2e-3);
        TestHarness.CheckRel("вторая — по большему I", r.Lambda[1], k * iz, 2e-3);
    }

    /// <summary>
    /// Двухпролётная двухэтажная рама Бритвина–Айзенбергера (IJCCSE 21(4), 2025, рис. 5): колонны HE 320C, ригели
    /// RVS 300×300×15, пролёты 6 + 6 м, этажи 3 + 3 м, базы защемлены; на каждом уровне P, 2P, P вниз и 5P по X.
    /// Плоская работа — из плоскости узлы закреплены. P_cr = 1,005·10⁷ Н.
    /// </summary>
    private static void RunBritvinFrame()
    {
        TestHarness.Section("Устойчивость: рама Бритвина (рис. 5)");
        var col = Section(2.3695e-2, 4.8711e-4, 4.8711e-4);
        var beam = Section(1.6521e-2, 2.1959e-4, 2.1959e-4);
        const int n = 4;
        var m = new RcStructuralModel();
        var ids = new Dictionary<(double, double), int>();
        int Node(double x, double z)
        {
            if (ids.TryGetValue((x, z), out int id)) return id;
            id = m.Nodes.Count + 1;
            m.Nodes.Add(new RcNode(id, x, 0, z));
            return ids[(x, z)] = id;
        }
        int beamId = 100;
        void Member(double x0, double z0, double x1, double z1, RcBeamSection s)
        {
            for (int k = 0; k < n; k++)
            {
                int a = Node(x0 + (x1 - x0) * k / n, z0 + (z1 - z0) * k / n);
                int b = Node(x0 + (x1 - x0) * (k + 1) / n, z0 + (z1 - z0) * (k + 1) / n);
                m.Beams.Add(new RcBeam(++beamId, a, b, s, new[] { 0.0, 1.0, 0.0 }));
            }
        }
        foreach (double x in new[] { 0.0, 6, 12 })
        {
            Member(x, 0, x, 3, col);
            Member(x, 3, x, 6, col);
        }
        foreach (double z in new[] { 3.0, 6 })
        {
            Member(0, z, 6, z, beam);
            Member(6, z, 12, z, beam);
        }
        foreach (var node in m.Nodes)
            m.Supports.Add(new RcSupport(node.Id, node.Z == 0 ? Fixed : 0b101010));   // плоская работа: Y, RX, RZ
        var lc = new RcLoadCase(1, "P");
        foreach (double z in new[] { 3.0, 6 })
        {
            lc.Nodal.Add(new RcNodalLoad(Node(0, z), new[] { 5.0, 0, -1, 0, 0, 0 }));
            lc.Nodal.Add(new RcNodalLoad(Node(6, z), new[] { 0.0, 0, -2, 0, 0, 0 }));
            lc.Nodal.Add(new RcNodalLoad(Node(12, z), new[] { 0.0, 0, -1, 0, 0, 0 }));
        }
        m.LoadCases.Add(lc);
        var r = Buckle(m, modes: 3);
        TestHarness.Check("сошлось", r.Converged, $"итераций {r.Iterations}");
        TestHarness.CheckRel("P_cr", r.Lambda[0], 1.005e7, 5e-3);
        TestHarness.Check("формы по возрастанию λ", r.Lambda.Zip(r.Lambda.Skip(1)).All(p => p.First <= p.Second),
            string.Join("; ", r.Lambda.Select(v => v.ToString("e4"))));
    }

    /// <summary>Растянутая стойка: положительных λ нет — пустой результат, без исключения.</summary>
    private static void RunNoCompression()
    {
        TestHarness.Section("Устойчивость: без сжатия");
        var m = Column(3.0, 4, Section(1e-2, 1e-4, 1e-4), Pin, XY);
        m.LoadCases[0].Nodal[0] = new RcNodalLoad(5, new[] { 0.0, 0, 1, 0, 0, 0 });
        var r = Buckle(m);
        TestHarness.Check("λ нет", r.Lambda.Length == 0, $"{r.Lambda.Length}");
    }
}
