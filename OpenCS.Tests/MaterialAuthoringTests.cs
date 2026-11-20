using CScore;
using OpenCS.Services;
using OpenCS.Utilites;
using System.IO;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Служба материала: одна проверка и одно сохранение для страницы материала и консоли.</summary>
public sealed class MaterialAuthoringTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"opencs_mat_{Guid.NewGuid():N}");
    public MaterialAuthoringTests() => Directory.CreateDirectory(_dir);
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    static readonly CalcType[] Sets = [CalcType.C, CalcType.CL, CalcType.N, CalcType.NL];

    [Theory]
    [InlineData("A240")]
    [InlineData("A400")]
    [InlineData("A500C")]
    [InlineData("B500C")]
    public void CatalogRebar_RscEqualsMinusFc(string cls)
    {
        var m = MaterialCatalog.CreateRebar(cls)!;
        foreach (var ct in Sets)
        {
            var ch = m.GetChars(ct)!;
            Assert.True(ch.Rsc == 0 || Math.Abs(ch.Rsc + ch.Fc) < 1e-6, $"{cls} {ct}: Rsc={ch.Rsc}, Fc={ch.Fc}");
        }
    }

    [Fact]
    public void Validate_Catalog_NoErrors()
    {
        Assert.Empty(MaterialAuthoring.Validate(MaterialCatalog.CreateHeavyConcrete("B25")!));
        Assert.Empty(MaterialAuthoring.Validate(MaterialCatalog.CreateRebar("A500C")!));
        Assert.Empty(MaterialAuthoring.Validate(MaterialCatalog.CreateStructuralSteel("С255")!));
    }

    [Fact]
    public void Validate_TypeAndTag()
    {
        var m = new Material(0);
        var paths = MaterialAuthoring.Validate(m).Select(e => e.Path).ToList();
        Assert.Contains("type", paths);
        Assert.Contains("tag", paths);
    }

    [Fact]
    public void Validate_ConcreteCompressionAndModulus()
    {
        var m = MaterialCatalog.CreateHeavyConcrete("B25")!;
        m.C!.Fc = 5;
        m.N!.E = 0;
        var paths = MaterialAuthoring.Validate(m).Select(e => e.Path).ToList();
        Assert.Contains("chars.C.fc", paths);
        Assert.Contains("chars.N.e", paths);
    }

    [Fact]
    public void Validate_MissingChars()
    {
        var m = new Material(0) { Tag = "X", Type = MatType.Concrete };
        m.MaterialChars = [new MaterialChars(CalcType.N) { E = 1, Fc = -1 }];
        var paths = MaterialAuthoring.Validate(m).Select(e => e.Path).ToList();
        Assert.Contains("chars.C", paths);
        Assert.DoesNotContain("chars.N", paths);
    }

    [Fact]
    public void Save_Invalid_NothingWritten_Valid_Persisted()
    {
        string path = Path.Combine(_dir, "p.db");
        using (var db = new DatabaseService(path))
        {
            Assert.NotEmpty(MaterialAuthoring.Save(db, new Material(0)));
            Assert.Empty(db.Materials);

            var m = MaterialCatalog.CreateHeavyConcrete("B25")!;
            Assert.Empty(MaterialAuthoring.Save(db, m));
            Assert.True(m.Id > 0);
            m.Description = "правка";
            Assert.Empty(MaterialAuthoring.Save(db, m));
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var again = new DatabaseService(path);
        again.LoadAll();
        var saved = Assert.Single(again.Materials);
        Assert.Equal("правка", saved.Description);
    }

    [Fact]
    public void Normalize_RebarRscFollowsCompressionStrength()
    {
        var m = MaterialCatalog.CreateRebar("A500C")!;
        m.N!.Fc = -380_000;
        MaterialAuthoring.Normalize(m);
        Assert.Equal(380_000, m.N.Rsc, 6);
    }
}
