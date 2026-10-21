namespace CScore.Fem.Fields;

/// <summary>
/// Совмещение системы координат таблицы со схемой: масштаб единиц длины, зеркало (X → −X, Y → −Y), поворот вокруг Z
/// на <see cref="AngleDeg"/> против часовой стрелки, сдвиг. Вертикаль у ГТО и схемы общая — пространственного
/// поворота нет.
/// </summary>
public sealed record FieldAlignment(
    double Scale = 1, bool MirrorX = false, bool MirrorY = false, double AngleDeg = 0,
    double Dx = 0, double Dy = 0, double Dz = 0)
{
    public FieldVec3 Apply(FieldVec3 p)
    {
        double x = p.X * Scale * (MirrorX ? -1 : 1), y = p.Y * Scale * (MirrorY ? -1 : 1);
        double a = AngleDeg * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        return new FieldVec3(c * x - s * y + Dx, s * x + c * y + Dy, p.Z * Scale + Dz);
    }
}

/// <summary>Совмещение после подбора и невязка: расстояния от точек таблицы до ближайшей цели, м.</summary>
public sealed record FieldFitResult(FieldAlignment Alignment, double Median, double P95, double Max, int Count);

/// <summary>Автоподбор сдвига (Dx, Dy и при наличии Z — Dz); масштаб, зеркало и угол задаёт пользователь.</summary>
public static class FieldAlignmentFit
{
    /// <summary>По габаритам: центры охватывающих параллелепипедов точек и целей совмещаются.</summary>
    public static FieldFitResult ByExtents(FieldAlignment start, IReadOnlyList<FieldVec3> source,
        IReadOnlyList<FieldVec3> targets, bool hasZ)
    {
        if (source.Count == 0 || targets.Count == 0) return new FieldFitResult(start, 0, 0, 0, 0);
        var sc = Center(source.Select(start.Apply).ToList());
        var tc = Center(targets);
        var a = start with
        {
            Dx = start.Dx + tc.X - sc.X,
            Dy = start.Dy + tc.Y - sc.Y,
            Dz = hasZ ? start.Dz + tc.Z - sc.Z : start.Dz,
        };
        return Residual(a, source, targets, hasZ);
    }

    /// <summary>
    /// По точечным целям (сваи ↔ узлы): от подбора по габаритам — итерации «ближайшая цель → средний сдвиг».
    /// </summary>
    public static FieldFitResult ByPoints(FieldAlignment start, IReadOnlyList<FieldVec3> source,
        IReadOnlyList<FieldVec3> targets, bool hasZ, int iterations = 20)
    {
        var a = ByExtents(start, source, targets, hasZ).Alignment;
        if (source.Count == 0 || targets.Count == 0) return new FieldFitResult(a, 0, 0, 0, 0);
        var tree = new KdTree3(targets.Select(t => Flat(t, hasZ)).ToList());
        for (int it = 0; it < iterations; it++)
        {
            double sx = 0, sy = 0, sz = 0;
            foreach (var s in source)
            {
                var p = Flat(a.Apply(s), hasZ);
                var t = Flat(targets[tree.Nearest(p).Index], hasZ);
                sx += t.X - p.X; sy += t.Y - p.Y; sz += t.Z - p.Z;
            }
            var step = new FieldVec3(sx / source.Count, sy / source.Count, sz / source.Count);
            a = a with { Dx = a.Dx + step.X, Dy = a.Dy + step.Y, Dz = a.Dz + step.Z };
            if (step.Length < 1e-6) break;
        }
        return Residual(a, source, targets, hasZ);
    }

    /// <summary>Невязка совмещения: медиана, 95 % и максимум расстояния точка → ближайшая цель.</summary>
    public static FieldFitResult Residual(FieldAlignment a, IReadOnlyList<FieldVec3> source,
        IReadOnlyList<FieldVec3> targets, bool hasZ)
    {
        if (source.Count == 0 || targets.Count == 0) return new FieldFitResult(a, 0, 0, 0, 0);
        var tree = new KdTree3(targets.Select(t => Flat(t, hasZ)).ToList());
        var d = source.Select(s => tree.Nearest(Flat(a.Apply(s), hasZ)).Distance).OrderBy(x => x).ToList();
        return new FieldFitResult(a, Pct(d, 0.5), Pct(d, 0.95), d[^1], d.Count);
    }

    static FieldVec3 Flat(FieldVec3 v, bool hasZ) => hasZ ? v : v with { Z = 0 };

    static FieldVec3 Center(IReadOnlyList<FieldVec3> p) => new(
        (p.Min(v => v.X) + p.Max(v => v.X)) / 2, (p.Min(v => v.Y) + p.Max(v => v.Y)) / 2, (p.Min(v => v.Z) + p.Max(v => v.Z)) / 2);

    static double Pct(List<double> sorted, double q) => sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * q))];
}
