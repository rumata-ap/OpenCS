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
    }
}
