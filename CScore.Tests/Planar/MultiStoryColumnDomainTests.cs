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

        [Fact]
        public void Validate_AcceptsValidAggregateWithoutErrors()
        {
            var fragment = BuildValidFragment();

            var diagnostics = fragment.Validate();

            Assert.DoesNotContain(diagnostics, d => d.IsError);
        }

        [Fact]
        public void Validate_RejectsFewerThanTwoLevels()
        {
            var fragment = BuildValidFragment();
            fragment.Levels.RemoveAt(1);
            fragment.Segments.Clear();

            var diagnostics = fragment.Validate();

            Assert.Contains(diagnostics, d => d.Code == "multistory_column_level_count_invalid");
        }

        [Fact]
        public void Validate_RejectsSegmentCountMismatch()
        {
            var fragment = BuildValidFragment();
            fragment.Segments.Add(new ColumnSegment { Id = "extra" });

            var diagnostics = fragment.Validate();

            Assert.Contains(diagnostics, d => d.Code == "multistory_column_segment_sequence_invalid");
        }

        [Fact]
        public void Validate_RejectsDuplicateLevelIds()
        {
            var fragment = BuildValidFragment();
            fragment.Levels[1].Id = fragment.Levels[0].Id;

            var diagnostics = fragment.Validate();

            Assert.Contains(diagnostics, d => d.Code == "multistory_column_duplicate_id");
        }

        [Fact]
        public void Validate_RejectsDuplicateSegmentIds()
        {
            var fragment = BuildValidFragment();
            fragment.Segments[0].Id = "same";
            fragment.Levels.Add(new ColumnFloorLevel
            {
                Id = "level-3",
                PlateRegion = MakePlateRegion(3, 0, 8),
                PlateSection = new PlateSection { H = 0.2, NLayers = 4, ConcreteMaterialId = 1, RebarMaterialId = 2 },
                ColumnAnchorLocalXY = (2, 2)
            });
            fragment.Segments.Add(new ColumnSegment { Id = "same", GJ = 1000 });

            var diagnostics = fragment.Validate();

            Assert.Contains(diagnostics, d => d.Code == "multistory_column_duplicate_id");
        }

        [Fact]
        public void Validate_RejectsAnchorOutsideHull()
        {
            var fragment = BuildValidFragment();
            fragment.Levels[0].ColumnAnchorLocalXY = (100, 100);

            var diagnostics = fragment.Validate();

            Assert.Contains(diagnostics, d => d.Code == "multistory_column_anchor_outside_hull");
        }

        [Fact]
        public void Validate_RejectsAnchorInsideHole()
        {
            var fragment = BuildValidFragment();
            var region = fragment.Levels[0].PlateRegion;
            region.Contours.Add(new Contour
            {
                Type = ContourType.Hole,
                X = [1, 3, 3, 1],
                Y = [1, 1, 3, 3]
            });
            fragment.Levels[0].ColumnAnchorLocalXY = (2, 2);

            var diagnostics = fragment.Validate();

            Assert.Contains(diagnostics, d => d.Code == "multistory_column_anchor_inside_hole");
        }

        [Fact]
        public void Validate_RejectsMissingBoundaryTemplate()
        {
            var fragment = BuildValidFragment();
            fragment.Levels[0].Boundaries.Add(new FloorJunctionBoundary
            {
                Id = "fix",
                RegionId = fragment.Levels[0].PlateRegion.Id,
                Cut = new PlanarCutInterface { Id = "fix" }
            });

            var diagnostics = fragment.Validate();

            Assert.Contains(diagnostics, d => d.Code == "multistory_column_boundary_template_missing");
        }

        static PlanarRegion MakePlateRegion(int id, double originX, double originY)
        {
            var region = PlanarRegion.CreateFromContour(
                new Contour { X = [0, 4, 4, 0], Y = [0, 0, 4, 4] },
                frame: new Frame3D(
                    new PlanarVector3(originX, 0, originY), new PlanarVector3(1, 0, 0),
                    new PlanarVector3(0, 1, 0), new PlanarVector3(0, 0, 1)),
                tag: $"level-{id}");
            region.Id = id;
            return region;
        }

        static MultiStoryColumnFragment BuildValidFragment()
        {
            var level1 = new ColumnFloorLevel
            {
                Id = "level-1",
                PlateRegion = MakePlateRegion(1, 0, 0),
                PlateSection = new PlateSection { H = 0.2, NLayers = 4, ConcreteMaterialId = 1, RebarMaterialId = 2 },
                ColumnAnchorLocalXY = (2, 2)
            };
            var level2 = new ColumnFloorLevel
            {
                Id = "level-2",
                PlateRegion = MakePlateRegion(2, 0, 4),
                PlateSection = new PlateSection { H = 0.2, NLayers = 4, ConcreteMaterialId = 1, RebarMaterialId = 2 },
                ColumnAnchorLocalXY = (2, 2)
            };
            var fragment = new MultiStoryColumnFragment
            {
                FragmentId = 1,
                Name = "Column A",
                Levels = { level1, level2 },
                Segments = { new ColumnSegment { Id = "seg-1", GJ = 1000 } },
                BaseSupport = ColumnBaseFixity.Fixed,
                StageConfig = FragmentStageConfig.CreateDefault1Stage()
            };
            return fragment;
        }
    }
}
