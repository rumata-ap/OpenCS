using System.Collections.Generic;
using CScore.Planar.Fragments;
using Xunit;

namespace CScore.Tests.Planar
{
    public class FragmentAuditReportTests
    {
        static VerticalPlanarFragmentResult ValidResult() => new()
        {
            IsConverged = true,
            MeshDiagnostics = new List<string>(),
            BoundaryDiagnostics = new List<string>(),
            EnergyConfidence = "ExternalWorkOnly",
            MaxConcreteCompressionStrain = -0.0018,
            MaxRebarTensileStrain = 0.0021,
            ForceUnbalanceRatio = 0.001
        };

        [Fact]
        public void Audit_WithValidResult_ReturnsValidVerdictAndNoIssues()
        {
            var report = new FragmentAuditReport().Audit(new VerticalPlanarFragment(), ValidResult());

            Assert.Equal(FragmentAuditVerdict.Valid, report.Verdict);
            Assert.Empty(report.Issues);
        }

        [Fact]
        public void Audit_WithMeshDiagnostics_ReturnsInvalidVerdict()
        {
            var result = ValidResult();
            result.MeshDiagnostics = new List<string> { "Сетка содержит вырожденный T3-элемент." };

            var report = new FragmentAuditReport().Audit(new VerticalPlanarFragment(), result);

            Assert.Equal(FragmentAuditVerdict.Invalid, report.Verdict);
            Assert.Contains(report.Issues, i => i.Contains("сетк", System.StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void Audit_WithExceededConcreteStrain_ReturnsInvalidVerdict()
        {
            var result = ValidResult();
            result.MaxConcreteCompressionStrain = -0.0042;

            var report = new FragmentAuditReport().Audit(new VerticalPlanarFragment(), result);

            Assert.Equal(FragmentAuditVerdict.Invalid, report.Verdict);
            Assert.Contains(report.Issues, i => i.Contains("СП 63.13330"));
        }

        [Fact]
        public void Audit_WithExceededRebarStrain_ReturnsInvalidVerdict()
        {
            var result = ValidResult();
            result.MaxRebarTensileStrain = 0.03;

            var report = new FragmentAuditReport().Audit(new VerticalPlanarFragment(), result);

            Assert.Equal(FragmentAuditVerdict.Invalid, report.Verdict);
            Assert.Contains(report.Issues, i => i.Contains("СП 63.13330"));
        }

        [Fact]
        public void Audit_WithNotConverged_ReturnsInvalidVerdict()
        {
            var result = ValidResult();
            result.IsConverged = false;

            var report = new FragmentAuditReport().Audit(new VerticalPlanarFragment(), result);

            Assert.Equal(FragmentAuditVerdict.Invalid, report.Verdict);
        }

        [Fact]
        public void Audit_WithUnavailableEnergyConfidence_ReturnsWarningNotInvalid()
        {
            var result = ValidResult();
            result.EnergyConfidence = "Unavailable";

            var report = new FragmentAuditReport().Audit(new VerticalPlanarFragment(), result);

            Assert.Equal(FragmentAuditVerdict.Warning, report.Verdict);
            Assert.Empty(report.Issues);
            Assert.NotEmpty(report.Warnings);
        }

        [Fact]
        public void Audit_WithMultipleSimultaneousViolations_ReportsAllIssues()
        {
            var result = ValidResult();
            result.MaxConcreteCompressionStrain = -0.0042;
            result.ForceUnbalanceRatio = 0.02;

            var report = new FragmentAuditReport().Audit(new VerticalPlanarFragment(), result);

            Assert.Equal(FragmentAuditVerdict.Invalid, report.Verdict);
            Assert.True(report.Issues.Count >= 2);
        }
    }
}
