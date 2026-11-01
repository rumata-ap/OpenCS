using CScore;
using CScore.Fem.Fragment;
using Microsoft.Data.Sqlite;
using OpenCS.Services;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Фрагмент вокруг стен и колонн, сервис (срез 3): призма по выделению, перекрытия i…k и «вся высота», запись сведений
/// о призме. Родитель — <see cref="StoreyFragmentDbFixture"/>: колонна C1 (0, 0) z 0…6, плиты S1 (z 3) и S2 (z 9) 6×6.
/// </summary>
public sealed class ElementFragmentServiceTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"opencs-element-fragment-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_path)) File.Delete(_path); } catch { }
    }

    [Fact]
    public void Column_NoSection_SquareAndLevels()
    {
        using var db = new DatabaseService(_path);
        var parent = StoreyFragmentDbFixture.CreateParent(db);
        var s = ElementFragmentService.PrismFromSelection(db, parent.Id, ["C1"], 1.5);

        Assert.False(s.HasErrors, string.Join("; ", s.Diagnostics.Select(d => d.Message)));
        Assert.Equal((0.0, 0.0, 1.5, 1.5), (s.Prism!.CenterX, s.Prism.CenterY, s.Prism.HalfLength, s.Prism.HalfWidth));
        Assert.Equal((0.0, 6.0), (s.ElemZMin, s.ElemZMax));
        Assert.Equal([3.0, 9.0], s.Levels.Select(l => Math.Round(l.Z, 6)));
        Assert.Equal([0, 1], s.Height.Available);
        Assert.Equal(0, s.Height.Default);
        Assert.Equal((0, 1), (s.Height.FullFrom, s.Height.FullTo));

        var one = ElementFragmentService.Request(s, 0, 0, "Фрагмент · C1");
        Assert.Equal((-1.0, 6.0), (one.Z1, one.Z2));
        Assert.Same(s.Prism, one.Prism);
        Assert.Equal((3.0, 3.0, false), (one.PrismInfo!.FloorFromZ!.Value, one.PrismInfo.FloorToZ!.Value, one.PrismInfo.FullHeight));

        var full = ElementFragmentService.FullHeightRequest(s, "Фрагмент · C1");
        Assert.Equal((-1.0, 10.0), (full.Z1, full.Z2));
        Assert.True(full.PrismInfo!.FullHeight);
        Assert.Equal(9.0, full.PrismInfo.FloorToZ!.Value, 6);
    }

    [Fact]
    public void Column_SectionByMeshElements()
    {
        using var db = new DatabaseService(_path);
        var parent = StoreyFragmentDbFixture.CreateParent(db);
        var area = new MaterialArea { Tag = "Бетон" };
        area.Contours.Add(new Contour([-.2, .2, .2, -.2, -.2], [-.3, -.3, .3, .3, -.3], "outer") { Type = ContourType.Hull });
        db.CrossSections.Add(new CrossSection { Id = 77, Num = 1, Tag = "К1", Areas = [area] });
        db.SetFemElementCrossSections(db.GetFemMeshElements(parent.Id).Where(e => e.SourceMemberTag == "C1").Select(e => (e, (int?)77)));

        var s = ElementFragmentService.PrismFromSelection(db, parent.Id, ["C1"], 1.0);
        Assert.False(s.HasErrors, string.Join("; ", s.Diagnostics.Select(d => d.Message)));
        // Габарит сечения 0,4 × 0,6 + d = 1 с каждой стороны.
        Assert.Equal([1.2, 1.3], new[] { s.Prism!.HalfLength, s.Prism.HalfWidth }.Select(v => Math.Round(v, 9)).Order());
    }

    [Theory]
    [InlineData(new[] { "S1" }, 1.5, "fragment_prism_not_vertical")]
    [InlineData(new[] { "C1", "S1" }, 1.5, "fragment_prism_not_vertical")]
    [InlineData(new string[0], 1.5, "fragment_prism_empty")]
    [InlineData(new[] { "C1" }, 0.0, "fragment_prism_offset_invalid")]
    [InlineData(new[] { "X9" }, 1.5, "fragment_prism_member_missing")]
    public void Refusals(string[] tags, double d, string code)
    {
        using var db = new DatabaseService(_path);
        var parent = StoreyFragmentDbFixture.CreateParent(db);
        var s = ElementFragmentService.PrismFromSelection(db, parent.Id, tags, d);
        Assert.True(s.HasErrors);
        Assert.Null(s.Prism);
        Assert.Empty(s.Levels);
        Assert.Contains(s.Diagnostics, x => x.Code == code && x.IsError);
        Assert.Throws<InvalidOperationException>(() => ElementFragmentService.FullHeightRequest(s, "Ф"));
    }

    [Fact]
    public void Build_WritesPrismInfo()
    {
        using var db = new DatabaseService(_path);
        var parent = StoreyFragmentDbFixture.CreateParent(db);
        var s = ElementFragmentService.PrismFromSelection(db, parent.Id, ["C1"], 1.5);
        var result = StoreyFragmentService.Build(db, parent.Id, ElementFragmentService.Request(s, 0, 0, "Фрагмент · C1"));

        Assert.NotNull(result.Schema);
        Assert.Contains(result.Draft.Cuts, c => c.Side == FragmentCutSide.Lateral);
        var info = FragmentPrismCodec.Decode(result.Schema!.FragmentPrismJson)!;
        Assert.Equal(s.Prism, info.Prism);
        Assert.Equal(1.5, info.OffsetM);
        Assert.Equal(["C1"], info.MemberTags!);
        Assert.Equal((3.0, 3.0, false), (info.FloorFromZ!.Value, info.FloorToZ!.Value, info.FullHeight));
        Assert.Equal(StoreyFragmentService.CheckParent(db, result.Schema), FragmentParentStatus.Unchanged);
    }
}
