using CScore.Planar.Fragments;
using Xunit;

namespace CScore.Tests.Planar
{
    public class MultiStoryColumnAuditReportTests
    {
        [Fact]
        public void Audit_WithNoBlockingDiagnosticsAndConverged_ReturnsValid()
        {
            var fragment = new MultiStoryColumnFragment { FragmentId = 1 };
            var result = new MultiStoryColumnResult { FragmentId = 1, IsConverged = true };

            var report = new MultiStoryColumnAuditReport().Audit(fragment, result);

            Assert.Equal(FragmentAuditVerdict.Valid, report.Verdict);
            Assert.Empty(report.Issues);
        }

        [Fact]
        public void Audit_WithNullResult_ReturnsInvalid()
        {
            var report = new MultiStoryColumnAuditReport().Audit(
                new MultiStoryColumnFragment { FragmentId = 1 }, null!);

            Assert.Equal(FragmentAuditVerdict.Invalid, report.Verdict);
            Assert.NotEmpty(report.Issues);
        }

        [Fact]
        public void Audit_WithMeshOrAssemblyDiagnostics_ReturnsInvalid()
        {
            var fragment = new MultiStoryColumnFragment { FragmentId = 1 };
            var result = new MultiStoryColumnResult { FragmentId = 1, IsConverged = true };
            result.MeshDiagnostics.Add("multistory_column_anchor_node_missing: узел не найден.");

            var report = new MultiStoryColumnAuditReport().Audit(fragment, result);

            Assert.Equal(FragmentAuditVerdict.Invalid, report.Verdict);
        }

        [Fact]
        public void Audit_WithIncompleteAnalysis_ReturnsInvalid()
        {
            var fragment = new MultiStoryColumnFragment { FragmentId = 1 };
            var result = new MultiStoryColumnResult { FragmentId = 1, IsConverged = false };

            var report = new MultiStoryColumnAuditReport().Audit(fragment, result);

            Assert.Equal(FragmentAuditVerdict.Invalid, report.Verdict);
            Assert.Contains(report.Issues, issue => issue.Contains("не достиг полной нагрузки"));
        }

        [Fact]
        public void Audit_WithForceBalanceAboveTolerance_ReturnsInvalid()
        {
            var fragment = new MultiStoryColumnFragment { FragmentId = 1 };
            var result = new MultiStoryColumnResult
            {
                FragmentId = 1, IsConverged = true,
                ForceBalance = new FloorJunctionForceBalance(1000, 1050, 0.05)
            };

            var report = new MultiStoryColumnAuditReport().Audit(fragment, result);

            Assert.Equal(FragmentAuditVerdict.Invalid, report.Verdict);
            Assert.Contains(report.Issues, issue => issue.Contains("Невязка равновесия"));
        }

        [Fact]
        public void Audit_WithShellLayerConcreteStrainOverLimit_ReturnsInvalid()
        {
            var fragment = new MultiStoryColumnFragment { FragmentId = 1 };
            var result = new MultiStoryColumnResult
                { FragmentId = 1, IsConverged = true, MaxConcreteCompressionStrain = -0.004 };

            var report = new MultiStoryColumnAuditReport().Audit(fragment, result);

            Assert.Equal(FragmentAuditVerdict.Invalid, report.Verdict);
            Assert.Contains(report.Issues, issue => issue.Contains("сжатия бетона"));
        }

        [Fact]
        public void Audit_WithColumnFiberRebarStrainOverLimit_ReturnsInvalid()
        {
            // MaxRebarTensileStrain агрегирует и shell-слои, и фибры колонны вместе —
            // экстремум мог прийти от фибры арматуры сегмента колонны.
            var fragment = new MultiStoryColumnFragment { FragmentId = 1 };
            var result = new MultiStoryColumnResult
                { FragmentId = 1, IsConverged = true, MaxRebarTensileStrain = 0.03 };

            var report = new MultiStoryColumnAuditReport().Audit(fragment, result);

            Assert.Equal(FragmentAuditVerdict.Invalid, report.Verdict);
            Assert.Contains(report.Issues, issue => issue.Contains("растяжения арматуры"));
        }

        [Fact]
        public void Result_DefaultAuditReportIsNotValid()
        {
            var result = new MultiStoryColumnResult { FragmentId = 1 };

            Assert.Equal(FragmentAuditVerdict.Invalid, result.AuditReport.Verdict);
        }
    }
}
