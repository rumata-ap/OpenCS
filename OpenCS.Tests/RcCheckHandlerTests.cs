using System.Text.Json;
using CScore;
using CScore.Fem;
using OpenCS.Tasks;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Проверка «rc_check» стержня схемы МКЭ — прочность по НДМ по каждой строке.
/// Регрессия 26.09.2026: обработчика не было, проверка группы показывала «24 прошло»
/// с Кисп = 0,000, хотя ни одна строка не считалась.
/// </summary>
public sealed class RcCheckHandlerTests
{
    // Балка 300×600, 2Ø28 A500 понизу (y = −0,25): растяжение низа — отрицательный Mx.
    // M_u ≈ Rs·As·z ≈ 435 000 · 0,001232 · 0,5 ≈ 270 кН·м.

    [Fact]
    public void RegisteredInTaskRunner() => Assert.Contains("rc_check", TaskRunner.KindList);

    [Fact]
    public void ModerateMomentPassesWithUtilizationBelowOne()
    {
        var r = Run(mx: -100.0);

        Assert.Equal("ok", r.Status);
        using var doc = JsonDocument.Parse(r.DataJson);
        double u = doc.RootElement.GetProperty("utilization").GetDouble();
        Assert.InRange(u, 0.01, 1.0);
        Assert.True(doc.RootElement.GetProperty("passed").GetBoolean());
        Assert.Equal(2, doc.RootElement.GetProperty("details").GetArrayLength());
    }

    [Fact]
    public void MomentAboveCapacityFails()
    {
        var r = Run(mx: -600.0);

        Assert.Contains(r.Status, new[] { "not_passed", "not_converged" });
        using var doc = JsonDocument.Parse(r.DataJson);
        Assert.False(doc.RootElement.GetProperty("passed").GetBoolean());
    }

    /// <summary>
    /// Нормативный набор (N, NL): растянутый бетон учитывается только до образования трещин. Колонна 25×60 см,
    /// 4 угловых стержня по 1,5 см² (B20, A400, нормативные характеристики), строка РСУ (N) косого
    /// внецентренного сжатия: с растянутым бетоном Ньютон срывается (невязка ≈ 41 кН), без него НДС находится.
    /// Проверка перерешивает без растяжения — без ложного «НДС не найден» (колонны по РСУ (N)/(NL), 01.10.2026).
    /// </summary>
    [Theory]
    [InlineData(CalcType.N)]
    [InlineData(CalcType.NL)]
    public void NormativeSet_CrackedSection_PassesWithoutConcreteTension(CalcType calcType)
    {
        var r = TaskRunner.Run(new CalcTask { Kind = "rc_check", Tag = "rc", CalcType = calcType },
            Column25x60(), new LoadItem { Label = "э.57 с1 к4 A2", N = -67.0, Mx = 27.2, My = -16.6 });

        Assert.Equal("ok", r.Status);
        using var doc = JsonDocument.Parse(r.DataJson);
        Assert.True(doc.RootElement.GetProperty("passed").GetBoolean());
        Assert.False(doc.RootElement.GetProperty("concrete_tension").GetBoolean());
    }

    static CrossSection Column25x60()
    {
        static MaterialChars Concrete(CalcType ct) => new(ct)
        {
            Type = MatType.Concrete, E = 27_500_000.0, Fc = -15_000.0, Ft = 1_350.0,
            Ec0 = -0.002, Ec1 = -0.6 * 15_000.0 / 27_500_000.0, Ec2 = -0.0035, Ec1Red = -0.0015,
            Et0 = 0.0001, Et1 = 0.6 * 1_350.0 / 27_500_000.0, Et2 = 0.00015, Et1Red = 0.00008,
        };
        static MaterialChars Rebar(CalcType ct) => new(ct)
        {
            Type = MatType.ReSteelF, E = 200_000_000.0, Fc = -390_000.0, Ft = 390_000.0,
            Ec0 = -0.00195, Et0 = 0.00195, Ec2 = -0.0035, Et2 = 0.025,
        };
        static Material Make(Material m, Func<CalcType, MaterialChars> chars)
        {
            m.C = chars(CalcType.C); m.CL = chars(CalcType.CL); m.N = chars(CalcType.N); m.NL = chars(CalcType.NL);
            return m;
        }
        var concrete = Make(new Material { Id = 1, Tag = "B20", Type = MatType.Concrete, E = 27_500_000.0 }, Concrete);
        var rebar = Make(new Material { Id = 2, Tag = "A400", Type = MatType.ReSteelF, E = 200_000_000.0 }, Rebar);
        var profile = new CScore.Import.LiraBarProfile(3, 0.25, 0.60, concrete, rebar);
        var (bars, _) = CScore.Import.LiraBarSectionBuilder.SelectedLayout(
            new CScore.Import.LiraAspBarAreas(1.5, 1.5, 1.5, 1.5, 0.01, 0.01, 0.01, 0.01, 0, 0, 0), profile, (4, 4, 4));
        return CScore.Import.LiraBarSectionBuilder.Build("колонна 25×60", profile, bars!);
    }

    /// <summary>До образования трещин растянутый бетон в нормативном наборе учитывается.</summary>
    [Fact]
    public void NormativeSet_UncrackedSection_KeepsConcreteTension()
    {
        var r = Run(mx: -5.0, CalcType.N);

        Assert.Equal("ok", r.Status);
        using var doc = JsonDocument.Parse(r.DataJson);
        Assert.True(doc.RootElement.GetProperty("concrete_tension").GetBoolean());
    }

    [Fact]
    public void FemCheckRunnerCountsRealRcCheckRows()
    {
        var check  = new FemCheck { SchemaId = 1, ElementId = 7, NormCode = "rc_check", Tag = "rc" };
        var member = new FemMember { Id = 7, SchemaId = 1, ElemTag = "7", ElemType = "beam" };
        var fs = new ForceSet
        {
            Id = 1, Tag = "Балка — РСУ (C)",
            Items = [new LoadItem { Label = "мал", Mx = -100 }, new LoadItem { Label = "вел", Mx = -600 }]
        };

        var result = FemCheckRunner.RunMulti(check, member, ReportFixtures.BuildBeam(), null, [fs],
            (task, sect, item) => TaskRunner.Run(task, sect, item));

        using var doc = JsonDocument.Parse(result.DataJson);
        Assert.Equal(1, doc.RootElement.GetProperty("passedRows").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("failedRows").GetInt32());
        Assert.Equal("not_passed", result.Status);
    }

    static CalcResult Run(double mx, CalcType calcType = CalcType.C) => TaskRunner.Run(
        new CalcTask { Kind = "rc_check", Tag = "rc", CalcType = calcType },
        ReportFixtures.BuildBeam(),
        new LoadItem { Label = "1", Mx = mx });
}
