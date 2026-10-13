using CScore.PlateStrip;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

public sealed class EquivalentSectionDatabaseTests
{
    [Fact]
    public void EquivalentSection_RoundTripsThroughSqlite()
    {
        string path = TempPath();
        try
        {
            var source = new DatabaseService(path);
            var original = Section();
            source.SaveEquivalentSection(original);

            var loaded = new DatabaseService(path);
            loaded.LoadAll();

            var actual = Assert.Single(loaded.EquivalentSections);
            Assert.NotEqual(0, actual.Id);
            Assert.Equal(original.Tag, actual.Tag);
            Assert.Equal(original.SourceRegionId, actual.SourceRegionId);
            Assert.Equal(original.SourceSchemaId, actual.SourceSchemaId);
            Assert.Equal(original.InputFingerprint, actual.InputFingerprint);
            Assert.Equal(original.ResultFingerprint, actual.ResultFingerprint);
            Assert.Equal(original.BeamTangent[0, 1], actual.BeamTangent[0, 1], 12);
            Assert.Contains(actual.Diagnostics, d => d.Code == "test_warning");
            Assert.Equal(original.SourceRegionFingerprint, actual.SourceRegionFingerprint);
            Assert.Equal(original.SpanStationFraction, actual.SpanStationFraction, 12);

            loaded.Dispose();
            source.Dispose();
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void EquivalentSection_RoundTripsTorsionShearAndFormulation()
    {
        string path = TempPath();
        try
        {
            var source = new DatabaseService(path);
            var original = Section();
            original.TorsionalStiffness = 4321.5;
            original.ShearStiffnessGAy = 7.0e4;
            original.ShearStiffnessGAz = 8.0e4;
            original.BeamFormulation = StripBeamFormulation.Timoshenko;
            original.ReductionPolicy = ReductionPolicy.EnergyEquivalent;
            source.SaveEquivalentSection(original);

            var loaded = new DatabaseService(path);
            loaded.LoadAll();

            var actual = Assert.Single(loaded.EquivalentSections);
            Assert.Equal(4321.5, actual.TorsionalStiffness, 12);
            Assert.Equal(7.0e4, actual.ShearStiffnessGAy, 6);
            Assert.Equal(8.0e4, actual.ShearStiffnessGAz, 6);
            Assert.Equal(StripBeamFormulation.Timoshenko, actual.BeamFormulation);
            Assert.Equal(ReductionPolicy.EnergyEquivalent, actual.ReductionPolicy);

            loaded.Dispose();
            source.Dispose();
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void EquivalentSection_SaveUpdatesExistingRowAndDeleteRemovesIt()
    {
        string path = TempPath();
        try
        {
            using var db = new DatabaseService(path);
            var section = Section();
            db.SaveEquivalentSection(section);
            int id = section.Id;

            section.Tag = "updated";
            db.SaveEquivalentSection(section);
            Assert.Single(db.EquivalentSections);
            Assert.Equal(id, db.EquivalentSections[0].Id);
            Assert.Equal("updated", db.EquivalentSections[0].Tag);

            db.DeleteEquivalentSection(section);
            Assert.Empty(db.EquivalentSections);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void EquivalentSection_RoundTripsSupportProvenance()
    {
        string path = TempPath();
        try
        {
            var source = new DatabaseService(path);
            var original = Section();
            original.Strip.StartSupportLocus.Kind = StripSupportKind.Column;
            original.Strip.StartSupportLocus.SourceReferences =
                [new CScore.Planar.PlanarBoundarySourceReference("fem_member", "C1", MemberId: 5)];
            source.SaveEquivalentSection(original);

            var loaded = new DatabaseService(path);
            loaded.LoadAll();

            var actual = Assert.Single(loaded.EquivalentSections);
            Assert.Equal(StripSupportKind.Column, actual.Strip.StartSupportLocus.Kind);
            var reference = Assert.Single(actual.Strip.StartSupportLocus.SourceReferences);
            Assert.Equal("C1", reference.SourceId);
            Assert.Equal(5, reference.MemberId);
            Assert.Equal(StripSupportKind.Manual, actual.Strip.EndSupportLocus.Kind);

            loaded.Dispose();
            source.Dispose();
        }
        finally
        {
            TryDelete(path);
        }
    }

    static StripNonlinearRunResult RunResult(int sectionId, string fingerprint, double marker) => new()
    {
        EquivalentSectionId = sectionId,
        CreatedUtc = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc),
        EquivalentInputFingerprint = fingerprint,
        Formulation = StripBeamFormulation.Timoshenko,
        IsConverged = true,
        AchievedLoadFactor = 1.0,
        TotalIterations = 14,
        LengthM = 6.0,
        StationFractions = [0.0, 0.5, 1.0],
        BeamResultants = [[0.0, 0.0, 0.0], [1.5, -marker, 0.25], [0.0, 0.0, 0.0]],
        BeamDeflections = [0.0, -0.012, 0.0],
        MaxDeflectionM = 0.012,
        FiberChecked = true,
        FiberResultants = [[0.0, 0.0, 0.0], [1.5, -marker, 0.25], [0.0, 0.0, 0.0]],
        FiberDeflections = [0.0, -0.0121, 0.0],
        FiberMaxDeflectionM = 0.0121,
        FiberMomentMismatch = 0.004,
        FiberDeflectionMismatch = 0.008,
        Input = new StripNonlinearRunInput
        {
            StartCondition = StripBeamEndCondition.Fixed,
            AxialRestraint = StripAxialRestraint.BothEnds,
            LoadSteps = 12,
            ElementCount = 6,
            FiberCheck = true,
            Loads =
            [
                new StripNonlinearLoadItem { Kind = StripNonlinearLoadKind.UniformQz, FromFraction = 0.1, ToFraction = 0.9, Value = -25.0 },
                new StripNonlinearLoadItem { Kind = StripNonlinearLoadKind.PointPz, FromFraction = 0.5, Value = -40.0 }
            ]
        },
        Diagnostics = [new("run_note", "note", false)]
    };

    [Fact]
    public void StripNonlinearResult_RoundTripsThroughSqlite()
    {
        string path = TempPath();
        try
        {
            var source = new DatabaseService(path);
            var section = Section();
            source.SaveEquivalentSection(section);
            var original = RunResult(section.Id, "input-fp", marker: 123.5);
            source.SaveStripNonlinearResult(original);
            Assert.NotEqual(0, original.Id);

            var loaded = new DatabaseService(path);
            loaded.LoadAll();

            var actual = Assert.Single(loaded.StripNonlinearResults);
            Assert.Equal(section.Id, actual.EquivalentSectionId);
            Assert.Equal(original.CreatedUtc, actual.CreatedUtc);
            Assert.Equal("input-fp", actual.EquivalentInputFingerprint);
            Assert.Equal(StripBeamFormulation.Timoshenko, actual.Formulation);
            Assert.True(actual.IsConverged);
            Assert.Equal(14, actual.TotalIterations);
            Assert.Equal(-123.5, actual.BeamResultants[1][1], 12);
            Assert.Equal(-0.012, actual.BeamDeflections[1], 12);
            Assert.True(actual.FiberChecked);
            Assert.Equal(0.004, actual.FiberMomentMismatch, 12);
            Assert.Equal(StripBeamEndCondition.Fixed, actual.Input.StartCondition);
            Assert.Equal(StripAxialRestraint.BothEnds, actual.Input.AxialRestraint);
            Assert.Equal(12, actual.Input.LoadSteps);
            Assert.Equal(2, actual.Input.Loads.Count);
            Assert.Equal(StripNonlinearLoadKind.PointPz, actual.Input.Loads[1].Kind);
            Assert.Equal(-40.0, actual.Input.Loads[1].Value, 12);
            Assert.Contains(actual.Diagnostics, d => d.Code == "run_note");

            loaded.Dispose();
            source.Dispose();
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void StripNonlinearResult_KeepsLatestTenPerSection_AndDeleteSectionRemovesResults()
    {
        string path = TempPath();
        try
        {
            using var db = new DatabaseService(path);
            var first = Section();
            var second = Section();
            second.Tag = "equiv-2";
            db.SaveEquivalentSection(first);
            db.SaveEquivalentSection(second);

            for (int i = 0; i < 13; i++)
                db.SaveStripNonlinearResult(RunResult(first.Id, "fp", marker: i));
            db.SaveStripNonlinearResult(RunResult(second.Id, "fp", marker: 99));

            Assert.Equal(10, db.StripNonlinearResults.Count(r => r.EquivalentSectionId == first.Id));
            Assert.Equal(1, db.StripNonlinearResults.Count(r => r.EquivalentSectionId == second.Id));
            // Остались последние: самый старый из оставшихся — прогон номер 3.
            Assert.Equal(-12.0, db.LatestStripNonlinearResult(first.Id)!.BeamResultants[1][1], 12);
            Assert.Equal(-3.0, db.StripNonlinearResults
                .Where(r => r.EquivalentSectionId == first.Id).OrderBy(r => r.Id).First().BeamResultants[1][1], 12);

            db.DeleteEquivalentSection(first);
            Assert.DoesNotContain(db.StripNonlinearResults, r => r.EquivalentSectionId == first.Id);
            Assert.Single(db.StripNonlinearResults);

            using var reloaded = new DatabaseService(path);
            reloaded.LoadAll();
            Assert.Single(reloaded.StripNonlinearResults);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void StripNonlinearResult_IsStaleWhenEquivalentInputFingerprintChanges()
    {
        var section = Section();
        var result = RunResult(1, section.InputFingerprint, marker: 1);
        Assert.False(result.IsStaleFor(section));

        section.InputFingerprint = "changed";
        Assert.True(result.IsStaleFor(section));
    }

    static EquivalentSection Section() => new()
    {
        Num = 2,
        Tag = "equiv-1",
        Description = "test",
        SourceSchemaId = 7,
        SourceRegionId = 11,
        SourcePlateSectionId = 13,
        SourceRegionFingerprint = "region-fp-test",
        SpanStationFraction = 0.75,
        Strip = new PlateStripBeamAnalogy
        {
            Id = "strip-1",
            SourceRegionId = 11,
            ExplicitWidthM = 2.0,
            Fingerprint = "strip-fp",
            Geometry = new PlateStripGeometry { LengthM = 6.0 }
        },
        ReductionPolicy = ReductionPolicy.ConstitutiveIntegration,
        SourceKind = EquivalentSectionSourceKind.PlateSectionTangentSnapshot,
        WidthIntegrationPoints = 2,
        BeamTangent = new[,] { { 2000.0, 40.0, 0.0 }, { 40.0, 600.0, 0.0 }, { 0.0, 0.0, 666.0 } },
        EA = 2000.0,
        EIy = 600.0,
        EIz = 666.0,
        IsCalculable = true,
        Diagnostics = [new("test_warning", "test", false)],
        InputFingerprint = "input-fp",
        ResultFingerprint = "result-fp"
    };

    static string TempPath() => Path.Combine(Path.GetTempPath(), $"opencs-equivalent-{Guid.NewGuid():N}.db");

    static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
