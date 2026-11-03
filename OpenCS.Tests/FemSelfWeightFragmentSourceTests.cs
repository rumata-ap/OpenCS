using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Import;
using OpenCS.Services;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Жёсткости КЭ фрагмента SCAD: программа, файл модели и единицы — у родителя; таблица — копия фрагмента; номер
/// жёсткости КЭ фрагмента — из опорного КЭ родителя (спека 4а, дефект D2).
/// </summary>
public sealed class FemSelfWeightFragmentSourceTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"opencs-fragment-sw-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_path)) File.Delete(_path); } catch { }
    }

    [Fact]
    public void ScadFragment_BarTakesParentStiffnessNumber_UnmappedBarHasNone()
    {
        using var db = new DatabaseService(_path);
        var parent = new FemSchema { Tag = "Родитель", SourceType = "scad" };
        db.SaveFemSchema(parent);
        db.SaveFemSchemaSourceFile(parent.Id, FemSchemaSourceFileKind.ScadAnalysisModel, "model.json", "{}"u8.ToArray());
        var record = new LiraStiffnessRecord(3, ScadStiffnessParams.ScadKindCode, "Колонна", "S0 3e6 30 50 NU 0.25", 0.01);
        db.SaveFemSchemaStiffnesses(parent.Id, [record]);

        var fragment = new FemSchema
        {
            Tag = "Фрагмент", ParentSchemaId = parent.Id, FragmentZ1 = 1.5, FragmentZ2 = 4.5, ParentSourceType = "scad",
        };
        db.SaveFemSchema(fragment);
        // Таблица жёсткостей фрагмента — копия родительской (у фрагмента файла модели нет).
        db.SaveFemSchemaStiffnesses(fragment.Id, [record]);
        db.ReplaceStoreyFragmentData(fragment.Id, [],
            [new FragmentRefElement("501", "beam", [new(0, 0, 1.5), new(0, 0, 3)], stiffnessNum: 3, sourceMemberTag: "C1")], []);
        db.SaveFemMeshSnapshot(fragment.Id,
            [
                new FemMeshNode { NodeTag = "1", X = 0, Y = 0, Z = 1.5 },
                new FemMeshNode { NodeTag = "2", X = 0, Y = 0, Z = 3 },
                new FemMeshNode { NodeTag = "3", X = 5, Y = 5, Z = 1.5 },
                new FemMeshNode { NodeTag = "4", X = 5, Y = 5, Z = 3 },
            ],
            [
                new FemElement { ElemTag = "501", ElemType = "beam", NodeIdsJson = "[1,2]", SourceMemberTag = "C1" },
                new FemElement { ElemTag = "502", ElemType = "beam", NodeIdsJson = "[3,4]", SourceMemberTag = "C2" },
            ]);
        db.LoadAll();

        var source = FemSelfWeightSourceFactory.Create(db, fragment.Id, out var notes);
        Assert.NotNull(source);
        var mesh = db.GetFemMeshElements(fragment.Id).ToDictionary(e => e.ElemTag);
        Assert.NotNull(source!.Bar(mesh["501"]));
        Assert.Null(source.Bar(mesh["502"]));
        Assert.Null(mesh["501"].StiffnessNum);
    }
}
