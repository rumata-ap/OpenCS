using System.Text.Json;
using CScore.Fem;
using CScore.Import;
using Xunit;

namespace CScore.Tests.Import;

/// <summary>Сечение стержневого КЭ по данным ЛИРЫ: размеры из жёсткости, арматура из подбора (ASP) и ТЗА (RBT).</summary>
public class LiraBarSectionSourcesTests
{
    const double B = 0.3, H = 0.5;

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

    static readonly LiraBarProfile Profile = new(7, B, H, Concrete, Rebar);

    static LiraAspBarAreas Areas(double au1 = 0, double au2 = 0, double au3 = 0, double au4 = 0,
                                 double as1 = 0, double as2 = 0, double as3 = 0, double as4 = 0, double asw1 = 0) =>
        new(au1, au2, au3, au4, as1, as2, as3, as4, 0, asw1, 0);

    static LiraStiffnessRecord BarStiffness(int num = 7) =>
        new(num, LiraStiffnessParams.BarRectKind, "Брус 30 X 50", "Ro:2.5 E:3e+06 B:30 H:50 BAR_END", 0.01);

    static FemCheckScopeElement Elem(int num, int? stiffness = 7, string? typeIds = null) =>
        new(num, new FemElement { ElemTag = num.ToString(), ElemType = "beam", StiffnessNum = stiffness, ReinforcementTypeIds = typeIds }, null);

    static LiraBarSectionContext Context(LiraAspFile? asp, Func<string, Material?>? concrete = null) =>
        new(new Dictionary<int, LiraStiffnessRecord> { [7] = BarStiffness() }, asp,
            concrete ?? (c => c == "B25" ? Concrete : null), r => r == "A500" ? Rebar : null);

    /// <summary>КЭ 5: два сечения — внизу 4 см² в углах снизу, во втором сечении — 8 см² по нижней грани.</summary>
    static LiraAspFile Asp(LiraAspBarAreas? second = null) => new()
    {
        Bars = new Dictionary<int, LiraAspBar>
        {
            [5] = new()
            {
                ElementId = 5, ConcreteClass = "B25", RebarClass = "A500", Covers = (4, 5, 3),
                Envelope = Areas(au1: 2, au2: 2, as1: 8),
                Sections =
                [
                    new LiraAspBarSection(Areas(au1: 2, au2: 2), Areas()),
                    new LiraAspBarSection(second ?? Areas(as1: 8), Areas()),
                ],
            },
        },
    };

    static double TotalCm2(CrossSection s) =>
        s.Areas.Where(a => a.Category == AreaCategory.RebarGroup).SelectMany(a => a.Fibers).Sum(f => f.Area) * 1e4;

    // ── Жёсткость ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void StiffnessParams_BarRectAndPlateThickness()
    {
        Assert.Equal(new LiraBarRect(0.3, 0.5), LiraStiffnessParams.BarRect(BarStiffness()));
        Assert.Null(LiraStiffnessParams.BarRect(BarStiffness() with { KindCode = 11 }));
        Assert.Null(LiraStiffnessParams.BarRect(BarStiffness() with { Params = "Ro:2.5 E:3e+06 B:30 BAR_END" }));

        var plate = new LiraStiffnessRecord(2, 36, "Пластина H 20", "Ro:2.5 E:3e+06 V:0.2 H:20 PLATE_END", 0.01);
        Assert.Equal(0.2, LiraStiffnessParams.PlateThicknessM(plate)!.Value, 9);
        Assert.Null(LiraStiffnessParams.BarRect(plate));
        Assert.Equal(0.2, LiraStiffnessParams.Value(plate.Params, "V"));
        Assert.Null(LiraStiffnessParams.Value(plate.Params, "B"));
    }

    // ── Раскладка подбора ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void SelectedLayout_CornersAndFaces_InSectionAxes()
    {
        var areas = Areas(au1: 1, au2: 2, au3: 3, au4: 4, as1: 9, as3: 0.9);
        var (bars, reason) = LiraBarSectionBuilder.SelectedLayout(areas, Profile, (4, 5, 3));

        Assert.Null(reason);
        Assert.Equal(19.9, bars!.Sum(b => b.AreaM2) * 1e4, 9);
        // Углы: a1 = 4 см снизу, a2 = 5 см сверху, a3 = 3 см сбоку; x — вдоль Y1 (ширина), y — вдоль Z1 (высота).
        Assert.Contains(bars, b => Near(b, -B / 2 + 0.03, -H / 2 + 0.04, 1e-4));
        Assert.Contains(bars, b => Near(b, B / 2 - 0.03, -H / 2 + 0.04, 2e-4));
        Assert.Contains(bars, b => Near(b, -B / 2 + 0.03, H / 2 - 0.05, 3e-4));
        Assert.Contains(bars, b => Near(b, B / 2 - 0.03, H / 2 - 0.05, 4e-4));
        // AS1 — между углами по нижней грани, AS3 — по левой.
        var bottom = bars.Where(b => Math.Abs(b.Y - (-H / 2 + 0.04)) < 1e-9 && b.X > -B / 2 + 0.031 && b.X < B / 2 - 0.031).ToList();
        Assert.Equal(LiraBarSectionBuilder.DistributedPoints, bottom.Count);
        Assert.Equal(9, bottom.Sum(b => b.AreaM2) * 1e4, 9);
        Assert.All(bars.Where(b => b.Y > -H / 2 + 0.041 && b.Y < H / 2 - 0.051), b => Assert.Equal(-B / 2 + 0.03, b.X, 9));
    }

    static bool Near(LiraBarPoint b, double x, double y, double area) =>
        Math.Abs(b.X - x) < 1e-9 && Math.Abs(b.Y - y) < 1e-9 && Math.Abs(b.AreaM2 - area) < 1e-12;

    [Fact]
    public void SelectedLayout_FailureCodeAndOversizeCovers_GiveReason()
    {
        var (bars, reason) = LiraBarSectionBuilder.SelectedLayout(Areas(au1: 1, asw1: -274), Profile, (4, 5, 3));
        Assert.Null(bars);
        Assert.Contains("274", reason);

        (bars, reason) = LiraBarSectionBuilder.SelectedLayout(Areas(au1: 1), Profile, (30, 25, 3));
        Assert.Null(bars);
        Assert.Contains("не помещаются", reason);
    }

    // ── Раскладка ТЗА ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AssignedLayout_RowsAtBottomAndTop()
    {
        var bottom = new LiraBarReinforcementType
        {
            Id = 1, Face = LiraBarRebarFace.Bottom, Count = 3, DiameterMm = 16, BarAreaCm2 = 2.011,
            A = 4, ASide = 4, Binding = LiraRebarBinding.Centroid,
        };
        var top = new LiraBarReinforcementType
        {
            Id = 2, Face = LiraBarRebarFace.Top, Count = 2, DiameterMm = 12, BarAreaCm2 = 1.131,
            A = 3, ASide = 3, Binding = LiraRebarBinding.Cover,
        };

        var (bars, reason) = LiraBarSectionBuilder.AssignedLayout([bottom, top], Profile);

        Assert.Null(reason);
        Assert.Equal(5, bars!.Count);
        var low = bars.Where(b => b.Y < 0).OrderBy(b => b.X).ToList();
        Assert.Equal([-0.11, 0, 0.11], low.Select(b => Math.Round(b.X, 9)));
        Assert.All(low, b => Assert.Equal(-H / 2 + 0.04, b.Y, 9));
        // Защитный слой — до грани стержня: центр на a + d/2.
        var high = bars.Where(b => b.Y > 0).OrderBy(b => b.X).ToList();
        Assert.All(high, b => Assert.Equal(H / 2 - 0.036, b.Y, 9));
        Assert.Equal(-B / 2 + 0.036, high[0].X, 9);
    }

    // ── Источник ASP ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Selected_EachSectionOfElementGetsItsOwnRebar()
    {
        var source = new LiraSelectedBarSectionSource(Context(Asp()));
        var e = Elem(5);

        Assert.True(source.PerSection);
        Assert.Null(source.MissingReason(e));
        var s1 = source.Resolve(e, 1);
        var s2 = source.Resolve(e, 2);
        Assert.Equal("ASP", s1.Label);
        Assert.Equal(4, TotalCm2(s1.Section!), 9);
        Assert.Equal(8, TotalCm2(s2.Section!), 9);
        Assert.Contains("с2", s2.Section!.Tag);
        Assert.Same(s1, source.Resolve(e, 1));
        // Строка без номера сечения — огибающая ЛИРЫ.
        Assert.Equal(12, TotalCm2(source.Resolve(e, null).Section!), 9);
        Assert.Contains("нет сечения 3", source.Resolve(e, 3).Reason);

        var concrete = s1.Section!.Areas.Single(a => a.Category == AreaCategory.Region);
        Assert.Same(Concrete, concrete.Material);
    }

    [Fact]
    public void Selected_FailedSection_IsMissingAlsoForWholeElement()
    {
        var source = new LiraSelectedBarSectionSource(Context(Asp(second: Areas(as1: 8, asw1: -274))));
        var e = Elem(5);

        Assert.NotNull(source.Resolve(e, 1).Section);
        Assert.Contains("274", source.Resolve(e, 2).Reason);
        // Огибающая кодов ошибок не содержит — у строки без сечения ошибка берётся из сечений.
        Assert.Contains("274", source.Resolve(e, null).Reason);
    }

    [Fact]
    public void Selected_MissingData_GivesReasons()
    {
        var source = new LiraSelectedBarSectionSource(Context(Asp()));
        Assert.Equal("КЭ нет в файле ASP", source.MissingReason(Elem(6)));
        Assert.Contains("нет номера жёсткости", source.MissingReason(Elem(5, stiffness: null)));
        Assert.Contains("жёсткости 8 нет", source.MissingReason(Elem(5, stiffness: 8)));

        var noConcrete = new LiraSelectedBarSectionSource(Context(Asp(), concrete: _ => null));
        Assert.Contains("нет бетона B25", noConcrete.MissingReason(Elem(5)));
    }

    // ── Источник ТЗА ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Assigned_SumsTypesOfElement_AndReportsUnknownTypes()
    {
        var rbt = new LiraRbtFile
        {
            BarTypes = new Dictionary<int, LiraBarReinforcementType>
            {
                [1] = new() { Id = 1, Face = LiraBarRebarFace.Bottom, Count = 2, DiameterMm = 16, BarAreaCm2 = 2, A = 4, ASide = 4, Binding = LiraRebarBinding.Centroid },
                [2] = new() { Id = 2, Face = LiraBarRebarFace.Top, Count = 2, DiameterMm = 12, BarAreaCm2 = 1, A = 4, ASide = 4, Binding = LiraRebarBinding.Centroid },
            },
        };
        var source = new LiraAssignedBarSectionSource(Context(Asp()), rbt);

        var ok = source.Resolve(Elem(5, typeIds: "1 2"), 1);
        Assert.Equal("ТЗА 1 2", ok.Label);
        Assert.Equal(6, TotalCm2(ok.Section!), 9);
        Assert.Contains("ТЗА 3", source.MissingReason(Elem(5, typeIds: "1 3")));
        Assert.Equal("КЭ не назначены ТЗА", source.MissingReason(Elem(5)));
    }

    // ── Раннер: сечение по номеру сечения КЭ ───────────────────────────────────────────────────

    [Fact]
    public void RunPerElement_UsesSectionOfRowSectionNumber()
    {
        var scope = new FemCheckScope([], [Elem(5)], RefersToMeshElements: true);
        var check = new FemCheck { NormCode = "rc_check", Tag = "проверка" };
        var fs = new ForceSet
        {
            Id = 1, Tag = "РСУ",
            Items =
            [
                new LoadItem { Label = "э.5 с1", N = 0.3, SourceElementNum = 5, SourceSectionNum = 1 },
                new LoadItem { Label = "э.5 с2", N = 0.6, SourceElementNum = 5, SourceSectionNum = 2 },
            ],
        };
        var inputs = new FemPerElementInputs
        {
            BarSectionById = _ => null,
            BarSources = [new LiraSelectedBarSectionSource(Context(Asp()))],
            BarSelectedAsCm2 = new Dictionary<int, double> { [5] = 12 },
        };
        var seen = new Dictionary<string, double>();

        var result = FemCheckRunner.RunPerElement(check, new FemMemberGroup { Tag = "Г" }, scope, [fs], inputs,
            (_, section, item) =>
            {
                seen[item.Label] = TotalCm2(section);
                return new CalcResult { Status = "ok", DataJson = JsonSerializer.Serialize(new { utilization = item.N }) };
            });

        Assert.Equal("ok", result.Status);
        Assert.Equal(4, seen["э.5 с1"], 9);
        Assert.Equal(8, seen["э.5 с2"], 9);
        using var doc = JsonDocument.Parse(result.DataJson);
        var root = doc.RootElement;
        Assert.Equal(FemCheckRebarSource.Selected, root.GetProperty("rebarSources")[0].GetString());
        var e5 = root.GetProperty("elements").EnumerateArray().Single();
        Assert.Equal(2, e5.GetProperty("rows").GetInt32());
        Assert.Equal(0.6, e5.GetProperty("utilMax").GetDouble(), 9);
        // Принятая площадь у подбора — наибольшая по сечениям; с самим подбором не сравнивается.
        Assert.Equal(8, e5.GetProperty("asProvided").GetDouble(), 9);
    }
}
