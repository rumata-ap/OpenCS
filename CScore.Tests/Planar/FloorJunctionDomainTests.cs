using System.Collections.Generic;
using CScore;
using CScore.Planar;
using CScore.Planar.Fragments;
using Xunit;

namespace CScore.Tests.Planar
{
    public class FloorJunctionDomainTests
    {
        [Fact]
        public void CreationAndProperties_AreSettable()
        {
            var plate = PlanarRegion.CreateFromContour(
                new Contour { Id = 1, Tag = "plate", X = [0, 4, 4, 0], Y = [0, 0, 4, 4] },
                frame: Frame3D.Identity, tag: "plate");
            plate.Id = 10;
            var wall = PlanarRegion.CreateFromContour(
                new Contour { Id = 2, Tag = "wall", X = [0, 4, 4, 0], Y = [0, 0, 3, 3] },
                frame: new Frame3D(
                    new PlanarVector3(2, 0, 0), new PlanarVector3(0, 1, 0),
                    new PlanarVector3(0, 0, 1), new PlanarVector3(1, 0, 0)),
                tag: "wall");
            wall.Id = 20;
            var connection = new PlanarConnection
            {
                Id = 7,
                MeshMode = PlanarConnectionMeshMode.ConformingPartition,
                SideA = new ConnectionLocus(10, [new PlanarPoint2D(2, 0), new PlanarPoint2D(2, 4)]),
                SideB = new ConnectionLocus(20, [new PlanarPoint2D(0, 0), new PlanarPoint2D(4, 0)])
            };

            var fragment = new FloorJunctionFragment
            {
                FragmentId = 1,
                Name = "Junction 1",
                PlateRegion = plate,
                WallRegion = wall,
                PlateSection = new PlateSection { H = 0.2, NLayers = 4, ConcreteMaterialId = 1, RebarMaterialId = 2 },
                WallSection = new PlateSection { H = 0.2, NLayers = 4, ConcreteMaterialId = 1, RebarMaterialId = 2 },
                Connection = connection,
                StageConfig = FragmentStageConfig.CreateDefault1Stage()
            };

            Assert.Equal(1, fragment.FragmentId);
            Assert.Equal("Junction 1", fragment.Name);
            Assert.Same(plate, fragment.PlateRegion);
            Assert.Same(wall, fragment.WallRegion);
            Assert.Equal(10, fragment.PlateRegion.Id);
            Assert.Equal(20, fragment.WallRegion.Id);
            Assert.Same(connection, fragment.Connection);
            Assert.Equal(7, fragment.Connection.Id);
            Assert.Equal(PlanarConnectionMeshMode.ConformingPartition, fragment.Connection.MeshMode);
            Assert.Equal(10, fragment.Connection.SideA.RegionId);
            Assert.Equal(20, fragment.Connection.SideB.RegionId);
            Assert.NotNull(fragment.PlateSection);
            Assert.NotNull(fragment.WallSection);
            Assert.Single(fragment.StageConfig.Stages);
            Assert.Equal(1, fragment.StageConfig.Stages[0].StageIndex);
            Assert.NotNull(fragment.Boundaries);
            Assert.Empty(fragment.Boundaries);
            Assert.NotNull(fragment.BoundaryTemplates);
            Assert.Empty(fragment.BoundaryTemplates);
        }

        [Fact]
        public void Validate_AcceptsValidAggregateWithoutErrors()
        {
            var fragment = BuildValidFragment();

            var diagnostics = fragment.Validate();

            Assert.DoesNotContain(diagnostics, d => d.IsError);
        }

        [Fact]
        public void Validate_RejectsEqualRegionIds()
        {
            var fragment = BuildValidFragment();
            fragment.WallRegion = fragment.PlateRegion; // один и тот же объект/ID

            var diagnostics = fragment.Validate();

            Assert.Contains(diagnostics, d => d.Code == "floor_junction_region_mismatch");
        }

        [Fact]
        public void Validate_RejectsNonConformingMeshMode()
        {
            var fragment = BuildValidFragment();
            fragment.Connection.MeshMode = PlanarConnectionMeshMode.EmbeddedLocus;

            var diagnostics = fragment.Validate();

            Assert.Contains(diagnostics, d => d.Code == "floor_junction_mesh_mode_unsupported");
        }

        [Fact]
        public void Validate_RejectsConnectionSidesNotReferencingRegions()
        {
            var fragment = BuildValidFragment();
            fragment.Connection.SideB = new ConnectionLocus(999, fragment.Connection.SideB.Points);

            var diagnostics = fragment.Validate();

            Assert.Contains(diagnostics, d => d.Code == "floor_junction_region_mismatch");
        }

        [Fact]
        public void Validate_RejectsDuplicateBoundaryIds()
        {
            var fragment = BuildValidFragment();
            fragment.Boundaries.Add(fragment.Boundaries[0]); // одинаковый Id

            var diagnostics = fragment.Validate();

            Assert.Contains(diagnostics, d => d.Code == "floor_junction_boundary_duplicate_id");
        }

        [Fact]
        public void Validate_RejectsBoundaryOnUnknownRegion()
        {
            var fragment = BuildValidFragment();
            fragment.Boundaries[0].RegionId = 777;

            var diagnostics = fragment.Validate();

            Assert.Contains(diagnostics, d => d.Code == "floor_junction_boundary_unknown_region");
        }

        [Fact]
        public void Validate_RejectsBoundaryReferencingJunctionAsCut()
        {
            var fragment = BuildValidFragment();
            var cut = fragment.Boundaries[0].Cut;
            fragment.Boundaries[0].Cut = new PlanarCutInterface
            {
                Id = cut.Id,
                Kind = cut.Kind,
                Geometry = cut.Geometry,
                NormalFromFragmentToOmittedSide = cut.NormalFromFragmentToOmittedSide,
                Frame = cut.Frame,
                ModeByDof = cut.ModeByDof,
                MeshConstraintId = "connection:7:region:10",
                BoundaryKey = cut.BoundaryKey,
                OmittedSideReference = cut.OmittedSideReference,
                ToleranceM = cut.ToleranceM
            };

            var diagnostics = fragment.Validate();

            Assert.Contains(diagnostics, d => d.Code == "floor_junction_boundary_uses_junction");
        }

        [Fact]
        public void Validate_RejectsBoundaryWithoutCutMapping()
        {
            var fragment = BuildValidFragment();
            fragment.Boundaries[0].Cut = null!; // намеренно: валидатор должен вернуть mapping_missing

            var diagnostics = fragment.Validate();

            Assert.Contains(diagnostics, d => d.Code == "floor_junction_boundary_mapping_missing");
        }

        [Fact]
        public void Validate_RejectsMissingBoundaryTemplate()
        {
            var fragment = BuildValidFragment();
            fragment.BoundaryTemplates.Clear();

            var diagnostics = fragment.Validate();

            Assert.Contains(diagnostics, d => d.Code == "floor_junction_boundary_template_missing");
        }

        [Fact]
        public void Validate_RejectsTemplateWithoutBoundary()
        {
            var fragment = BuildValidFragment();
            fragment.BoundaryTemplates["orphan"] = new PlanarBoundaryActionSet
                { SourceMode = PlanarBoundaryActionSourceMode.Template };

            var diagnostics = fragment.Validate();

            Assert.Contains(diagnostics, d => d.Code == "floor_junction_boundary_template_missing");
        }

        static FloorJunctionFragment BuildValidFragment()
        {
            var plate = PlanarRegion.CreateFromContour(
                new Contour { Id = 1, Tag = "plate", X = [0, 4, 4, 0], Y = [0, 0, 4, 4] },
                frame: Frame3D.Identity, tag: "plate");
            plate.Id = 10;
            var wall = PlanarRegion.CreateFromContour(
                new Contour { Id = 2, Tag = "wall", X = [0, 4, 4, 0], Y = [0, 0, 3, 3] },
                frame: new Frame3D(
                    new PlanarVector3(2, 0, 0), new PlanarVector3(0, 1, 0),
                    new PlanarVector3(0, 0, 1), new PlanarVector3(1, 0, 0)),
                tag: "wall");
            wall.Id = 20;
            var fragment = new FloorJunctionFragment
            {
                FragmentId = 1,
                Name = "Junction 1",
                PlateRegion = plate,
                WallRegion = wall,
                PlateSection = new PlateSection { H = 0.2, NLayers = 4, ConcreteMaterialId = 1, RebarMaterialId = 2 },
                WallSection = new PlateSection { H = 0.2, NLayers = 4, ConcreteMaterialId = 1, RebarMaterialId = 2 },
                Connection = new PlanarConnection
                {
                    Id = 7,
                    MeshMode = PlanarConnectionMeshMode.ConformingPartition,
                    SideA = new ConnectionLocus(10, [new PlanarPoint2D(2, 0), new PlanarPoint2D(2, 4)]),
                    SideB = new ConnectionLocus(20, [new PlanarPoint2D(0, 0), new PlanarPoint2D(4, 0)])
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
                        [new PlanarPoint2D(0, 0), new PlanarPoint2D(0, 4)]),
                    NormalFromFragmentToOmittedSide = new PlanarVector3(-1, 0, 0),
                    BoundaryKey = new PlanarBoundaryKey(BoundaryLoop.Outer, 0, 3, 0),
                    ModeByDof = PlanarBoundaryModeByDof.All(PlanarBoundaryDofMode.PreserveSupport)
                }
            });
            fragment.BoundaryTemplates["plate-fix"] = new PlanarBoundaryActionSet
                { SourceMode = PlanarBoundaryActionSourceMode.Template };
            return fragment;
        }
    }
}
