using CScore.Fem;
using CScore.Planar;
using CSfea.CScoreBridge;
using CSfea.Core;

namespace CSfea.Tests;

/// <summary>Проверяет перенос cut-interface actions в полный boundary input CSfea.</summary>
public static class PlanarBoundaryActionShellMeshAdapterTests
{
    public static void RunAll()
    {
        TestHarness.Section("Planar boundary action → CSfea ShellMesh");
        Apply_MapsForceAndMomentToSixDofVector();
        Apply_MapsPreserveSupportAndKinematicValues();
        Apply_RejectsConflictingDofs();
        Apply_RejectsNonCalculableMapping();
    }

    static void Apply_MapsForceAndMomentToSixDofVector()
    {
        var result = new PlanarBoundaryActionMeshMappingResult
        {
            NodalActions =
            [
                new(1, new(10, 20, 30), new(1, 2, 3)),
                new(1, new(1, 2, 3), new(4, 5, 6))
            ],
            MappedForceGlobal = new(11, 22, 33),
            MappedMomentGlobal = new(5, -26, 31)
        };

        var mapped = PlanarBoundaryActionShellMeshAdapter.Apply(Mesh(), result);

        TestHarness.Check("force mapping calculable", mapped.IsCalculable, Diagnostics(mapped));
        TestHarness.Check("six DOF force/moment записаны и сложены", mapped.NodalForceVector[6..12].SequenceEqual([11, 22, 33, 5, 7, 9]),
            string.Join(',', mapped.NodalForceVector[6..12]));
        var force = new PlanarVector3(mapped.NodalForceVector[6], mapped.NodalForceVector[7], mapped.NodalForceVector[8]);
        var moment = new PlanarVector3(mapped.NodalForceVector[9], mapped.NodalForceVector[10], mapped.NodalForceVector[11]);
        var nodalMoment = new PlanarVector3(1, 0, 0).Cross(force) + moment;
        TestHarness.Check("force/moment balance сохраняется", force == result.MappedForceGlobal && nodalMoment == result.MappedMomentGlobal,
            $"force={force}, moment={nodalMoment}");
        TestHarness.Check("provenance mapping сохраняется", ReferenceEquals(result, mapped.SourceMapping));
    }

    static void Apply_MapsPreserveSupportAndKinematicValues()
    {
        var result = new PlanarBoundaryActionMeshMappingResult
        {
            PreservedSupportDofs = new HashSet<(int NodeIndex, int Dof)> { (0, 0) },
            PrescribedDofs = new Dictionary<(int NodeIndex, int Dof), double> { [(1, 2)] = 0.01 }
        };

        var mapped = PlanarBoundaryActionShellMeshAdapter.Apply(Mesh(), result);

        TestHarness.Check("boundary conditions calculable", mapped.IsCalculable, Diagnostics(mapped));
        TestHarness.Check("fixed DOF отсортированы", mapped.FixedDofs.SequenceEqual([0, 8]),
            string.Join(',', mapped.FixedDofs));
        TestHarness.Check("uFixed сохраняет nonzero prescribed value", mapped.UFixed.SequenceEqual([0, 0.01]),
            string.Join(',', mapped.UFixed));
    }

    static void Apply_RejectsConflictingDofs()
    {
        var result = new PlanarBoundaryActionMeshMappingResult
        {
            PreservedSupportDofs = new HashSet<(int NodeIndex, int Dof)> { (1, 2) },
            PrescribedDofs = new Dictionary<(int NodeIndex, int Dof), double> { [(1, 2)] = 0.01 }
        };

        var mapped = PlanarBoundaryActionShellMeshAdapter.Apply(Mesh(), result);

        TestHarness.Check("fixed/prescribed conflict блокирует", !mapped.IsCalculable,
            Diagnostics(mapped));
    }

    static void Apply_RejectsNonCalculableMapping()
    {
        var result = new PlanarBoundaryActionMeshMappingResult
        {
            Diagnostics = [new FemValidationDiagnostic("mapping_failed", "failed")]
        };

        var mapped = PlanarBoundaryActionShellMeshAdapter.Apply(Mesh(), result);

        TestHarness.Check("нерасчётный mapping не передаётся в CSfea", !mapped.IsCalculable,
            Diagnostics(mapped));
        TestHarness.Check("исходная диагностика сохраняется",
            mapped.Diagnostics.Any(diagnostic => diagnostic.Code == "mapping_failed"),
            Diagnostics(mapped));
    }

    static ShellMesh Mesh()
    {
        var response = new LinearLaminateResponse(new Laminate(
            [new Ply(new OrthotropicMaterial(30_000, 30_000, 0.2, 12_500), 0, 0.1)]));
        return new ShellMesh(
            [[0, 0, 0], [1, 0, 0], [0, 1, 0]],
            [[0, 1, 2]],
            response);
    }

    static string Diagnostics(PlanarBoundaryShellMeshResult result) =>
        string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message));
}
