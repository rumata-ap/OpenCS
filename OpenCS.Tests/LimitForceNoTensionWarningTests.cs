using System.IO;
using System.Text.Json;
using CScore;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Бетонное сечение без арматуры при отключённом растяжении бетона в 1-й ГПС воспринимать растяжение
/// не может: предельный момент без сжимающей силы выходит численным нулём (случай коллег — 300×500, B25,
/// Mx_lim = 0,10 кН·м при статусе «ok»). Задача предельных усилий должна писать предупреждение.
/// </summary>
public sealed class LimitForceNoTensionWarningTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void LimitForce_PlainConcreteBending_TensionOff_Warns()
    {
        var result = Run(PlainConcrete(), new LoadItem { N = 0.0, Mx = 10.0 }, tension: false);

        var warnings = Warnings(result.DataJson);
        Assert.Single(warnings);
        Assert.Contains("нечем воспринимать растяжение", warnings[0]);
    }

    /// <remarks>
    /// nx = 1 — сетка параметрического прямоугольника: все волокна на x = 0, якобиан по kz нулевой.
    /// Раньше решатель плоскости уходил в запасной шаг 1e-6·невязка и давал Mx_lim = 0,37 кН·м.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    public void LimitForce_PlainConcreteBending_TensionOn_NoWarning_AndRbtW(int nx)
    {
        var result = Run(PlainConcrete(nx: nx), new LoadItem { N = 0.0, Mx = 10.0 }, tension: true);

        Assert.Empty(Warnings(result.DataJson));
        // С растянутым бетоном предельный момент — порядка Rbt·W = 1050·0,3·0,5²/6 = 13,1 кН·м и выше
        // (пластика растянутого бетона по двухлинейной диаграмме), а не численный ноль.
        using var doc = JsonDocument.Parse(result.DataJson);
        double mxLim = Math.Abs(doc.RootElement.GetProperty("Mx_limit").GetDouble());
        output.WriteLine($"nx = {nx}: Mx_lim = {mxLim:0.###} кН·м");
        Assert.True(mxLim is > 13.0 and < 20.0, result.DataJson);
    }

    [Fact]
    public void LimitForce_PlainConcreteEccentricCompression_NoWarning()
    {
        var result = Run(PlainConcrete(), new LoadItem { N = -500.0, Mx = 20.0 }, tension: false);
        Assert.Empty(Warnings(result.DataJson));
    }

    [Fact]
    public void LimitForce_ReinforcedSection_NoWarning()
    {
        var result = Run(PlainConcrete(withRebar: true), new LoadItem { N = 0.0, Mx = 10.0 }, tension: false);
        Assert.Empty(Warnings(result.DataJson));
    }

    [Fact]
    public void LimitForce_EmptyRebarGroup_StillWarns()
    {
        var result = Run(PlainConcrete(withRebar: true, emptyRebar: true), new LoadItem { N = 0.0, Mx = 10.0 }, tension: false);
        Assert.Single(Warnings(result.DataJson));
    }

    [Fact]
    public void LimitForceBatch_PlainConcrete_WarnsWithAffectedRowCount()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-notension-{Guid.NewGuid():N}.db");
        try
        {
            using var database = new DatabaseService(path);
            database.ForceSets.Add(new ForceSet
            {
                Id = 1, Kind = "bar", Tag = "изгиб и сжатие",
                Items =
                [
                    new LoadItem { Num = 1, Label = "M", N = 0.0, Mx = 10.0 },
                    new LoadItem { Num = 2, Label = "N+M", N = -500.0, Mx = 20.0 },
                ]
            });
            var task = new CalcTask
            {
                Id = 1, Kind = "limit_force_batch", CalcType = CalcType.C, ForceSetId = 1,
                ParamsJson = "{\"solver\":\"bisection\"}",
            };

            var result = new LimitForceBatchHandler().Run(task, PlainConcrete(), new LoadItem(),
                Settings(tension: false), new TaskRunContext { Database = database });

            var warnings = Warnings(result.DataJson);
            Assert.Single(warnings);
            Assert.Contains("1 из 2", warnings[0]);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    static CalcResult Run(CrossSection section, LoadItem item, bool tension)
    {
        var task = new CalcTask
        {
            Id = 1, Kind = "limit_force", Tag = "Предельное усилие", CalcType = CalcType.C,
            ParamsJson = JsonSerializer.Serialize(new { solver = "bisection", N = item.N, Mx = item.Mx, My = item.My }),
        };
        var result = new LimitForceHandler().Run(task, section, item, Settings(tension));
        Assert.NotEqual("error", result.Status);
        return result;
    }

    static CalcSettings Settings(bool tension)
    {
        var s = CalcSettings.Default;
        s.ConsiderConcreteTensionUls = tension;
        return s;
    }

    static List<string> Warnings(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("warnings").EnumerateArray().Select(e => e.GetString()!).ToList();
    }

    static void Fill(Material m, Func<CalcType, MaterialChars> chars) => m.MaterialChars =
        [chars(CalcType.C), chars(CalcType.CL), chars(CalcType.N), chars(CalcType.NL)];

    /// <summary>Прямоугольник 300×500 из B25 (как в проекте коллег), по желанию — с арматурой A500 понизу.</summary>
    static CrossSection PlainConcrete(bool withRebar = false, bool emptyRebar = false, int nx = 1)
    {
        var concrete = new Material { Id = 1, Tag = "B25", Type = MatType.Concrete, E = 30_000_000.0 };
        Fill(concrete, calc => new MaterialChars(calc)
        {
            Type = MatType.Concrete, Fc = -14_500.0, Ft = 1_050.0, E = 30_000_000.0,
            Ec1Red = -0.0015, Ec2 = -0.0035, Et1Red = 0.00008, Et2 = 0.00015,
        });

        var area = new MaterialArea
        {
            Id = 1, Tag = "concrete", Category = AreaCategory.Region,
            Material = concrete, MaterialId = concrete.Id, DiagrammType = DiagrammType.L2,
            Hull = new Contour([-0.15, 0.15, 0.15, -0.15, -0.15],
                               [-0.25, -0.25, 0.25, 0.25, -0.25], "outer"),
        };
        area.SetWKT();
        area.SliceXY(nx: nx, ny: 50);

        var areas = new List<MaterialArea> { area };
        if (withRebar)
        {
            var steel = new Material { Id = 2, Tag = "A500", Type = MatType.ReSteelF, E = 200_000_000.0 };
            Fill(steel, calc => new MaterialChars(calc)
            {
                Type = MatType.ReSteelF, Fc = -435_000.0, Ft = 435_000.0, E = 200_000_000.0,
                Ec2 = -0.0035, Et2 = 0.025,
            });
            areas.Add(new MaterialArea
            {
                Id = 2, Tag = "rebar", Category = AreaCategory.RebarGroup,
                Material = steel, MaterialId = steel.Id, DiagrammType = DiagrammType.L2,
                Fibers = emptyRebar
                    ? []
                    : [Fiber.CreatePoint(0.016, -0.10, -0.22), Fiber.CreatePoint(0.016, 0.10, -0.22)],
            });
        }

        var section = new CrossSection { Id = 1, Tag = "300×500", Areas = areas };
        section.ResolveAndBuildDiagramms(0.85, pool: null, rebarDifferentialDiagram: false);
        return section;
    }
}
