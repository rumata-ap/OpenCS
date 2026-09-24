using System.Globalization;
using System.Text;
using CScore;
using CScore.Planar;

namespace OpenCS.Gmsh.Generation;

/// <summary>Детерминированно строит локальную Gmsh-геометрию host-региона и его loci.
///
/// Точка constraint, совпадающая по координатам с уже записанной точкой (вершиной контура или
/// точкой другого constraint), не создаётся заново, а переиспользуется: для OpenCASCADE две
/// точки с одинаковыми координатами — разные сущности, и кривая, «касающаяся» вершины контура
/// своей копией, даёт вырожденную сетку. Точечный constraint в вершине контура в поверхность
/// повторно не встраивается — вершина уже принадлежит её границе.</summary>
public static class GmshPlanarGeoBuilder
{
    const int OuterPhysicalGroup = 1001;
    const int HolePhysicalGroupBase = 1002;
    const int SurfacePhysicalGroup = 2001;
    const int ConstraintPhysicalGroupBase = 3001;

    public static string Build(
        PlanarRegion region,
        PlanarMeshSettings settings,
        IReadOnlyList<PlanarConstraintObject>? constraintObjects = null)
    {
        ArgumentNullException.ThrowIfNull(region);
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        var result = new StringBuilder();
        result.AppendLine("SetFactory(\"OpenCASCADE\");");
        result.AppendLine("Mesh.ElementOrder = 1;");
        result.AppendLine($"Mesh.Algorithm = {settings.Algorithm};");
        result.AppendLine($"Mesh.CharacteristicLengthMax = {Fmt(settings.MaxElementSizeM)};");
        if (settings.ElementMode is PlanarMeshElementMode.Quads or PlanarMeshElementMode.Mixed)
            result.AppendLine("Mesh.RecombineAll = 1;");

        var contours = new List<LoopInfo>();
        var emitted = new List<(double U, double V, int Id)>();
        var hostPoints = new HashSet<int>();
        var point = 1;
        var line = 1;
        var loop = 1;
        var holeIndex = 0;
        foreach (var contour in region.Contours)
        {
            var (x, y) = PlanarRegionTopologyValidator.ToOpenLoop(contour.X, contour.Y);
            var points = Enumerable.Range(point, x.Length).ToArray();
            for (var i = 0; i < points.Length; i++)
            {
                result.AppendLine($"Point({points[i]}) = {{{Fmt(x[i])}, {Fmt(y[i])}, 0, {Fmt(settings.MaxElementSizeM)}}};");
                emitted.Add((x[i], y[i], points[i]));
                hostPoints.Add(points[i]);
            }

            var lines = Enumerable.Range(line, x.Length).ToArray();
            for (var i = 0; i < lines.Length; i++)
                result.AppendLine($"Line({lines[i]}) = {{{points[i]}, {points[(i + 1) % points.Length]}}};");
            result.AppendLine($"Curve Loop({loop}) = {{{string.Join(", ", lines)}}};");
            var physical = contour.Type == ContourType.Hull ? OuterPhysicalGroup : HolePhysicalGroupBase + holeIndex++;
            var physicalName = contour.Type == ContourType.Hull ? "host:outer" : $"host:hole:{holeIndex - 1}";
            result.AppendLine($"Physical Curve(\"{physicalName}\", {physical}) = {{{string.Join(", ", lines)}}};");
            contours.Add(new(loop, points, lines, contour.Type));
            point += x.Length;
            line += x.Length;
            loop++;
        }

        result.AppendLine($"Plane Surface(1) = {{{string.Join(", ", contours.Select(contour => contour.LoopId))}}};");
        result.AppendLine($"Physical Surface(\"host:surface\", {SurfacePhysicalGroup}) = {{1}};");

        var nextPoint = point;
        var nextLine = line;
        var nextLoop = loop;
        var physicalGroup = ConstraintPhysicalGroupBase;
        foreach (var constraint in (constraintObjects ?? region.ConstraintObjects).OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            var group = physicalGroup++;
            var name = SafeName(constraint.Id);
            switch (constraint.Geometry.Kind)
            {
                case PlanarConstraintGeometryKind.Point:
                {
                    var p = constraint.Geometry.Points[0];
                    int id = PointId(p, ref nextPoint);
                    if (!hostPoints.Contains(id))
                        result.AppendLine($"Point {{{id}}} In Surface {{1}};");
                    result.AppendLine($"Physical Point(\"constraint:{name}:point\", {group}) = {{{id}}};");
                    break;
                }
                case PlanarConstraintGeometryKind.Curve:
                {
                    var points = constraint.Geometry.Points;
                    var pointIds = points.Select(p => PointId(p, ref nextPoint)).ToArray();
                    var lineIds = Enumerable.Range(nextLine, points.Count - 1).ToArray();
                    for (var i = 0; i < lineIds.Length; i++)
                        result.AppendLine($"Line({lineIds[i]}) = {{{pointIds[i]}, {pointIds[i + 1]}}};");
                    result.AppendLine($"Line {{{string.Join(", ", lineIds)}}} In Surface {{1}};");
                    result.AppendLine($"Physical Curve(\"constraint:{name}:curve\", {group}) = {{{string.Join(", ", lineIds)}}};");
                    nextLine += lineIds.Length;
                    break;
                }
                case PlanarConstraintGeometryKind.Region:
                {
                    var points = constraint.Geometry.Points;
                    var pointIds = points.Select(p => PointId(p, ref nextPoint)).ToArray();
                    var lineIds = Enumerable.Range(nextLine, points.Count).ToArray();
                    for (var i = 0; i < lineIds.Length; i++)
                        result.AppendLine($"Line({lineIds[i]}) = {{{pointIds[i]}, {pointIds[(i + 1) % pointIds.Length]}}};");
                    result.AppendLine($"Curve Loop({nextLoop}) = {{{string.Join(", ", lineIds)}}};");
                    result.AppendLine($"Line {{{string.Join(", ", lineIds)}}} In Surface {{1}};");
                    result.AppendLine($"Physical Curve(\"constraint:{name}:region\", {group}) = {{{string.Join(", ", lineIds)}}};");
                    nextLine += lineIds.Length;
                    nextLoop++;
                    break;
                }
            }
        }

        return result.ToString();

        // Id точки: существующий при совпадении координат, иначе новая точка.
        int PointId(PlanarPoint2D p, ref int next)
        {
            foreach (var (u, v, id) in emitted)
                if (Math.Abs(u - p.U) <= PointMergeToleranceM && Math.Abs(v - p.V) <= PointMergeToleranceM)
                    return id;
            int created = next++;
            result.AppendLine($"Point({created}) = {{{Fmt(p.U)}, {Fmt(p.V)}, 0, {Fmt(settings.MaxElementSizeM)}}};");
            emitted.Add((p.U, p.V, created));
            return created;
        }
    }

    /// <summary>Допуск совпадения точек при переиспользовании, м.</summary>
    const double PointMergeToleranceM = 1e-9;

    static string SafeName(string value) => value.Replace("\"", "_");
    static string Fmt(double value) => value.ToString("G17", CultureInfo.InvariantCulture);

    sealed record LoopInfo(int LoopId, int[] PointIds, int[] LineIds, ContourType Type);
}
