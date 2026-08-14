using System.Text.Json;
using CScore;
using CScore.Fem;
using OpenCS.OpenSees.Structural;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Проверки независимой видимости 3D-схемы и масштабов силовых компонент.</summary>
public class FemAnalysisResultVM3DDisplayTests
{
    [Fact]
    public void VM_UsesIndependentForceScalePerComponentAndKeepsManualOverride()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), "opencs_3d_display_" + Guid.NewGuid().ToString("N") + ".db");
        var db = new DatabaseService(dbPath);
        try
        {
            var schema = new FemSchema { Tag = "Схема" };
            db.SaveFemSchema(schema);
            db.SaveFemMeshSnapshot(schema.Id,
                [
                    new FemMeshNode { NodeTag = "1", X = 0 },
                    new FemMeshNode { NodeTag = "2", X = 1 }
                ],
                [new FemElement { ElemTag = "1", NodeIdsJson = "[1,2]" }]);
            var linear = new FemLinearResult
            {
                Status = "ok",
                ElementForces =
                [new FemElementEndForces(1, 1000, 0, 0, 0, 0, 10000, 1000, 0, 0, 0, 0, 10000)]
            };
            var vm = new FemAnalysisResultVM(
                new CalcResult { Status = "ok", DataJson = JsonSerializer.Serialize(linear) },
                db, schema);

            Assert.Equal(0.01, vm.ForceScale, 12);
            vm.SelectedForceComponent = FemForceComponent.N;
            Assert.Equal(0.1, vm.ForceScale, 12);

            vm.SelectedForceComponent = FemForceComponent.Mz;
            vm.ForceScale = 7.0;
            vm.SelectedForceComponent = FemForceComponent.N;
            vm.SelectedForceComponent = FemForceComponent.Mz;
            Assert.Equal(7.0, vm.ForceScale, 12);

            vm.ResetForceScaleCommand.Execute(null);
            Assert.Equal(0.01, vm.ForceScale, 12);

            vm.ShowDeformedSchema = false;
            vm.ShowDeformedNodes = false;
            vm.ShowNodeResultValues = false;
            Assert.Equal(FemNodalComponent.Ux, vm.SelectedNodalComponent);
            vm.SelectedNodalComponent = FemNodalComponent.Rz;
            Assert.False(vm.ShowDeformedSchema);
            Assert.False(vm.ShowDeformedNodes);
            Assert.False(vm.ShowNodeResultValues);
            Assert.Equal(FemNodalComponent.Rz, vm.SelectedNodalComponent);
        }
        finally
        {
            db.Dispose();
            try { File.Delete(dbPath); } catch (IOException) { }
        }
    }
}
