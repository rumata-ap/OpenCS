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
            if (StrainCriterionGeometry.IsConcrete(area))
            {
                var ch = area.Material!.GetChars(Calc)
                    ?? throw new InvalidOperationException($"Область «{area.Tag}»: нет характеристик бетона для {Calc}.");
                foreach (var c in StrainCriterionGeometry.Contours(area))
                    foreach (var (x, y) in StrainCriterionGeometry.OpenVertices(c))
                        pts.Add(new StrainControlPoint(x, y, StrainPointKind.Concrete,
                            ch.Ec2, double.PositiveInfinity, ch.Ec0, ch.Ec2, ai));
            }
            else
            {
                double epsSu = RebarEpsSu(area, Calc);
                foreach (var f in area.Fibers.Where(f => f.TypeFiber == FiberType.point))
                    pts.Add(new StrainControlPoint(f.X, f.Y, StrainPointKind.Rebar,
                        double.NegativeInfinity, epsSu - f.Eps_p, double.NaN, double.NaN, ai));
            }
        }
        return new StrainDomain(pts);
    }

    /// <summary>Предельная деформация растяжения арматуры εs2 (Et2 материала; иначе 0,025).</summary>
    internal static double RebarEpsSu(MaterialArea area, CalcType calc)
    {
        var ch = area.Material?.GetChars(calc);
        return ch is not null && double.IsFinite(ch.Et2) && ch.Et2 > 0 ? ch.Et2 : 0.025;
    }
}
