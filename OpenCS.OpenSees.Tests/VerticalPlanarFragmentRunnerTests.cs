using CScore.Planar;
using CScore.Planar.Fragments;
using OpenCS.OpenSees.CScore.Fragments;
using Xunit;

namespace OpenCS.OpenSees.Tests
{
    public class VerticalPlanarFragmentRunnerTests
    {
        [Fact]
        public void FragmentRunner_Execution_ReturnsValidConvergedResult()
        {
            var fragment = new VerticalPlanarFragment
            {
                FragmentId = 20,
                Name = "Runner Test Wall",
                StageConfig = FragmentStageConfig.CreateDefault1Stage()
            };

            var runner = new VerticalPlanarFragmentRunner();
            var result = runner.Run(fragment);

            Assert.NotNull(result);
            Assert.Equal(20, result.FragmentId);
            Assert.True(result.IsConverged);
            Assert.Equal(FragmentAuditVerdict.Valid, result.AuditReport.Verdict);
        }
    }
}
