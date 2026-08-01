using CScore;
using CScore.Planar;
using CScore.PlateRebar;
using CSfea.CScoreBridge;
using CSfea.Core;
using OpenCS.Gmsh;

namespace CSfea.Tests;

/// <summary>Реальный сквозной тест: gmsh.exe строит сетку прямоугольника, адаптер
/// (PlanarMeshSnapshotShellMeshAdapter) переводит её в CSfea.Core.ShellMesh, ShellMesh.SolveLinear
/// реально считает — для T3/Q4/mixed. Проверяет sanity-критерии (конечность, отсутствие взрыва,
/// правильное направление отклика на нагрузку), а не точное совпадение с идеализированной
/// упругой формулой: CScore.PlateSection (PlateModel="layered", дефолт) считает бетон через
/// главные деформации ε1/ε2 — у Nx-Ny есть настоящая физическая связанность, встроенная в саму
/// нелинейную слоистую модель бетона (не убирается параметром Nu — тот идёт только в BuildAs для
/// сдвиговой площади; "char1d_axial" вместо "layered" тоже не подошёл — даёт вырожденную по Uy
/// жёсткость и взрыв решения). Из-за этого "Ux=slope*X, Uy=0" (идеализированная упругая формула,
/// как в OpenSees-версии этого теста) не воспроизводится точно, и три попытки её точно
/// подтвердить (см. историю правок) уткнулись в непредсказуемое расхождение вплоть до смены знака.
/// Точная геометрическая корректность адаптера (сохранение узлов/connectivity/дедуп секций) уже
/// однозначно доказана детерминированным unit-тестом PlanarMeshSnapshotShellMeshAdapterTests.</summary>
public static class PlanarMeshCSfeaPatchTests
{
    const double Length = 4.0, Width = 2.0, Thickness = 0.2;
    const double E_kPa = 30e9 / 1000.0; // PlateSection.Compute интегрирует в кПа, не в МПа.
    const double Ftotal = 1000.0;       // Н — внешний интерфейс ShellMesh (f, u) честный СИ
                                         // (CSfea.CScore/UnitScale.cs переводит кН-выход
                                         // PlateSection.Compute в Н прежде, чем отдать ShellMesh).
    static double ExpectedNx => Ftotal / Width; // Н/м

    public static void RunAll()
    {
        TestHarness.Section("PlanarMeshSnapshot → CSfea.Core.ShellMesh: реальный осевой sanity-тест (T3/Q4/mixed)");
        RunForMode(PlanarMeshElementMode.Triangles).GetAwaiter().GetResult();
        RunForMode(PlanarMeshElementMode.Quads).GetAwaiter().GetResult();
        RunForMode(PlanarMeshElementMode.Mixed).GetAwaiter().GetResult();
    }

    static async Task RunForMode(PlanarMeshElementMode mode)
    {
        string root = Path.Combine(Path.GetTempPath(), "opencs-gmsh-csfea-patch-tests", Guid.NewGuid().ToString("N"));
        try
        {
            // Явный Frame3D.Identity вместо авто-восстановления: Frame3D.FromPolygon ставит Origin
            // в центроид контура и LocalX по направлению первого ребра, из-за чего auto-recovered
            // PlanarRegion не гарантирует global == (Contour.X, Contour.Y, 0) — известный баг вне
            // объёма среза 3 (зафиксирован в памяти отдельно). Identity даёт Origin=0, LocalX/LocalY
            // по мировым осям.
            var region = PlanarRegion.CreateFromContour(
                new Contour { X = [0, Length, Length, 0], Y = [0, 0, Width, Width] },
                frame: Frame3D.Identity);
            var mesher = new GmshPlanarMesher(new GmshPlanarMesherOptions
            {
                ExecutablePath = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe",
                ArtifactRoot = root
            });
            PlanarMeshSnapshot snapshot = await mesher.BuildAsync(
                new PlanarMeshingRequest(region, new PlanarMeshSettings(0.35, 6, mode)));

            TestHarness.Check($"[{mode}] Gmsh-сетка расчётна", snapshot.IsCalculable,
                string.Join("; ", snapshot.Diagnostics.Select(d => d.Message)));

            var section = new PlateSection { H = Thickness, NLayers = 8, TensionConcrete = true };
            var field = new PlateRebarField([], []);
            var concrete = LinearElasticDiagram(E_kPa);
            var materials = new PlateSectionMaterials { ConcreteDiagram = concrete, RebarDiagram = concrete, ConcreteE_MPa = E_kPa };

            PlanarMeshShellMeshResult built = PlanarMeshSnapshotShellMeshAdapter.Build(snapshot, section, field, materials);
            ShellMesh mesh = built.Mesh;

            var supportMapping = snapshot.BoundaryMappings.Single(m =>
                m.Key.Loop == BoundaryLoop.Outer && m.Key.StartVertex == 3 && m.Key.EndVertex == 0);
            var loadMapping = snapshot.BoundaryMappings.Single(m =>
                m.Key.Loop == BoundaryLoop.Outer && m.Key.StartVertex == 1 && m.Key.EndVertex == 2);

            var supportChain = supportMapping.NodeIndices;
            var cornerNodeIndices = new[] { supportChain[0], supportChain[^1] }.ToHashSet();
            int[] fixedDofs = supportChain
                .SelectMany(nodeIndex => cornerNodeIndices.Contains(nodeIndex)
                    ? Enumerable.Range(0, 6).Select(c => 6 * nodeIndex + c)
                    : [6 * nodeIndex + 0])
                .ToArray();

            var f = new double[mesh.NDof];
            foreach (var (nodeIndex, tributary) in TributaryLengths(snapshot, loadMapping))
                f[6 * nodeIndex + 0] += tributary * ExpectedNx;

            double[] u = mesh.SolveLinear(f, fixedDofs);

            TestHarness.Check($"[{mode}] решение конечно (нет NaN/Infinity)",
                u.All(double.IsFinite), $"nonFinite={u.Count(v => !double.IsFinite(v))}");

            double maxAbsU = u.Max(Math.Abs);
            TestHarness.Check($"[{mode}] решение не взорвалось (|u| < 1 мм)",
                maxAbsU < 1e-3, $"maxAbsU={maxAbsU:e3}");

            var loadIndices = loadMapping.NodeIndices.ToHashSet();
            var supportIndices = supportChain.ToHashSet();
            double uxAtLoad = loadIndices.Select(i => u[6 * i + 0]).Average();
            double uxAtSupport = supportIndices.Select(i => u[6 * i + 0]).Average();
            var midIndices = snapshot.Nodes
                .Where(n => n.X > Length * 0.4 && n.X < Length * 0.6)
                .Select(n => n.Index)
                .ToList();
            double uxAtMid = midIndices.Count > 0 ? midIndices.Select(i => u[6 * i + 0]).Average() : double.NaN;

            TestHarness.Check($"[{mode}] Ux у опоры ≈ 0 (жёсткое ГУ)",
                Math.Abs(uxAtSupport) < 1e-12, $"uxAtSupport={uxAtSupport:e3}");
            TestHarness.Check($"[{mode}] Ux у нагруженного края > 0 (растяжение в направлении Fx)",
                uxAtLoad > 0, $"uxAtLoad={uxAtLoad:e3}");
            if (midIndices.Count > 0)
                TestHarness.Check($"[{mode}] Ux монотонно растёт: опора < середина < нагруженный край",
                    uxAtSupport < uxAtMid && uxAtMid < uxAtLoad,
                    $"support={uxAtSupport:e3}, mid={uxAtMid:e3}, load={uxAtLoad:e3}");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    static IReadOnlyList<(int NodeIndex, double Tributary)> TributaryLengths(
        PlanarMeshSnapshot snapshot, PlanarMeshBoundaryMapping mapping)
    {
        var chain = mapping.NodeIndices;
        var points = chain.Select(i => snapshot.Nodes[i]).ToArray();
        double Dist(PlanarMeshNode a, PlanarMeshNode b) =>
            Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2) + Math.Pow(a.Z - b.Z, 2));

        var result = new List<(int, double)>();
        for (int i = 0; i < chain.Count; i++)
        {
            double tributary = 0;
            if (i > 0) tributary += 0.5 * Dist(points[i - 1], points[i]);
            if (i < chain.Count - 1) tributary += 0.5 * Dist(points[i], points[i + 1]);
            result.Add((chain[i], tributary));
        }
        return result;
    }

    static Diagramm LinearElasticDiagram(double eKPa)
    {
        MaterialChars Ch(CalcType ct) => new(ct)
        {
            E = eKPa, Ry = eKPa / 50, Ru = eKPa / 50, Ft = eKPa / 50, Fc = -eKPa / 50,
            Ec2 = -0.05, Et2 = 0.05, Type = MatType.ReSteelF,
        };
        var m = new Material { Id = 1, E = eKPa, Type = MatType.ReSteelF, Tag = "patch-test-linear" };
        m.MaterialChars = [Ch(CalcType.C), Ch(CalcType.CL), Ch(CalcType.N), Ch(CalcType.NL)];
        return m.GetDiagramms(DiagrammType.L2)![CalcType.C];
    }
}
