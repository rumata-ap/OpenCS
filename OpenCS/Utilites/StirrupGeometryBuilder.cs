using CScore;

namespace OpenCS.Utilites;

/// <summary>Построитель геометрии замкнутых хомутов и открытых срезов-стержней.</summary>
public static class StirrupGeometryBuilder
{
    /// <summary>Строит замкнутый хомут по оффсету внешнего контура области-носителя.</summary>
    public static StirrupElement? BuildOffsetLoop(MaterialArea anchor, double offset,
                                                   double diameterM, out string? error)
    {
        if (!TryGetHull(anchor, out var hull, out error)) return null;

        if (!ContourOffset.TryOffset(hull!, offset, out var points, out error))
            return null;

        var xs = points.Select(point => point.X).ToList();
        var ys = points.Select(point => point.Y).ToList();
        xs.Add(xs[0]);
        ys.Add(ys[0]);

        var contour = new Contour(xs, ys, "хомут");
        error = null;
        return new StirrupElement
        {
            CenterlineContour = contour,
            BarAreaM2 = BarArea(diameterM),
            BarDiameterM = diameterM,
            Source = new StirrupElementSource
            {
                Kind = StirrupElementKind.OffsetLoop,
                AnchorAreaId = anchor.Id,
                OffsetM = offset
            }
        };
    }

    /// <summary>Строит один или несколько открытых срезов по линии через оффсетный контур.</summary>
    public static IReadOnlyList<StirrupElement> BuildCuts(
        MaterialArea anchor, StirrupCutDirection direction, double position,
        double offset, double diameterM, out string? error)
    {
        if (!TryGetHull(anchor, out var hull, out error)) return [];
        if (direction == StirrupCutDirection.TwoPoints)
        {
            error = "Произвольный срез по двум точкам пока не поддерживается этим методом.";
            return [];
        }

        if (!ContourOffset.TryOffset(hull!, offset, out var points, out error))
            return [];

        var intersections = Intersections(points, direction, position);
        if (intersections.Count < 2)
        {
            error = "Линия среза не пересекает оффсетный контур.";
            return [];
        }

        var result = new List<StirrupElement>();
        for (int i = 0; i + 1 < intersections.Count; i += 2)
        {
            double first = intersections[i];
            double second = intersections[i + 1];
            if (second - first < 1e-9) continue;

            double x0, y0, x1, y1;
            if (direction == StirrupCutDirection.Vertical)
            {
                x0 = x1 = position;
                y0 = first;
                y1 = second;
            }
            else
            {
                x0 = first;
                x1 = second;
                y0 = y1 = position;
            }

            result.Add(new StirrupElement
            {
                CenterlineContour = Contour.Polyline([x0, x1], [y0, y1], "срез"),
                BarAreaM2 = BarArea(diameterM),
                BarDiameterM = diameterM,
                Source = new StirrupElementSource
                {
                    Kind = StirrupElementKind.Cut,
                    AnchorAreaId = anchor.Id,
                    OffsetM = offset,
                    Direction = direction,
                    Position = position
                }
            });
        }

        if (result.Count == 0)
        {
            error = "Линия среза не образует отрезка ненулевой длины.";
            return [];
        }

        error = null;
        return result;
    }

    /// <summary>Переносит геометрию элемента и записывает параметры копирования.</summary>
    public static StirrupElement Translate(StirrupElement source, double dx, double dy, int baseIndex)
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = source.Clone(preserveId: false);
        result.CenterlineContour.X = result.CenterlineContour.X.Select(x => x + dx).ToList();
        result.CenterlineContour.Y = result.CenterlineContour.Y.Select(y => y + dy).ToList();
        result.CenterlineContour.Points = result.CenterlineContour.XYsToPoints();
        result.CenterlineContour.SetWKT();

        var sourceInfo = result.Source?.Clone() ?? new StirrupElementSource { Kind = StirrupElementKind.Manual };
        sourceInfo.Dx = dx;
        sourceInfo.Dy = dy;
        sourceInfo.BaseIndex = baseIndex;
        result.Source = sourceInfo;
        return result;
    }

    /// <summary>Возвращает площадь круглого стержня по диаметру.</summary>
    public static double BarArea(double diameterM) => Math.PI * diameterM * diameterM / 4.0;

    static bool TryGetHull(MaterialArea anchor, out Contour? hull, out string? error)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        hull = anchor.Hull;
        if (hull is null)
        {
            error = "У области-носителя нет внешнего контура.";
            return false;
        }
        if (anchor.Holes.Count > 0)
        {
            error = "Область с отверстиями не поддерживается в первой версии.";
            return false;
        }
        error = null;
        return true;
    }

    static List<double> Intersections(IReadOnlyList<(double X, double Y)> points,
                                      StirrupCutDirection direction, double position)
    {
        var values = new List<double>();
        const double eps = 1e-10;
        for (int i = 0; i < points.Count; i++)
        {
            var p0 = points[i];
            var p1 = points[(i + 1) % points.Count];
            double fixed0 = direction == StirrupCutDirection.Vertical ? p0.X : p0.Y;
            double fixed1 = direction == StirrupCutDirection.Vertical ? p1.X : p1.Y;
            double variable0 = direction == StirrupCutDirection.Vertical ? p0.Y : p0.X;
            double variable1 = direction == StirrupCutDirection.Vertical ? p1.Y : p1.X;

            if (Math.Abs(fixed0 - position) <= eps && Math.Abs(fixed1 - position) <= eps)
            {
                values.Add(variable0);
                values.Add(variable1);
                continue;
            }

            double denominator = fixed1 - fixed0;
            if (Math.Abs(denominator) <= eps) continue;
            double t = (position - fixed0) / denominator;
            if (t < -eps || t > 1.0 + eps) continue;
            values.Add(variable0 + t * (variable1 - variable0));
        }

        values.Sort();
        var distinct = new List<double>(values.Count);
        foreach (var value in values)
            if (distinct.Count == 0 || Math.Abs(value - distinct[^1]) > 1e-8)
                distinct.Add(value);
        return distinct;
    }
}
