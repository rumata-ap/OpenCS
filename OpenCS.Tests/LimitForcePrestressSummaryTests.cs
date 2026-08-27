using System.Text.Json;
using CScore;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Сводка задачи предельных усилий должна показывать блок преднапряжения — тот же, что в
/// задаче «плоскость деформаций». Блок рисует StrainSummaryBody по <c>StrainPart</c>
/// (LimitForceSummaryView.xaml), а StrainSummaryVM ищет в DataJson поле <c>prestress</c>.
///
/// Смысл блока здесь — исходное обжатие, вошедшее в расчёт: оно НЕ масштабируется
/// коэффициентом k и от найденной предельной точки не зависит. Без него из сводки не видно
/// ни того, что сечение преднапряжённое, ни предупреждения σ_sp·γ_sp > Ft.
/// </summary>
public sealed class LimitForcePrestressSummaryTests
{
    [Fact]
    public void LimitMoment_PrestressedSection_WritesPrestressBlock()
    {
        var result = new LimitMomentHandler().Run(
            Task(), PrestressedBeam(), new LoadItem { N = -500.0, Mx = -100.0, My = 25.0 },
            CalcSettings.Default);

        Assert.Equal("ok", result.Status);
        using var doc = JsonDocument.Parse(result.DataJson);
        var prestress = doc.RootElement.GetProperty("prestress");

        var groups = prestress.GetProperty("groups").EnumerateArray().ToList();
        Assert.Single(groups);
        Assert.Equal(900.0, groups[0].GetProperty("sigSp_MPa").GetDouble(), 6);

        // Обжатие: N отрицательна (сжимает сечение), фактическое ниже номинального —
        // σ_sp = 900 МПа задано выше расчётного сопротивления A1000.
        double nominalN = prestress.GetProperty("nominal").GetProperty("N_kN").GetDouble();
        double actualN = prestress.GetProperty("actual").GetProperty("N_kN").GetDouble();
        Assert.True(nominalN < 0);
        Assert.True(Math.Abs(actualN) < Math.Abs(nominalN));
        Assert.True(prestress.GetProperty("hasGroupsAboveStrength").GetBoolean());
    }

    [Fact]
    public void LimitMoment_SectionWithoutPrestress_WritesEmptyGroups()
    {
        var result = new LimitMomentHandler().Run(
            Task(), PrestressedBeam(sigSp: 0.0), new LoadItem { N = -500.0, Mx = -100.0, My = 25.0 },
            CalcSettings.Default);

        using var doc = JsonDocument.Parse(result.DataJson);
        Assert.Empty(doc.RootElement.GetProperty("prestress").GetProperty("groups").EnumerateArray());
    }

    static CalcTask Task() => new()
    {
        Id = 1,
        Kind = "limit_moment",
        Tag = "Предельный момент",
        CalcType = CalcType.C,
        ParamsJson = "{\"solver\":\"fast\",\"N\":-500,\"Mx\":-100,\"My\":25}",
    };

    /// <summary>
    /// Характеристики на все четыре вида расчёта — через список: сеттеры Material.C/CL/...
    /// наполняют только внутренний словарь, а Material.C читает список materialChars.
    /// </summary>
    static void Fill(Material m, Func<CalcType, MaterialChars> chars) => m.MaterialChars =
        [chars(CalcType.C), chars(CalcType.CL), chars(CalcType.N), chars(CalcType.NL)];

    /// <summary>Прямоугольник 300×500: B25, A500 сверху/снизу и напрягаемая A1000 понизу.</summary>
    static CrossSection PrestressedBeam(double sigSp = 900.0)
    {
        var concrete = new Material { Id = 1, Tag = "B25", Type = MatType.Concrete, E = 30_000_000.0 };
        Fill(concrete, calc => new MaterialChars(calc)
        {
            Type = MatType.Concrete, Fc = -14_500.0, Ft = 1_050.0, E = 30_000_000.0,
            Ec1Red = -0.0015, Ec2 = -0.0035, Et1Red = 0.00008, Et2 = 0.00015,
        });

        var rebarSteel = new Material { Id = 2, Tag = "A500", Type = MatType.ReSteelF, E = 200_000_000.0 };
        Fill(rebarSteel, calc => new MaterialChars(calc)
        {
            Type = MatType.ReSteelF, Fc = -435_000.0, Ft = 435_000.0, E = 200_000_000.0,
            Ec2 = -0.0035, Et2 = 0.025,
        });

        var strandSteel = new Material { Id = 3, Tag = "A1000", Type = MatType.ReSteelU, E = 200_000_000.0 };
        Fill(strandSteel, calc => new MaterialChars(calc)
        {
            Type = MatType.ReSteelU, Fc = -870_000.0, Ft = 870_000.0, E = 200_000_000.0,
            Ec0 = -0.00635, Ec1 = -0.003915, Ec2 = -0.0035,
            Et0 = 0.00635, Et1 = 0.003915, Et2 = 0.015,
        });

        var area = new MaterialArea
        {
            Id = 1, Tag = "concrete", Category = AreaCategory.Region,
            Material = concrete, MaterialId = concrete.Id, DiagrammType = DiagrammType.L2,
            Hull = new Contour([-0.15, 0.15, 0.15, -0.15, -0.15],
                               [-0.25, -0.25, 0.25, 0.25, -0.25], "outer"),
        };
        area.SetWKT();
        area.SliceXY(nx: 12, ny: 20);

        var rebar = new MaterialArea
        {
            Id = 2, Tag = "rebar", Category = AreaCategory.RebarGroup,
            Material = rebarSteel, MaterialId = rebarSteel.Id, DiagrammType = DiagrammType.L2,
            Fibers =
            [
                Fiber.CreatePoint(0.016, -0.12, 0.22),
                Fiber.CreatePoint(0.016, 0.12, 0.22),
                Fiber.CreatePoint(0.020, -0.12, -0.22),
                Fiber.CreatePoint(0.020, 0.12, -0.22),
            ],
        };

        var strands = new MaterialArea
        {
            Id = 3, Tag = "strands", Category = AreaCategory.RebarGroup,
            Material = strandSteel, MaterialId = strandSteel.Id, DiagrammType = DiagrammType.L3,
            SigSp = sigSp, GammaSp = 1.0,
            Fibers = [Fiber.CreatePoint(0.02, -0.04, -0.22), Fiber.CreatePoint(0.02, 0.04, -0.22)],
        };

        var section = new CrossSection { Id = 1, Tag = "rect 300x500 ps", Areas = [area, rebar, strands] };
        section.ResolveAndBuildDiagramms(0.85, pool: null, rebarDifferentialDiagram: false);
        return section;
    }
}
