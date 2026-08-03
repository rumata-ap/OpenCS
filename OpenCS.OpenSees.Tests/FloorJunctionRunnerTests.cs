using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CScore;
using CScore.Fem;
using CScore.Planar;
using CScore.Planar.Fragments;
using CSmath;
using OpenCS.OpenSees.Audit;
using OpenCS.OpenSees.CScore.Fragments;
using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Structural;
using ShellResult = OpenCS.OpenSees.Structural.ShellResult;
using Xunit;

namespace OpenCS.OpenSees.Tests;

/// <summary>Preflight-тесты FloorJunctionRunner: доменная валидация -> двухсторонний Gmsh
/// workflow -> assembly -> stages -> boundary pipeline. Анализ (Task 11) здесь не выполняется;
/// на всех negative ветках инъецируемый IShellAnalysisRunner не вызывается.</summary>
public sealed class FloorJunctionRunnerTests
{
    [Fact]
    public async Task RunAsync_WithInvalidDomain_ReturnsAssemblyDiagnosticsAndDoesNotRunOpenSees()
    {
        var fragment = BuildJunctionFragment();
        fragment.Connection.MeshMode = PlanarConnectionMeshMode.EmbeddedLocus;
        var mesher = new JunctionFakeMesher();
        var analysisRunner = new FakeAnalysisRunner(_ => CompletedResult(1.0, [], []));

        var result = await new FloorJunctionRunner(analysisRunner).RunAsync(
            fragment, mesher, Settings(), Settings(), LookupMaterial, CalcType.C,
            "unused.exe", CancellationToken.None);

        Assert.False(result.IsConverged);
        Assert.NotEmpty(result.AssemblyDiagnostics);
        Assert.Contains(result.AssemblyDiagnostics, d => d.Contains("floor_junction_mesh_mode_unsupported"));
        Assert.Equal(0, analysisRunner.CallCount);
    }

    [Fact]
    public async Task RunAsync_WithUncalculableMesh_ReturnsMeshDiagnosticsAndDoesNotRunOpenSees()
    {
        var fragment = BuildJunctionFragment();
        var mesher = new JunctionFakeMesher { Calculable = false };
        var analysisRunner = new FakeAnalysisRunner(_ => CompletedResult(1.0, [], []));

        var result = await new FloorJunctionRunner(analysisRunner).RunAsync(
            fragment, mesher, Settings(), Settings(), LookupMaterial,
            CalcType.C, "unused.exe", CancellationToken.None);

        Assert.False(result.IsConverged);
        Assert.NotEmpty(result.MeshDiagnostics);
        Assert.Equal(0, analysisRunner.CallCount);
    }

    [Fact]
    public async Task RunAsync_WithMissingBoundaryTemplate_ReturnsAssemblyDiagnosticsAndDoesNotRunOpenSees()
    {
        var fragment = BuildJunctionFragment();
        fragment.BoundaryTemplates.Clear(); // boundaries есть, шаблонов нет
        var mesher = new JunctionFakeMesher();
        var analysisRunner = new FakeAnalysisRunner(_ => CompletedResult(1.0, [], []));

        var result = await new FloorJunctionRunner(analysisRunner).RunAsync(
            fragment, mesher, Settings(), Settings(), LookupMaterial,
            CalcType.C, "unused.exe", CancellationToken.None);

        Assert.False(result.IsConverged);
        Assert.NotEmpty(result.AssemblyDiagnostics);
        Assert.Contains(result.AssemblyDiagnostics, d => d.Contains("floor_junction_boundary_template_missing"));
        Assert.Equal(0, analysisRunner.CallCount);
    }

    [Fact]
    public async Task RunAsync_WithValidPreflight_ProducesNoPreflightDiagnostics()
    {
        var fragment = BuildJunctionFragment();
        var mesher = new JunctionFakeMesher();
        var analysisRunner = new FakeAnalysisRunner(_ => CompletedResult(1.0, [], []));

        var result = await new FloorJunctionRunner(analysisRunner).RunAsync(
            fragment, mesher, Settings(), Settings(), LookupMaterial,
            CalcType.C, "unused.exe", CancellationToken.None);

        // Шаг анализа реализуется в Task 11 — здесь важен только preflight без ошибок.
        Assert.Empty(result.MeshDiagnostics);
        Assert.Empty(result.AssemblyDiagnostics);
        Assert.Empty(result.BoundaryDiagnostics);
    }

    // --- fixtures (общие для Task 10 и 11) ---

    static (Material Concrete, Material Rebar) Materials() =>
    (
        new Material { Id = 1, Tag = "B25", Type = MatType.Concrete,
            C = new MaterialChars { E = 30_000_000, Fc = -17_000, Ft = 1_150, Ec0 = -0.002, Ec2 = -0.0035 } },
        new Material { Id = 2, Tag = "A400", Type = MatType.ReSteelF,
            C = new MaterialChars { E = 200_000_000, Ft = 355_000, Ru = 500_000, Et2 = 0.05 } }
    );

    static Material? LookupMaterial(int id) => id switch
    {
        1 => Materials().Concrete,
        2 => Materials().Rebar,
        _ => null
    };

    static PlanarMeshSettings Settings() => new(0.5, 6, PlanarMeshElementMode.Mixed);

    static ShellAnalysisRunResult CompletedResult(
        double loadFactor,
        IReadOnlyList<ShellNodeDisplacement> displacements,
        IReadOnlyList<ShellNodeReaction> reactions) => new(
        ShellAnalysisOutcome.Completed,
        new ShellResult
        {
            Status = "completed",
            Steps =
            [
                new RCShellStepResult(0, 0, loadFactor, true, displacements, reactions, [], [], [])
            ],
            Displacements = displacements,
            Reactions = reactions
        },
        @"C:\temp\fake-artifacts",
        null);

    static FloorJunctionFragment BuildJunctionFragment()
    {
        var plate = PlanarRegion.CreateFromContour(
            new Contour { Id = 1, Tag = "plate", X = [0, 4, 4, 0], Y = [0, 0, 4, 4] },
            frame: Frame3D.Identity, tag: "plate");
        plate.Id = 10;
        var wall = PlanarRegion.CreateFromContour(
            new Contour { Id = 2, Tag = "wall", X = [0, 4, 4, 0], Y = [-1, -1, 1, 1] },
            frame: new Frame3D(new(2, 0, 0), new(0, 1, 0), new(0, 0, 1), new(1, 0, 0)),
            tag: "wall");
        wall.Id = 20;

        var fragment = new FloorJunctionFragment
        {
            FragmentId = 1,
            Name = "Junction Runner Test",
            PlateRegion = plate,
            WallRegion = wall,
            PlateSection = new PlateSection { H = 0.2, NLayers = 4, ConcreteMaterialId = 1, RebarMaterialId = 2 },
            WallSection = new PlateSection { H = 0.2, NLayers = 4, ConcreteMaterialId = 1, RebarMaterialId = 2 },
            Connection = new PlanarConnection
            {
                Id = 7,
                MeshMode = PlanarConnectionMeshMode.ConformingPartition,
                MatchingToleranceM = 1e-8,
                SideA = new ConnectionLocus(10, [new(2, 1), new(2, 3)]),
                SideB = new ConnectionLocus(20, [new(1, 0), new(3, 0)])
            },
            StageConfig = FragmentStageConfig.CreateDefault1Stage()
        };
        fragment.Boundaries.Add(new FloorJunctionBoundary
        {
            Id = "plate-fix",
            RegionId = 10,
            Cut = new PlanarCutInterface
            {
                Id = "plate-fix",
                Geometry = new PlanarConstraintGeometry(PlanarConstraintGeometryKind.Curve,
                    [new(0, 0), new(0, 4)]),
                NormalFromFragmentToOmittedSide = new(-1, 0, 0),
                BoundaryKey = new PlanarBoundaryKey(BoundaryLoop.Outer, 0, 3, 0),
                ModeByDof = PlanarBoundaryModeByDof.All(PlanarBoundaryDofMode.PreserveSupport)
            }
        });
        fragment.Boundaries.Add(new FloorJunctionBoundary
        {
            Id = "wall-top",
            RegionId = 20,
            Cut = new PlanarCutInterface
            {
                Id = "wall-top",
                Geometry = new PlanarConstraintGeometry(PlanarConstraintGeometryKind.Curve,
                    [new(4, 1), new(0, 1)]),
                NormalFromFragmentToOmittedSide = new(0, 0, 1),
                BoundaryKey = new PlanarBoundaryKey(BoundaryLoop.Outer, 0, 2, 3),
                ModeByDof = PlanarBoundaryModeByDof.All(PlanarBoundaryDofMode.Force)
            }
        });
        fragment.BoundaryTemplates["plate-fix"] = new PlanarBoundaryActionSet
        {
            SourceMode = PlanarBoundaryActionSourceMode.Template
        };
        fragment.BoundaryTemplates["wall-top"] = new PlanarBoundaryActionSet
        {
            SourceMode = PlanarBoundaryActionSourceMode.Template,
            ForceActions =
            [
                new PlanarBoundaryForceAction
                {
                    InterfaceId = "wall-top",
                    DofMask = PlanarDofMask.UZ,
                    Frame = Frame3D.Identity,
                    Samples =
                    [
                        new(0, new PlanarVector3(0, 0, -1500), PlanarVector3.Zero),
                        new(1, new PlanarVector3(0, 0, -1500), PlanarVector3.Zero)
                    ]
                }
            ]
        };
        return fragment;
    }

    sealed class FakeAnalysisRunner(
        System.Func<ShellOpenSeesModel, ShellAnalysisRunResult> resultFactory) : IShellAnalysisRunner
    {
        public int CallCount { get; private set; }
        public ShellOpenSeesModel? LastModel { get; private set; }

        public Task<ShellAnalysisRunResult> RunAsync(
            ShellOpenSeesModel model, string executablePath, CancellationToken cancellationToken)
        {
            CallCount++;
            LastModel = model;
            return Task.FromResult(resultFactory(model));
        }
    }

    /// <summary>Двухсторонний fake mesher, воспроизводящий сетки Task 7 и Constraint/Boundary
    /// mappings, необходимые connection- и boundary-mapping-ам.</summary>
    sealed class JunctionFakeMesher : IPlanarMesher
    {
        public bool Calculable { get; init; } = true;

        public Task<PlanarMeshSnapshot> BuildAsync(
            PlanarMeshingRequest request, CancellationToken cancellationToken = default)
        {
            if (!Calculable)
            {
                return Task.FromResult(new PlanarMeshSnapshot
                {
                    RegionId = request.Region.Id,
                    InputFingerprint = "failed",
                    IsCalculable = false,
                    Diagnostics = [new FemValidationDiagnostic("fake_mesh_error", "Fake mesh failure.")]
                });
            }

            bool isPlate = request.Region.Id == 10;
            PlanarMeshNode[] nodes;
            if (isPlate)
            {
                nodes =
                [
                    new(0, 0, 0, 0, 0, 0), new(1, 4, 0, 4, 0, 0),
                    new(2, 4, 4, 4, 4, 0), new(3, 0, 4, 0, 4, 0),
                    new(4, 2, 1, 2, 1, 0), new(5, 2, 3, 2, 3, 0)
                ];
            }
            else
            {
                // Wall frame: (U,V) -> (2, U, V), контур V в [-1, 1] (V=0 внутри стены).
                // Connection: (U=1..3, V=0) -> global (2,1..3, 0).
                nodes =
                [
                    new(0, 0, -1, 2, 0, -1), new(1, 4, -1, 2, 4, -1),
                    new(2, 4, 1, 2, 4, 1), new(3, 0, 1, 2, 0, 1),
                    new(4, 1, 0, 2, 1, 0), new(5, 3, 0, 2, 3, 0)
                ];
            }
            var elements = new List<PlanarMeshElement>
            {
                new(0, PlanarMeshElementKind.Quadrangle4, [0, 4, 5, 3]),
                new(1, PlanarMeshElementKind.Quadrangle4, [4, 1, 2, 5])
            };
            var constraintMappings = request.EffectiveConstraintObjects
                .Select(constraint => new PlanarConstraintMeshMapping
                {
                    ConstraintObjectId = constraint.Id,
                    OrderedCurveEdges = [new PlanarMeshEdge(4, 5)]
                })
                .ToList();
            var boundaryMappings = isPlate
                ? new List<PlanarMeshBoundaryMapping>
                  {
                      new() { Key = new PlanarBoundaryKey(BoundaryLoop.Outer, 0, 3, 0), NodeIndices = [0, 3] }
                  }
                : new List<PlanarMeshBoundaryMapping>
                  {
                      new() { Key = new PlanarBoundaryKey(BoundaryLoop.Outer, 0, 2, 3), NodeIndices = [2, 3] }
                  };

            return Task.FromResult(new PlanarMeshSnapshot
            {
                Id = request.Region.Id + 100,
                RegionId = request.Region.Id,
                InputFingerprint = request.Region.Id == 10 ? "snapshot-a" : "snapshot-b",
                IsCalculable = true,
                Nodes = nodes,
                Elements = elements,
                ConstraintMappings = constraintMappings,
                BoundaryMappings = boundaryMappings
            });
        }
    }
}
