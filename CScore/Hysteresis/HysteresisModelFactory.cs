using CScore.LimitSurface;

namespace CScore.Hysteresis;

/// <summary>Модели материала с состоянием для области по её диаграмме вида расчёта.</summary>
public sealed record AreaModels(IHysteresisModel Main, IHysteresisModel? Host, bool IsConcrete);

public static class HysteresisModelFactory
{
    /// <summary>
    /// Бетон — Karsan–Jirsa над диаграммой области (с обёрткой спада, если она уже стоит); сталь — по опциям, параметры
    /// из диаграммы. Стержень с разностной диаграммой получает второе состояние — бетон-носитель (Karsan–Jirsa над Host).
    /// </summary>
    /// <param name="rebarRuptureLimit">εs2 при роли «Разрушается» у арматуры; null — без отказа.</param>
    public static AreaModels Create(MaterialArea area, CalcType calc, HysteresisOptions o,
        double? rebarRuptureLimit, double softening)
    {
        if (!area.Diagramms.TryGetValue(calc, out var d))
            throw new InvalidOperationException($"Область «{area.Tag}»: нет диаграммы для {calc}.");
        if (StrainCriterionGeometry.IsConcrete(area))
            return new AreaModels(new KarsanJirsaConcrete(d), null, true);

        var steelDiagram = d.Own ?? d;
        var (e, fyT, fyC, b) = SteelParams(steelDiagram);
        IHysteresisModel steel = o.Steel == SteelHysteresisModel.Bilinear
            ? new BilinearSteel(e, fyT, fyC, b)
            : new MenegottoPintoSteel(e, fyT, fyC, b, o.R0, o.CR1, o.CR2);
        if (rebarRuptureLimit is double lim) steel = new RuptureDamage(steel, lim, softening);
        var host = d.Own != null && d.Host != null ? new KarsanJirsaConcrete(d.Host) : null;
        return new AreaModels(steel, host, false);
    }

    /// <summary>
    /// Билинейная аппроксимация диаграммы стали по первому излому: E = σ/ε первого узла растяжения после нуля,
    /// fyT — σ в нём, fyC — |σ| первого узла сжатия, b — наклон второго участка растяжения, делённый на E (0…0,5).
    /// </summary>
    public static (double E, double FyT, double FyC, double B) SteelParams(Diagramm d)
    {
        int it = Array.FindIndex(d.It.X, x => x > 1e-12);
        int ic = Array.FindLastIndex(d.Ic.X, x => x < -1e-12);
        if (it < 0 || ic < 0) throw new InvalidOperationException($"Диаграмма «{d.Tag}»: нет узлов текучести стали.");
        double e = d.It.Y[it] / d.It.X[it];
        double fyT = d.It.Y[it];
        double fyC = Math.Abs(d.Ic.Y[ic]);
        double b = it + 1 < d.It.X.Length
            ? (d.It.Y[it + 1] - d.It.Y[it]) / (d.It.X[it + 1] - d.It.X[it]) / e
            : 0;
        return (e, fyT, fyC, Math.Clamp(b, 0, 0.5));
    }
}
