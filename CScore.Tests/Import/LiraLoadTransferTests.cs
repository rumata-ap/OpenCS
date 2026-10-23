using CScore.Fem;
using CScore.Fem.Loads;
using CScore.Import;
using Xunit;

namespace CScore.Tests.Import;

/// <summary>Перенос нагрузок из текстового файла ЛИРЫ (документы 6 и 7) в сеточный уровень схемы (срез 4 плана 4б ЛИРЫ + 4ж).</summary>
public sealed class LiraLoadTransferTests
{
    const double TonToKn = 9.80665;
    const double T = TonToKn * 1e3; // Н в тонне-силе

    static readonly HashSet<string> Nodes = ["1", "2", "3", "4"];

    static readonly Dictionary<string, string> Types = new() { ["1"] = "beam", ["2"] = "shell" };

    static LiraTextSchema Parse(string text) => LiraTextReader.Read(new StringReader(text));

    static int _next;

    static FemLoadTransferResult Run(LiraTextSchema s, IReadOnlyList<FemLoadCase>? cases = null,
        IReadOnlyList<FemElementLoad>? loads = null, IReadOnlyList<FemMeshNodeLoad>? nodeLoads = null)
    {
        _next = 0;
        return LiraLoadTransfer.Transfer(s, Nodes, Types, TonToKn, cases ?? [], loads ?? [], nodeLoads ?? [], () => --_next);
    }

    static void AssertValues(double[] expected, IReadOnlyList<double> actual)
    {
        Assert.Equal(expected.Length, actual.Count);
        for (int i = 0; i < expected.Length; i++) Assert.Equal(expected[i], actual[i], 1e-9 * Math.Max(1, Math.Abs(expected[i])));
    }

    /// <summary>
    /// КЭ 1 — стержень 1–2 вдоль X (6 м), КЭ 2 — пластина 1 2 3 4 (хранение 1-2-4-3, квадрат 6 × 6 в плоскости XY),
    /// КЭ 3 — стержень 2–4, которого нет в сетке. Узел 5 — вне сетки.
    /// </summary>
    const string Schema = """
        ( 0/ 1; synthetic/ 2; 5/
        33;M 1 CM 100 T 1 C 1 /
        39;
        1: ПОСТОЯННОЕ ;
        2: ВРЕМЕННОЕ ;
        3: ПУСТОЕ ;
         /
        )
        ( 1/
        10 1 1 2 /44 2 1 2 3 4 /10 1 2 4 /
        )
        ( 4/
        0 0 0 /6 0 0 /0 6 0 /6 6 0 /9 9 9 /
        )
        ( 6/
        1 0 3 1 1 /1 0 4 2 /1 0 3 1 /
        1 16 3 3 /2 16 3 3 /
        2 17 3 4 2 /1 17 2 5 /2 15 3 6 /1 15 1 7 /2 19 3 8 /1 6 3 3 /
        2 19 3 9 /1 8 1 10 /2 4 3 3 /3 16 3 3 /1 16 3 11 /5 0 3 1 /1 0 3 12 /2 16 4 3 /2 60 3 3 /
        )
        ( 7/
        1 2 0 /2 0.5 0 /3 1 0 0 0 /4 1 2 3 4 /5 1 1 2 5 /6 3 1 2 0 /7 3 2 0 0 /8 2 4 1 1.5 /9 1 4 1 1 /
        10 20 1.2e-05 0 0 /11 1 0.5 0 0 /12 0 2 0.06 25.13 /
        )
        """;

    [Fact]
    public void Synthetic_MapsKindsUnitsAndSigns()
    {
        var s = Parse(Schema);
        Assert.Empty(s.Warnings);
        var r = Run(s);

        Assert.Equal(["ПОСТОЯННОЕ", "ВРЕМЕННОЕ", "ПУСТОЕ"], r.LoadCases.Select(c => c.Tag));
        Assert.Equal([1, 2, 3], r.LoadCases.Select(c => c.SourceLoadNum!.Value));
        Assert.All(r.LoadCases, c => Assert.Null(c.SelfWeightFactor));
        int lc1 = r.LoadCases[0].Id, lc2 = r.LoadCases[1].Id;

        // Узловые: две записи Fz на узел 1 складываются, момент — т·м → Н·м; знак обратный.
        var node = Assert.Single(r.MeshNodeLoads);
        Assert.Equal(("1", lc1, LiraLoadTransfer.Origin), (node.MeshNodeTag, node.LoadCaseId, node.Origin));
        Assert.Equal(-4 * T, node.Fz, 1e-6);
        Assert.Equal(-0.5 * T, node.Mx, 1e-6);

        FemElementLoad One(int lc, string kind, string tag) =>
            Assert.Single(r.ElementLoads, l => l.LoadCaseId == lc && l.LoadKind == kind && l.TargetTags.Contains(tag));

        // Равномерная: стержень т/м → Н/м, пластина т/м² → Па (в метрах одно число — одна запись на оба КЭ).
        var barQ = One(lc1, FemElementLoadKinds.Uniform, "1");
        AssertValues([-T], barQ.Values);
        Assert.Equal(("global", "z"), (barQ.CoordinateSystem, barQ.Axis));
        AssertValues([-T], One(lc1, FemElementLoadKinds.Uniform, "2").Values);

        AssertValues([-T, -2 * T, -3 * T, -4 * T], One(lc2, FemElementLoadKinds.Nodal, "2").Values);
        var trap = One(lc2, FemElementLoadKinds.Trapezoid, "1");
        Assert.Equal("y", trap.Axis);
        AssertValues([-T, 1, -2 * T, 5], trap.Values);
        AssertValues([-3 * T, 1, 2], One(lc2, FemElementLoadKinds.Point, "2").Values);
        var barP = One(lc2, FemElementLoadKinds.Point, "1");
        Assert.Equal("x", barP.Axis);
        AssertValues([-3 * T, 2], barP.Values);
        AssertValues([2, 4, -T, -1.5 * T], One(lc2, FemElementLoadKinds.Edge, "2").Values);
        var local = One(lc2, FemElementLoadKinds.Uniform, "1");
        Assert.Equal(("local", "z"), (local.CoordinateSystem, local.Axis));
        Assert.Equal(7, r.ElementLoads.Count);

        string Line(string what) => Assert.Single(r.Report, l => l.Contains(what));
        Line("узлы i, j — не ребро");
        Line("температура (вид 8)");
        Line("вид 4 (в описании формата нет)");
        Line("вид 60 (в описании формата нет)");
        Line("вид 16 на КЭ, которых нет в сетке");
        Line("равномерная (вид 16) с дополнительным числом");
        Line("узловые на узлы, которых нет в сетке");
        Line("узловые с параметрами динамики");
        Line("моменты на КЭ (вид 16, направление 4)");
        Line("Местные нагрузки по Y1/Z1 стержней (1)");
        Assert.Equal(9, r.SkippedRecords);
        Assert.StartsWith("Перенесено из ЛИРЫ: загружений 3 (новых 3), нагрузок на КЭ 7, узловых 1; не перенесено записей 9.",
            r.Report[0]);
    }

    [Fact]
    public void ForceUnitsFromHeader_KilonewtonsAndMillimetres()
    {
        var s = Parse("""
            ( 0/ 1; u/ 33;MM 1000 CM 100 KN 9.81 C 1 / 39; 1: A ; / )
            ( 1/ 44 1 1 2 3 4 / )
            ( 4/ 0 0 0 /6000 0 0 /0 6000 0 /6000 6000 0 / )
            ( 6/ 1 16 3 1 1 /1 15 3 2 / )
            ( 7/ 1 0.002 /2 5 100 200 / )
            """);
        _next = 0;
        var r = LiraLoadTransfer.Transfer(s, Nodes, new Dictionary<string, string> { ["1"] = "shell" }, TonToKn, [], [], [], () => --_next);
        AssertValues([-0.002 * 1e3 / 1e-6], r.ElementLoads.Single(l => l.LoadKind == "uniform").Values);
        AssertValues([-5e3, 0.1, 0.2], r.ElementLoads.Single(l => l.LoadKind == "point").Values);
    }

    [Fact]
    public void LocalTransverseOnRotatedBar_IsTransferred_AngleGoesToMeshAxes()
    {
        var s = Parse("""
            ( 0/ 1; a/ 39; 1: A ; / )
            ( 1/ 10 1 1 2 /44 2 1 2 3 4 / )
            ( 4/ 0 0 0 /6 0 0 /0 6 0 /6 6 0 / )
            ( 6/ 1 6 3 1 1 /1 6 1 1 /1 5 2 2 /2 6 3 1 / )
            ( 7/ 1 0.5 /2 1 3 / )
            ( 17/ 1 90 /2 45 /2 686.3 0 0 0 / )
            """);
        Assert.Equal(new Dictionary<int, double> { [1] = 90, [2] = 45 }, s.ElementAngles);
        Assert.Contains(s.SkippedDocuments, d => d.Number == 17 && d.Records == 1);

        var r = Run(s);
        Assert.Equal(0, r.SkippedRecords);
        Assert.Contains(r.Report, l => l.Contains("Местные нагрузки по Y1/Z1 стержней (2)"));
        Assert.Contains(r.ElementLoads, l => l.LoadKind == "uniform" && l.Axis == "x" && l.TargetTags.SequenceEqual(["1"]));
        // q по Z1 на стержне 1 и на пластине 2 одинаковы (единицы — метры) — одна запись на оба КЭ
        Assert.Contains(r.ElementLoads, l => l.LoadKind == "uniform" && l.Axis == "z" && l.CoordinateSystem == "local" &&
            l.TargetTags.SequenceEqual(["1", "2"]));
        Assert.Contains(r.ElementLoads, l => l.LoadKind == "point" && l.Axis == "y" && l.TargetTags.SequenceEqual(["1"]));
    }

    [Fact]
    public void SameLoadOnSeveralElements_MergesAndRepeatsOnOneElementAdd()
    {
        var s = Parse("""
            ( 0/ 1; m/ 39; 1: A ; / )
            ( 1/ 44 1 1 2 3 4 /44 1 1 2 3 4 / )
            ( 4/ 0 0 0 /6 0 0 /0 6 0 /6 6 0 / )
            ( 6/ 1 16 3 1 1 /2 16 3 1 /1 16 3 1 / )
            ( 7/ 1 0.5 / )
            """);
        _next = 0;
        var types = new Dictionary<string, string> { ["1"] = "shell", ["2"] = "shell" };
        var r = LiraLoadTransfer.Transfer(s, Nodes, types, TonToKn, [], [], [], () => --_next);
        Assert.Equal(2, r.ElementLoads.Count);
        Assert.Equal(["1", "2"], r.ElementLoads[0].TargetTags);
        Assert.Equal(["1"], r.ElementLoads[1].TargetTags);
    }

    [Fact]
    public void RepeatedTransfer_ReplacesImportedKeepsManualAndReportsOrphans()
    {
        var s = Parse(Schema);
        var existing = new FemLoadCase { Id = 7, Tag = "ПОСТОЯННОЕ", Origin = LiraLoadTransfer.Origin, SourceLoadNum = 1 };
        var orphan = new FemLoadCase { Id = 8, Tag = "Старое", Origin = LiraLoadTransfer.Origin, SourceLoadNum = 9 };
        var manualCase = new FemLoadCase { Id = 9, Tag = "ВРЕМЕННОЕ" };
        var manual = new FemElementLoad { LoadCaseId = 9 };
        manual.SetTargetTags(["2"]);
        var oldImported = new FemElementLoad { LoadCaseId = 7, Origin = LiraLoadTransfer.Origin };
        var oldNode = new FemMeshNodeLoad { LoadCaseId = 7, Origin = LiraLoadTransfer.Origin, MeshNodeTag = "2" };

        var r = Run(s, [existing, orphan, manualCase], [manual, oldImported], [oldNode]);

        Assert.Contains(manual, r.ElementLoads);
        Assert.DoesNotContain(oldImported, r.ElementLoads);
        Assert.DoesNotContain(oldNode, r.MeshNodeLoads);
        Assert.Equal(7, r.MeshNodeLoads.Single().LoadCaseId);
        // Имя занято ручным загружением — новое получает суффикс.
        Assert.Contains(r.LoadCases, c => c.Tag == "ВРЕМЕННОЕ (2)" && c.SourceLoadNum == 2);
        Assert.Equal(2, r.CasesCreated);
        Assert.Contains(r.Report, l => l.Contains("«Старое» (ЛИРА 9) в файле отсутствует"));
    }

    /// <summary>
    /// «Тест_стена»: стена в плоскости XZ, загружение 1 — гидростатика (вид 17 по Y) и узловые силы, 2 — равномерная по Y
    /// на всех пластинах. Сумма узловых сил сетки после переноса = сумме, посчитанной прямо по документам 6/7 (площадь
    /// прямоугольного КЭ × среднее значений по узлам, узловые силы как есть), с переводом т → Н и обратным знаком.
    /// </summary>
    [Fact]
    public void WallFixture_NodalSumEqualsDirectSumPerLoadCase()
    {
        var s = LiraTextReader.Read(Path.Combine(AppContext.BaseDirectory, "Import", "Fixtures", "lira-text-wall.txt"));
        var meshNodes = s.Nodes.Select((p, i) => new FemMeshNode { NodeTag = (i + 1).ToString(), X = p.X, Y = p.Y, Z = p.Z }).ToList();
        var elements = s.Elements.Where(e => e.Nodes.Length is 3 or 4).Select(e => new FemElement
        {
            ElemTag = e.Number.ToString(), ElemType = "shell", NodeIdsJson = System.Text.Json.JsonSerializer.Serialize(e.Nodes),
        }).ToList();
        _next = 0;
        var r = LiraLoadTransfer.Transfer(s, meshNodes.Select(n => n.NodeTag).ToHashSet(),
            elements.ToDictionary(e => e.ElemTag, e => e.ElemType), TonToKn, [], [], [], () => --_next);
        Assert.Equal(0, r.SkippedRecords);
        Assert.Equal(["ПОСТОЯННОЕ", "ВРЕМЕННЫЕ"], r.LoadCases.Select(c => c.Tag));

        var mesh = new FemLoadMeshContext(meshNodes, elements);
        foreach (var lc in r.LoadCases)
        {
            var result = FemLoadCaseNodalForces.Resolve(lc, r.ElementLoads, r.MeshNodeLoads, mesh);
            Assert.Empty(result.Diagnostics);

            var direct = new double[3];
            foreach (var l in s.Loads.Where(l => l.LoadCase == lc.SourceLoadNum))
            {
                var v = s.LoadValues[l.ValueRow];
                double f = l.Kind == 0 ? v[0] : RectArea(s, l.Target) * (l.Kind == 16 ? v[0] : v.Take(4).Average());
                direct[l.Direction - 1] -= f * T;
            }
            var (fx, fy, fz) = result.Total;
            Assert.Equal(direct[0], fx, 1e-6 * Math.Max(1, Math.Abs(direct[0])));
            Assert.Equal(direct[1], fy, 1e-6 * Math.Abs(direct[1]));
            Assert.Equal(direct[2], fz, 1e-6 * Math.Max(1, Math.Abs(direct[2])));
            Assert.True(Math.Abs(direct[1]) > 0);
        }
    }

    /// <summary>Площадь прямоугольного КЭ (хранение 1-2-4-3): |(p2 − p1) × (p3 − p1)|.</summary>
    static double RectArea(LiraTextSchema s, int element)
    {
        var n = s.Elements[element - 1].Nodes;
        var (a, b, c) = (s.Nodes[n[0] - 1], s.Nodes[n[1] - 1], s.Nodes[n[2] - 1]);
        double ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z, vx = c.X - a.X, vy = c.Y - a.Y, vz = c.Z - a.Z;
        double cx = uy * vz - uz * vy, cy = uz * vx - ux * vz, cz = ux * vy - uy * vx;
        return Math.Sqrt(cx * cx + cy * cy + cz * cz);
    }
}
