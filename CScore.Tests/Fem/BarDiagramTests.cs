using CScore.Fem;
using CScore.Import;
using CScore.PlateRebar;
using Xunit;

namespace CScore.Tests.Fem;

/// <summary>Цепочки стержневых КЭ и эпюры вдоль них: импортированные усилия и подобранная арматура.</summary>
public class BarDiagramTests
{
    static BarChainBar Bar(int num, int ni, int nj, (double, double, double) pi, (double, double, double) pj) =>
        new(num, ni, nj, pi, pj);

    static LoadItem Row(int elem, int? section, double mx = 0, double n = 0) =>
        new() { SourceElementNum = elem, SourceSectionNum = section, Mx = mx, N = n };

    /// <summary>Балка из трёх КЭ вдоль X; средний КЭ задан от правого узла к левому.</summary>
    static IReadOnlyList<BarChainBar> Beam() =>
    [
        Bar(12, 3, 4, (3, 0, 0), (6, 0, 0)),
        Bar(10, 1, 2, (0, 0, 0), (1, 0, 0)),
        Bar(11, 3, 2, (3, 0, 0), (1, 0, 0)),
    ];

    [Fact]
    public void Chain_StraightBeam_OrderedFromLeftWithReversedElement()
    {
        var chain = Assert.Single(BarChains.Build(Beam()));

        Assert.Equal(6, chain.Length, 9);
        Assert.Equal([10, 11, 12], chain.Elements.Select(e => e.ElemNum));
        Assert.Equal((0, 1), (chain.Elements[0].SStart, chain.Elements[0].SEnd));
        // КЭ 11 направлен против обхода: его начальный узел — дальний.
        Assert.Equal((3, 1), (chain.Elements[1].SStart, chain.Elements[1].SEnd));
        Assert.Equal((3, 6), (chain.Elements[2].SStart, chain.Elements[2].SEnd));
    }

    [Fact]
    public void Chain_BreaksAtBranchAndAtKink()
    {
        // Колонна 1 (снизу вверх) и ригель 2 сходятся под прямым углом; к ригелю в середине примыкает балка 4.
        var chains = BarChains.Build(
        [
            Bar(1, 1, 2, (0, 0, 0), (0, 0, 3)),
            Bar(2, 2, 3, (0, 0, 3), (2, 0, 3)),
            Bar(3, 3, 4, (2, 0, 3), (4, 0, 3)),
            Bar(4, 3, 5, (2, 0, 3), (2, 5, 3)),
        ]);

        Assert.Equal([[1], [2], [3], [4]], chains.Select(c => c.Elements.Select(e => e.ElemNum).ToArray()));
    }

    [Fact]
    public void Chain_Column_GoesUpward()
    {
        var chain = Assert.Single(BarChains.Build(
        [
            Bar(2, 3, 2, (0, 0, 6), (0, 0, 3)),
            Bar(1, 2, 1, (0, 0, 3), (0, 0, 0)),
        ]));

        Assert.Equal([1, 2], chain.Elements.Select(e => e.ElemNum));
        Assert.Equal((3, 0), (chain.Elements[0].SStart, chain.Elements[0].SEnd));
    }

    [Fact]
    public void Chain_ZeroLengthElement_IsSkipped()
    {
        var chains = BarChains.Build([Bar(1, 1, 2, (0, 0, 0), (0, 0, 0)), Bar(2, 2, 3, (0, 0, 0), (1, 0, 0))]);

        Assert.Equal([2], Assert.Single(chains).Elements.Select(e => e.ElemNum));
    }

    [Fact]
    public void Forces_SectionsPlacedAlongElement_ReversedElementMirrored()
    {
        var chain = Assert.Single(BarChains.Build(Beam()));
        var set = new ForceSet
        {
            Kind = "bar",
            Items =
            [
                Row(10, 1, mx: 0), Row(10, 2, mx: 10),
                Row(11, 1, mx: 30), Row(11, 2, mx: 20), Row(11, 3, mx: 10),
                Row(12, 1, mx: 30), Row(12, 2, mx: 0),
            ],
        };

        var series = BarDiagram.Forces(chain, set, BarForceComponent.Mx);

        Assert.Empty(series.Lower);
        Assert.Equal(
        [
            new BarDiagramSegment(0, 1, 0, 10),
            // Сечение 1 КЭ 11 — у его начального узла, т.е. при s = 3.
            new BarDiagramSegment(1, 2, 10, 20),
            new BarDiagramSegment(2, 3, 20, 30),
            new BarDiagramSegment(3, 6, 30, 0),
        ], series.Upper);
        Assert.Equal([0, 1, 1, 2, 3, 3, 6], series.Points.Select(p => p.S));
        Assert.Equal([1, 2, 3, 2, 1, 1, 2], series.Points.Select(p => p.SectionNum));
    }

    [Fact]
    public void Forces_SeveralRowsInSection_GiveEnvelope()
    {
        var chain = Assert.Single(BarChains.Build([Bar(5, 1, 2, (0, 0, 0), (4, 0, 0))]));
        var set = new ForceSet
        {
            Kind = "bar",
            Items = [Row(5, 1, mx: -5), Row(5, 1, mx: 8), Row(5, 2, mx: 3), Row(5, 2, mx: -1)],
        };

        var series = BarDiagram.Forces(chain, set, BarForceComponent.Mx);

        Assert.Equal([new BarDiagramSegment(0, 4, 8, 3)], series.Upper);
        Assert.Equal([new BarDiagramSegment(0, 4, -5, -1)], series.Lower);
        Assert.Equal((8.0, -5.0), (series.Points[0].Max, series.Points[0].Min));
    }

    [Fact]
    public void Forces_RowsWithoutSectionNumber_ConstantAlongElement()
    {
        var chain = Assert.Single(BarChains.Build([Bar(5, 1, 2, (0, 0, 0), (4, 0, 0))]));
        var set = new ForceSet { Kind = "bar", Items = [Row(5, null, n: -100), Row(99, 1, n: 7)] };

        var series = BarDiagram.Forces(chain, set, BarForceComponent.N);

        Assert.Equal([new BarDiagramSegment(0, 4, -100, -100)], series.Upper);
        Assert.Null(Assert.Single(series.Points).SectionNum);
    }

    [Fact]
    public void Forces_ElementWithoutRows_LeavesGap()
    {
        var chain = Assert.Single(BarChains.Build(Beam()));
        var set = new ForceSet { Kind = "bar", Items = [Row(10, 1, mx: 1), Row(10, 2, mx: 2), Row(12, 1, mx: 3), Row(12, 2, mx: 4)] };

        var series = BarDiagram.Forces(chain, set, BarForceComponent.Mx);

        Assert.Equal([new BarDiagramSegment(0, 1, 1, 2), new BarDiagramSegment(3, 6, 3, 4)], series.Upper);
    }

    sealed class Source(Dictionary<string, PlateRebarValue[]> sections) : IBarRebarFieldSource
    {
        public bool Supports(BarRebarComponent component) => true;

        public PlateRebarValue Get(string elemTag, BarRebarComponent component) => PlateRebarValue.Missing;

        public IReadOnlyList<PlateRebarValue> GetSections(string elemTag, BarRebarComponent component) =>
            sections.TryGetValue(elemTag, out var s) ? s : [];
    }

    [Fact]
    public void Rebar_StepsSplitMidwayBetweenSections_FailedSectionLeftOut()
    {
        var chain = Assert.Single(BarChains.Build(
        [
            Bar(1, 1, 2, (0, 0, 0), (4, 0, 0)),
            Bar(2, 2, 3, (4, 0, 0), (6, 0, 0)),
        ]));
        var source = new Source(new()
        {
            ["1"] = [PlateRebarValue.Of(2), PlateRebarValue.Of(5), PlateRebarValue.Of(3)],
            ["2"] = [PlateRebarValue.Failure(274), PlateRebarValue.Of(7)],
        });

        var series = BarDiagram.Rebar(chain, source, BarRebarComponent.As1);

        Assert.Equal(
        [
            new BarDiagramSegment(0, 1, 2, 2),
            new BarDiagramSegment(1, 3, 5, 5),
            new BarDiagramSegment(3, 4, 3, 3),
            new BarDiagramSegment(5, 6, 7, 7),
        ], series.Upper);
        var failed = series.Points.Single(p => p.FailureCode != null);
        Assert.Equal((2, 1, 4.0, 274), (failed.ElemNum, failed.SectionNum, failed.S, failed.FailureCode));
    }

    [Fact]
    public void ForceProfiles_AggregateBySectionRows_SingleSectionIsConstant()
    {
        var set = new ForceSet
        {
            Kind = "bar",
            Items = [Row(5, 1, mx: -5), Row(5, 1, mx: 3), Row(5, 3, mx: 8), Row(6, null, mx: 2), Row(null, 1, mx: 9)],
        };

        var maxAbs = BarDiagram.ForceProfiles(set, BarForceComponent.Mx, ForceRowAggregate.MaxAbs);
        var max = BarDiagram.ForceProfiles(set, BarForceComponent.Mx, ForceRowAggregate.Max);

        // Сечения 1 и 3 из трёх: концы КЭ.
        Assert.Equal([(0.0, -5.0), (1.0, 8.0)], maxAbs[5]);
        Assert.Equal([(0.0, 3.0), (1.0, 8.0)], max[5]);
        Assert.Equal([(0.0, 2.0), (1.0, 2.0)], maxAbs[6]);
        Assert.Equal([5, 6], maxAbs.Keys.Order());
    }

    static LoadItem Row(int? elem, int? section, double mx) =>
        new() { SourceElementNum = elem, SourceSectionNum = section, Mx = mx };

    [Fact]
    public void RebarProfile_StepsWithoutFailedSections()
    {
        var profile = BarDiagram.RebarProfile([PlateRebarValue.Of(2), PlateRebarValue.Failure(274), PlateRebarValue.Of(3)]);

        Assert.Equal([(0.0, 2.0), (0.25, 2.0), (0.75, 3.0), (1.0, 3.0)], profile);
    }

    [Fact]
    public void LocalAxes_HorizontalBarZUp_VerticalBarZAgainstGlobalX()
    {
        var beam = BarDiagramGeometry.LocalAxes((0, 0, 0), (0, 5, 0))!.Value;
        Assert.Equal((0, 0, 1), beam.Z1);
        Assert.Equal((-1, 0, 0), beam.Y1);

        var column = BarDiagramGeometry.LocalAxes((0, 0, 0), (0, 0, 3))!.Value;
        Assert.Equal((-1, 0, 0), column.Z1);
        Assert.Equal((0, 1, 0), column.Y1);

        // Наклонный стержень: Z1 перпендикулярна оси и смотрит вверх.
        var inclined = BarDiagramGeometry.LocalAxes((0, 0, 0), (3, 0, 4))!.Value;
        Assert.Equal(-0.8, inclined.Z1.X, 9);
        Assert.Equal(0.6, inclined.Z1.Z, 9);
        Assert.Null(BarDiagramGeometry.LocalAxes((1, 1, 1), (1, 1, 1)));
    }

    [Fact]
    public void AddBar_OrdinatesAlongZ1_SignChangeSplitsOutline()
    {
        var positive = new List<BarDiagramLine>();
        var negative = new List<BarDiagramLine>();

        BarDiagramGeometry.AddBar(positive, negative, (0, 0, 0), (4, 0, 0), [(0, 10), (1, -30)], BarDiagramPlane.Z1, scale: 0.1);

        // Ордината +1 м в начале, −3 м в конце; ноль — в четверти длины.
        Assert.Equal(
        [
            new BarDiagramLine((0, 0, 0), (0, 0, 1)),
            new BarDiagramLine((0, 0, 1), (1, 0, 0)),
        ], positive);
        Assert.Equal(
        [
            new BarDiagramLine((1, 0, 0), (4, 0, -3)),
            new BarDiagramLine((4, 0, 0), (4, 0, -3)),
        ], negative);
    }

    [Fact]
    public void AddBar_PlaneY1_ZeroValuesNotDrawn()
    {
        var positive = new List<BarDiagramLine>();
        var negative = new List<BarDiagramLine>();

        BarDiagramGeometry.AddBar(positive, negative, (0, 0, 0), (2, 0, 0), [(0, 0), (0.5, 0), (1, 4)], BarDiagramPlane.Y1, scale: 0.5);

        // У стержня вдоль X ось Y1 совпадает с глобальной Y.
        Assert.Equal(
        [
            new BarDiagramLine((1, 0, 0), (2, 2, 0)),
            new BarDiagramLine((2, 0, 0), (2, 2, 0)),
        ], positive);
        Assert.Empty(negative);
    }

    [Fact]
    public void LiraAspSource_BarComponents_EnvelopeSectionsAndFailure()
    {
        var asp = LiraAspReader.Read(Path.Combine(AppContext.BaseDirectory, "Import", "Fixtures", "asp-scheme-1lin.asp"));
        var source = new LiraAspBarRebarSource(asp);

        Assert.True(source.HasBars);
        // Колонна (КЭ 1): AS1..AS4 по 6,39 в огибающей; в сечениях 1,44 и 6,39.
        Assert.Equal(4 * 6.39, source.Get("1", BarRebarComponent.LongitudinalSum).Value!.Value, 1);
        Assert.Equal([1.44, 6.39], source.GetSections("1", BarRebarComponent.As1).Select(v => Math.Round(v.Value!.Value, 2)));
        Assert.Equal(0.71, source.Get("1", BarRebarComponent.Percent).Value!.Value, 2);
        // Балка (КЭ 24): несимметричное армирование.
        Assert.Equal(0.96, source.Get("24", BarRebarComponent.Au3).Value!.Value, 2);
        Assert.Equal(8.24, source.Get("24", BarRebarComponent.Asw1).Value!.Value, 2);
        // КЭ 146: подбор поперечной арматуры не выполнен (код 274), продольная подобрана.
        Assert.Equal(274, source.Get("146", BarRebarComponent.Asw1).FailureCode);
        Assert.All(source.GetSections("146", BarRebarComponent.Asw1), v => Assert.Equal(274, v.FailureCode));
        Assert.Null(source.Get("146", BarRebarComponent.LongitudinalSum).FailureCode);
        Assert.True(source.Get("100500", BarRebarComponent.As1).IsMissing);
        Assert.Empty(source.GetSections("100500", BarRebarComponent.As1));
    }

    static FemCheckRowResult Util(int elem, int? section, double? u, string source = "selected", bool notChecked = false) =>
        new(elem, section, source, u, u <= 1, notChecked);

    [Fact]
    public void Utilization_StepsBySection_MaxOfRows_OnlyChosenSource()
    {
        var chain = Assert.Single(BarChains.Build([Bar(7, 1, 2, (0, 0, 0), (4, 0, 0))]));
        FemCheckRowResult[] rows =
        [
            Util(7, 1, 0.4), Util(7, 1, 0.9), Util(7, 2, 1.3),
            Util(7, 1, 5, source: "section"),
        ];

        var series = BarDiagram.Utilization(chain, rows, "selected");

        Assert.Equal([new BarDiagramSegment(0, 2, 0.9, 0.9), new BarDiagramSegment(2, 4, 1.3, 1.3)], series.Upper);
        Assert.Empty(series.Lower);
        Assert.Equal([(1, 0.0, 0.9), (2, 4.0, 1.3)], series.Points.Select(p => (p.SectionNum!.Value, p.S, p.Max!.Value)));

        var profiles = BarDiagram.UtilizationProfiles(rows, "selected");
        Assert.Equal([(0.0, 0.9), (0.5, 0.9), (0.5, 1.3), (1.0, 1.3)], profiles[7]);
    }

    [Fact]
    public void Utilization_NotCheckedSection_HasEmptyPoint_RowsWithoutSection_WholeElement()
    {
        var chain = Assert.Single(BarChains.Build([Bar(7, 1, 2, (0, 0, 0), (4, 0, 0)), Bar(8, 2, 3, (4, 0, 0), (6, 0, 0))]));
        FemCheckRowResult[] rows = [Util(7, 1, 0.5), Util(7, 2, null, notChecked: true), Util(8, null, 0.7)];

        var series = BarDiagram.Utilization(chain, rows, "selected");

        Assert.Equal([new BarDiagramSegment(0, 2, 0.5, 0.5), new BarDiagramSegment(4, 6, 0.7, 0.7)], series.Upper);
        Assert.Null(series.Points.Single(p => p.ElemNum == 7 && p.SectionNum == 2).Max);
        Assert.Equal(5, series.Points.Single(p => p.ElemNum == 8).S, 9);
        Assert.False(BarDiagram.UtilizationProfiles(rows, "selected").ContainsKey(9));
    }

    [Fact]
    public void ParseRows_ReadsElementSectionSourceAndUtilization()
    {
        const string json = """
            { "rows": [
              { "elemNum": 7, "sectionNum": 2, "rebarSource": "selected", "utilization": 1.25, "passed": false, "notChecked": false },
              { "elemNum": 8, "sectionNum": null, "rebarSource": "", "utilization": null, "passed": false, "notChecked": true },
              { "elemNum": null, "label": "ручная", "utilization": 0.3 } ] }
            """;

        var rows = FemCheckElementResults.ParseRows(json);

        Assert.Equal(2, rows.Count);
        Assert.Equal(new FemCheckRowResult(7, 2, "selected", 1.25, false, false), rows[0]);
        Assert.Equal(new FemCheckRowResult(8, null, "", null, false, true), rows[1]);
        Assert.Empty(FemCheckElementResults.ParseRows("не json"));
    }
}
