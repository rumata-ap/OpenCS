namespace CScore.LimitSurface;

/// <summary>
/// ПС1 по п. 8.1.30 СП 63: бетон — вершины контуров и отверстий своей области с εb0, εb2 её
/// материала (растяжение не ограничено — трещины); арматура — точечные фибры прочих областей с
/// пределом удлинения εs,ult − εp (сжатую арматуру ограничивает бетон). Совпадает с набором
/// точек <see cref="CrossSectionLimitAdapter"/>.
/// </summary>
public sealed class UlsStrainCriterion : IStrainSurfaceCriterion
{
    /// <summary>Создаёт критерий ПС1.</summary>
    public UlsStrainCriterion(CalcType calc = CalcType.C, bool ten = true)
    {
        Calc = calc;
        Ten = ten;
    }

    /// <inheritdoc/>
    public string Name => "ПС1, п. 8.1.30 СП 63";

    /// <inheritdoc/>
    public CalcType Calc { get; }

    /// <inheritdoc/>
    public bool Ten { get; }

    /// <inheritdoc/>
    public StrainDomain BuildDomain(CrossSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        var pts = new List<StrainControlPoint>();
        for (int ai = 0; ai < section.Areas.Count; ai++)
        {
            var area = section.Areas[ai];
            if (IsConcrete(area))
            {
                var ch = area.Material!.GetChars(Calc)
                    ?? throw new InvalidOperationException($"Область «{area.Tag}»: нет характеристик бетона для {Calc}.");
                foreach (var c in Contours(area))
                    foreach (var (x, y) in OpenVertices(c))
                        pts.Add(new StrainControlPoint(x, y, StrainPointKind.Concrete,
                            ch.Ec2, double.PositiveInfinity, ch.Ec0, ch.Ec2, ai));
            }
            else
            {
                double epsSu = RebarEpsSu(area);
                foreach (var f in area.Fibers.Where(f => f.TypeFiber == FiberType.point))
                    pts.Add(new StrainControlPoint(f.X, f.Y, StrainPointKind.Rebar,
                        double.NegativeInfinity, epsSu - f.Eps_p, double.NaN, double.NaN, ai));
            }
        }
        return new StrainDomain(pts);
    }

    static bool IsConcrete(MaterialArea area) =>
        area.Material?.Type == MatType.Concrete ||
        (area.Material?.Type == MatType.Custom && area.Material.BaseType == MatType.Concrete);

    static IEnumerable<Contour> Contours(MaterialArea area)
    {
        if (area.Hull is not null) yield return area.Hull;
        foreach (var h in area.Holes) yield return h;
    }

    /// <summary>Вершины контура без замыкающего повтора и без подряд идущих дублей.</summary>
    static IEnumerable<(double X, double Y)> OpenVertices(Contour c)
    {
        int n = Math.Min(c.X.Count, c.Y.Count);
        if (n == 0) yield break;
        double x0 = c.X[0], y0 = c.Y[0];
        double px = double.NaN, py = double.NaN;
        for (int i = 0; i < n; i++)
        {
            double x = c.X[i], y = c.Y[i];
            if (x == px && y == py) continue;
            if (i == n - 1 && x == x0 && y == y0) break;
            yield return (x, y);
            px = x; py = y;
        }
    }

    double RebarEpsSu(MaterialArea area)
    {
        var ch = area.Material?.GetChars(Calc);
        return ch is not null && double.IsFinite(ch.Et2) && ch.Et2 > 0 ? ch.Et2 : 0.025;
    }
}
