using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CScore;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Валидационные тесты задачи «Проверка прочности по НДМ» (<see cref="StrengthNDMBatchHandler"/>,
/// <c>Kind = "strength_ndm_batch"</c>) — до сих пор ни одного теста на этот обработчик не было.
///
/// Два независимых теста:
/// 1. <see cref="IVG6_1_EccentricCompressionInPlane_ConvergesAndSatisfiesStrength"/> — обычное
///    (не преднапряжённое) сечение IV.Г.6.1 Пособия Краковского (внецентренное сжатие, B30/A500,
///    4⌀28 по углам, N=1700/M=382 кН·м) — проверяет сам обработчик и критерий εb,ult по
///    ф.8.53/8.1.30 на геометрии, уже провалидированной через <see cref="LimitForceSolver"/> в
///    <c>Sp63EccentricKrakovskyTests</c> (CScore.Tests).
/// 2. <see cref="PrestressedIBeam_PureTransferStage_ConvergesAndSatisfiesStrength"/> — ДЕЙСТВИТЕЛЬНО
///    преднапряжённое сечение (двутавр III.Б.1.1, канаты K1400 с σsp(1)=796,3 МПа через
///    <see cref="Fiber.Eps_p"/>/<see cref="MaterialArea.SigSp"/> — штатный для OpenCS способ
///    моделирования преднапряжения, в отличие от методики книги п.9.2.10, вводящей усилие
///    обжатия как внешнюю N/M — та методика на этой геометрии не сходится у
///    <see cref="StrainSolver"/>, см. историю правок файла/сессии).
/// </summary>
public sealed class StrengthNdmBatchPrestressKrakovskyTests
{
    static Material ConcreteB30()
    {
        var m = new Material { Id = 400, Tag = "B30", Type = MatType.Concrete, E = 32_500_000.0 };
        MaterialChars Chars(CalcType calc) => new()
        {
            Type = MatType.Concrete, TypeCalc = calc, E = 32_500_000.0,
            Fc = -17_000.0, Ft = 1_150.0,
            Ec1Red = -0.0015, Ec0 = -0.002, Ec2 = -0.0035, Et1Red = 0.00008, Et2 = 0.00015,
        };
        m.MaterialChars = [Chars(CalcType.C), Chars(CalcType.CL), Chars(CalcType.N), Chars(CalcType.NL)];
        return m;
    }

    static Material RebarA500()
    {
        var m = new Material { Id = 401, Tag = "A500", Type = MatType.ReSteelF, E = 200_000_000.0 };
        MaterialChars Chars(CalcType calc) => new()
        {
            Type = MatType.ReSteelF, TypeCalc = calc,
            Fc = -435_000.0, Ft = 435_000.0, E = 200_000_000.0,
            Ec2 = -0.025, Et2 = 0.025,
        };
        m.MaterialChars = [Chars(CalcType.C), Chars(CalcType.CL), Chars(CalcType.N), Chars(CalcType.NL)];
        return m;
    }

    const double B = 0.400;
    const double H = 0.500;
    const double Cover = 0.040;
    const double AsTotal = 24.63e-4;

    /// <summary>
    /// Прямоугольное сечение с угловой арматурой — копия
    /// <c>Sp63EccentricKrakovskyTests.RectangularSectionCornerBars</c> (CScore.Tests), включая
    /// знаковую конвенцию y (положительный y — сторона, где формулы пособия дают бОльшее
    /// растяжение/меньшее сжатие).
    /// </summary>
    static CrossSection Section()
    {
        var concrete = ConcreteB30();
        var rebar = RebarA500();

        double y0 = -H / 2.0, y1 = H / 2.0, x0 = -B / 2.0, x1 = B / 2.0;
        var concreteArea = new MaterialArea
        {
            Id = 1,
            Tag = "concrete", Category = AreaCategory.Region,
            Material = concrete, MaterialId = concrete.Id,
            DiagrammType = DiagrammType.L2,
            Hull = new Contour([x0, x1, x1, x0, x0], [y0, y0, y1, y1, y0], "outer"),
        };
        concreteArea.SetWKT();
        concreteArea.SliceXY(nx: 8, ny: 100);

        double xr = B / 2.0 - Cover, yr = H / 2.0 - Cover;
        double areaPerBar = AsTotal / 4.0;
        var corners = new (double x, double y)[] { (-xr, -yr), (xr, -yr), (xr, yr), (-xr, yr) };

        var areas = new List<MaterialArea> { concreteArea };
        int i = 2;
        foreach (var (x, y) in corners)
        {
            var fiber = Fiber.CreatePoint(0.001, x, y);
            fiber.Area = areaPerBar;
            areas.Add(new MaterialArea
            {
                Id = i++,
                Tag = $"rebar_{i}", Category = AreaCategory.RebarGroup,
                Material = rebar, MaterialId = rebar.Id,
                DiagrammType = DiagrammType.L2,
                Fibers = [fiber],
            });
        }

        return new CrossSection { Id = 1, Tag = "IV.Г.6.1 прямоугольное сечение", Areas = areas };
    }

    static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"opencs-strength-ndm-{Guid.NewGuid():N}.db");

    static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    [Fact]
    public void IVG6_1_EccentricCompressionInPlane_ConvergesAndSatisfiesStrength()
    {
        string path = TempPath();
        try
        {
            using var database = new DatabaseService(path);
            var forceSet = new ForceSet
            {
                Id = 1,
                Kind = "bar",
                Tag = "IV.Г.6.1",
                // N — сжатие, отрицательное по конвенции проекта; Mx — книжный знак напрямую
                // (см. описание класса — конвенция этой фикстуры совпадает с книжной без инверсии).
                Items = [new LoadItem { Num = 1, Label = "в плоскости", N = -1700.0, Mx = 382.0 }]
            };
            database.ForceSets.Add(forceSet);

            var task = new CalcTask
            {
                Id = 1,
                Kind = "strength_ndm_batch",
                CalcType = CalcType.C,
                ForceSetId = 1,
            };

            var result = new StrengthNDMBatchHandler().Run(
                task, Section(), new LoadItem(), CalcSettings.Default,
                new TaskRunContext { Database = database });

            Assert.True(result.Status == "ok", $"Status={result.Status}: {result.DataJson}");
            using var doc = JsonDocument.Parse(result.DataJson);
            var row = doc.RootElement.GetProperty("rows")[0];

            Assert.Equal("ok", row.GetProperty("status").GetString());
            Assert.True(row.GetProperty("concrete_ok").GetBoolean());
            Assert.True(row.GetProperty("rebar_ok").GetBoolean());
            Assert.True(row.GetProperty("strength_ok").GetBoolean());

            // N=1700/M=382 заметно меньше предельных (Mult≈772 при том же N — см.
            // Sp63EccentricKrakovskyTests), поэтому деформации бетона должны оставаться
            // в разумных пределах, далёких от εb,ult.
            double epsConcrete = Math.Abs(row.GetProperty("eps_concrete_compression").GetDouble());
            Assert.InRange(epsConcrete, 0.0001, 0.0035);
        }
        finally { TryDelete(path); }
    }

    // ==================================================================================
    // Второй тест: та же задача strength_ndm_batch, но на ДЕЙСТВИТЕЛЬНО преднапряжённом
    // сечении — преднапряжение задаётся штатным для OpenCS способом (Fiber.Eps_p через
    // MaterialArea.SigSp), а не имитируется внешней силой N (как того требует методика
    // книги п.9.2.10, но что не сходится у StrainSolver на этой геометрии — см. класс).
    // ==================================================================================

    const double EsCanat = 195_000_000.0;
    const double RsnCanat = 1_400_000.0;

    static Material ConcreteB40()
    {
        var m = new Material { Id = 300, Tag = "B40", Type = MatType.Concrete, E = 36_000_000.0 };
        MaterialChars Chars(CalcType calc) => new()
        {
            Type = MatType.Concrete, TypeCalc = calc, E = 36_000_000.0, Class = 40,
            Fc = -18_500.0, Ft = 1_550.0,
            Ec1Red = -0.0015, Ec0 = -0.002, Ec2 = -0.0035, Et1Red = 0.00008, Et2 = 0.00015,
        };
        m.MaterialChars = [Chars(CalcType.C), Chars(CalcType.CL), Chars(CalcType.N), Chars(CalcType.NL)];
        return m;
    }

    static Material CanatK1400()
    {
        var m = new Material { Id = 301, Tag = "K1400", Type = MatType.ReSteelU, E = EsCanat };
        MaterialChars Chars(CalcType calc) => new()
        {
            Type = MatType.ReSteelU, TypeCalc = calc, E = EsCanat,
            Fc = -RsnCanat, Ft = RsnCanat, Et2 = 0.015,
        };
        m.MaterialChars = [Chars(CalcType.C), Chars(CalcType.CL), Chars(CalcType.N), Chars(CalcType.NL)];
        return m;
    }

    /// <summary>
    /// Двутавр III.Б.1.1 (та же геометрия, что и в <c>PrestressLossKrakovskyTests.IBeamSection</c>),
    /// но с напряжением в канатах, заданным через штатный <see cref="MaterialArea.SigSp"/>
    /// (σsp(1)=796,3 МПа — напряжение с учётом первых потерь, уже подтверждённое в
    /// <c>PrestressLossKrakovskyTests.Compute_BaseExample_FirstLossesAndPFirstMatchManualControl</c>
    /// для всех 5 групп канатов одинаково). Начало координат сдвинуто в центр тяжести сечения.
    /// </summary>
    static CrossSection PrestressedIBeamSection()
    {
        var concreteMaterial = ConcreteB40();
        var canatMaterial = CanatK1400();

        double[] rawX = [0.040, 0.320, 0.320, 0.220, 0.220, 0.360, 0.360, 0.000, 0.000, 0.140, 0.140, 0.040, 0.040];
        double[] rawY = [0.000, 0.000, 0.250, 0.250, 1.260, 1.260, 1.500, 1.500, 1.260, 1.260, 0.250, 0.250, 0.000];
        double ycg = new GeoProps(new Contour(rawX, rawY, "raw")).Sx / new GeoProps(new Contour(rawX, rawY, "raw")).A;
        double Y(double y) => y - ycg;

        var hull = new Contour(rawX, rawY.Select(Y).ToArray(), "outer");
        var concrete = new MaterialArea
        {
            Id = 1, Tag = "concrete", Category = AreaCategory.Region,
            Material = concreteMaterial, MaterialId = concreteMaterial.Id,
            DiagrammType = DiagrammType.L2, Hull = hull,
        };
        concrete.SetWKT();
        concrete.SliceXY(nx: 8, ny: 150);

        var areas = new List<MaterialArea> { concrete };

        double[] tensionRowsY = [0.050, 0.100, 0.150, 0.200];
        double[] columnsX = [0.090, 0.180, 0.270];
        int nextId = 2;
        foreach (var y in tensionRowsY)
        {
            areas.Add(new MaterialArea
            {
                Id = nextId++,
                Tag = $"tension_y{y:0.000}",
                Category = AreaCategory.RebarGroup,
                Material = canatMaterial, MaterialId = canatMaterial.Id,
                SigSp = 796.3, GammaSp = 1.0,
                Fibers = columnsX.Select(x => Fiber.CreatePoint(0.015, x, Y(y))).ToList(),
            });
        }

        areas.Add(new MaterialArea
        {
            Id = nextId,
            Tag = "compression_y1.450",
            Category = AreaCategory.RebarGroup,
            Material = canatMaterial, MaterialId = canatMaterial.Id,
            SigSp = 796.3, GammaSp = 1.0,
            Fibers =
            [
                Fiber.CreatePoint(0.015, 0.050, Y(1.450)),
                Fiber.CreatePoint(0.015, 0.190, Y(1.450)),
                Fiber.CreatePoint(0.015, 0.330, Y(1.450)),
            ],
        });

        return new CrossSection { Id = 2, Tag = "III.Б.1.1 двутавр (преднапряжённый)", Areas = areas };
    }

    /// <summary>
    /// Двутавр III.Б.1.1 с реальным преднапряжением канатов (σsp(1)=796,3 МПа через
    /// <see cref="MaterialArea.SigSp"/>/<see cref="Fiber.Eps_p"/> — штатный для OpenCS способ,
    /// в отличие от книжной методики п.9.2.10, вводящей усилие обжатия как внешнюю N/M).
    /// Внешняя нагрузка — нулевая (чистое обжатие, без момента от собственного веса):
    /// проверяем, что одно только преднапряжение не превышает прочность бетона.
    /// </summary>
    [Fact]
    public void PrestressedIBeam_PureTransferStage_ConvergesAndSatisfiesStrength()
    {
        string path = TempPath();
        try
        {
            using var database = new DatabaseService(path);
            var forceSet = new ForceSet
            {
                Id = 1,
                Kind = "bar",
                Tag = "обжатие (без внешней нагрузки)",
                Items = [new LoadItem { Num = 1, Label = "обжатие", N = 0.0, Mx = 0.0 }]
            };
            database.ForceSets.Add(forceSet);

            var task = new CalcTask
            {
                Id = 2,
                Kind = "strength_ndm_batch",
                CalcType = CalcType.C,
                ForceSetId = 1,
            };

            var result = new StrengthNDMBatchHandler().Run(
                task, PrestressedIBeamSection(), new LoadItem(), CalcSettings.Default,
                new TaskRunContext { Database = database });

            Assert.True(result.Status == "ok", $"Status={result.Status}: {result.DataJson}");
            using var doc = JsonDocument.Parse(result.DataJson);
            var row = doc.RootElement.GetProperty("rows")[0];

            Assert.Equal("ok", row.GetProperty("status").GetString());
            Assert.True(row.GetProperty("concrete_ok").GetBoolean());
            Assert.True(row.GetProperty("rebar_ok").GetBoolean());
            Assert.True(row.GetProperty("strength_ok").GetBoolean());

            double epsConcrete = Math.Abs(row.GetProperty("eps_concrete_compression").GetDouble());
            Assert.InRange(epsConcrete, 0.00001, 0.0035);
        }
        finally { TryDelete(path); }
    }
}
