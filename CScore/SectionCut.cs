using System;
using System.Collections.Generic;
using System.Linq;

namespace CScore;

/// <summary>Режим построения линии разреза сечения.</summary>
public enum CutMode
{
    /// <summary>Одна точка — направление берётся из градиента плоскости деформаций.</summary>
    GradientSnap,
    /// <summary>Две точки задают направление и положение линии явно.</summary>
    Free
}

/// <summary>Одна точка эпюры вдоль разреза. S — расстояние от начала разреза [м].</summary>
public readonly record struct CutSample(double S, double X, double Y, double? Eps, double? Sig, int? AreaIndex);

/// <summary>Непрерывный участок эпюры внутри одной области (AreaIndex == null — пустота/дыра).</summary>
public sealed record CutSegment(IReadOnlyList<CutSample> Points, int? AreaIndex);

/// <summary>Стержень арматуры, спроецированный на линию разреза.</summary>
public sealed record CutRebarMarker(double S, double X, double Y, double Eps, double Sig, double DiameterM, int? Num)
{
    /// <summary>Продольное усилие в стержне, кН (σ[МПа]·A[м²]·1000).</summary>
    public double ForceKN => Sig * Math.PI * DiameterM * DiameterM / 4.0 * 1000.0;
}

/// <summary>Результат построения разреза сечения: геометрия линии + сегменты эпюры + арматура.</summary>
public sealed record SectionCutResult(
    (double X, double Y) Start,
    (double X, double Y) End,
    IReadOnlyList<CutSegment> Segments,
    IReadOnlyList<CutRebarMarker> Rebars,
    IReadOnlyList<CutRebarMarker> NearbyRebars);

/// <summary>Строит эпюру σ/ε вдоль произвольного разреза сечения.</summary>
public static class SectionCutBuilder
{
    const double Tol = 1e-9;
    const int SamplesPerInterval = 40;

    /// <summary>
    /// Строит разрез. Возвращает null, если линия не пересекает ни одну область сечения.
    /// В режиме Free требуется p2; в режиме GradientSnap направление берётся из (k.kz, k.ky),
    /// с запасным горизонтальным направлением при нулевом градиенте (ky=kz=0).
    /// </summary>
    public static SectionCutResult? Build(
        CrossSection section,
        Kurvature k,
        CalcType calcType,
        CutMode mode,
        (double X, double Y) p1,
        (double X, double Y)? p2,
        double rebarThresholdM,
        bool tenB = true,
        bool comprA = true)
    {
        var (origin, dir) = ResolveLine(mode, k, p1, p2);

        var regionAreas = section.EnumerateAreas(k)
            .Where(t => t.area.Hull != null && t.area.Diagramms.ContainsKey(calcType))
            .ToList();
        if (regionAreas.Count == 0) return null;

        var areaIntervals = new List<(int AreaIndex, MaterialArea Area, Kurvature Ka, double TStart, double TEnd)>();
        for (int i = 0; i < regionAreas.Count; i++)
        {
            var (area, ka) = regionAreas[i];
            var holes = area.Holes;
            foreach (var (tStart, tEnd) in InsideIntervals(area.Hull!, holes, origin, dir))
                areaIntervals.Add((i, area, ka, tStart, tEnd));
        }
        if (areaIntervals.Count == 0) return null;

        double tMin = areaIntervals.Min(a => a.TStart);
        double tMax = areaIntervals.Max(a => a.TEnd);

        var segments = new List<CutSegment>();
        var covered = areaIntervals.OrderBy(a => a.TStart).ToList();
        double cursor = tMin;
        foreach (var seg in covered)
        {
            if (seg.TStart - cursor > Tol)
            {
                segments.Add(new CutSegment(
                    new[]
                    {
                        SampleAt(cursor, tMin, origin, dir, null, null, null),
                        SampleAt(seg.TStart, tMin, origin, dir, null, null, null)
                    },
                    AreaIndex: null));
            }
            segments.Add(BuildAreaSegment(seg.AreaIndex, seg.Area, seg.Ka, calcType, tMin, origin, dir, seg.TStart, seg.TEnd, tenB, comprA));
            cursor = Math.Max(cursor, seg.TEnd);
        }

        var rebars = new List<CutRebarMarker>();
        var nearbyRebars = new List<CutRebarMarker>();
        double nearbyMax = rebarThresholdM * 3.0;
        foreach (var (area, ka) in section.EnumerateAreas(k))
        {
            if (!area.Diagramms.TryGetValue(calcType, out var dgr)) continue;
            foreach (var f in area.Fibers)
            {
                if (f.TypeFiber != FiberType.point) continue;
                double relX = f.X - origin.X, relY = f.Y - origin.Y;
                double dist = Math.Abs(relX * dir.Y - relY * dir.X);
                double t = relX * dir.X + relY * dir.Y;
                if (t < tMin - nearbyMax || t > tMax + nearbyMax) continue;
                double eps = ka.e0 + ka.ky * f.Y + ka.kz * f.X;
                double sig = dgr.SigValue(eps, tenB, comprA) / 1000.0;
                var marker = new CutRebarMarker(t - tMin, f.X, f.Y, eps, sig, f.Diameter, f.Num);
                if (dist <= rebarThresholdM)
                    rebars.Add(marker);
                else if (dist <= nearbyMax)
                    nearbyRebars.Add(marker);
            }
        }

        var start = (X: origin.X + dir.X * tMin, Y: origin.Y + dir.Y * tMin);
        var end = (X: origin.X + dir.X * tMax, Y: origin.Y + dir.Y * tMax);
        return new SectionCutResult(start, end, segments, rebars, nearbyRebars);
    }

    static ((double X, double Y) Origin, (double X, double Y) Dir) ResolveLine(
        CutMode mode, Kurvature k, (double X, double Y) p1, (double X, double Y)? p2)
    {
        if (mode == CutMode.Free)
        {
            if (p2 is not { } pt2) throw new ArgumentException("Режим Free требует вторую точку.", nameof(p2));
            double dx = pt2.X - p1.X, dy = pt2.Y - p1.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6) throw new ArgumentException("Точки разреза слишком близки друг к другу.");
            return (p1, (dx / len, dy / len));
        }

        double gx = k.kz, gy = k.ky;
        double glen = Math.Sqrt(gx * gx + gy * gy);
        if (glen < 1e-12) return (p1, (1.0, 0.0));
        return (p1, (gx / glen, gy / glen));
    }

    static IEnumerable<(double TStart, double TEnd)> InsideIntervals(
        Contour hull, IList<Contour> holes, (double X, double Y) origin, (double X, double Y) dir)
    {
        var ts = new List<double>();
        CollectEdgeT(hull, origin, dir, ts);
        foreach (var hole in holes) CollectEdgeT(hole, origin, dir, ts);

        ts = ts.Distinct().OrderBy(t => t).ToList();
        for (int i = 0; i < ts.Count - 1; i++)
        {
            double tStart = ts[i], tEnd = ts[i + 1];
            if (tEnd - tStart < Tol) continue;
            double tMid = (tStart + tEnd) / 2.0;
            double mx = origin.X + dir.X * tMid, my = origin.Y + dir.Y * tMid;

            if (!WktHelper.PointInPolygon(hull.X, hull.Y, mx, my)) continue;
            if (holes.Any(h => WktHelper.PointInPolygon(h.X, h.Y, mx, my))) continue;

            yield return (tStart, tEnd);
        }
    }

    static void CollectEdgeT(Contour c, (double X, double Y) origin, (double X, double Y) dir, List<double> ts)
    {
        int n = c.X.Count - 1; // последняя точка дублирует первую (замкнутое кольцо)
        for (int i = 0; i < n; i++)
        {
            double ax = c.X[i], ay = c.Y[i];
            double bx = c.X[i + 1], by = c.Y[i + 1];
            double ex = bx - ax, ey = by - ay;
            double det = dir.Y * ex - dir.X * ey;
            if (Math.Abs(det) < 1e-12) continue; // ребро параллельно линии разреза

            double rx = ax - origin.X, ry = ay - origin.Y;
            double t = (ry * ex - rx * ey) / det;
            double u = (dir.X * ry - dir.Y * rx) / det;
            if (u >= -1e-9 && u <= 1 + 1e-9)
                ts.Add(t);
        }
    }

    static CutSample SampleAt(double t, double tMin, (double X, double Y) origin, (double X, double Y) dir,
        double? eps, double? sig, int? areaIndex)
    {
        double x = origin.X + dir.X * t, y = origin.Y + dir.Y * t;
        return new CutSample(t - tMin, x, y, eps, sig, areaIndex);
    }

    static CutSegment BuildAreaSegment(int areaIndex, MaterialArea area, Kurvature ka, CalcType calcType,
        double tMin, (double X, double Y) origin, (double X, double Y) dir,
        double tStart, double tEnd, bool tenB, bool comprA)
    {
        var dgr = area.Diagramms[calcType];

        double EpsAt(double t)
        {
            double x = origin.X + dir.X * t, y = origin.Y + dir.Y * t;
            return ka.e0 + ka.ky * y + ka.kz * x;
        }

        double epsStart = EpsAt(tStart), epsEnd = EpsAt(tEnd);
        var breakTs = new List<double> { tStart, tEnd };
        double slope = epsEnd - epsStart;
        if (Math.Abs(slope) > 1e-12)
        {
            foreach (double critEps in dgr.GetCriticalStrains())
            {
                double frac = (critEps - epsStart) / slope;
                double t = tStart + frac * (tEnd - tStart);
                if (t > tStart + Tol && t < tEnd - Tol)
                    breakTs.Add(t);
            }
        }
        for (int i = 1; i < SamplesPerInterval; i++)
            breakTs.Add(tStart + (tEnd - tStart) * i / SamplesPerInterval);

        breakTs = breakTs.Distinct().OrderBy(t => t).ToList();

        var points = new List<CutSample>(breakTs.Count);
        foreach (double t in breakTs)
        {
            double x = origin.X + dir.X * t, y = origin.Y + dir.Y * t;
            double eps = ka.e0 + ka.ky * y + ka.kz * x;
            double sig = dgr.SigValue(eps, tenB, comprA) / 1000.0;
            points.Add(new CutSample(t - tMin, x, y, eps, sig, areaIndex));
        }
        return new CutSegment(points, areaIndex);
    }
}
