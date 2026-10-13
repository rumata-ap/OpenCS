using CScore.Fem;
using CScore.Fem.Loads;
using CScore.Planar;

namespace CScore.PlateStrip;

/// <summary>Результат переноса нагрузок схемы на полосу плиты.</summary>
/// <param name="Loads">Нагрузки полосы: равномерные Qz по участкам пролёта и сосредоточенные Pz.</param>
/// <param name="Diagnostics">Что перенесено, что нет и почему.</param>
/// <param name="TransferredKn">Σ нормальной (по Z полосы) нагрузки, пришедшейся на полосу, кН.</param>
/// <param name="RegionKn">Σ нормальной нагрузки на все КЭ региона, кН (для оценки доли полосы).</param>
public sealed record StripSchemaLoadTransferResult(
    IReadOnlyList<StripNonlinearLoadItem> Loads,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics,
    double TransferredKn,
    double RegionKn);

/// <summary>
/// Перенос нагрузок схемы (загружение или сочетание) на полосу плиты. Нагрузки сеточного уровня приводятся к
/// согласованным узловым силам каждого КЭ региона (<see cref="FemLoadCaseNodalForces"/>, собственный вес, нагрузки
/// на пластины, по узлам, сосредоточенные, по ребру), равнодействующая КЭ вдоль нормали полосы делится между
/// участками пролёта пропорционально площади пересечения КЭ с прямоугольником «участок × ширина полосы» —
/// для равномерных нагрузок и собственного веса это точно. Узловые нагрузки сетки, попавшие в полосу, становятся
/// сосредоточенными силами. Нагрузки в плоскости полосы прогоном не поддерживаются и в диагностике.
/// </summary>
public static class StripSchemaLoadTransfer
{
    const double Tiny = 1e-12;

    /// <param name="strip">Полоса: система координат (начало — начало оси, X — вдоль оси, Z — нормаль), ширина, длина.</param>
    /// <param name="terms">Загружения с коэффициентами (одно загружение — коэффициент 1; сочетание — по выражению).</param>
    /// <param name="regionMemberTags">Теги конструктивных элементов региона полосы — по ним отбираются КЭ сетки.</param>
    /// <param name="intervals">Число участков пролёта, на которые делится равномерная нагрузка.</param>
    public static StripSchemaLoadTransferResult Transfer(
        PlateStripBeamAnalogy strip,
        IReadOnlyList<(FemLoadCase LoadCase, double Factor)> terms,
        IReadOnlyList<FemElementLoad> elementLoads,
        IReadOnlyList<FemMeshNodeLoad> meshNodeLoads,
        FemLoadMeshContext mesh,
        IReadOnlyCollection<string> regionMemberTags,
        int intervals)
    {
        ArgumentNullException.ThrowIfNull(strip);
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(elementLoads);
        ArgumentNullException.ThrowIfNull(meshNodeLoads);
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(regionMemberTags);

        var diagnostics = new List<FemValidationDiagnostic>();
        StripSchemaLoadTransferResult Empty() => new([], diagnostics, 0.0, 0.0);

        double length = strip.Geometry.LengthM, width = strip.ExplicitWidthM;
        if (intervals < 1 || !(length > 0.0) || !(width > 0.0))
        {
            diagnostics.Add(new("strip_load_invalid_strip", "Длина, ширина полосы и число участков должны быть положительными."));
            return Empty();
        }
        if (terms.Count == 0)
        {
            diagnostics.Add(new("strip_load_no_source", "Не выбран источник нагрузок схемы."));
            return Empty();
        }

        var tags = new HashSet<string>(regionMemberTags, StringComparer.Ordinal);
        var elements = mesh.Elements
            .Where(e => e.SourceMemberTag != null && tags.Contains(e.SourceMemberTag)).ToList();
        if (elements.Count == 0)
        {
            diagnostics.Add(new("strip_load_no_region_mesh",
                "У региона полосы нет КЭ сетки — постройте сетку схемы, чтобы перенести нагрузки."));
            return Empty();
        }

        // Узловые силы по КЭ: приёмник забирает всё, общий результат остаётся только узловым нагрузкам сетки.
        var perElement = new Dictionary<string, Dictionary<string, double[]>>(StringComparer.Ordinal);
        Dictionary<string, double[]>? Sink(FemElement e)
        {
            if (!perElement.TryGetValue(e.ElemTag, out var forces))
                perElement[e.ElemTag] = forces = new Dictionary<string, double[]>(StringComparer.Ordinal);
            return forces;
        }
        var resolved = FemLoadCaseNodalForces.Resolve(terms, elementLoads, meshNodeLoads, mesh, Sink);
        diagnostics.AddRange(resolved.Diagnostics);

        var frame = strip.StripFrame;
        var normal = frame.LocalZ;
        double half = width / 2.0;
        var shares = new double[intervals];
        double regionN = 0.0, inPlane = 0.0;
        int barsLoaded = 0;
        var regionNodes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var element in elements)
        {
            var geometry = mesh.Geometry(element);
            if (geometry == null) continue;
            foreach (var tag in geometry.NodeTags) regionNodes.Add(tag);
            if (!perElement.TryGetValue(element.ElemTag, out var nodal) || nodal.Count == 0) continue;

            var force = new PlanarVector3(
                nodal.Values.Sum(v => v[0]), nodal.Values.Sum(v => v[1]), nodal.Values.Sum(v => v[2]));
            if (!geometry.IsShell)
            {
                if (force.Length > Tiny) barsLoaded++;
                continue;
            }

            double normalForce = force.Dot(normal);
            regionN += normalForce;
            inPlane += (force - normal * normalForce).Length;
            if (Math.Abs(normalForce) < Tiny) continue;

            var polygon = geometry.Contour.Select(i => ToStrip(frame, geometry.Points[i])).ToArray();
            double area = Math.Abs(PolygonArea(polygon));
            if (area < Tiny) continue;
            if (polygon.Max(p => p.U) <= 0.0 || polygon.Min(p => p.U) >= length ||
                polygon.Max(p => p.V) <= -half || polygon.Min(p => p.V) >= half) continue;

            for (int i = 0; i < intervals; i++)
            {
                double clipped = ClippedArea(polygon, length * i / intervals, length * (i + 1) / intervals, -half, half);
                if (clipped > 0.0) shares[i] += normalForce * clipped / area;
            }
        }

        var loads = new List<StripNonlinearLoadItem>();
        double transferred = shares.Sum();

        // Участки одной интенсивности сливаются в одну строку: равномерная нагрузка — одна запись.
        double step = length / intervals;
        double[] q = shares.Select(s => s / step / 1000.0).ToArray();
        double peak = q.Length == 0 ? 0.0 : q.Max(Math.Abs);
        int start = 0;
        while (start < intervals)
        {
            int end = start + 1;
            while (end < intervals && Math.Abs(q[end] - q[start]) <= 1e-9 * Math.Max(peak, Tiny)) end++;
            if (Math.Abs(q[start]) > 1e-9 * Math.Max(peak, Tiny) && Math.Abs(q[start]) > Tiny)
                loads.Add(new StripNonlinearLoadItem
                {
                    Kind = StripNonlinearLoadKind.UniformQz,
                    FromFraction = (double)start / intervals,
                    ToFraction = (double)end / intervals,
                    Value = q[start]
                });
            start = end;
        }

        // Узловые нагрузки сетки в пределах полосы — сосредоточенные силы.
        int pointCount = 0;
        double pointKn = 0.0;
        foreach (var nodeForce in resolved.Forces)
        {
            if (!regionNodes.Contains(nodeForce.NodeTag) ||
                !mesh.NodesByTag.TryGetValue(nodeForce.NodeTag, out var node)) continue;
            var f = new PlanarVector3(nodeForce.Fx, nodeForce.Fy, nodeForce.Fz);
            double normalForce = f.Dot(normal);
            regionN += normalForce;
            if (Math.Abs(normalForce) < Tiny) continue;
            var p = ToStrip(frame, new PlanarVector3(node.X, node.Y, node.Z));
            if (p.U < -1e-9 || p.U > length + 1e-9 || Math.Abs(p.V) > half + 1e-9) continue;
            loads.Add(new StripNonlinearLoadItem
            {
                Kind = StripNonlinearLoadKind.PointPz,
                FromFraction = Math.Clamp(p.U / length, 0.0, 1.0),
                Value = normalForce / 1000.0
            });
            pointCount++;
            pointKn += normalForce / 1000.0;
        }

        double transferredKn = transferred / 1000.0 + pointKn;
        double regionKn = regionN / 1000.0;
        if (loads.Count == 0)
            diagnostics.Add(new("strip_load_nothing", "На полосу не приходится нагрузок выбранного источника.", false));
        else
            diagnostics.Add(new("strip_load_transferred",
                $"Перенесено на полосу {transferredKn:F2} кН" +
                (Math.Abs(regionKn) > Tiny ? $" ({transferredKn / regionKn:P0} от нормальной нагрузки КЭ региона, {regionKn:F2} кН)" : "") +
                (pointCount > 0 ? $"; узловых нагрузок сетки в полосе: {pointCount}" : "") + ".", false));

        if (inPlane > 1e-3 * Math.Max(Math.Abs(regionN), 1.0) && inPlane > 1.0)
            diagnostics.Add(new("strip_load_in_plane",
                $"Нагрузки в плоскости плиты (Σ|F∥| = {inPlane / 1000.0:F2} кН) не переносятся: прогон принимает только нагрузку поперёк плиты.",
                false));
        if (barsLoaded > 0)
            diagnostics.Add(new("strip_load_bars",
                $"Нагрузки на стержневые КЭ региона ({barsLoaded}) не переносятся.", false));

        var ids = terms.Select(t => t.LoadCase.Id).ToHashSet();
        if (elementLoads.Any(l => ids.Contains(l.LoadCaseId) &&
                                  l.LoadKind is FemElementLoadKinds.Point or FemElementLoadKinds.Edge or FemElementLoadKinds.Nodal))
            diagnostics.Add(new("strip_load_smeared",
                "Сосредоточенные нагрузки, нагрузки по ребру и по узлам на КЭ распределены по площади КЭ: " +
                "в пределах одного КЭ положение нагрузки не различается.", false));

        return new StripSchemaLoadTransferResult(loads, diagnostics, transferredKn, regionKn);
    }

    static (double U, double V) ToStrip(Frame3D frame, PlanarVector3 point)
    {
        var d = point - frame.Origin;
        return (d.Dot(frame.LocalX), d.Dot(frame.LocalY));
    }

    static double PolygonArea(IReadOnlyList<(double U, double V)> p)
    {
        double s = 0.0;
        for (int i = 0; i < p.Count; i++)
        {
            var (u0, v0) = p[i];
            var (u1, v1) = p[(i + 1) % p.Count];
            s += u0 * v1 - u1 * v0;
        }
        return s / 2.0;
    }

    /// <summary>Площадь пересечения выпуклого многоугольника с прямоугольником (Сазерленд–Ходжман).</summary>
    static double ClippedArea(IReadOnlyList<(double U, double V)> polygon, double u0, double u1, double v0, double v1)
    {
        var points = polygon.ToList();
        points = Clip(points, p => p.U - u0);
        points = Clip(points, p => u1 - p.U);
        points = Clip(points, p => p.V - v0);
        points = Clip(points, p => v1 - p.V);
        return points.Count < 3 ? 0.0 : Math.Abs(PolygonArea(points));
    }

    /// <summary>Оставляет часть многоугольника, где <paramref name="inside"/> ≥ 0.</summary>
    static List<(double U, double V)> Clip(List<(double U, double V)> input, Func<(double U, double V), double> inside)
    {
        var output = new List<(double U, double V)>();
        for (int i = 0; i < input.Count; i++)
        {
            var current = input[i];
            var previous = input[(i + input.Count - 1) % input.Count];
            double dc = inside(current), dp = inside(previous);
            if (dc >= 0.0)
            {
                if (dp < 0.0) output.Add(Intersect(previous, current, dp, dc));
                output.Add(current);
            }
            else if (dp >= 0.0)
                output.Add(Intersect(previous, current, dp, dc));
        }
        return output;
    }

    static (double U, double V) Intersect((double U, double V) a, (double U, double V) b, double da, double db)
    {
        double t = da / (da - db);
        return (a.U + (b.U - a.U) * t, a.V + (b.V - a.V) * t);
    }
}
