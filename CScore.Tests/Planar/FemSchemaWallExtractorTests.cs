using System.Collections.Generic;
using System.Linq;
using CScore.Fem;
using CScore.Planar;
using CScore.Planar.Fragments;
using CSmath;
using Xunit;

namespace CScore.Tests.Planar
{
    public class FemSchemaWallExtractorTests
    {
        [Fact]
        public void ExtractWallFragment_FromSimpleWallMember_BuildsValidFragment()
        {
            var schema = new FemSchema { Id = 1, Tag = "Building Schema" };

            var nodes = new List<FemNode>
            {
                new FemNode { Id = 1, NodeTag = "1", X = 0, Y = 0, Z = 0 },
                new FemNode { Id = 2, NodeTag = "2", X = 4, Y = 0, Z = 0 },
                new FemNode { Id = 3, NodeTag = "3", X = 4, Y = 0, Z = 3 },
                new FemNode { Id = 4, NodeTag = "4", X = 0, Y = 0, Z = 3 }
            };

            var member = new FemMember
            {
                Id = 10,
                ElemTag = "W1",
                ElemType = "shell",
                NodeIdsJson = "[1, 2, 3, 4]",
                ThicknessM = 0.2
            };

            var extractor = new FemSchemaWallExtractor();
            var fragment = extractor.ExtractWallFragment(schema, member, nodes, "Extracted Wall 1");

            Assert.NotNull(fragment);
            Assert.Equal("Extracted Wall 1", fragment.Name);
            Assert.NotNull(fragment.Region);
            Assert.NotNull(fragment.BottomCut);
            Assert.NotNull(fragment.TopCut);
            Assert.Equal(PlanarCutInterfaceKind.BottomCut, fragment.BottomCut!.Kind);
            Assert.Equal(PlanarCutInterfaceKind.TopCut, fragment.TopCut!.Kind);

            // BoundaryKey должен указывать на РЕАЛЬНОЕ ребро Hull региона (не embedded curve).
            Assert.NotNull(fragment.BottomCut.BoundaryKey);
            Assert.NotNull(fragment.TopCut.BoundaryKey);
            Assert.Equal(BoundaryLoop.Outer, fragment.BottomCut.BoundaryKey!.Loop);
            Assert.Equal(BoundaryLoop.Outer, fragment.TopCut.BoundaryKey!.Loop);
            Assert.Null(fragment.BottomCut.MeshConstraintId);
            Assert.Null(fragment.TopCut.MeshConstraintId);

            // Вершины BottomCut.BoundaryKey должны лежать на минимальной V (низ стены),
            // TopCut — на максимальной V (верх стены), в нормализованном Hull региона.
            var hull = fragment.Region!.Hull!;
            double minY = hull.Y.Take(hull.Y.Count - 1).Min();
            double maxY = hull.Y.Take(hull.Y.Count - 1).Max();
            int bStart = fragment.BottomCut.BoundaryKey.StartVertex;
            int bEnd = fragment.BottomCut.BoundaryKey.EndVertex;
            Assert.True(System.Math.Abs(hull.Y[bStart] - minY) < 1e-9);
            Assert.True(System.Math.Abs(hull.Y[bEnd] - minY) < 1e-9);
            int tStart = fragment.TopCut.BoundaryKey.StartVertex;
            int tEnd = fragment.TopCut.BoundaryKey.EndVertex;
            Assert.True(System.Math.Abs(hull.Y[tStart] - maxY) < 1e-9);
            Assert.True(System.Math.Abs(hull.Y[tEnd] - maxY) < 1e-9);
        }
    }
}
