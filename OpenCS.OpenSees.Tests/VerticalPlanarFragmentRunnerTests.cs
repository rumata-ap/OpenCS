using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CScore;
using CScore.Fem;
using CScore.Planar;
using CScore.Planar.Fragments;
using CSmath;
using OpenCS.OpenSees.CScore.Fragments;
using OpenCS.OpenSees.Tests.Fixtures;
using Xunit;

namespace OpenCS.OpenSees.Tests
{
    public class VerticalPlanarFragmentRunnerTests
    {
        [Fact]
        public async Task RunAsync_WithUncalculableMesh_ReturnsMeshDiagnosticsWithoutRunningOpenSees()
        {
            var fragment = BuildFragment();
            var mesher = new FakeMesher(calculable: false);

            var result = await new VerticalPlanarFragmentRunner().RunAsync(
                fragment, mesher, DefaultSettings(), _ => null, CalcType.C,
                openSeesExecutablePath: "unused.exe", CancellationToken.None);

            Assert.False(result.IsConverged);
            Assert.NotEmpty(result.MeshDiagnostics);
        }

        [Fact]
        public async Task RunAsync_WithMissingBoundaryKeyMapping_ReturnsBoundaryDiagnostics()
        {
            var fragment = BuildFragment();
            // BottomCut.BoundaryKey не совпадает ни с одним boundary mapping снимка — mesher
            // возвращает расчётный, но пустой по boundary mappings снимок.
            var mesher = new FakeMesher(calculable: true, withBoundaryMapping: false);

            var result = await new VerticalPlanarFragmentRunner().RunAsync(
                fragment, mesher, DefaultSettings(), _ => null, CalcType.C,
                openSeesExecutablePath: "unused.exe", CancellationToken.None);

            Assert.False(result.IsConverged);
            Assert.NotEmpty(result.BoundaryDiagnostics);
        }

        [Fact]
        public async Task RunAsync_WithMissingBoundaryTemplate_ReturnsBoundaryDiagnosticsAndDoesNotRunOpenSees()
        {
            var fragment = BuildFragment(); // без BoundaryTemplates — намеренно пусто
            var mesher = new FakeMesher(calculable: true);
            var (concrete, rebar) = NonlinearMaterials();
            var lookup = new Dictionary<int, Material> { [1] = concrete, [2] = rebar };

            var result = await new VerticalPlanarFragmentRunner().RunAsync(
                fragment, mesher, DefaultSettings(), id => lookup.GetValueOrDefault(id), CalcType.C,
                openSeesExecutablePath: "unused.exe", CancellationToken.None);

            Assert.False(result.IsConverged);
            Assert.NotEmpty(result.BoundaryDiagnostics);
            Assert.Contains(result.BoundaryDiagnostics, d => d.Contains("planar_boundary_provider_missing")
                || d.Contains("template", System.StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task RunAsync_WithRealGmshAndOpenSees_ProducesNonHardcodedResult()
        {
            string openSeesExecutable = OpenSeesTestExecutable.ResolveOrSkip();
            string gmshRoot = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "opencs-fragment-runner-smoke", System.Guid.NewGuid().ToString("N"));
            try
            {
                var mesher = new OpenCS.Gmsh.GmshPlanarMesher(new OpenCS.Gmsh.GmshPlanarMesherOptions
                {
                    ExecutablePath = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe",
                    ArtifactRoot = gmshRoot
                });
                var fragment = BuildFragment(); // 2x3 м прямоугольная стена без проёма
                var (concrete, rebar) = NonlinearMaterials();
                var lookup = new Dictionary<int, Material> { [1] = concrete, [2] = rebar };

                // BottomCut.ModeByDof = PreserveSupport (см. BuildFragment) -> реальный OpenSees
                // `fix`, а не `sp`-констрейнт на нулевое перемещение. Шаблон нужен только чтобы
                // Runner вызвал mapping pipeline для этого cut interface — действий в нём нет.
                fragment.BoundaryTemplates["bottom"] = new PlanarBoundaryActionSet
                {
                    SourceMode = PlanarBoundaryActionSourceMode.Template
                };
                fragment.BoundaryTemplates["top"] = new PlanarBoundaryActionSet
                {
                    SourceMode = PlanarBoundaryActionSourceMode.Template,
                    ForceActions =
                    [
                        new PlanarBoundaryForceAction
                        {
                            InterfaceId = "top",
                            DofMask = PlanarDofMask.UZ,
                            Samples = [new(0, new PlanarVector3(0, 0, -2000), PlanarVector3.Zero),
                                       new(1, new PlanarVector3(0, 0, -2000), PlanarVector3.Zero)]
                        }
                    ]
                };

                var result = await new VerticalPlanarFragmentRunner().RunAsync(
                    fragment, mesher, new PlanarMeshSettings(0.5, 6, PlanarMeshElementMode.Quads),
                    id => lookup.GetValueOrDefault(id), CalcType.C, openSeesExecutable, CancellationToken.None);

                Assert.Empty(result.MeshDiagnostics);
                Assert.Empty(result.BoundaryDiagnostics);
                Assert.True(result.IsConverged, "Реальный расчёт должен сойтись на простой прямоугольной стене.");
                Assert.NotEqual(-0.0015, result.MaxConcreteCompressionStrain);
                Assert.NotEqual(0.0020, result.MaxRebarTensileStrain);
                Assert.NotEqual(0.001, result.ForceUnbalanceRatio);
                Assert.True(double.IsFinite(result.MaxConcreteCompressionStrain));
                Assert.True(double.IsFinite(result.MaxRebarTensileStrain));
                Assert.NotEmpty(result.LayerStates);
            }
            finally
            {
                if (System.IO.Directory.Exists(gmshRoot)) System.IO.Directory.Delete(gmshRoot, recursive: true);
            }
        }

        static (Material Concrete, Material Rebar) NonlinearMaterials() =>
        (
            new Material { Id = 1, Tag = "B25", Type = MatType.Concrete,
                C = new MaterialChars { E = 30_000_000, Fc = -17_000, Ft = 1_150, Ec0 = -0.002, Ec2 = -0.0035 } },
            new Material { Id = 2, Tag = "A400", Type = MatType.ReSteelF,
                C = new MaterialChars { E = 200_000_000, Ft = 355_000, Ru = 500_000, Et2 = 0.05 } }
        );

        static VerticalPlanarFragment BuildFragment()
        {
            var contour = new Contour { Id = 1, Tag = "wall", X = [0, 2, 2, 0], Y = [0, 0, 3, 3] };
            var region = PlanarRegion.CreateFromContour(contour, frame: Frame3D.Identity, tag: "wall");
            region.Id = 1;
            var bottomKey = new PlanarBoundaryKey(BoundaryLoop.Outer, 0, 0, 1);
            var topKey = new PlanarBoundaryKey(BoundaryLoop.Outer, 0, 2, 3);
            return new VerticalPlanarFragment
            {
                FragmentId = 1,
                Name = "Runner Test Wall",
                Region = region,
                Section = new PlateSection
                {
                    H = 0.2, NLayers = 4, ConcreteMaterialId = 1, RebarMaterialId = 2,
                    RebarLayers =
                    [
                        new PlateRebarLayer { Asx = 0.0006, Asy = 0.0006, Zsx = 0.08, Zsy = 0.08, MaterialId = 2 },
                        new PlateRebarLayer { Asx = 0.0006, Asy = 0.0006, Zsx = -0.08, Zsy = -0.08, MaterialId = 2 }
                    ]
                },
                BottomCut = new PlanarCutInterface
                {
                    Id = "bottom", Kind = PlanarCutInterfaceKind.BottomCut,
                    Geometry = new PlanarConstraintGeometry(PlanarConstraintGeometryKind.Curve,
                        [new(0, 0), new(2, 0)]),
                    NormalFromFragmentToOmittedSide = new(0, -1, 0),
                    BoundaryKey = bottomKey,
                    ModeByDof = PlanarBoundaryModeByDof.All(PlanarBoundaryDofMode.PreserveSupport)
                },
                TopCut = new PlanarCutInterface
                {
                    Id = "top", Kind = PlanarCutInterfaceKind.TopCut,
                    Geometry = new PlanarConstraintGeometry(PlanarConstraintGeometryKind.Curve,
                        [new(2, 3), new(0, 3)]),
                    NormalFromFragmentToOmittedSide = new(0, 1, 0),
                    BoundaryKey = topKey
                },
                StageConfig = FragmentStageConfig.CreateDefault1Stage()
            };
        }

        static PlanarMeshSettings DefaultSettings() => new(0.5, 6, PlanarMeshElementMode.Mixed);

        sealed class FakeMesher(bool calculable, bool withBoundaryMapping = true) : IPlanarMesher
        {
            public Task<PlanarMeshSnapshot> BuildAsync(PlanarMeshingRequest request, CancellationToken cancellationToken = default)
            {
                if (!calculable)
                {
                    return Task.FromResult(new PlanarMeshSnapshot
                    {
                        IsCalculable = false,
                        Diagnostics = [new FemValidationDiagnostic("fake_mesh_error", "Fake mesh failure.")]
                    });
                }

                var nodes = new List<PlanarMeshNode>
                {
                    new(0, 0, 0, 0, 0, 0), new(1, 2, 0, 2, 0, 0),
                    new(2, 2, 3, 2, 3, 0), new(3, 0, 3, 0, 3, 0)
                };
                var elements = new List<PlanarMeshElement>
                {
                    new(0, PlanarMeshElementKind.Quadrangle4, [0, 1, 2, 3])
                };
                var boundaryMappings = withBoundaryMapping
                    ? new List<PlanarMeshBoundaryMapping>
                      {
                          new() { Key = new PlanarBoundaryKey(BoundaryLoop.Outer, 0, 0, 1), NodeIndices = [0, 1] },
                          new() { Key = new PlanarBoundaryKey(BoundaryLoop.Outer, 0, 2, 3), NodeIndices = [2, 3] }
                      }
                    : new List<PlanarMeshBoundaryMapping>();

                return Task.FromResult(new PlanarMeshSnapshot
                {
                    Id = 1,
                    RegionId = request.Region.Id,
                    InputFingerprint = "fake-fingerprint",
                    IsCalculable = true,
                    Nodes = nodes,
                    Elements = elements,
                    BoundaryMappings = boundaryMappings
                });
            }
        }
    }
}
