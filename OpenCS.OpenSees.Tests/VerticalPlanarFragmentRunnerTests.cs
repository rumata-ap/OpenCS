using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CScore;
using CScore.Fem;
using CScore.Planar;
using CScore.Planar.Fragments;
using CSmath;
using OpenCS.OpenSees.CScore.Fragments;
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
                Section = new PlateSection { H = 0.2, NLayers = 4, ConcreteMaterialId = 1, RebarMaterialId = 2 },
                BottomCut = new PlanarCutInterface
                {
                    Id = "bottom", Kind = PlanarCutInterfaceKind.BottomCut,
                    Geometry = new PlanarConstraintGeometry(PlanarConstraintGeometryKind.Curve,
                        [new(0, 0), new(2, 0)]),
                    NormalFromFragmentToOmittedSide = new(0, -1, 0),
                    BoundaryKey = bottomKey
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
