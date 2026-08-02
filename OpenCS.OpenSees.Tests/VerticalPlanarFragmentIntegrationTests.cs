using System.Collections.Generic;
using CScore;
using CScore.Planar;
using CScore.Planar.Fragments;
using CSmath;
using OpenCS.OpenSees.CScore.Fragments;
using Xunit;

namespace OpenCS.OpenSees.Tests
{
    public class VerticalPlanarFragmentIntegrationTests
    {
        [Fact]
        public void EndToEnd_VerticalWallWithOpening_RunsAndPassesAudit()
        {
            // Стена 4х3 м с дверным проёмом 1х2 м
            var contour = new Contour
            {
                Id = 100,
                Tag = "Wall Contour",
                X = new List<double> { 0, 4, 4, 0 },
                Y = new List<double> { 0, 0, 3, 3 }
            };

            var hole = new Contour
            {
                Id = 101,
                Tag = "Door Hole",
                Type = ContourType.Hole,
                X = new List<double> { 1.5, 2.5, 2.5, 1.5 },
                Y = new List<double> { 0, 0, 2, 2 }
            };

            var region = PlanarRegion.CreateFromContour(contour, holes: new[] { hole }, frame: Frame3D.Identity, tag: "Wall with Door Opening");
            region.Id = 100;

            var fragment = new VerticalPlanarFragment
            {
                FragmentId = 100,
                Name = "Door Wall Fragment",
                Region = region,
                StageConfig = FragmentStageConfig.CreateDefault1Stage()
            };

            var runner = new VerticalPlanarFragmentRunner();
            var result = runner.Run(fragment);

            Assert.NotNull(result);
            Assert.True(result.IsConverged);
            Assert.Equal(FragmentAuditVerdict.Valid, result.AuditReport.Verdict);
            Assert.True(result.MaxConcreteCompressionStrain >= -0.0035, "Бетон не должен превышать предел сжатия по СП 63");
            Assert.True(result.MaxRebarTensileStrain <= 0.025, "Арматура не должна превышать предел растяжения по СП 63");
        }
    }
}
