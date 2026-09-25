namespace CScore.Sp16;

/// <summary>Табличные данные СП 16.13330.2017 (изм. № 1–6) и интерполяция.</summary>
public static partial class Sp16Tables
{
    /// <summary>Линейная интерполяция по упорядоченной сетке с ограничением краями.</summary>
    public static double Interp(double x, IReadOnlyList<double> xs, IReadOnlyList<double> ys)
    {
        if (x <= xs[0]) return ys[0];
        if (x >= xs[^1]) return ys[^1];
        for (int i = 0; i < xs.Count - 1; i++)
            if (x <= xs[i + 1])
                return ys[i] + (ys[i + 1] - ys[i]) * (x - xs[i]) / (xs[i + 1] - xs[i]);
        return ys[^1];
    }

    /// <summary>Билинейная интерполяция по таблице values[row, col] (ряды — xs, столбцы — ys).</summary>
    public static double Interp2(double x, double y, IReadOnlyList<double> xs, IReadOnlyList<double> ys, double[,] values)
    {
        var col = new double[xs.Count];
        for (int i = 0; i < xs.Count; i++)
        {
            var row = new double[ys.Count];
            for (int j = 0; j < ys.Count; j++) row[j] = values[i, j];
            col[i] = Interp(y, ys, row);
        }
        return Interp(x, xs, col);
    }

    // ── Табл. 9: предельная условная гибкость стенки центрально сжатых элементов ──

    /// <summary>
    /// λ̄uw по табл. 9 (формулы (23)–(29)); null — сечение в табл. 9 не приведено.
    /// Для коробчатого сечения — для стенок, параллельных плоскости проверки устойчивости
    /// (λ̄ этой плоскости), и для поясных листов по 7.3.9 (как для стенок короба).
    /// </summary>
    public static double? WebLimitCentral(Sp16Section s, double lambdaBar, out string? note)
    {
        note = null;
        switch (s.Kind)
        {
            case SteelProfileKind.IBeam:
                return lambdaBar <= 2 ? 1.30 + 0.15 * lambdaBar * lambdaBar : Math.Min(1.20 + 0.35 * lambdaBar, 2.3);
            case SteelProfileKind.Box:
                return lambdaBar <= 1 ? 1.2 : Math.Min(1.0 + 0.2 * lambdaBar, 1.6);
            case SteelProfileKind.Channel when s.Profile.Fabrication != SteelFabrication.Bent:
                return lambdaBar <= 1 ? 1.2 : Math.Min(1.0 + 0.2 * lambdaBar, 1.6);
            case SteelProfileKind.Channel:
                return lambdaBar <= 0.8 ? 1.0 : Math.Min(0.85 + 0.19 * lambdaBar, 1.6);
            case SteelProfileKind.Tee:
            {
                double lb = Math.Clamp(lambdaBar, 0.8, 4.0);
                double ratio = s.Hef > 0 ? s.Profile.Bf1 / s.Hef : 0;
                if (ratio < 1 || ratio > 2)
                {
                    note = $"табл. 9, прим. 2: для тавра должно быть 1 ≤ bf/hef ≤ 2 (bf/hef = {ratio:0.###}); отношение ограничено этим диапазоном";
                    ratio = Math.Clamp(ratio, 1, 2);
                }
                return (0.40 + 0.07 * lb) * (1 + 0.25 * Math.Sqrt(2 - ratio));
            }
            default:
                return null;
        }
    }

    // ── Табл. 10: предельная условная гибкость свеса пояса (полки) центрально сжатых ──

    /// <summary>
    /// λ̄uf по табл. 10 (формулы (37)–(39)); λ̄ ограничивается диапазоном 0,8…4 (7.3.8).
    /// Двутавр, тавр — (37); швеллер и неравнополочный уголок — (38); равнополочный уголок — (39).
    /// </summary>
    public static double? FlangeLimitCentral(Sp16Section s, double lambdaBar)
    {
        double lb = Math.Clamp(lambdaBar, 0.8, 4.0);
        return s.Kind switch
        {
            SteelProfileKind.IBeam or SteelProfileKind.Tee => 0.36 + 0.10 * lb,
            SteelProfileKind.Channel => 0.43 + 0.08 * lb,
            SteelProfileKind.Angle => Math.Abs(s.Profile.H - s.Profile.Bf1) <= 1e-6 ? 0.40 + 0.07 * lb : 0.43 + 0.08 * lb,
            _ => null,
        };
    }

    // ── Табл. 32 и 33: предельные гибкости ──

    /// <summary>λu по табл. 32; α = N/(φARyγc) ≥ 0,5.</summary>
    public static double CompressionSlendernessLimit(CompressionMemberCategory c, double alpha)
    {
        double a = Math.Max(alpha, 0.5);
        return c switch
        {
            CompressionMemberCategory.TrussChordPlanar => 180 - 60 * a,
            CompressionMemberCategory.TrussChordSpatial => 120,
            CompressionMemberCategory.TrussWebPlanar => 210 - 60 * a,
            CompressionMemberCategory.TrussWebSpatialBolted => 220 - 40 * a,
            CompressionMemberCategory.TrussTopChordErection => 220,
            CompressionMemberCategory.MainColumn => 180 - 60 * a,
            CompressionMemberCategory.SecondaryColumn => 210 - 60 * a,
            CompressionMemberCategory.Bracing => 200,
            _ => 150,
        };
    }

    /// <summary>Табл. 32: зависит ли предел от α.</summary>
    public static bool CompressionLimitDependsOnAlpha(CompressionMemberCategory c) => c is
        CompressionMemberCategory.TrussChordPlanar or CompressionMemberCategory.TrussWebPlanar
        or CompressionMemberCategory.TrussWebSpatialBolted or CompressionMemberCategory.MainColumn
        or CompressionMemberCategory.SecondaryColumn;

    /// <summary>λu по табл. 33; null — для сочетания позиции и вида нагрузки значение не установлено («–»).</summary>
    public static double? TensionSlendernessLimit(TensionMemberCategory c, TensionLoadKind load)
    {
        // Столбцы: динамические, статические, краны/ж.д. составы.
        double?[] row = c switch
        {
            TensionMemberCategory.TrussChord => [250, 400, 250],
            TensionMemberCategory.TrussWeb => [350, 400, 300],
            TensionMemberCategory.CraneTrussBottomChord => [null, null, 150],
            TensionMemberCategory.ColumnBracing => [300, 300, 200],
            TensionMemberCategory.OtherBracing => [400, 400, 300],
            TensionMemberCategory.PowerLineChord => [250, null, null],
            TensionMemberCategory.PowerLineOther => [350, null, null],
            _ => [150, null, null],
        };
        return row[(int)load];
    }
}
