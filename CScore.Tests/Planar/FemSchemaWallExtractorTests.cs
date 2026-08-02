using System.Collections.Generic;
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
            Assert.Equal(PlanarCutInterfaceKind.BottomCut, fragment.BottomCut.Kind);
            Assert.Equal(PlanarCutInterfaceKind.TopCut, fragment.TopCut.Kind);
        }
    }
}
