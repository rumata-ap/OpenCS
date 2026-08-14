using System.Text.Json;
using CScore;
using CScore.Fem;
using OpenCS.OpenSees.Structural;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Проверки подключения отображаемых узловых результатов к result VM.</summary>
public class FemAnalysisResultVMDisplayTests
{
    [Fact]
    public void VM_RebuildsDisplayedRowsWhenFilterAndUnitsChange()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), "opencs_display_" + Guid.NewGuid().ToString("N") + ".db");
        var db = new DatabaseService(dbPath);
        try
        {
            var schema = new FemSchema { Tag = "Схема" };
            db.SaveFemSchema(schema);
            db.SaveFemMeshSnapshot(schema.Id,
                [
                    new FemMeshNode { NodeTag = "1", X = 0, SourceMemberTag = "M1" },
                    new FemMeshNode { NodeTag = "2", X = 1, SourceMemberTag = "M1" }
                ],
                [
                    new FemElement { ElemTag = "1", NodeIdsJson = "[1,2]", SourceMemberTag = "M1" }
                ]);

            var linear = new FemLinearResult
            {
                Status = "ok",
                Displacements =
                [
                    new FemNodeDisplacement(1, 0.001, 0.002, 0.003, 0.01, 0.02, 0.03),
                    new FemNodeDisplacement(2, 0.002, 0.004, 0.006, 0.02, 0.04, 0.06)
                ]
            };
            var vm = new FemAnalysisResultVM(
                new CalcResult { Status = "ok", DataJson = JsonSerializer.Serialize(linear) },
                db, schema);

            var changed = new List<string?>();
            vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            vm.DisplacementLengthUnit = FemLengthUnit.Millimeters;
            vm.RotationDisplayScale = FemRotationScale.OneHundred;
            vm.DisplacementDisplayMode = FemDisplacementDisplayMode.ExtremesOnly;

            Assert.Equal(2, vm.DisplayedDisplacements.Count);
            Assert.Equal(1.0, vm.DisplayedDisplacements[0].Ux, 12);
            Assert.Equal(1.0, vm.DisplayedDisplacements[0].Rx, 12);
            Assert.All(vm.DisplayedDisplacements, row => Assert.Equal("M1", row.MemberTag));
            Assert.Contains(nameof(FemAnalysisResultVM.DisplayedDisplacements), changed);
            Assert.Equal(0.001, vm.Displacements[0].Ux, 12);
            Assert.Equal(0.01, vm.Displacements[0].Rx, 12);

            vm.SelectNode(2);

            Assert.Equal(2, vm.SelectedDisplacementRow?.NodeTag);
            Assert.Equal(2, vm.SelectedDisplayedDisplacementRow?.NodeTag);
        }
        finally
        {
            db.Dispose();
            try { File.Delete(dbPath); } catch (IOException) { }
        }
    }
}
