using CScore.Planar;
using CSfea.CScoreBridge;
using CSfea.Core;

namespace CSfea.Tests;

/// <summary>Проверяет перенос узловых PlanarLoad-результатов в вектор CSfea.</summary>
public static class PlanarLoadShellMeshAdapterTests
{
    public static void RunAll()
    {
        TestHarness.Section("PlanarLoad → CSfea.Core.ShellMesh: nodal force vector");
        RunTranslationalDofsOnly();
        RunBoundarySetMapping();
    }

    static void RunTranslationalDofsOnly()
    {
        var response = new LinearLaminateResponse(new Laminate(
            [new Ply(new OrthotropicMaterial(30_000, 30_000, 0.2, 12_500), 0, 0.1)]));
        var mesh = new ShellMesh(
            [[0, 0, 0], [1, 0, 0], [0, 1, 0]],
            [[0, 1, 2]],
            response);
        var result = new PlanarLoadMappingResult(
            true,
            [],
            new Dictionary<int, PlanarVector3> { [1] = new(10, 20, 30) },
            [],
            new(10, 20, 30),
            PlanarVector3.Zero,
            new(10, 20, 30),
            PlanarVector3.Zero);

        double[] vector = PlanarLoadShellMeshAdapter.ToNodalForceVector(result, mesh);

        TestHarness.Check("вектор имеет размер 6·N", vector.Length == 18, $"count={vector.Length}");
        TestHarness.Check("Fx/Fy/Fz записаны в узел 1", vector[6] == 10 && vector[7] == 20 && vector[8] == 30,
            $"[{vector[6]}, {vector[7]}, {vector[8]}]");
        TestHarness.Check("вращательные DOF остаются нулевыми", vector.Skip(9).All(value => value == 0),
            $"nonzero={string.Join(',', vector.Skip(9).Where(value => value != 0))}");
    }

    static void RunBoundarySetMapping()
    {
        var response = new LinearLaminateResponse(new Laminate(
            [new Ply(new OrthotropicMaterial(30_000, 30_000, 0.2, 12_500), 0, 0.1)]));
        var mesh = new ShellMesh(
            [[0, 0, 0], [1, 0, 0], [0, 1, 0]],
            [[0, 1, 2]],
            response);
        var set = new PlanarBoundarySet(
            BoundaryRole.Support,
            [new PlanarBoundaryKey(BoundaryLoop.Outer, 0, 0, 1)],
            [0, 1],
            [(0, 1)]);

        var mapped = PlanarLoadShellMeshAdapter.MapBoundarySet(set, mesh);

        TestHarness.Check("boundary set сохраняет роль", mapped.Role == BoundaryRole.Support);
        TestHarness.Check("boundary set сохраняет nodes/edges", mapped.NodeIndices.SequenceEqual([0, 1]) && mapped.Edges.SequenceEqual([(0, 1)]));
    }
}
