using CScore;
using CScore.PlateStrip;
using OpenCS.Services;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

public sealed class EquivalentSectionProjectServiceTests
{
    [Fact]
    public void BuildAndSave_ReportsMissingPlateMaterialWithoutThrowing()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-equivalent-service-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            var service = new EquivalentSectionProjectService(db, new Dictionary<int, Material>());

            var result = service.BuildAndSave(
                Strip(), 4, new PlateSection { Id = 12, TensionConcrete = true },
                CalcType.C, ReductionPolicy.ConstitutiveIntegration, 2);

            Assert.False(result.IsCalculable);
            Assert.Contains(result.Diagnostics, d => d.Code == "equivalent_section_missing_source");
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void RefreshStale_MarksMissingSourceWithoutDiscardingResult()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-equivalent-stale-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            var service = new EquivalentSectionProjectService(db, new Dictionary<int, Material>());
            var equivalent = new EquivalentSection
            {
                InputFingerprint = "old-input",
                IsCalculable = true,
                BeamTangent = new double[3, 3]
            };

            bool changed = service.RefreshStale(equivalent, null, CalcType.C);

            Assert.True(changed);
            Assert.True(equivalent.IsStale);
            Assert.Equal("old-input", equivalent.InputFingerprint);
            Assert.Contains(equivalent.Diagnostics, d => d.Code == "equivalent_section_stale");
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    static PlateStripBeamAnalogy Strip() => new()
    {
        Id = "strip-service",
        SourceRegionId = 8,
        ExplicitWidthM = 2.0,
        Fingerprint = "strip-fp",
        Geometry = new PlateStripGeometry { LengthM = 6.0 }
    };
}
