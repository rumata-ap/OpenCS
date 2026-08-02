using System;
using System.Collections.Generic;
using CScore;
using CScore.Planar;
using CScore.Planar.Fragments;
using CSmath;
using Xunit;

namespace CScore.Tests.Planar
{
    public class VerticalPlanarFragmentDomainTests
    {
        [Fact]
        public void VerticalPlanarFragment_CreationAndProperties_Valid()
        {
            var sourceContour = new Contour
            {
                Id = 1,
                Tag = "Wall 1 Contour",
                X = new List<double> { 0, 3, 3, 0 },
                Y = new List<double> { 0, 0, 3, 3 }
            };

            var region = PlanarRegion.CreateFromContour(sourceContour, frame: Frame3D.Identity, tag: "Wall 1");
            region.Id = 1;

            var bottomCut = new PlanarCutInterface
            {
                Id = "bottom",
                Kind = PlanarCutInterfaceKind.BottomCut,
                Geometry = new PlanarConstraintGeometry(
                    PlanarConstraintGeometryKind.Curve,
                    new List<PlanarPoint2D> { new PlanarPoint2D(0, 0), new PlanarPoint2D(3, 0) }),
                NormalFromFragmentToOmittedSide = new PlanarVector3(0, -1, 0)
            };

            var topCut = new PlanarCutInterface
            {
                Id = "top",
                Kind = PlanarCutInterfaceKind.TopCut,
                Geometry = new PlanarConstraintGeometry(
                    PlanarConstraintGeometryKind.Curve,
                    new List<PlanarPoint2D> { new PlanarPoint2D(0, 3), new PlanarPoint2D(3, 3) }),
                NormalFromFragmentToOmittedSide = new PlanarVector3(0, 1, 0)
            };

            var stageConfig = FragmentStageConfig.CreateDefault2Stage();

            var fragment = new VerticalPlanarFragment
            {
                FragmentId = 10,
                Name = "Wall Fragment 1",
                Region = region,
                BottomCut = bottomCut,
                TopCut = topCut,
                StageConfig = stageConfig
            };

            Assert.Equal(10, fragment.FragmentId);
            Assert.Equal("Wall Fragment 1", fragment.Name);
            Assert.NotNull(fragment.Region);
            Assert.NotNull(fragment.BottomCut);
            Assert.NotNull(fragment.TopCut);
            Assert.Equal(2, fragment.StageConfig.Stages.Count);
        }

        [Fact]
        public void VerticalPlanarFragment_SectionAndBoundaryTemplates_AreSettable()
        {
            var section = new PlateSection { H = 0.2, NLayers = 4, ConcreteMaterialId = 1, RebarMaterialId = 2 };
            var template = new PlanarBoundaryActionSet { SourceMode = PlanarBoundaryActionSourceMode.Template };

            var fragment = new VerticalPlanarFragment
            {
                FragmentId = 1,
                Name = "Wall",
                Section = section,
                BoundaryTemplates = new Dictionary<string, PlanarBoundaryActionSet> { ["top"] = template }
            };

            Assert.Same(section, fragment.Section);
            Assert.Same(template, fragment.BoundaryTemplates["top"]);
        }
    }
}
