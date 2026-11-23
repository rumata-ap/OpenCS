using CScore;
using OpenCS.Services;
using OpenCS.Utilites;
using System.IO;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Служба контуров и кругов пула: проверка кольца, шаблоны, сохранение, удаление используемого.</summary>
public sealed class ContourAuthoringTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"opencs_contour_{Guid.NewGuid():N}");
    public ContourAuthoringTests() => Directory.CreateDirectory(_dir);
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    static readonly (double X, double Y)[] Square = [(0, 0), (0.4, 0), (0.4, 0.6), (0, 0.6)];

    [Fact]
    public void ValidateRing_Errors_WithIndex()
    {
        Assert.Empty(ContourAuthoring.ValidateRing(Square));
        Assert.Empty(ContourAuthoring.ValidateRing([.. Square, (0, 0)])); // замыкающая вершина допустима

        var errs = ContourAuthoring.ValidateRing([(0, 0), (1, 0)]);
        Assert.Contains(errs, e => e.Path == "points" && e.Message == Loc.S("ContourErrTooFewVertices"));

        errs = ContourAuthoring.ValidateRing([(0, 0), (1, 0), (1, 0), (0, 1)]);
        Assert.Contains(errs, e => e.Path == "points[2]");

        // «бабочка»: рёбра 1 и 3 пересекаются
        errs = ContourAuthoring.ValidateRing([(0, 0), (1, 1), (1, 0), (0, 1)]);
        Assert.Contains(errs, e => e.Path == "points[2]");

        errs = ContourAuthoring.ValidateRing([(0, 0), (1, 0), (2, 0)]);
        Assert.Contains(errs, e => e.Message == Loc.S("ContourErrZeroArea") || e.Message.Contains("1"));

        errs = ContourAuthoring.ValidateRing([(0, 0), (double.NaN, 0), (0, 1)]);
        Assert.Contains(errs, e => e.Path == "points[1]");
    }

    [Fact]
    public void Validate_TagAndType()
    {
        var c = ContourAuthoring.FromPoints(Square, " ", ContourType.Hull);
        Assert.Contains(ContourAuthoring.Validate(c), e => e.Path == "tag");
        c.Tag = "K";
        c.Type = (ContourType)7;
        Assert.Contains(ContourAuthoring.Validate(c), e => e.Path == "type");
    }

    [Theory]
    [InlineData(ContourTemplate.Rect, 0.4, 0.6, 0, 0, 0.24)]
    [InlineData(ContourTemplate.Tee, 0.4, 0.6, 0.1, 0.15, 0.4 * 0.15 + 0.1 * 0.45)]
    [InlineData(ContourTemplate.IBeam, 0.3, 0.6, 0.01, 0.02, 2 * 0.3 * 0.02 + 0.01 * 0.56)]
    [InlineData(ContourTemplate.Angle, 0.1, 0.1, 0.01, 0.01, 0.1 * 0.01 + 0.01 * 0.09)]
    public void FromTemplate_AreaMatchesFormula(ContourTemplate kind, double w, double h, double tw, double tf, double area)
    {
        var size = new ContourTemplateSize(w, h, tw, tf);
        Assert.Empty(ContourAuthoring.ValidateTemplate(kind, size));
        var c = ContourAuthoring.FromTemplate(kind, size, "T");
        Assert.True(c.IsClosed);
        Assert.Empty(ContourAuthoring.Validate(c));
        Assert.Equal(area, Math.Abs(new GeoProps(c).A), 9);
    }

    [Fact]
    public void ValidateTemplate_Inconsistent_Refused()
    {
        Assert.Contains(ContourAuthoring.ValidateTemplate(ContourTemplate.Tee, new(0.4, 0.6, 0.5, 0.1)), e => e.Path == "tw");
        Assert.Contains(ContourAuthoring.ValidateTemplate(ContourTemplate.IBeam, new(0.3, 0.6, 0.01, 0.3)), e => e.Path == "tf");
        Assert.Contains(ContourAuthoring.ValidateTemplate(ContourTemplate.Angle, new(0.1, 0.1, 0.01, 0.1)), e => e.Path == "tf");
        Assert.Contains(ContourAuthoring.ValidateTemplate(ContourTemplate.Rect, new(0, 0.6)), e => e.Path == "width");
        Assert.Contains(ContourAuthoring.ValidateTemplate(ContourTemplate.Circle, new(0.5, Segments: 3)), e => e.Path == "segments");
        Assert.Empty(ContourAuthoring.ValidateTemplate(ContourTemplate.Circle, new(0.5, Segments: 32)));
    }

    [Fact]
    public void Save_InvalidWritesNothing_ValidAddsToPool_Reload()
    {
        var path = Path.Combine(_dir, "c.db");
        using (var db = new DatabaseService(path))
        {
            var bad = ContourAuthoring.FromPoints([(0, 0), (1, 1), (1, 0), (0, 1)], "Bad", ContourType.Hull);
            Assert.NotEmpty(ContourAuthoring.Save(db, bad));
            Assert.Equal(0, bad.Id);
            Assert.Empty(db.Contours);

            var c = ContourAuthoring.FromPoints(Square, "K1", ContourType.Hull);
            Assert.Empty(ContourAuthoring.Save(db, c));
            Assert.True(c.Id > 0);
            Assert.Single(db.Contours);
            Assert.False(string.IsNullOrEmpty(c.WKT));
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var again = new DatabaseService(path);
        again.LoadAll();
        var k = Assert.Single(again.Contours);
        Assert.Equal("K1", k.Tag);
        Assert.Equal(0.24, Math.Abs(new GeoProps(k).A), 9);
    }

    [Fact]
    public void TryDelete_UsedByArea_Refused_FreeRemovedFromPool()
    {
        using var db = new DatabaseService(Path.Combine(_dir, "d.db"));
        var used = ContourAuthoring.FromPoints(Square, "K1", ContourType.Hull);
        var free = ContourAuthoring.FromPoints(Square, "K2", ContourType.Hull);
        Assert.Empty(ContourAuthoring.Save(db, used));
        Assert.Empty(ContourAuthoring.Save(db, free));
        db.MaterialAreas.Add(new MaterialArea { Tag = "Бетон", PoolContourId = used.Id });

        Assert.False(ContourAuthoring.TryDelete(db, used, out var usedBy));
        Assert.Equal(["Бетон"], usedBy);
        Assert.True(ContourAuthoring.TryDelete(db, free, out usedBy));
        Assert.Empty(usedBy);
        Assert.Equal([used], db.Contours);
        Assert.DoesNotContain(db.Points, p => ReferenceEquals(p.Contour, free));
    }

    [Fact]
    public void SaveCircle_ValidatesRadius_DerivesDiameterAndArea()
    {
        using var db = new DatabaseService(Path.Combine(_dir, "k.db"));
        var bad = new CircleP { Radius = 0 };
        Assert.Contains(ContourAuthoring.SaveCircle(db, bad), e => e.Path == "d");
        Assert.Empty(db.Circles);

        var c = new CircleP(0.1, 0.2, 0.0125) { Tag = "Ø25" };
        c.Radius = 0.01;
        Assert.Empty(ContourAuthoring.SaveCircle(db, c));
        Assert.Equal(0.02, c.Diameter, 12);
        Assert.Equal(Math.PI * 1e-4, c.Area, 12);
        Assert.Single(db.Circles);
    }
}
