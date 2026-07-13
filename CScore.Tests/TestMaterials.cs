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
        m.C = ConcreteChars(CalcType.C, fc: -14500, ft: 1050, e: 30_000_000);
        m.CL = ConcreteChars(CalcType.CL, fc: -13050, ft: 1050, e: 30_000_000);
        m.N = ConcreteChars(CalcType.N, fc: -18500, ft: 1550, e: 30_000_000);
        m.NL = ConcreteChars(CalcType.NL, fc: -18500, ft: 1550, e: 17_857_142.86,
            ec1Red: -0.0024, ec2: -0.0042, et1Red: 0.00019, et2: 0.00027);
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
        m.C = RebarChars(fc: -435000, ft: 435000);
        m.CL = RebarChars(fc: -435000, ft: 435000);
        m.N = RebarChars(fc: -500000, ft: 500000);
        m.NL = RebarChars(fc: -500000, ft: 500000);
        return m;
    }

    static MaterialChars ConcreteChars(CalcType calc, double fc, double ft, double e,
        double ec1Red = -0.0015, double ec2 = -0.0035, double et1Red = 0.00008, double et2 = 0.00015) => new()
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
    };

    static MaterialChars RebarChars(double fc, double ft) => new()
    {
        Type = MatType.ReSteelF,
        Fc = fc,
        Ft = ft,
        E = 200_000_000.0,
        Ec2 = -0.0035,
        Et2 = 0.025,
    };
}
