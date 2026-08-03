using System.Collections.Generic;
using CScore;
using CScore.Planar;
using CScore.Planar.Fragments;
using Xunit;

namespace CScore.Tests.Planar
{
    public class MultiStoryColumnDomainTests
    {
        [Fact]
        public void SupportTypes_ConstructWithExpectedDefaults()
        {
            var segment = new ColumnSegment { Id = "seg-1" };
            Assert.Equal("seg-1", segment.Id);
            Assert.Equal(0, segment.GJ);
            Assert.Equal((1, 0, 0), segment.Vecxz);
            Assert.Equal(5, segment.NumIntegrationPoints);
            Assert.NotNull(segment.Section);

            var level = new ColumnFloorLevel { Id = "level-1" };
            Assert.Equal("level-1", level.Id);
            Assert.Equal((0, 0), level.ColumnAnchorLocalXY);
            Assert.Empty(level.Loads);
            Assert.Empty(level.Boundaries);

            Assert.Equal(ColumnBaseFixity.None, default(ColumnBaseFixity));
        }
    }
}
