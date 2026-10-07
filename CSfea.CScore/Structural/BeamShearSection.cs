using CScore;
using CScore.Sp63Shear;
using CSfea.Core;

namespace CSfea.CScoreBridge.Structural;

/// <summary>
/// Закон сдвига стержня с наклонными трещинами по одному направлению (единицы CSfea, Н): Q = Qcr + <see cref="Kv"/>·(γ − γcr)
/// после трещины. Площадки (текучести хомутов) нет: закон — для деформаций, прочность наклонных сечений проверяется
/// отдельно. Q_cr при трещине вместе с нормальной мал, и по ферме вся Q сверх него шла бы в хомуты — площадка
/// Q_cr + R_sw·A_sw·h₀/s_w без вклада бетона остановила бы балку C3 Vecchio–Shim на 200 кН при опытных 265 кН (изгиб).
/// </summary>
/// <param name="Kv">Жёсткость сдвига стенки с наклонными трещинами по ферменной аналогии, Н.</param>
public sealed record BeamShearCracked(double Kv);

/// <summary>
/// Сдвиговые характеристики стержня по сечению CScore для КЭ Тимошенко секущего расчёта (оси КЭ CSfea: y ↔ X сечения,
/// плоскость <see cref="ShearPlane.Vx"/>; z ↔ Y сечения, плоскость <see cref="ShearPlane.Vy"/>).
/// <list type="bullet">
/// <item>До наклонной трещины GA₀ = G·A_v, G = 0,4·E_b (п. 6.1.15 СП 63), A_v = min(5/6·A_b, b_w·h).</item>
/// <item>После — ферменная аналогия Park &amp; Paulay (1975, гл. 10.4; θ = 45°, вертикальные хомуты):
/// K_v = ρ_w/(1 + 4·n·ρ_w)·E_s·b_w·h₀, ρ_w = A_sw/(b_w·s_w), n = E_s/E_b (E_s — модуль материала хомутов).</item>
/// </list>
/// b_w — суммарная хорда бетона на уровне его центра тяжести, h — размер бетона по направлению Q, h₀ — от сжатой грани до
/// центра тяжести продольной арматуры растянутой половины (знак — по кривизне в момент трещины; нет арматуры —
/// 0,9·h). Без хомутов закона после трещины нет — сдвиг остаётся упругим.
/// </summary>
public sealed class BeamShearSection
{
    private readonly BeamShearCracked?[] _y = new BeamShearCracked?[2], _z = new BeamShearCracked?[2];

    private BeamShearSection(BeamShearStiffness initial) => Initial = initial;

    /// <summary>Упругие сдвиговые жёсткости GA₀ по осям КЭ y и z, Н.</summary>
    public BeamShearStiffness Initial { get; }

    /// <summary>Закон после трещины по оси КЭ y (<paramref name="tensionPositive"/> — растянута сторона +X сечения).</summary>
    public BeamShearCracked? CrackedY(bool tensionPositive) => _y[tensionPositive ? 1 : 0];

    /// <summary>Закон после трещины по оси КЭ z (<paramref name="tensionPositive"/> — растянута сторона +Y сечения).</summary>
    public BeamShearCracked? CrackedZ(bool tensionPositive) => _z[tensionPositive ? 1 : 0];

    /// <summary>Характеристики по сечению; null — в сечении нет бетона (сдвиг не учитывается).</summary>
    public static BeamShearSection? From(CrossSection section, CalcType calc)
    {
        ArgumentNullException.ThrowIfNull(section);
        var concrete = section.Areas.Where(a => MaterialArea.IsCalcActive(a) && a.Material?.Type == MatType.Concrete
                                                && a.Hull != null).ToList();
        if (concrete.Count == 0) return null;

        double ab = 0.0, sx = 0.0, sy = 0.0, eb = 0.0;
        double xMin = double.MaxValue, xMax = double.MinValue, yMin = double.MaxValue, yMax = double.MinValue;
        foreach (var a in concrete)
        {
            var (area, cx, cy) = Polygon(a.Hull!);
            foreach (var h in a.Holes)
            {
                var (ha, hx, hy) = Polygon(h);
                cx = (cx * area - hx * ha) / (area - ha);
                cy = (cy * area - hy * ha) / (area - ha);
                area -= ha;
            }
            ab += area; sx += cx * area; sy += cy * area;
            double e = a.Material!.GetChars(calc)?.E ?? a.Material.E;
            eb += e * area;
            xMin = Math.Min(xMin, a.Hull!.X.Min()); xMax = Math.Max(xMax, a.Hull.X.Max());
            yMin = Math.Min(yMin, a.Hull.Y.Min()); yMax = Math.Max(yMax, a.Hull.Y.Max());
        }
        if (!(ab > 0.0) || !(eb > 0.0)) return null;
        double xc = sx / ab, yc = sy / ab;
        eb /= ab;

        // Продольная арматура (точечные фибры).
        var bars = section.Areas.Where(a => MaterialArea.IsCalcActive(a) && a.Material?.Type is MatType.ReSteelF or MatType.ReSteelU)
            .SelectMany(a => a.Fibers.Where(f => f.TypeFiber == FiberType.point)).ToList();

        // Хомуты: Σ E_s·A_sw/s_w по плоскостям.
        double esVy = 0.0, esVx = 0.0;
        foreach (var group in section.Areas.SelectMany(a => a.Stirrups))
        {
            var chars = section.Areas.Select(a => a.Material).FirstOrDefault(m => m != null && m.Id == group.MaterialId)
                ?.GetChars(calc);
            if (chars == null || !(group.SpacingM > 0.0)) continue;
            foreach (var el in group.Elements)
            {
                var (vy, vx) = StirrupResolver.BranchAreas(el);
                esVy += chars.E * vy / group.SpacingM;
                esVx += chars.E * vx / group.SpacingM;
            }
        }

        double g = 0.4 * eb;
        double bwVy = ChordWidthScanner.ChordLengthAt(concrete, ShearPlane.Vy, yc);
        double bwVx = ChordWidthScanner.ChordLengthAt(concrete, ShearPlane.Vx, xc);
        double gaZ = g * AvOf(ab, bwVy, yMax - yMin) * UnitScale.Force;
        double gaY = g * AvOf(ab, bwVx, xMax - xMin) * UnitScale.Force;
        var r = new BeamShearSection(new BeamShearStiffness(gaY, gaZ));

        foreach (bool pos in new[] { false, true })
        {
            r._z[pos ? 1 : 0] = Cracked(esVy, bwVy, eb, H0(bars.Select(f => (f.Y, f.Area)), pos, yc, yMin, yMax));
            r._y[pos ? 1 : 0] = Cracked(esVx, bwVx, eb, H0(bars.Select(f => (f.X, f.Area)), pos, xc, xMin, xMax));
        }
        return r;
    }

    private static double AvOf(double ab, double bw, double h) => bw > 0.0 ? Math.Min(5.0 / 6.0 * ab, bw * h) : 5.0 / 6.0 * ab;

    // h₀ при растянутой стороне + (pos) или −: от сжатой грани до центра тяжести стержней растянутой половины.
    private static double H0(IEnumerable<(double V, double Area)> bars, bool pos, double c, double min, double max)
    {
        var side = bars.Where(b => pos ? b.V > c : b.V < c).ToList();
        double a = side.Sum(b => b.Area);
        if (!(a > 0.0)) return 0.9 * (max - min);
        double s = side.Sum(b => b.V * b.Area) / a;
        return pos ? s - min : max - s;
    }

    private static BeamShearCracked? Cracked(double esAswS, double bw, double eb, double h0)
    {
        if (!(esAswS > 0.0) || !(bw > 0.0) || !(h0 > 0.0)) return null;
        // ρ_w·E_s·b_w = Σ E_s·A_sw/s_w, n·ρ_w = Σ E_s·A_sw/s_w / (E_b·b_w).
        double kv = esAswS * h0 / (1.0 + 4.0 * esAswS / (eb * bw));
        return new BeamShearCracked(kv * UnitScale.Force);
    }

    private static (double Area, double Cx, double Cy) Polygon(Contour c)
    {
        double a = 0.0, cx = 0.0, cy = 0.0;
        int n = Math.Min(c.X.Count, c.Y.Count);
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            double cr = c.X[i] * c.Y[j] - c.X[j] * c.Y[i];
            a += cr;
            cx += (c.X[i] + c.X[j]) * cr;
            cy += (c.Y[i] + c.Y[j]) * cr;
        }
        a *= 0.5;
        if (Math.Abs(a) < 1e-300) return (0.0, 0.0, 0.0);
        return (Math.Abs(a), cx / (6.0 * a), cy / (6.0 * a));
    }
}
