using CScore;

namespace CScore.Tests;

/// <summary>
/// Тестовые материалы для модульных тестов CScore.
/// Числа взяты из реальных справочников проекта (B25 — OpenCS/DataSource/Бетон_тяжелый_*.csv,
/// A500 — OpenCS/DataSource/Арматура стальная_*.csv), чтобы диаграммы D2L() строились корректно
/// (знаки: сжатие — отрицательные деформации/напряжения, растяжение — положительные).
/// </summary>
internal static class TestMaterials
{
    static int _nextId = 1;

    public static Material Concrete(string tag = "B25")
    {
        var m = new Material
        {
            Id = _nextId++,
            Tag = tag,
            Type = MatType.Concrete,
            E = 30_000_000.0, // кПа
        };
        // Через список, а не через m.C/m.CL/...: сеттеры одиночных свойств заполняют только
        // внутренний словарь, а Material.C читает список materialChars — иначе он остаётся
        // пустым, m.C возвращает null, и потребители (CrossSectionLimitAdapter.ResolveChars)
        // молча уходят на резервные ветки вместо характеристик материала.
        m.MaterialChars =
        [
            ConcreteChars(CalcType.C, fc: -14500, ft: 1050, e: 30_000_000),
            ConcreteChars(CalcType.CL, fc: -13050, ft: 1050, e: 30_000_000),
            ConcreteChars(CalcType.N, fc: -18500, ft: 1550, e: 30_000_000),
            ConcreteChars(CalcType.NL, fc: -18500, ft: 1550, e: 17_857_142.86,
                ec1Red: -0.0024, ec2: -0.0042, et1Red: 0.00019, et2: 0.00027),
        ];
        return m;
    }

    public static Material Rebar(string tag = "A500")
    {
        var m = new Material
        {
            Id = _nextId++,
            Tag = tag,
            Type = MatType.ReSteelF,
            E = 200_000_000.0, // кПа
        };
        m.MaterialChars =
        [
            RebarChars(CalcType.C, fc: -435000, ft: 435000),
            RebarChars(CalcType.CL, fc: -435000, ft: 435000),
            RebarChars(CalcType.N, fc: -500000, ft: 500000),
            RebarChars(CalcType.NL, fc: -500000, ft: 500000),
        ];
        return m;
    }

    /// <summary>
    /// Напрягаемая арматура A1000 (условный предел текучести, ReSteelU) — числа из
    /// OpenCS/DataSource/Арматура стальная_*.csv. Нужна для тестов преднапряжения:
    /// у такой арматуры диаграмма криволинейная уже до Ft, поэтому σ_sp/E и σ(ε_sp)
    /// расходятся.
    /// </summary>
    public static Material PrestressedRebar(string tag = "A1000", double epsSu = 0.015)
    {
        var m = new Material
        {
            Id = _nextId++,
            Tag = tag,
            Type = MatType.ReSteelU,
            E = 200_000_000.0, // кПа
        };
        m.MaterialChars =
        [
            PrestressedRebarChars(CalcType.C, fc: -870000, ft: 870000, et2: epsSu),
            PrestressedRebarChars(CalcType.CL, fc: -870000, ft: 870000, et2: epsSu),
            PrestressedRebarChars(CalcType.N, fc: -1000000, ft: 1000000, et2: epsSu),
            PrestressedRebarChars(CalcType.NL, fc: -1000000, ft: 1000000, et2: epsSu),
        ];
        return m;
    }

    static MaterialChars PrestressedRebarChars(CalcType calc, double fc, double ft, double et2 = 0.015) => new()
    {
        Type = MatType.ReSteelU,
        TypeCalc = calc,
        Fc = fc,
        Ft = ft,
        E = 200_000_000.0,
        Ec0 = -0.00635,
        Ec1 = -0.003915,
        Ec2 = -0.0035,
        Et0 = 0.00635,
        Et1 = 0.003915,
        Et2 = et2,
    };

    static MaterialChars ConcreteChars(CalcType calc, double fc, double ft, double e,
        double ec1Red = -0.0015, double ec2 = -0.0035, double et1Red = 0.00008, double et2 = 0.00015,
        double ec0 = -0.002, double ec1 = -0.00029, double et0 = 0.0001, double et1 = 0.000021) => new()
    {
        Type = MatType.Concrete,
        TypeCalc = calc,
        Fc = fc,
        Ft = ft,
        E = e,
        Ec1Red = ec1Red,
        Ec2 = ec2,
        Et1Red = et1Red,
        Et2 = et2,
        // Нужны трёхлинейной диаграмме (D3L); двухлинейная их не использует, поэтому
        // существующие L2-тесты от их появления не меняются.
        Ec0 = ec0,
        Ec1 = ec1,
        Et0 = et0,
        Et1 = et1,
    };

    static MaterialChars RebarChars(CalcType calc, double fc, double ft) => new()
    {
        Type = MatType.ReSteelF,
        TypeCalc = calc,
        Fc = fc,
        Ft = ft,
        E = 200_000_000.0,
        Ec2 = -0.0035,
        Et2 = 0.025,
    };
}
