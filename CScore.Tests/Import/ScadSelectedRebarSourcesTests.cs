using CScore.Fem;
using CScore.Import;
using CScore.PlateRebar;
using Xunit;

namespace CScore.Tests.Import;

/// <summary>Подбор SCAD (выгрузка плагина): мозаики, сечения пластин и стержней для проверок по КЭ.</summary>
public class ScadSelectedRebarSourcesTests
{
    // ── Материалы ───────────────────────────────────────────────────────────────────────────────

    static MaterialChars ConcreteChars(CalcType ct) => new(ct)
    {
        Type = MatType.Concrete, E = 30_000_000.0, Fc = -14_500.0, Ft = 1_050.0,
        Ec0 = -0.002, Ec1 = -0.6 * 14_500.0 / 30_000_000.0, Ec2 = -0.0035, Ec1Red = -0.0015,
        Et0 = 0.0001, Et1 = 0.6 * 1_050.0 / 30_000_000.0, Et2 = 0.00015, Et1Red = 0.00008,
    };

    static MaterialChars RebarChars(CalcType ct) => new(ct)
    {
        Type = MatType.ReSteelF, E = 200_000_000.0, Fc = -435_000.0, Ft = 435_000.0, Ec2 = -0.025, Et2 = 0.025,
    };

    static readonly Material Concrete = Make(new Material { Id = 1, Tag = "B25", Type = MatType.Concrete, E = 30_000_000.0 }, ConcreteChars);
    static readonly Material Rebar = Make(new Material { Id = 2, Tag = "A500", Type = MatType.ReSteelF, E = 200_000_000.0 }, RebarChars);

    static Material Make(Material m, Func<CalcType, MaterialChars> chars)
    {
        m.C = chars(CalcType.C);
        m.CL = chars(CalcType.CL);
        m.N = chars(CalcType.N);
        m.NL = chars(CalcType.NL);
        return m;
    }

    // ── Данные SCAD ─────────────────────────────────────────────────────────────────────────────

    static ScadSelectedBarSection Sec(double s1, double s2, double s3, double s4, double iwz = 0, double iwy = 0) =>
        new(s1, s2, s3, s4, iwz, iwy);

    /// <summary>
    /// Пластины 23 (0,43 / 0 / 0,81 / 0,27) и 55459; стержни 814 (4 × 4,14), 818 (сеч. 1 — 4,56 / 3,56 / 4,06 / 4,06,
    /// сеч. 2 — ошибка SCAD), 900 (все сечения — ошибка).
    /// </summary>
    static ScadSelectedRebarFile File() => new()
    {
        Plates = new Dictionary<int, ScadSelectedPlate>
        {
            [23] = new(23, 0.43, 0, 0.81, 0.27, 2.0, 3.0),
            [55459] = new(55459, 0.49, 1.68, 0.42, 1.58, null, null),
            [77] = new(77, null, 1, 1, 1, 0, 0),
        },
        Bars = new Dictionary<int, ScadSelectedBar>
        {
            [814] = new(814, [Sec(4.14, 4.14, 4.14, 4.14, 1.5, 0.7)]),
            [818] = new(818, [Sec(4.56, 3.56, 4.06, 4.06, 1, 2), null]),
            [900] = new(900, [null, null]),
        },
    };

    static ScadConcreteGroup Group(int num, double[] range, params int[] ids) =>
        new(num, "Г" + num, 1, range, "B25", "A500", "A240", false, [0.4, 0.3], ids);

    // ── Мозаика пластин ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PlateMosaic_ComponentsAndMissing()
    {
        var src = new ScadSelectedPlateRebarSource(File());

        Assert.Equal(0.43, src.Get("23", PlateRebarMosaicComponent.BottomX).Value);
        Assert.Equal(0.0, src.Get("23", PlateRebarMosaicComponent.TopX).Value);
        Assert.Equal(0.81, src.Get("23", PlateRebarMosaicComponent.BottomY).Value);
        Assert.Equal(0.27, src.Get("23", PlateRebarMosaicComponent.TopY).Value);
        Assert.Equal(3.0, src.Get("23", PlateRebarMosaicComponent.Transverse).Value);
        Assert.True(src.Get("55459", PlateRebarMosaicComponent.Transverse).IsMissing);
        Assert.True(src.Get("77", PlateRebarMosaicComponent.BottomX).IsMissing);
        Assert.True(src.Get("1", PlateRebarMosaicComponent.BottomX).IsMissing);
        Assert.True(src.Get("x", PlateRebarMosaicComponent.BottomX).IsMissing);
    }

    // ── Мозаика и эпюра стержней ────────────────────────────────────────────────────────────────

    [Fact]
    public void BarMosaic_ComponentsSectionsAndMissing()
    {
        var src = new ScadSelectedBarRebarSource(File());

        Assert.True(src.HasBars);
        Assert.Equal(4 * 4.14, src.Get("814", BarRebarComponent.LongitudinalSum).Value!.Value, 9);
        Assert.Equal(4.14, src.Get("814", BarRebarComponent.As3).Value);
        Assert.Equal(1.5, src.Get("814", BarRebarComponent.Asw1).Value);
        Assert.Equal(0.7, src.Get("814", BarRebarComponent.Asw2).Value);
        Assert.False(src.Supports(BarRebarComponent.Au1));
        Assert.False(src.Supports(BarRebarComponent.Percent));
        Assert.False(src.Supports(BarRebarComponent.Bottom));
        Assert.True(src.Get("814", BarRebarComponent.Au1).IsMissing);

        var sections = src.GetSections("818", BarRebarComponent.As1);
        Assert.Equal(2, sections.Count);
        Assert.Equal(4.56, sections[0].Value);
        Assert.True(sections[1].IsMissing);
        Assert.Equal(4.56, src.Get("818", BarRebarComponent.As1).Value);

        Assert.True(src.Get("900", BarRebarComponent.LongitudinalSum).IsMissing);
        Assert.True(src.Get("1", BarRebarComponent.LongitudinalSum).IsMissing);
        Assert.Empty(src.GetSections("1", BarRebarComponent.As1));
    }

    // ── Сечение пластины ────────────────────────────────────────────────────────────────────────

    const double TemplateCover = 0.035;

    static PlateSection Template(double h = 0.2) => new()
    {
        Tag = "Пл", H = h, NLayers = 40, PlateModel = "layered", ConcreteDiagramType = DiagrammType.L3,
        ConcreteMaterialId = 1, RebarMaterialId = 2,
        RebarLayers = [TemplateLayer(-(h / 2 - TemplateCover)), TemplateLayer(h / 2 - TemplateCover)],
    };

    static PlateRebarLayer TemplateLayer(double z) => new()
    {
        Name = "t", InputMode = "direct", Asx = 565e-6, Asy = 565e-6, Zsx = z, Zsy = z, DiameterX = 0.012, DiameterY = 0.012,
    };

    static FemCheckScopeElement Plate(int num, double? thickness = null) =>
        new(num, new FemElement { ElemTag = num.ToString(), ElemType = "shell", ThicknessM = thickness }, null);

    [Fact]
    public void PlateSection_CoversFromGroup_ZeroA3A4TakeA1A2()
    {
        // КЭ 23: h = 0,18, a1 = a2 = 30 мм, a3 = a4 = 0 → все слои на ∓(0,09 − 0,03) = ∓0,06.
        var groups = new ScadConcreteGroupIndex([Group(1, [0.03, 0.03, 0, 0], 23)]);
        var src = new ScadSelectedPlateSectionSource(Template(), File(), groups);

        var r = src.Resolve(Plate(23, 0.18));

        Assert.Null(r.Reason);
        Assert.Equal("SCAD", r.Label);
        Assert.Equal(0.18, r.Section!.H, 9);
        var bottom = r.Section.RebarLayers.Single(l => l.Zsx < 0);
        Assert.Equal(-0.06, bottom.Zsx, 9);
        Assert.Equal(-0.06, bottom.Zsy, 9);
        Assert.Equal(0.43e-4, bottom.Asx, 12);
        Assert.Equal(0.81e-4, bottom.Asy, 12);
        Assert.Equal(0.012, bottom.DiameterX, 9);
        var top = r.Section.RebarLayers.Single(l => l.Zsx > 0);
        Assert.Equal(0.06, top.Zsy, 9);
        Assert.Equal(0, top.Asx, 12);
        Assert.Equal(0.27e-4, top.Asy, 12);
    }

    [Fact]
    public void PlateSection_DifferentCovers_GoToOwnLayersAndDirections()
    {
        var groups = new ScadConcreteGroupIndex([Group(1, [0.03, 0.04, 0.05, 0.06], 55459)]);
        var src = new ScadSelectedPlateSectionSource(Template(), File(), groups);

        var s = src.Resolve(Plate(55459)).Section!;

        Assert.Equal(0.2, s.H, 9);
        var bottom = s.RebarLayers.Single(l => l.Zsx < 0);
        Assert.Equal(-(0.1 - 0.03), bottom.Zsx, 9);
        Assert.Equal(-(0.1 - 0.05), bottom.Zsy, 9);
        var top = s.RebarLayers.Single(l => l.Zsx > 0);
        Assert.Equal(0.1 - 0.04, top.Zsx, 9);
        Assert.Equal(0.1 - 0.06, top.Zsy, 9);
        Assert.Equal(1.68e-4, top.Asx, 12);
    }

    [Fact]
    public void PlateSection_WithoutGroup_TemplateCovers_AndWarning()
    {
        var groups = new ScadConcreteGroupIndex([Group(1, [0.03, 0.03, 0, 0], 23)]);
        var src = new ScadSelectedPlateSectionSource(Template(), File(), groups);

        var s = src.Resolve(Plate(55459, 0.25)).Section!;
        Assert.Equal(0.25, s.H, 9);
        Assert.Equal(-(0.125 - TemplateCover), s.RebarLayers.Single(l => l.Zsx < 0).Zsx, 9);

        var w = src.Warnings([Plate(23), Plate(55459)], Concrete, Rebar);
        Assert.Contains(w, x => x.Contains("не входят в ЖБ-группы"));
    }

    [Fact]
    public void PlateSection_Reasons()
    {
        var groups = new ScadConcreteGroupIndex([Group(1, [0.1, 0.1, 0, 0], 23)]);
        var src = new ScadSelectedPlateSectionSource(Template(), File(), groups);

        Assert.Contains("не помещаются", src.Resolve(Plate(23, 0.18)).Reason);
        Assert.Contains("нет в подборе", src.Resolve(Plate(5)).Reason);
        Assert.Contains("не выполнен", src.Resolve(Plate(77)).Reason);
    }

    [Fact]
    public void PlateSection_ClassMismatch_IsWarning()
    {
        var g = Group(1, [0.03, 0.03, 0, 0], 23) with { ConcreteClass = "B30" };
        var src = new ScadSelectedPlateSectionSource(Template(), File(), new ScadConcreteGroupIndex([g]));

        var w = src.Warnings([Plate(23)], Concrete, Rebar);
        Assert.Contains(w, x => x.Contains("B30") && x.Contains("SCAD"));
        Assert.DoesNotContain(w, x => x.Contains("арматуры в подборе"));
    }

    // ── Стержни ─────────────────────────────────────────────────────────────────────────────────

    static readonly LiraBarProfile Profile60 = new(3, 0.6, 0.6, Concrete, Rebar);

    [Fact]
    public void BarLayout_Element814_60x60()
    {
        var (bars, reason) = ScadBarSectionBuilder.SelectedLayout(Sec(4.14, 4.14, 4.14, 4.14), Profile60, 0.04, 0.04);

        Assert.Null(reason);
        Assert.Equal(4 * 4.14, bars!.Sum(b => b.AreaM2) * 1e4, 9);
        var s1 = bars.Where(b => Math.Abs(b.Y + 0.26) < 1e-9).ToList();
        var s2 = bars.Where(b => Math.Abs(b.Y - 0.26) < 1e-9).ToList();
        Assert.Equal(9, s1.Count);
        Assert.Equal(9, s2.Count);
        Assert.Equal(-0.26, s1.Min(b => b.X), 9);
        Assert.Equal(0.26, s1.Max(b => b.X), 9);
        Assert.Equal(4.14, s1.Sum(b => b.AreaM2) * 1e4, 9);

        var s3 = bars.Where(b => Math.Abs(b.X + 0.26) < 1e-9 && Math.Abs(b.Y) < 0.26 - 1e-9).ToList();
        var s4 = bars.Where(b => Math.Abs(b.X - 0.26) < 1e-9 && Math.Abs(b.Y) < 0.26 - 1e-9).ToList();
        Assert.Equal(9, s3.Count);
        Assert.Equal(9, s4.Count);
        Assert.Equal(4.14, s3.Sum(b => b.AreaM2) * 1e4, 9);
        // В углах только точки S1/S2.
        Assert.Single(bars, b => Math.Abs(b.X + 0.26) < 1e-9 && Math.Abs(b.Y + 0.26) < 1e-9);
    }

    [Fact]
    public void BarLayout_Unsymmetric_Element818_AndUnequalCovers()
    {
        var (bars, _) = ScadBarSectionBuilder.SelectedLayout(Sec(4.56, 3.56, 4.06, 4.06), Profile60, 0.04, 0.05);

        Assert.Equal(4.56 + 3.56 + 4.06 + 4.06, bars!.Sum(b => b.AreaM2) * 1e4, 9);
        Assert.Equal(4.56, bars.Where(b => Math.Abs(b.Y + 0.26) < 1e-9).Sum(b => b.AreaM2) * 1e4, 9);
        Assert.Equal(3.56, bars.Where(b => Math.Abs(b.Y - 0.25) < 1e-9).Sum(b => b.AreaM2) * 1e4, 9);
        // Боковой отступ — min(a1, a2).
        Assert.Equal(-0.26, bars.Min(b => b.X), 9);
    }

    [Fact]
    public void BarLayout_Reasons()
    {
        Assert.Contains("не помещаются",
            ScadBarSectionBuilder.SelectedLayout(Sec(1, 1, 1, 1), Profile60, 0.3, 0.3).Reason);
        Assert.Contains("не выполнен",
            ScadBarSectionBuilder.SelectedLayout(new ScadSelectedBarSection(1, null, 1, 1, 0, 0), Profile60, 0.04, 0.04).Reason);
    }

    static LiraStiffnessRecord S0(int id, string text) =>
        new(id, ScadStiffnessParams.ScadKindCode, "Колонна", text, 1);

    static FemCheckScopeElement Bar(int num, int stiffness = 3) =>
        new(num, new FemElement { ElemTag = num.ToString(), ElemType = "beam", StiffnessNum = stiffness }, null);

    static ScadBarSectionContext Context(ScadConcreteGroupIndex? groups) =>
        new(new Dictionary<int, LiraStiffnessRecord>
            {
                [3] = S0(3, "S0 3e+10 0.6 0.6 NU 0.2"),
                [4] = S0(4, "STZ 1 2 3"),
            },
            File(), groups, c => c == "B25" ? Concrete : null, r => r == "A500" ? Rebar : null);

    [Fact]
    public void BarSource_PerSection_Envelope_AndReasons()
    {
        var groups = new ScadConcreteGroupIndex([Group(1, [0.04, 0.04, 0, 0], 814, 818, 900, 5)]);
        var src = new ScadSelectedBarSectionSource(Context(groups));

        Assert.True(src.PerSection);
        Assert.Null(src.MissingReason(Bar(814)));
        var r = src.Resolve(Bar(814), 1);
        Assert.Null(r.Reason);
        Assert.Equal("SCAD 600×600 э.814 с1", r.Section!.Tag);
        Assert.Equal("SCAD", r.Label);
        Assert.Same(r, src.Resolve(Bar(814), 1));
        Assert.Equal("SCAD 600×600 э.814", src.Resolve(Bar(814), null).Section!.Tag);

        Assert.Null(src.Resolve(Bar(818), 1).Reason);
        Assert.Contains("сечении 2 не выполнен", src.Resolve(Bar(818), 2).Reason);
        Assert.Contains("сечении 2 не выполнен", src.Resolve(Bar(818), null).Reason);
        Assert.Contains("нет сечения 3", src.Resolve(Bar(818), 3).Reason);
        Assert.Contains("не выполнен", src.Resolve(Bar(900), null).Reason);
        Assert.Contains("нет в подборе", src.Resolve(Bar(5), 1).Reason);
        Assert.Contains("форма сечения", new ScadSelectedBarSectionSource(Context(groups)).Resolve(Bar(814, stiffness: 4), 1).Reason);
    }

    [Fact]
    public void BarSource_WithoutGroups_CoversUnknown()
    {
        var src = new ScadSelectedBarSectionSource(Context(null));
        Assert.Contains("ЖБ-групп", src.Resolve(Bar(814), 1).Reason);
    }
}
