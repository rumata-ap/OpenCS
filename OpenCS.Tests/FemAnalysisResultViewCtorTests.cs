using System.Text.Json;
using CScore;
using CScore.Fem;
using OpenCS.OpenSees.Structural;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using OpenCS.Views;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Проверки конструктора результатного вида FEM: XAML-инициализация тумблеров
/// (IsChecked="True") вызывает Checked-обработчики до присваивания _vm — вид не должен падать.</summary>
public class FemAnalysisResultViewCtorTests
{
    [Fact]
    public void Ctor_WithSectionMarkers_DoesNotThrowDuringXamlInit()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), "opencs_view_ctor_" + Guid.NewGuid().ToString("N") + ".db");
        string artifactDir = Path.Combine(Path.GetTempPath(), "opencs_view_ctor_" + Guid.NewGuid().ToString("N"));
        Exception? error = null;

        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                var db = new DatabaseService(dbPath);
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

                Directory.CreateDirectory(artifactDir);
                File.WriteAllText(Path.Combine(artifactDir, "section_order.json"), """
                {
                    "locations": [
                        { "elementTag": 10, "integrationPoint": 1, "sectionTag": 1, "distanceFromElementStartM": 1.0, "elementLengthM": 2.0, "relativePosition": 0.5 }
                    ]
                }
                """);
                File.WriteAllText(Path.Combine(artifactDir, "nonlinear_fiber_states.out"),
                    "1 1.0 10 1 0 1500000.0 0.0005\n");

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
                var vm = new FemAnalysisResultVM(calcResult, db, schema);

                var view = new FemAnalysisResultView(vm);

                db.Dispose();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();

        try
        {
            Assert.Null(error);
        }
        finally
        {
            try { Directory.Delete(artifactDir, true); } catch (IOException) { }
            try { File.Delete(dbPath); } catch (IOException) { }
        }
    }
}
