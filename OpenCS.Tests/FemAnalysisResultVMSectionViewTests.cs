using System.Text.Json;
using CScore;
using CScore.Fem;
using OpenCS.OpenSees.Structural;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Проверки событий действий контекстного меню результата OpenSees.</summary>
public class FemAnalysisResultVMSectionViewTests
{
    [Fact]
    public void RequestShowMemberSection_RaisesEventWithMemberTag()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), "opencs_section_view_" + Guid.NewGuid().ToString("N") + ".db");
        var db = new DatabaseService(dbPath);
        try
        {
            var schema = new FemSchema { Tag = "Схема" };
            db.SaveFemSchema(schema);
            var result = new FemLinearResult();
            var vm = new FemAnalysisResultVM(
                new CalcResult { Status = "ok", DataJson = JsonSerializer.Serialize(result) },
                db, schema);

            string? actualTag = null;
            vm.ShowMemberSectionRequested += tag => actualTag = tag;

            vm.RequestShowMemberSection("M1");

            Assert.Equal("M1", actualTag);
        }
        finally
        {
            db.Dispose();
            try { File.Delete(dbPath); } catch (IOException) { }
        }
    }
}
