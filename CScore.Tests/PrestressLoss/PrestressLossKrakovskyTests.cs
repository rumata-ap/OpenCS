using System.Collections.Generic;
using System.Linq;
using CScore.PrestressLoss;
using Xunit;

namespace CScore.Tests.PrestressLoss;

/// <summary>
/// Валидационный тест по примеру III.Б.1.1 Пособия Краковского к СП 63.13330.2012
/// (натяжение на упоры, двутавр, полный «ручной контроль» потерь преднапряжения) —
/// раздел III «Предварительное напряжение арматуры», формулы 9.1–9.13 СП63.
///
/// Геометрия сечения (h=1500, b=80 — стенка; нижняя (растянутая) полка 280×250,
/// верхняя (сжатая) полка 360×240 мм) реконструирована по рисунку III.1 примера
/// (таблица координат арматуры в исходном ПО) и проверена численно: расстояние до ц.т.
/// приведённого сечения совпадает с книжным y_cg=0,774 м с точностью ~0,5% — расхождение
/// в пределах точности реконструкции контура по растровому рисунку, не связано с кодом.
/// </summary>
public sealed class PrestressLossKrakovskyTests
{
    const double EsCanat = 195_000_000.0;   // К1400, кПа
    const double RsnCanat = 1_400_000.0;    // Rs,n К1400, кПа (используется как Ft при CalcType.N)
    const double EbB40 = 36_000_000.0;      // кПа, задано в примере (не по таблице 6.11)

    static Material CanatK1400()
    {
        var m = new Material { Id = 200, Tag = "K1400", Type = MatType.ReSteelU, E = EsCanat };
        MaterialChars Chars(CalcType calc) => new()
        {
            Type = MatType.ReSteelU, TypeCalc = calc, E = EsCanat,
            Fc = -RsnCanat, Ft = RsnCanat,
        };
        m.MaterialChars = [Chars(CalcType.C), Chars(CalcType.CL), Chars(CalcType.N), Chars(CalcType.NL)];
        return m;
    }

    static Material ConcreteB40()
    {
        var m = new Material { Id = 201, Tag = "B40", Type = MatType.Concrete, E = EbB40 };
        MaterialChars Chars(CalcType calc) => new()
        {
            Type = MatType.Concrete, TypeCalc = calc, E = EbB40, Class = 40,
            Fc = -18_500.0, Ft = 1_550.0,
        };
        m.MaterialChars = [Chars(CalcType.C), Chars(CalcType.CL), Chars(CalcType.N), Chars(CalcType.NL)];
        return m;
    }

    /// <summary>
    /// Двутавр III.Б.1.1: h=1,500; b(стенка)=0,080; нижняя полка 0,280×0,250;
    /// верхняя полка 0,360×0,240 м. Контур в м, обход CCW (иначе центр тяжести
    /// (Sx/Sy в <see cref="GeoProps"/>) получит неверный знак).
    /// Возвращает (сечение, id пяти арматурных групп: 4 растянутых ряда снизу вверх + 1 сжатый).
    /// </summary>
    static (CrossSection Section, int[] GroupAreaIds) IBeamSection()
    {
        var concreteMaterial = ConcreteB40();
        var canatMaterial = CanatK1400();

        var hull = new Contour(
            [0.040, 0.320, 0.320, 0.220, 0.220, 0.360, 0.360, 0.000, 0.000, 0.140, 0.140, 0.040, 0.040],
            [0.000, 0.000, 0.250, 0.250, 1.260, 1.260, 1.500, 1.500, 1.260, 1.260, 0.250, 0.250, 0.000],
            "outer");

        var concrete = new MaterialArea
        {
            Id = 1,
            Tag = "concrete",
            Category = AreaCategory.Region,
            Material = concreteMaterial,
            MaterialId = concreteMaterial.Id,
            Hull = hull,
        };

        var areas = new List<MaterialArea> { concrete };
        var groupIds = new List<int>();

        // Растянутая арматура: 4 ряда по 3 каната Ø15, y = 50/100/150/200 мм от низа,
        // x = 90/180/270 мм (столбцы симметричны в нижней полке [40;320] с отступом 50 мм).
        double[] tensionRowsY = [0.050, 0.100, 0.150, 0.200];
        double[] columnsX = [0.090, 0.180, 0.270];
        int nextId = 2;
        foreach (var y in tensionRowsY)
        {
            var group = new MaterialArea
            {
                Id = nextId,
                Tag = $"tension_y{y:0.000}",
                Category = AreaCategory.RebarGroup,
                Material = canatMaterial,
                MaterialId = canatMaterial.Id,
                Fibers = columnsX.Select(x => Fiber.CreatePoint(0.015, x, y)).ToList(),
            };
            areas.Add(group);
            groupIds.Add(nextId);
            nextId++;
        }

        // Сжатая арматура: 1 ряд, 3 каната Ø15, y = 1450 мм, x = 50/190/330 мм.
        var compression = new MaterialArea
        {
            Id = nextId,
            Tag = "compression_y1.450",
            Category = AreaCategory.RebarGroup,
            Material = canatMaterial,
            MaterialId = canatMaterial.Id,
            Fibers =
            [
                Fiber.CreatePoint(0.015, 0.050, 1.450),
                Fiber.CreatePoint(0.015, 0.190, 1.450),
                Fiber.CreatePoint(0.015, 0.330, 1.450),
            ],
        };
        areas.Add(compression);
        groupIds.Add(nextId);

        var section = new CrossSection { Id = 1, Tag = "III.Б.1.1 двутавр", Areas = areas };
        return (section, groupIds.ToArray());
    }

    static PrestressGroupParams TensionedGroup(int areaId) => new()
    {
        AreaId = areaId,
        SigSp0 = 980.0,
        RelaxFormula = RelaxFormula.ColdDrawnOrStrand,
        SubMethod = TensionSubMethod.Mechanical,
        UseDefaultDeltaT = true,
        UseDefaultFormDeform = true,
        UseDefaultAnchorDeform = true,
        LAnchor = 20.0,
    };

    [Fact]
    public void Compute_BaseExample_FirstLossesAndPFirstMatchManualControl()
    {
        var (section, groupIds) = IBeamSection();
        var p = new PrestressLossParams
        {
            Method = TensionMethod.OnSupports,
            Humidity = HumidityClass.H40_75,
            HeatTreated = false,
            ConcreteClassAuto = true,
            Groups = groupIds.Select(TensionedGroup).ToList(),
        };

        var result = PrestressLossCalc.Compute(p, section);

        Assert.Empty(result.Errors);

        // Первые потери не зависят от положения арматуры (ф. 9.3, 9.5, 9.6, 9.7) —
        // должны совпасть с «ручным контролем» книги практически точно для ВСЕХ 5 групп.
        foreach (var group in result.Groups)
        {
            Assert.Equal(52.9, group.DSp1, 1);
            Assert.Equal(81.3, group.DSp2, 0);   // 1,25×65=81,25 — книга округляет до 81,3
            Assert.Equal(30.0, group.DSp3, 1);
            Assert.Equal(19.5, group.DSp4, 1);
            Assert.Equal(183.7, group.TotalFirst, 0);
            Assert.Equal(796.3, group.SigSp1, 0);
        }

        // P_(1) = Σ Aspj·σsp(1)j — не зависит от Ared/Ired/A (только от площади арматуры
        // и SigSp1), поэтому должно совпасть с книжными 2111 кН практически точно.
        Assert.Equal(2111.0, result.PrecompForceFirst, 0);
    }

    [Fact]
    public void Compute_BaseExample_ShrinkageLossMatchesManualControl()
    {
        var (section, groupIds) = IBeamSection();
        // σ_bpj задаём вручную книжным значением 13,9 МПа (п. 9.1.11, формула 9.14,
        // включает момент от внешней нагрузки M=238 кН·м) — PrestressLossCalc пока не
        // реализует формулу 9.14 (нет параметра M), поэтому автоматический σ_bpj без
        // внешнего момента здесь не тестируем как «расхождение с книгой», а обходим
        // ручным заданием, чтобы изолированно проверить ф. 9.8 (усадка, не зависит от
        // σ_bpj вовсе) и структуру ф. 9.9 отдельно (см. следующий тест).
        var p = new PrestressLossParams
        {
            Method = TensionMethod.OnSupports,
            Humidity = HumidityClass.H40_75,
            ConcreteClassAuto = true,
            Groups = [TensionedGroup(groupIds[0])],
        };
        p.Groups[0].SigmaBpAuto = false;
        p.Groups[0].SigmaBpManual = 13.9;

        var result = PrestressLossCalc.Compute(p, section);

        Assert.Empty(result.Errors);
        // Усадка (ф. 9.8) зависит только от класса бетона/условий твердения — не от
        // σ_bpj и не от положения арматуры: точное совпадение с книгой (48,8 МПа).
        Assert.Equal(48.8, result.Groups[0].DSp5, 1);
    }

    /// <summary>
    /// Формула 9.9 (ползучесть, Δσsp6): при подстановке книжного σ_bpj=13,9 МПа
    /// код даёт ~105,6 МПа вместо книжных 89,1 — РАСХОЖДЕНИЕ С КНИГОЙ, не баг кода.
    ///
    /// Причина численно подтверждена сверкой с официальным текстом СП63.13330.2018
    /// (п. 9.1.9, формула 9.9): "μ_spj — коэффициент армирования, равный Aspj/A, где
    /// A ... площадь поперечного сечения ЭЛЕМЕНТА" — код (PrestressLossCalc.A_concrete)
    /// буквально следует этому тексту и берёт ВАЛОВУЮ площадь всего сечения (≈0,237 м²
    /// для этого двутавра). Пособие Краковского в «ручном контроле» примера III.Б.1.1
    /// использует A=0,07 м² — площадь ОДНОЙ (нижней, растянутой) полки, 280×250 мм,
    /// а не всего элемента. Сверка со старым СНиП 2.03.01-84* (формулы поз.6,9 табл.5)
    /// показала, что там используется СОВСЕМ ДРУГАЯ (не через μsp/Ared/Ired) формула
    /// потерь от ползучести — то есть текущая формула (9.9) появилась позже и не имеет
    /// в старом СНиП аналога, подтверждающего локальную трактовку A у Краковского.
    /// Итог: код корректен по букве действующего СП63; расхождение — особенность
    /// (вероятно, ошибка) конкретного примера Пособия, а не дефект OpenCS.
    /// </summary>
    [Fact]
    public void Compute_BaseExample_CreepLossDiffersFromManualControlByGrossVsLocalArea()
    {
        var (section, groupIds) = IBeamSection();
        var p = new PrestressLossParams
        {
            Method = TensionMethod.OnSupports,
            Humidity = HumidityClass.H40_75,
            ConcreteClassAuto = true,
            Groups = [TensionedGroup(groupIds[0])],
        };
        p.Groups[0].SigmaBpAuto = false;
        p.Groups[0].SigmaBpManual = 13.9;

        var result = PrestressLossCalc.Compute(p, section);

        Assert.Empty(result.Errors);
        // Код (валовая A по букве СП63): 105,57 МПа. Книга (локальная A полки): 89,1 МПа.
        Assert.Equal(105.57, result.Groups[0].DSp6, 1);
    }
}
