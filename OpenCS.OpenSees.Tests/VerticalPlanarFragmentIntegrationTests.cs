using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CScore;
using CScore.Planar;
using CScore.Planar.Fragments;
using CSmath;
using OpenCS.Gmsh;
using OpenCS.OpenSees.CScore.Fragments;
using OpenCS.OpenSees.Tests.Fixtures;
using Xunit;

namespace OpenCS.OpenSees.Tests
{
    public class VerticalPlanarFragmentIntegrationTests
    {
        [Fact]
        public async Task EndToEnd_VerticalWallWithOpening_RunsAndPassesAudit()
        {
            string openSeesExecutable = OpenSeesTestExecutable.ResolveOrSkip();
            string gmshRoot = Path.Combine(Path.GetTempPath(), "opencs-fragment-e2e", Guid.NewGuid().ToString("N"));
            try
            {
                var fragment = BuildDoorWallFragment(topForceZ: -3000);
                var result = await RunFragment(fragment, gmshRoot, openSeesExecutable);

                Assert.Empty(result.MeshDiagnostics);
                Assert.Empty(result.BoundaryDiagnostics);
                Assert.True(result.IsConverged, "Реальный нелинейный расчёт стены с проёмом должен сойтись.");
                Assert.True(result.MaxConcreteCompressionStrain >= -0.0035,
                    $"Бетон не должен превышать предел сжатия по СП 63: {result.MaxConcreteCompressionStrain}");
                Assert.True(result.MaxRebarTensileStrain <= 0.025,
                    $"Арматура не должна превышать предел растяжения по СП 63: {result.MaxRebarTensileStrain}");
                Assert.Equal(FragmentAuditVerdict.Valid, result.AuditReport.Verdict);

                // Явная защита от повторного схлопывания в заглушку.
                Assert.NotEqual(-0.0015, result.MaxConcreteCompressionStrain);
                Assert.NotEqual(0.0020, result.MaxRebarTensileStrain);
                Assert.NotEqual(0.001, result.ForceUnbalanceRatio);
            }
            finally
            {
                if (Directory.Exists(gmshRoot)) Directory.Delete(gmshRoot, recursive: true);
            }
        }

        [Fact]
        public async Task EndToEnd_VerticalWallWithOpening_ExcessiveLoad_ReturnsInvalidVerdict()
        {
            string openSeesExecutable = OpenSeesTestExecutable.ResolveOrSkip();
            string gmshRoot = Path.Combine(Path.GetTempPath(), "opencs-fragment-e2e-invalid", Guid.NewGuid().ToString("N"));
            try
            {
                // На порядок больше вертикальной нагрузки — заведомо превышает предел сжатия
                // бетона (либо расчёт не сходится, либо деформация выходит за -0.0035).
                var fragment = BuildDoorWallFragment(topForceZ: -3000000);
                var result = await RunFragment(fragment, gmshRoot, openSeesExecutable);

                Assert.NotEqual(FragmentAuditVerdict.Valid, result.AuditReport.Verdict);
                Assert.NotEmpty(result.AuditReport.Issues);
            }
            finally
            {
                if (Directory.Exists(gmshRoot)) Directory.Delete(gmshRoot, recursive: true);
            }
        }

        static async Task<VerticalPlanarFragmentResult> RunFragment(
            VerticalPlanarFragment fragment, string gmshRoot, string openSeesExecutable)
        {
            var mesher = new GmshPlanarMesher(new GmshPlanarMesherOptions
            {
                ExecutablePath = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe",
                ArtifactRoot = gmshRoot
            });
            var (concrete, rebar) = NonlinearMaterials();
            var lookup = new Dictionary<int, Material> { [1] = concrete, [2] = rebar };

            return await new VerticalPlanarFragmentRunner().RunAsync(
                fragment, mesher, new PlanarMeshSettings(0.4, 6, PlanarMeshElementMode.Quads),
                id => lookup.GetValueOrDefault(id), CalcType.C, openSeesExecutable, CancellationToken.None);
        }

        /// <summary>Стена 4x3 м с дверным проёмом 1x2 м, поднятым на 0.3 м от пола (порог) —
        /// проём НЕ касается BottomCut, иначе нижний край распадается на два несмежных участка,
        /// которые PlanarCutInterface (одна упорядоченная цепочка) представить не может.</summary>
        static VerticalPlanarFragment BuildDoorWallFragment(double topForceZ)
        {
            var contour = new Contour { Id = 100, Tag = "Wall Contour",
                X = [0, 4, 4, 0], Y = [0, 0, 3, 3] };
            var hole = new Contour { Id = 101, Tag = "Door Hole", Type = ContourType.Hole,
                X = [1.5, 2.5, 2.5, 1.5], Y = [0.3, 0.3, 2.3, 2.3] };
            var region = PlanarRegion.CreateFromContour(contour, holes: [hole], frame: Frame3D.Identity, tag: "Wall with Door Opening");
            region.Id = 100;

            var bottomCut = new PlanarCutInterface
            {
                Id = "bottom", Kind = PlanarCutInterfaceKind.BottomCut,
                Geometry = new PlanarConstraintGeometry(PlanarConstraintGeometryKind.Curve, [new(0, 0), new(4, 0)]),
                NormalFromFragmentToOmittedSide = new(0, -1, 0),
                BoundaryKey = new PlanarBoundaryKey(BoundaryLoop.Outer, 0, 0, 1),
                ModeByDof = PlanarBoundaryModeByDof.All(PlanarBoundaryDofMode.PreserveSupport)
            };
            var topCut = new PlanarCutInterface
            {
                Id = "top", Kind = PlanarCutInterfaceKind.TopCut,
                Geometry = new PlanarConstraintGeometry(PlanarConstraintGeometryKind.Curve, [new(4, 3), new(0, 3)]),
                NormalFromFragmentToOmittedSide = new(0, 1, 0),
                BoundaryKey = new PlanarBoundaryKey(BoundaryLoop.Outer, 0, 2, 3),
                ModeByDof = PlanarBoundaryModeByDof.All(PlanarBoundaryDofMode.Force)
            };

            var fragment = new VerticalPlanarFragment
            {
                FragmentId = 100,
                Name = "Door Wall Fragment",
                Region = region,
                Section = new PlateSection
                {
                    H = 0.2, NLayers = 6, ConcreteMaterialId = 1, RebarMaterialId = 2,
                    RebarLayers =
                    [
                        new PlateRebarLayer { Asx = 0.0006, Asy = 0.0006, Zsx = 0.08, Zsy = 0.08, MaterialId = 2 },
                        new PlateRebarLayer { Asx = 0.0006, Asy = 0.0006, Zsx = -0.08, Zsy = -0.08, MaterialId = 2 }
                    ]
                },
                BottomCut = bottomCut,
                TopCut = topCut,
                StageConfig = FragmentStageConfig.CreateDefault1Stage()
            };
            // ModeByDof = PreserveSupport -> реальный OpenSees `fix`; шаблон нужен только чтобы
            // Runner вызвал mapping pipeline для bottom cut interface, действий в нём нет.
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
                        Samples = [new(0, new PlanarVector3(0, 0, topForceZ), PlanarVector3.Zero),
                                   new(1, new PlanarVector3(0, 0, topForceZ), PlanarVector3.Zero)]
                    }
                ]
            };
            return fragment;
        }

        static (Material Concrete, Material Rebar) NonlinearMaterials() =>
        (
            new Material { Id = 1, Tag = "B25", Type = MatType.Concrete,
                C = new MaterialChars { E = 30_000_000, Fc = -17_000, Ft = 1_150, Ec0 = -0.002, Ec2 = -0.0035 } },
            new Material { Id = 2, Tag = "A400", Type = MatType.ReSteelF,
                C = new MaterialChars { E = 200_000_000, Ft = 355_000, Ru = 500_000, Et2 = 0.05 } }
        );
    }
}
