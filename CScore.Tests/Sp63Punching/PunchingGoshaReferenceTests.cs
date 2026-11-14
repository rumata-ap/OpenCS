using System.Text.Json;
using CScore.Sp63Punching;
using Xunit;

namespace CScore.Tests.Sp63Punching;

/// <summary>
/// Сверка с кодом strength-web/strength/punching/calc.py (порт книги Гоши «Продавливание.xlsx»).
/// Числа сняты генератором вне репозитория (OpenCSpy/punching_reference), см. поле source фикстуры.
/// </summary>
public class PunchingGoshaReferenceTests
{
    static readonly JsonElement Root = JsonDocument.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Sp63Punching", "Fixtures", "gosha_reference.json"))).RootElement;

    public static IEnumerable<object[]> Cases()
        => Root.GetProperty("cases").EnumerateObject().Select(p => new object[] { p.Name });

    static readonly Dictionary<string, string> KindToId = new()
    {
        ["inner"] = "closed", ["edge"] = "open_edge_0", ["corner"] = "open_corner_0_1", ["pylon"] = "pylon_broken"
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void MatchesGoshaCode(string name)
    {
        var c = Root.GetProperty("cases").GetProperty(name);
        var contours = c.GetProperty("contours");
        var any = contours.EnumerateObject().First().Value;
        var input = ToInput(c.GetProperty("input"), any);
        var result = PunchingChecker.Check(input);

        // Набор вариантов совпадает с набором контуров книги.
        var expectedIds = contours.EnumerateObject().Select(p => KindToId[p.Name]).OrderBy(x => x).ToList();
        Assert.Equal(expectedIds, result.Variants.Select(v => v.Variant.Id).OrderBy(x => x).ToList());

        foreach (var contour in contours.EnumerateObject())
        {
            var v = result.Variants.Single(x => x.Variant.Id == KindToId[contour.Name]);
            var e = contour.Value;
            void Check(string key, double actual, double scale = 1)
            {
                if (!e.TryGetProperty(key, out var p)) return;
                double expected = p.GetDouble() * scale;
                Assert.True(Math.Abs(actual - expected) <= 1e-9 * Math.Max(1e-3, Math.Abs(expected)),
                    $"{name}/{contour.Name}/{key}: ожидалось {expected:R}, получено {actual:R}");
            }
            Check("u", v.Geometry.U, 1e-3);
            Check("ey", v.Geometry.Yc, 1e-3);
            Check("ex", v.Geometry.Xc, 1e-3);
            Check("Ibx", v.Geometry.Ibx, 1e-9);
            Check("Iby", v.Geometry.Iby, 1e-9);
            Check("Wbx", v.Wbx, 1e-6);
            Check("Wby", v.Wby, 1e-6);
            Check("Fb", v.Fb);
            Check("Fsw", v.Fsw);
            Check("Mbx", v.Mbx);
            Check("Mby", v.Mby);
            Check("Msx", v.Mswx);
            Check("Msy", v.Mswy);
            Check("Fult", v.Fult);
            Check("Mxult", v.Mxult);
            Check("Myult", v.Myult);
            Check("Mxc", v.MxStar);
            Check("Myc", v.MyStar);
            Check("kF", v.KF);
            Check("kMraw", v.KMRaw);
            Check("kM", v.KM);
            Check("K", v.Utilization);
        }

        double kmax = contours.EnumerateObject().Max(p => p.Value.GetProperty("K").GetDouble());
        Assert.Equal(kmax, result.Utilization, 12);
    }

    static PunchingInput ToInput(JsonElement p, JsonElement contour)
    {
        double bk = p.GetProperty("bk").GetDouble() / 1000, hk = p.GetProperty("hk").GetDouble() / 1000;
        double c = p.GetProperty("c").GetDouble() / 1000;
        double h0 = contour.GetProperty("h0").GetDouble() / 1000;
        double rbt = contour.GetProperty("Rbt").GetDouble() * 1000;
        string location = p.GetProperty("location").GetString()!;

        PunchingShearReinforcement? sw = null;
        if (p.GetProperty("use_sw").GetBoolean())
        {
            double d = p.GetProperty("d_sw").GetDouble(), s = p.GetProperty("s_w").GetDouble();
            double aswMm2 = p.GetProperty("n_sw").GetInt32() * Math.PI * d * d / 4;
            double rswMpa = contour.GetProperty("qsw").GetDouble() * s / aswMm2;
            sw = new PunchingShearReinforcement(rswMpa * 1000, aswMm2 * 1e-6, s / 1000);
        }

        var edges = new List<PunchingSlabEdge>();
        if (location != "Внутри плиты")
            edges.Add(new PunchingSlabEdge(new PlanPoint(0, -(hk / 2 + c)), new PlanPoint(0, 1)));
        if (location == "Угол плиты")
            edges.Add(new PunchingSlabEdge(new PlanPoint(-(bk / 2 + c), 0), new PlanPoint(1, 0)));

        return new PunchingInput(
            new PunchingLoadArea.Rectangle(bk, hk), h0, rbt,
            new PunchingForces(p.GetProperty("F").GetDouble(), p.GetProperty("Mx").GetDouble(), p.GetProperty("My").GetDouble()),
            sw, edges,
            Options: new PunchingOptions(Mode(p.GetProperty("mode_x").GetString()!), Mode(p.GetProperty("mode_y").GetString()!)));
    }

    static EccentricityMode Mode(string s) => s switch
    {
        "Догружающее" => EccentricityMode.Loading,
        "Разгружающее" => EccentricityMode.Unloading,
        _ => EccentricityMode.Ignore
    };
}
