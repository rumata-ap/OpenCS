using CScore;
using OpenCS.Services;
using OpenCS.Utilites;
using System.IO;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Служба диаграмм пула: точки ↔ ветви, проверка, сохранение, удаление используемой.</summary>
public sealed class DiagramAuthoringTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"opencs_diag_{Guid.NewGuid():N}");
    public DiagramAuthoringTests() => Directory.CreateDirectory(_dir);
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    static readonly DiagramPointData[] Pts =
        [new(-0.0035, -14500), new(-0.002, -14500), new(0.0001, 1050), new(0.00015, 1050)];

    static Diagramm Make(string tag = "D1")
    {
        var d = new Diagramm { Tag = tag, Type = DiagrammType.Custom, CalcType = CalcType.C, MaterialType = MatType.Concrete };
        DiagramAuthoring.Apply(d, Pts, [-0.002]);
        return d;
    }

    [Fact]
    public void Apply_ToPoints_RoundTrip_OriginAddedOnce()
    {
        var d = Make();
        Assert.Equal([-0.0035, -0.002, 0.0], d.Ic.X);
        Assert.Equal([0.0, 0.0001, 0.00015], d.It.X);
        var back = DiagramAuthoring.ToPoints(d);
        Assert.Equal(5, back.Count);
        Assert.Equal(Pts.Concat([new DiagramPointData(0, 0)]).OrderBy(p => p.Eps), back);
        Assert.Equal([-0.002], d.CharacteristicStrains);
        Assert.Empty(DiagramAuthoring.Validate(d));
    }

    [Fact]
    public void ValidatePoints_Errors_WithIndex()
    {
        var errs = DiagramAuthoring.ValidatePoints([new(-0.001, -10), new(0.001, 5), new(-0.001, -12)]);
        Assert.Contains(errs, e => e.Path == "points[0]" || e.Path == "points[2]");

        errs = DiagramAuthoring.ValidatePoints([new(0, 5), new(0.001, 5)]);
        Assert.Contains(errs, e => e.Path == "points[0]");

        errs = DiagramAuthoring.ValidatePoints([new(0.001, 5)]);
        Assert.Contains(errs, e => e.Path == "points" && e.Message == Loc.S("DiagramErrNoCompression"));

        errs = DiagramAuthoring.ValidatePoints([new(-0.001, double.NaN), new(0.001, 5)]);
        Assert.Contains(errs, e => e.Path == "points[0]");

        Assert.Empty(DiagramAuthoring.ValidatePoints(Pts));
    }

    [Fact]
    public void Validate_TagMaterialTypeCharacteristic()
    {
        var d = Make("  ");
        d.MaterialType = MatType.Custom;
        d.CharacteristicStrains = [0.5];
        var paths = DiagramAuthoring.Validate(d).Select(e => e.Path).ToList();
        Assert.Contains("tag", paths);
        Assert.Contains("materialType", paths);

        d = Make();
        d.CharacteristicStrains = [0.5];
        Assert.Contains(DiagramAuthoring.Validate(d), e => e.Path == "characteristic[0]");
    }

    [Fact]
    public void Save_Invalid_NothingWritten_Valid_Persisted_InKpa()
    {
        string path = Path.Combine(_dir, "p.db");
        using (var db = new DatabaseService(path))
        {
            Assert.NotEmpty(DiagramAuthoring.Save(db, Make("")));
            Assert.Empty(db.Diagrams);
            var d = Make();
            Assert.Empty(DiagramAuthoring.Save(db, d));
            Assert.True(d.Id > 0);
            Assert.Single(db.Diagrams);
            Assert.Empty(DiagramAuthoring.Save(db, d));   // повторно — не дублирует
            Assert.Single(db.Diagrams);
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var again = new DatabaseService(path);
        again.LoadAll();
        var saved = Assert.Single(again.Diagrams);
        Assert.Equal(-14500, saved.Ic.Y[0]);
        Assert.Equal([-0.002], saved.CharacteristicStrains);
    }

    [Fact]
    public void TryDelete_UsedByCustomMaterial_Refused()
    {
        using var db = new DatabaseService(Path.Combine(_dir, "d.db"));
        var d = Make();
        Assert.Empty(DiagramAuthoring.Save(db, d));
        var m = new Material(0) { Tag = "M", Type = MatType.Custom, BaseType = MatType.Concrete };
        foreach (var ct in new[] { CalcType.C, CalcType.CL, CalcType.N, CalcType.NL }) m.CustomDiagramIds[ct] = d.Id;
        Assert.Empty(MaterialAuthoring.Save(db, m));

        Assert.False(DiagramAuthoring.TryDelete(db, d, out var usedBy));
        Assert.Equal(["M"], usedBy);
        Assert.Single(db.Diagrams);

        m.Type = MatType.Concrete;   // ссылка снимается только у custom
        Assert.True(DiagramAuthoring.TryDelete(db, Make("лишняя"), out _));
    }

    [Fact]
    public void MaterialCustom_Validate_BaseTypeAndFourDiagrams()
    {
        var pool = new List<Diagramm> { Make() };
        pool[0].Id = 7;
        var m = new Material(0) { Tag = "M", Type = MatType.Custom };
        m.CustomDiagramIds[CalcType.C] = 7;
        m.CustomDiagramIds[CalcType.CL] = 99;
        var paths = MaterialAuthoring.Validate(m, pool).Select(e => e.Path).ToList();
        Assert.Contains("custom.baseType", paths);
        Assert.DoesNotContain("custom.diagrams.C", paths);
        Assert.Contains("custom.diagrams.CL", paths);   // нет в пуле
        Assert.Contains("custom.diagrams.N", paths);    // не выбрана
        Assert.Contains("custom.diagrams.NL", paths);

        m.BaseType = MatType.Concrete;
        foreach (var ct in new[] { CalcType.CL, CalcType.N, CalcType.NL }) m.CustomDiagramIds[ct] = 7;
        Assert.Empty(MaterialAuthoring.Validate(m, pool));
    }
}
