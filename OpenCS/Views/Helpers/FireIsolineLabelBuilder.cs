using OpenCS.ViewModels;
using System.Windows;

namespace OpenCS.Views.Helpers;

/// <summary>Подписи изолиний в стиле matplotlib clabel.</summary>
public static class FireIsolineLabelBuilder
{
    const double PointTolMm = 0.05;
    const double MinLabelSpanMm = 8.0;

    public readonly record struct Label(Point PositionMm, double AngleDeg, string Text);

    public static IReadOnlyList<Label> Build(IReadOnlyList<FireIsolineSegment> segments)
    {
        if (segments.Count == 0)
            return [];

        var labels = new List<Label>();
        foreach (var levelGroup in segments.GroupBy(s => s.LevelCelsius))
        {
            foreach (var chain in ChainPolylines(levelGroup))
            {
                if (!TryPickLabelSpot(chain, levelGroup.Key, out var label))
                    continue;
                labels.Add(label);
            }
        }

        return labels;
    }

    static List<List<Point>> ChainPolylines(IEnumerable<FireIsolineSegment> segments)
    {
        var list = segments.ToList();
        var used = new bool[list.Count];
        var chains = new List<List<Point>>();

        for (int i = 0; i < list.Count; i++)
        {
            if (used[i])
                continue;

            var chain = new List<Point> { list[i].A, list[i].B };
            used[i] = true;
            bool extended;
            do
            {
                extended = false;
                for (int j = 0; j < list.Count; j++)
                {
                    if (used[j])
                        continue;

                    if (TryAppend(ref chain, list[j].A, list[j].B))
                    {
                        used[j] = true;
                        extended = true;
                    }
                }
            } while (extended);

            if (chain.Count >= 2)
                chains.Add(chain);
        }

        return chains;
    }

    static bool TryAppend(ref List<Point> chain, Point a, Point b)
    {
        if (Near(chain[0], a))
        {
            chain.Insert(0, b);
            return true;
        }
        if (Near(chain[0], b))
        {
            chain.Insert(0, a);
            return true;
        }
        if (Near(chain[^1], a))
        {
            chain.Add(b);
            return true;
        }
        if (Near(chain[^1], b))
        {
            chain.Add(a);
            return true;
        }
        return false;
    }

    static bool TryPickLabelSpot(List<Point> chain, double levelCelsius, out Label label)
    {
        label = default;
        if (chain.Count < 2)
            return false;

        double bestLen = 0;
        int bestIdx = -1;
        for (int i = 0; i < chain.Count - 1; i++)
        {
            double len = Dist(chain[i], chain[i + 1]);
            if (len > bestLen)
            {
                bestLen = len;
                bestIdx = i;
            }
        }

        if (bestIdx < 0 || bestLen < MinLabelSpanMm)
            return false;

        var p0 = chain[bestIdx];
        var p1 = chain[bestIdx + 1];
        var mid = new Point((p0.X + p1.X) * 0.5, (p0.Y + p1.Y) * 0.5);
        double angle = Math.Atan2(p1.Y - p0.Y, p1.X - p0.X) * 180.0 / Math.PI;
        if (angle > 90.0)
            angle -= 180.0;
        else if (angle < -90.0)
            angle += 180.0;

        string text = $"{levelCelsius:F0}";
        label = new Label(mid, angle, text);
        return true;
    }

    static bool Near(Point a, Point b)
        => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) <= PointTolMm * PointTolMm;

    static double Dist(Point a, Point b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
