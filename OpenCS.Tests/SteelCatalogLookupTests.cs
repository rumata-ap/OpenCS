using CScore.Import;
using CScore.Sp16;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Поиск строки Sortamenty.db3 по профилю импортированной схемы (стандарт + размеры до 0,1 мм).</summary>
public class SteelCatalogLookupTests
{
    static ProfileDB Db()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OpenCS.sln"))) dir = dir.Parent;
        return new ProfileDB(Path.Combine(dir!.FullName, "OpenCS", "DataSource", "Sortamenty.db3"));
    }

    [Fact]
    public void IBeam_Asсhm_FoundWithIt()
    {
        var shape = new ImportedSteelShape(SteelProfileKind.IBeam, SteelFabrication.Rolled,
            0.248, 0.124, 0.005, 0.008, 0.012, 0, 0, "СТО АСЧМ 20-93", "25Б1");

        var reference = Db().Find(shape);

        Assert.NotNull(reference);
        Assert.Equal("25Б1", reference!.Name);
        Assert.Contains("АСЧМ", reference.Standard);
        Assert.True(reference.It > 0);
    }

    [Fact]
    public void DifferentDimensions_OrUnknownStandard_Null()
    {
        var shape = new ImportedSteelShape(SteelProfileKind.IBeam, SteelFabrication.Rolled,
            0.248, 0.124, 0.005, 0.008, 0.012, 0, 0, "СТО АСЧМ 20-93", "25Б1");

        Assert.Null(Db().Find(shape with { Tw = 0.0052 }));
        Assert.Null(Db().Find(shape with { Standard = "ТУ 0925-016-00186269-2016" }));
        Assert.Null(Db().Find(shape with { Standard = "" }));
    }

    [Fact]
    public void SquareTube_ByDimensions()
    {
        var shape = new ImportedSteelShape(SteelProfileKind.Box, SteelFabrication.Bent,
            0.1, 0.1, 0.004, 0.004, 0.004, 0, 0, "ГОСТ Р 54157-2010", "100x4");

        var reference = Db().Find(shape);

        Assert.NotNull(reference);
        Assert.Contains("54157", reference!.Standard);
    }
}
