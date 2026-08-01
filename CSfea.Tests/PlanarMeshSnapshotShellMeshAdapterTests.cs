using CScore;
using CScore.Fem;
using CScore.Planar;
using CScore.PlateRebar;
using CSfea.CScoreBridge;

namespace CSfea.Tests;

/// <summary>Тесты геометрического адаптера PlanarMeshSnapshot → CSfea.Core.ShellMesh.</summary>
public static class PlanarMeshSnapshotShellMeshAdapterTests
{
    public static void RunAll()
    {
        TestHarness.Section("PlanarMeshSnapshot → CSfea.Core.ShellMesh: геометрия и дедуп секций");
        RunNoNodeReorderAndSectionDedup();
    }

    static void RunNoNodeReorderAndSectionDedup()
    {
        // Один T3 и один Q4, оба вне какой-либо зоны армирования (общий baseline-отклик).
        var snapshot = new PlanarMeshSnapshot
        {
            Id = 1,
            RegionId = 1,
            IsCalculable = true,
            Nodes =
            [
                new(0, 0, 0, 0, 0, 0),
                new(1, 1, 0, 1, 0, 0),
                new(2, 1, 1, 1, 1, 0),
                new(3, 0, 1, 0, 1, 0),
                new(4, 2, 0, 2, 0, 0),
                new(5, 2, 1, 2, 1, 0),
            ],
            Elements =
            [
                new(0, PlanarMeshElementKind.Triangle3, [0, 1, 2]),
                new(1, PlanarMeshElementKind.Quadrangle4, [1, 4, 5, 2]),
            ],
        };

        double e = 30_000, h = 0.2; // МПа, м
        var concrete = LinearDiagram(e);
        var section = new PlateSection { H = h, NLayers = 8, TensionConcrete = true };
        var field = new PlateRebarField([], []);
        var materials = new PlateSectionMaterials { ConcreteDiagram = concrete, RebarDiagram = concrete, ConcreteE_MPa = e };

        PlanarMeshShellMeshResult result = PlanarMeshSnapshotShellMeshAdapter.Build(snapshot, section, field, materials);

        TestHarness.Check("6 узлов сетки", result.Mesh.Nodes.Length == 6, $"count={result.Mesh.Nodes.Length}");
        TestHarness.Check("2 элемента сетки", result.Mesh.Elements.Length == 2, $"count={result.Mesh.Elements.Length}");
        TestHarness.Check("T3 connectivity сохранена без реордера",
            result.Mesh.Elements[0].SequenceEqual(new[] { 0, 1, 2 }),
            $"[{string.Join(',', result.Mesh.Elements[0])}]");
        TestHarness.Check("Q4 connectivity сохранена без реордера",
            result.Mesh.Elements[1].SequenceEqual(new[] { 1, 4, 5, 2 }),
            $"[{string.Join(',', result.Mesh.Elements[1])}]");
        TestHarness.Check("узел 4 — координаты (2,0,0)",
            result.Mesh.Nodes[4].SequenceEqual(new[] { 2.0, 0.0, 0.0 }),
            $"[{string.Join(',', result.Mesh.Nodes[4])}]");
        TestHarness.Check("оба элемента без армирования → один и тот же отклик-секция (дедуп)",
            ReferenceEquals(result.Mesh.Section(0), result.Mesh.Section(1)), "responses differ");
        TestHarness.Check("диагностик нет (нет зон армирования)", result.Diagnostics.Count == 0,
            $"count={result.Diagnostics.Count}");
    }

    static Diagramm LinearDiagram(double e_MPa)
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
}
