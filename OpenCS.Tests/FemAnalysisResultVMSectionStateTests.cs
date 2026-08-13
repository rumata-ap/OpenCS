using System.Text.Json;
using CScore;
using CScore.Fem;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Structural;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Интеграционные проверки маркеров ТИ и запроса состояния сечения в FemAnalysisResultVM.</summary>
public class FemAnalysisResultVMSectionStateTests
{
    static (DatabaseService Db, string ArtifactDir) Setup(out FemAnalysisResultVM vm)
    {
        var db = new DatabaseService(Path.Combine(Path.GetTempPath(),
            "opencs_vm_sec_" + Guid.NewGuid().ToString("N") + ".db"));
        var schema = new FemSchema { Tag = "Схема" };
        db.SaveFemSchema(schema);
        db.SaveFemMember(new FemMember { SchemaId = schema.Id, ElemTag = "M1", NodeIdsJson = "[1,2]", CrossSectionId = 42 });
        db.SaveFemMeshSnapshot(schema.Id,
            [
                new FemMeshNode { NodeTag = "1", X = 0, Y = 0, Z = 0 },
                new FemMeshNode { NodeTag = "2", X = 2, Y = 0, Z = 0 },
            ],
            [
                new FemElement { ElemTag = "10", NodeIdsJson = "[1,2]", SourceMemberTag = "M1", CrossSectionId = 42 },
            ]);

        var artifactDir = Path.Combine(Path.GetTempPath(), "opencs_vm_sec_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(artifactDir);
        File.WriteAllText(Path.Combine(artifactDir, "section_order.json"), """
        {
            "locations": [
                { "elementTag": 10, "integrationPoint": 1, "sectionTag": 1, "distanceFromElementStartM": 1.0, "elementLengthM": 2.0, "relativePosition": 0.5 }
            ]
        }
        """);
        File.WriteAllText(Path.Combine(artifactDir, "nonlinear_fiber_states.out"),
            "1 1.0 10 1 0 1500000.0 0.0005\n" +
            "1 1.0 10 1 1 3000000.0 0.001\n");

        var result = new FemNonlinearResult
        {
            Status = "ok",
            Steps = [new FemNonlinearStepResult(1, 1.0, true, [], [], [])],
            ArtifactDirectory = artifactDir,
            FiberStateFileName = "nonlinear_fiber_states.out",
            SectionOrderFileName = "section_order.json",
            CalcTypeName = "C",
        };
        var calcResult = new CalcResult { Status = "ok", DataJson = JsonSerializer.Serialize(result) };
        vm = new FemAnalysisResultVM(calcResult, db, schema);
        return (db, artifactDir);
    }

    [Fact]
    public void Ctor_WithRecordedSections_BuildsLocationsAndMarkers()
    {
        var (db, dir) = Setup(out var vm);
        try
        {
            var row = Assert.Single(vm.SectionLocations);
            Assert.True(row.IsStateAvailable);
            Assert.Equal(0.5, row.ElementLocalNormalized, 8);
            Assert.True(vm.CanRequestSectionState);
            var marker = Assert.Single(vm.SectionMarkers);
            Assert.Equal(1.0, marker.Point.X, 8);
        }
        finally { db.Dispose(); Directory.Delete(dir, true); }
    }

    [Fact]
    public void RequestSectionState_Available_RaisesEventWithRecordedFibers()
    {
        var (db, dir) = Setup(out var vm);
        try
        {
            FemSectionStateRequest? received = null;
            vm.SectionStateRequested += r => received = r;

            vm.RequestSectionState(vm.SectionLocations[0]);

            Assert.NotNull(received);
            Assert.Equal(42, received!.SectionId);
            var recorded = received.LoadRecordedFibers();
            Assert.Equal(2, recorded.Count);
            Assert.Equal(1500000.0, recorded[0].StressPa);
            Assert.Equal(0.001, recorded[1].Strain, 12);
        }
        finally { db.Dispose(); Directory.Delete(dir, true); }
    }

    [Fact]
    public void RequestSectionState_UnavailableLocation_DoesNotRaise()
    {
        var (db, dir) = Setup(out var vm);
        try
        {
            var unavailable = new FemSectionLocationRow("M1", 10, 1, 2, 1.0, 2.0, 0.5, 0.5, false);
            bool raised = false;
            vm.SectionStateRequested += _ => raised = true;

            vm.RequestSectionState(unavailable);

            Assert.False(raised);
        }
        finally { db.Dispose(); Directory.Delete(dir, true); }
    }

    [Fact]
    public void RequestSectionState_UnavailableLocation_RaisesUnavailableNotice()
    {
        var (db, dir) = Setup(out var vm);
        try
        {
            var unavailable = new FemSectionLocationRow("M1", 10, 1, 2, 1.0, 2.0, 0.5, 0.5, false);
            string? notice = null;
            vm.SectionStateUnavailable += key => notice = key;

            vm.RequestSectionState(unavailable);

            Assert.Equal("FemSectionStateUnavailable", notice);
        }
        finally { db.Dispose(); Directory.Delete(dir, true); }
    }
}
