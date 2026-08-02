using CScore.Planar.Fragments;
using Xunit;

namespace CScore.Tests.Planar
{
    public class FragmentAuditReportTests
    {
        [Fact]
        public void Audit_WithExceededConcreteStrain_ReturnsInvalidVerdict()
        {
            var fragment = new VerticalPlanarFragment { FragmentId = 1, Name = "Test Wall" };
            var result = new VerticalPlanarFragmentResult
            {
                IsConverged = true,
                MaxConcreteCompressionStrain = -0.0042, // Интенсивное сжатие/раздробление бетона (меньше -0.0035)
                MaxRebarTensileStrain = 0.0015,
                ForceUnbalanceRatio = 0.002
            };

            var auditor = new FragmentAuditReport();
            var report = auditor.Audit(fragment, result);

            Assert.Equal(FragmentAuditVerdict.Invalid, report.Verdict);
            Assert.Contains(report.Issues, i => i.Contains("СП 63.13330"));
        }

        [Fact]
        public void Audit_WithValidStrainsAndBalance_ReturnsValidVerdict()
        {
            var fragment = new VerticalPlanarFragment { FragmentId = 1, Name = "Test Wall" };
            var result = new VerticalPlanarFragmentResult
            {
                IsConverged = true,
                MaxConcreteCompressionStrain = -0.0018, // В допустимом пределе (>= -0.0035)
                MaxRebarTensileStrain = 0.0021,        // В допустимом пределе (<= 0.025)
                ForceUnbalanceRatio = 0.001
            };

            var auditor = new FragmentAuditReport();
            var report = auditor.Audit(fragment, result);

            Assert.Equal(FragmentAuditVerdict.Valid, report.Verdict);
            Assert.Empty(report.Issues);
        }
    }
}
