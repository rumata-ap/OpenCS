using System.Globalization;
using CSfea.CScoreBridge.Structural;
using CSfea.Core;

namespace CSfea.Tests;

/// <summary>
/// Примеры вебинара А. Голенкина (structuristik, «Расчётные длины»): плоские рамы, профили — СТО АСЧМ 20-93,
/// E = 2,06·10⁵ МПа. По каждой колонне — μ по формам (N_cr = λ·N), номограмма ASCE и табл. 31 СП 16 против значений
/// вебинара. Плоскость рамы — XZ, из плоскости все узлы закреплены (Y, RX, RZ).
/// </summary>
[HarnessChecks]
public class GolenkinWebinarTests
{
    private const double E = 2.06e11;
    private const int Plane = 0b101010, Pin = 0b101111, Fixed = 0b111111, Hinge = 0b110000;

    // Ix, см⁴, и A, см² (СТО АСЧМ 20-93); труба 160×6 — по размерам.
    private static RcBeamSection Profile(string name) => name switch
    {
        "30К1" => Section(name, 18850, 110.8),
        "40К2" => Section(name, 66620, 218.7),
        "20К1" => Section(name, 3846, 52.69),
        "60Б1" => Section(name, 68720, 120.5),
        "45Б1" => Section(name, 28700, 84.3),
        "35Б1" => Section(name, 11100, 52.68),
        "70Ш5" => Section(name, 319800, 375.7),
        "160x6" => Section(name, Math.PI / 64 * (Math.Pow(16, 4) - Math.Pow(14.8, 4)), Math.PI / 4 * (16 * 16 - 14.8 * 14.8)),
        _ => throw new ArgumentException(name),
    };

    private static RcBeamSection Section(string name, double ixCm4, double aCm2) =>
        new(name) { Elastic = new BeamSection(E, aCm2 * 1e-4, ixCm4 * 1e-8, ixCm4 * 1e-8) };

    [Fact]
    public static void RunAll()
    {
        RunExample11();
        RunExample2();
        RunExample4();
        RunLba();
        RunContest();
    }

    /// <summary>Построитель плоской рамы: узлы по (x, z), стержни с шарнирами, опоры, узловые силы (кН, вниз).</summary>
    private sealed class Frame
    {
        public readonly RcStructuralModel Model = new();
        readonly Dictionary<(double, double), int> _ids = new();
        readonly RcLoadCase _lc = new(1, "P");
        public readonly Dictionary<int, string> Names = new();

        public Frame() => Model.LoadCases.Add(_lc);

        public int Node(double x, double z)
        {
            if (_ids.TryGetValue((x, z), out int id)) return id;
            id = Model.Nodes.Count + 1;
            Model.Nodes.Add(new RcNode(id, x, 0, z));
            return _ids[(x, z)] = id;
        }

        public void Bar(string name, (double, double) a, (double, double) b, string profile, bool hingeI = false, bool hingeJ = false)
        {
            int id = 101 + Model.Beams.Count;
            Model.Beams.Add(new RcBeam(id, Node(a.Item1, a.Item2), Node(b.Item1, b.Item2), Profile(profile), [0.0, 1.0, 0.0],
                hingeI ? Hinge : 0, hingeJ ? Hinge : 0));
            Names[id] = name;
        }

        public void Load((double, double) at, double kN) => _lc.Nodal.Add(new RcNodalLoad(Node(at.Item1, at.Item2), [0.0, 0, -kN * 1e3, 0, 0, 0]));

        public void Supports(Func<RcNode, int> mask)
        {
            foreach (var n in Model.Nodes) Model.Supports.Add(new RcSupport(n.Id, mask(n)));
        }
    }

    private sealed record Row(string Name, double N, double Form, RcBucklingLengthSource Source, double Asce, double Sp16, RcNomogramPlane NAsce, RcNomogramPlane NSp16,
        double Length = 0, double EI = 0, double LocalBracedNcr = double.NaN, int Mode = -1);

    // Расчёт рамы: λ₁ и по колоннам (стержням с именами К…) μ в плоскости рамы (вокруг местной y) тремя методами.
    private static (double Lambda, List<Row> Rows) Solve(Frame f)
    {
        var units = f.Model.Beams.Select(b => (IReadOnlyList<int>)[b.Id]).ToList();
        var model = RcBucklingAnalysis.Prepare(f.Model, units, new RcBucklingOptions(MinSubdivisions: 8));
        var r = RcBucklingAnalysis.Solve(model, [(1, 1.0)], new RcBucklingOptions(Modes: 12));
        var local = RcBucklingAnalysis.SolveLocal(r, units);
        var form = RcBucklingLengths.Assign(r, units, local: local);
        var all = Enumerable.Range(0, units.Count);
        var asce = RcNomogramLengths.Assign(r, units, all, form, new RcNomogramOptions(RcLengthMethod.Asce), local);
        var sp16 = RcNomogramLengths.Assign(r, units, all, form, new RcNomogramOptions(RcLengthMethod.Sp16), local);
        var rows = new List<Row>();
        for (int i = 0; i < units.Count; i++)
        {
            string name = f.Names[f.Model.Beams[i].Id];
            if (!name.StartsWith('К') || form[i] is not { } x) continue;
            rows.Add(new Row(name, -x.N / 1e3, x.AboutY.Mu, x.AboutY.Source, asce[i]!.AboutY.Mu, sp16[i]!.AboutY.Mu,
                asce[i]!.AboutY.Nomogram!, sp16[i]!.AboutY.Nomogram!, x.Length, x.AboutY.EI, x.AboutY.LocalNcr, x.AboutY.Mode));
        }
        return (r.Lambda[0], rows);
    }

    private static string G(double g) => double.IsPositiveInfinity(g) ? "∞" : g.ToString("0.###", CultureInfo.InvariantCulture);

    private static void Print(double lambda, List<Row> rows)
    {
        TestHarness.Check($"λ₁ = {lambda.ToString("0.####", CultureInfo.InvariantCulture)}", true);
        foreach (var w in rows)
            TestHarness.Check(string.Create(CultureInfo.InvariantCulture,
                $"{w.Name}: N = {w.N:0.#} кН; по формам μ = {w.Form:0.###} ({w.Source}); ASCE {w.Asce:0.###} ({(w.NAsce.Sway ? "смещ." : "несмещ.")}, G {G(w.NAsce.A.G)}/{G(w.NAsce.B.G)}); СП 16 {w.Sp16:0.###} ({w.NSp16.Storey}, k = {w.NSp16.Spans}{(w.NSp16.Sp16 is { } t ? $", p = {G(t.P)}, n = {t.N:0.###}, {t.Formula}" : "")})"), true);
    }

    /// <summary>
    /// Пример 1.1 «Распределение раскрепления»: портал 11 м, колонны 30К1 с шарнирами внизу, ригели 70Ш5 (верх, 7,3 м)
    /// и 35Б1 (5,5 м), по 1800 кН на колонну. Вебинар: МКЭ (ЛРУ) P_cr = 2111 кН; номограмма с распределением
    /// раскрепления верхнего участка — K_BC = 2,423, P_cr = 2158 кН.
    /// </summary>
    private static void RunExample11()
    {
        TestHarness.Section("Голенкин, пример 1.1: распределение раскрепления");
        var f = new Frame();
        foreach (double x in new[] { 0.0, 11 })
        {
            f.Bar(x == 0 ? "К-BC лев." : "К-BC прав.", (x, 0), (x, 5.5), "30К1");
            f.Bar(x == 0 ? "К-AB лев." : "К-AB прав.", (x, 5.5), (x, 7.3), "30К1");
            f.Load((x, 7.3), 1800);
        }
        f.Bar("Р-35Б1", (0, 5.5), (11, 5.5), "35Б1");
        f.Bar("Р-70Ш5", (0, 7.3), (11, 7.3), "70Ш5");
        f.Supports(n => n.Z == 0 ? Pin : Plane);
        var (lambda, rows) = Solve(f);
        Print(lambda, rows);
        TestHarness.CheckRel("P_cr на колонну = 2111 кН (ЛРУ вебинара)", lambda * 1800, 2111, 0.02);
        var bc = rows.First(w => w.Name == "К-BC лев.");
        double kBc = Math.PI / 5.5 * Math.Sqrt(E * 18850e-8 / (lambda * 1800e3));
        TestHarness.Check(string.Create(CultureInfo.InvariantCulture, $"K_BC по ЛРУ = {kBc:0.###} (вебинар: распределение раскрепления 2,423)"), true);
        TestHarness.CheckRel("по формам μ_BC = K по ЛРУ", bc.Form, kBc, 1e-3);
    }

    /// <summary>
    /// Пример 2 «Распределение раскрепления»: колонны 40К2 (13,5 + 6,5 м) с шарнирами внизу, верхняя панель 10 × 6,5 м
    /// раскреплена крестом из труб 160×6, распорки 160×6 в уровнях A и B (все трубы — шарнирно), P_A = 600 кН, P_B = 10 кН.
    /// Ответ вебинара на снимке не виден — печать для сравнения.
    /// </summary>
    private static void RunExample2()
    {
        TestHarness.Section("Голенкин, пример 2: распределение раскрепления");
        var f = new Frame();
        foreach (double x in new[] { 0.0, 10 })
        {
            f.Bar(x == 0 ? "К-BC лев." : "К-BC прав.", (x, 0), (x, 13.5), "40К2");
            f.Bar(x == 0 ? "К-AB лев." : "К-AB прав.", (x, 13.5), (x, 20), "40К2");
            f.Load((x, 20), 600);
            f.Load((x, 13.5), 10);
        }
        f.Bar("распорка B", (0, 13.5), (10, 13.5), "160x6", true, true);
        f.Bar("распорка A", (0, 20), (10, 20), "160x6", true, true);
        f.Bar("раскос 1", (0, 13.5), (10, 20), "160x6", true, true);
        f.Bar("раскос 2", (0, 20), (10, 13.5), "160x6", true, true);
        f.Supports(n => n.Z == 0 ? Pin : Plane);
        var (lambda, rows) = Solve(f);
        Print(lambda, rows);
        foreach (var w in rows)
            TestHarness.Check(string.Create(CultureInfo.InvariantCulture,
                $"{w.Name}: l = {w.Length:0.##} м, форма {w.Mode + 1}, N_cr = λ·N = {lambda * w.N:0} кН, lef = {w.Form * w.Length:0.##} м; локально без смещения N_cr = {w.LocalBracedNcr / 1e3:0} кН, μ = {Math.PI / w.Length * Math.Sqrt(w.EI / w.LocalBracedNcr):0.###}"), true);
    }

    /// <summary>
    /// Пример 4 «Взаимное раскрепление»: две консоли 30К1 5 м, шарнирная распорка 45Б1, 4500 и 1000 кН. Вебинар (AISC,
    /// этажный подход с качанием): K₁ = max(1,563; √(5/8)·2) = 1,581, K₂ = 3,317. По формам — системный μ: тот же 3,317 и
    /// 1,563 без нижнего предела.
    /// </summary>
    private static void RunExample4()
    {
        TestHarness.Section("Голенкин, пример 4: взаимное раскрепление");
        var f = new Frame();
        f.Bar("К1", (0, 0), (0, 5), "30К1");
        f.Bar("К2", (6, 0), (6, 5), "30К1");
        f.Bar("распорка 45Б1", (0, 5), (6, 5), "45Б1", true, true);
        f.Load((0, 5), 4500);
        f.Load((6, 5), 1000);
        f.Supports(n => n.Z == 0 ? Fixed : Plane);
        var (lambda, rows) = Solve(f);
        Print(lambda, rows);
        double pcr = Math.PI * Math.PI * E * 18850e-8 / 100;   // консоль: (2·5)² = 100
        // Этажная формула ΣP_cr/ΣP приближённа: точное условие k₁(λP₁) + k₂(λP₂) = 0 нелинейно по P — МКЭ ниже на 0,7 %.
        TestHarness.CheckRel("λ₁ ≈ 2·P_cr,консоли/5500 кН (этажная формула AISC)", lambda, 2 * pcr / 5500e3, 0.01);
        TestHarness.CheckRel("К1 по формам — 1,563 (без предела √(5/8)·K_sw)", rows[0].Form, Math.PI / 5 * Math.Sqrt(E * 18850e-8 / (lambda * 4500e3)), 1e-3);
        TestHarness.CheckRel("К2 по формам ≈ 3,317 (вебинар, этажная формула)", rows[1].Form, 3.317, 0.005);
    }

    /// <summary>
    /// «Определение расчётных длин через ЛРУ»: 2 пролёта по 8 м, 2 этажа по 4 м, шарнирные опоры; левые и средние колонны
    /// 30К1, правые 20К1; ригели 60Б1, правый пролёт — шарнирно; нагрузки P, P, 2P на этаж. Вебинар: КЗУ = 1,320,
    /// K₁ = 3,012, K₂ = 4,260 (колонны 30К1 нижнего и верхнего этажа).
    /// </summary>
    private static void RunLba()
    {
        TestHarness.Section("Голенкин: расчётные длины через ЛРУ");
        var f = new Frame();
        foreach (var (x, p) in new[] { (0.0, "30К1"), (8.0, "30К1"), (16.0, "20К1") })
        {
            f.Bar($"К x={x} низ", (x, 0), (x, 4), p);
            f.Bar($"К x={x} верх", (x, 4), (x, 8), p);
        }
        foreach (double z in new[] { 4.0, 8 })
        {
            f.Bar($"Р лев. z={z}", (0, z), (8, z), "60Б1");
            f.Bar($"Р прав. z={z}", (8, z), (16, z), "60Б1", true, true);
            f.Load((0, z), 1);
            f.Load((8, z), 1);
            f.Load((16, z), 2);
        }
        f.Supports(n => n.Z == 0 ? Pin : Plane);
        var (lambda, rows) = Solve(f);
        Print(lambda, rows);
        double p1 = lambda / 1.32;
        TestHarness.Check(string.Create(CultureInfo.InvariantCulture, $"P при КЗУ = 1,32: {p1:0.#} кН (P_cr = {lambda:0.#} кН)"), true);
        var low = rows.First(w => w.Name == "К x=0 низ");
        var up = rows.First(w => w.Name == "К x=0 верх");
        TestHarness.CheckRel("K₁ (30К1 низ) = 3,012", low.Form, 3.012, 0.01);
        // Вебинар: верхний этаж тоже по λ₁·N (K₂ = K₁·√2); у нас верхняя колонна в первой форме почти не изгибается
        // (участие ниже порога) — μ по более высокой форме, заметно меньше.
        double k2 = Math.PI / 4 * Math.Sqrt(E * 18850e-8 / (lambda * 1e3));
        TestHarness.CheckRel("K₂ по λ₁·N (30К1 верх) = 4,260", k2, 4.260, 0.01);
        TestHarness.Check(string.Create(CultureInfo.InvariantCulture, $"K₂ по правилу участия OpenCS = {up.Form:0.###} (вебинар 4,260 — по λ₁)"), true);
    }

    /// <summary>
    /// Конкурсное задание (все колонны 30К1, ригели 60Б1, пролёты по 6 м, этажи по 4 м — высота верхнего этажа и пролёты
    /// D–G, G–K на снимке не подписаны, приняты 4 и 6 м). B, H — заделки, E, L — шарниры; ригель A–D жёсткий у A, у D —
    /// шарнир (точка); распорки CF, DG, GK шарнирные; крест C–G, D–F — трубы 160×6 (сечение на снимке не указано),
    /// шарнирные. Нагрузки (кН): A 1500, C 1500, D 1500, F 3000, G 3000, K 5000. Печать для сравнения.
    /// </summary>
    private static void RunContest()
    {
        TestHarness.Section("Голенкин: конкурс (допущения — в описании теста)");
        var f = new Frame();
        f.Bar("К1 (A–B)", (0, 0), (0, 4), "30К1");
        f.Bar("К2 (D–E)", (6, 0), (6, 4), "30К1");
        f.Bar("К3 (G–H)", (12, 0), (12, 4), "30К1");
        f.Bar("К4 (K–L)", (18, 0), (18, 4), "30К1");
        f.Bar("К5 (C–D)", (6, 4), (6, 8), "30К1");
        f.Bar("К6 (F–G)", (12, 4), (12, 8), "30К1");
        f.Bar("Р A–D", (0, 4), (6, 4), "60Б1", false, true);
        f.Bar("Р D–G", (6, 4), (12, 4), "60Б1", true, true);
        f.Bar("Р G–K", (12, 4), (18, 4), "60Б1", true, true);
        f.Bar("Р C–F", (6, 8), (12, 8), "60Б1", true, true);
        f.Bar("раскос C–G", (6, 8), (12, 4), "160x6", true, true);
        f.Bar("раскос D–F", (6, 4), (12, 8), "160x6", true, true);
        foreach (var (at, p) in new[] { ((0.0, 4.0), 1500.0), ((6.0, 8.0), 1500), ((6.0, 4.0), 1500), ((12.0, 8.0), 3000), ((12.0, 4.0), 3000), ((18.0, 4.0), 5000) })
            f.Load(at, p);
        f.Supports(n => n.Z != 0 ? Plane : n.X is 0 or 12 ? Fixed : Pin);
        var (lambda, rows) = Solve(f);
        Print(lambda, rows);
    }
}
