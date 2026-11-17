using CScore.Import;
using CScore.Planar;

namespace CScore.Sp63Punching.Fem;

/// <summary>
/// Контур продавливания ЛИРЫ (отрезки хвоста *.asp в общих осях) → контур пользователя ядра в осях узла: отрезки
/// сцепляются в цепочку; у замкнутого — все стороны, у разомкнутого (у края, у угла, П у края) замыкающая сторона
/// выключена. Так OpenCS считает по той же геометрии, что ЛИРА (u, ц. т., Wb — ядро по п. 8.1.50–8.1.51).
/// </summary>
public static class PunchingFemLiraContour
{
    /// <summary>Допуск совпадения концов отрезков, м (float *.asp).</summary>
    public const double JoinTol = 0.005;

    /// <summary>
    /// Многоугольник в осях узла и выключенные стороны (сторона i — от вершины i к i + 1); null — отрезков меньше двух
    /// или они не сцепляются в одну цепочку.
    /// </summary>
    public static (IReadOnlyList<PlanPoint> Points, IReadOnlyList<int> Inactive)? ToLocal(PunchingFemJoint joint,
        IReadOnlyList<LiraAspSegment> segments, double tol = JoinTol)
    {
        ArgumentNullException.ThrowIfNull(joint);
        ArgumentNullException.ThrowIfNull(segments);
        var segs = segments
            .Select(s => (A: joint.ToLocal(new PlanarVector3(s.X1, s.Y1, s.Z1)), B: joint.ToLocal(new PlanarVector3(s.X2, s.Y2, s.Z2))))
            .Where(s => Dist(s.A, s.B) > tol)
            .ToList();
        if (segs.Count < 2) return null;

        // Начало цепочки: конец, у которого нет пары (разомкнутый контур); у замкнутого — начало первого отрезка.
        int Degree(PlanPoint p) => segs.Count(s => Dist(s.A, p) <= tol) + segs.Count(s => Dist(s.B, p) <= tol);
        var start = segs.SelectMany(s => new[] { s.A, s.B }).FirstOrDefault(p => Degree(p) == 1, segs[0].A);

        var used = new bool[segs.Count];
        var chain = new List<PlanPoint> { start };
        var cur = start;
        for (int step = 0; step < segs.Count; step++)
        {
            int next = -1;
            bool reversed = false;
            for (int i = 0; i < segs.Count && next < 0; i++)
            {
                if (used[i]) continue;
                if (Dist(segs[i].A, cur) <= tol) next = i;
                else if (Dist(segs[i].B, cur) <= tol) { next = i; reversed = true; }
            }
            if (next < 0) return null;
            used[next] = true;
            cur = reversed ? segs[next].A : segs[next].B;
            chain.Add(cur);
        }

        bool closed = Dist(chain[^1], chain[0]) <= tol;
        if (closed) chain.RemoveAt(chain.Count - 1);
        if (chain.Count < 3) return null;
        return (chain, closed ? [] : [chain.Count - 1]);
    }

    static double Dist(PlanPoint a, PlanPoint b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
