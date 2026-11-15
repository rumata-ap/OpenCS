using CScore.Fem;
using CScore.Fem.Fragment.Masks;
using CScore.Fem.Loads;
using CScore.Planar;

namespace CScore.Sp63Punching.Fem;

/// <summary>
/// Справочный способ C для источников без узловых сил КЭ (импорт ЛИРА-САПР/SCAD): интеграл действия внешней части
/// плиты на границе основания пирамиды по усилиям пластин в центре КЭ (<see cref="FragmentShellForceRecovery.Traction"/>:
/// поперечная сила Q·n), кусочно-постоянно по КЭ, через которые проходит граница. Поперечные силы программ и CSfea на
/// одной сетке расходятся до четверти — поэтому способ справочный, главный у импорта — по стержням колонн.
/// </summary>
public static class PunchingFemShellCut
{
    /// <summary>Точек на отрезке на размер КЭ.</summary>
    const int PointsPerElement = 6;

    /// <summary>
    /// Сила по разрезу основания, кН, со знаком силы продавливания (+ — снизу); null — у пластины на границе нет усилий
    /// по ключу.
    /// </summary>
    public static double? Compute(PunchingFemJoint joint, IReadOnlyDictionary<string, FemMeshNode> nodes,
        IReadOnlyDictionary<string, FemElement> elements, IFragmentParentResult result, FragmentMaskKey key)
    {
        ArgumentNullException.ThrowIfNull(joint);
        // Пластины основания в осях узла.
        var shells = new List<(string Tag, List<PlanPoint> Poly, double Size)>();
        foreach (var tag in joint.BaseShells)
        {
            if (!elements.TryGetValue(tag, out var e) || FemElementGeometry.Of(e, nodes) is not { IsShell: true } g) continue;
            var poly = g.Contour.Select(i => joint.ToLocal(g.Points[i])).ToList();
            if (Area(poly) < 0) poly.Reverse();
            double size = Math.Sqrt(Math.Abs(Area(poly)));
            shells.Add((tag, poly, size));
        }
        if (shells.Count == 0) return null;
        double step = shells.Min(s => s.Size) / PointsPerElement;

        var cache = new Dictionary<string, FragmentShellForces?>(StringComparer.Ordinal);
        FragmentShellForces? Forces(string tag)
        {
            if (!cache.TryGetValue(tag, out var f))
                cache[tag] = f = result.TryShellForces(key, tag, out var v) ? v : null;
            return f;
        }

        var basePoly = joint.PyramidBase;
        double total = 0;
        for (int i = 0; i < basePoly.Count; i++)
        {
            var a = basePoly[i];
            var b = basePoly[(i + 1) % basePoly.Count];
            var d = b - a;
            double len = d.Length;
            if (len < 1e-12) continue;
            var outward = new PlanPoint(d.Y / len, -d.X / len);
            var nGlobal = joint.AxisX * outward.X + joint.AxisY * outward.Y;
            int n = Math.Max(1, (int)Math.Ceiling(len / step));
            double ds = len / n;
            for (int k = 0; k < n; k++)
            {
                var p = a + ((k + 0.5) / n) * d;
                var hit = shells.FirstOrDefault(s => PunchingFemJointFinder.Contains(s.Poly, p, 1e-9));
                if (hit.Tag == null) continue;                 // отверстие или край: силы нет
                if (Forces(hit.Tag) is not { } f) return null;
                var t = FragmentShellForceRecovery.Traction(f, nGlobal);
                total += t.Force.Dot(joint.Normal) * ds;
            }
        }
        // Действие внешней части на основание; сила продавливания — обратная по нормали, кН.
        return -total / 1e3;
    }

    static double Area(IReadOnlyList<PlanPoint> p)
    {
        double s = 0;
        for (int i = 0; i < p.Count; i++) s += p[i].Cross(p[(i + 1) % p.Count]);
        return s / 2;
    }
}
