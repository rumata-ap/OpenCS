using System.Globalization;
using System.Text;
using CScore;
using CScore.Planar;

namespace OpenCS.Gmsh.Generation;

/// <summary>Детерминированно строит локальную Gmsh-геометрию host-региона и его loci.</summary>
public static class GmshPlanarGeoBuilder
{
    const int OuterPhysicalGroup = 1001;
    const int HolePhysicalGroupBase = 1002;
    const int SurfacePhysicalGroup = 2001;
    const int ConstraintPhysicalGroupBase = 3001;

    public static string Build(PlanarRegion region, PlanarMeshSettings settings)
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
        var point = 1;
        var line = 1;
        var loop = 1;
        var holeIndex = 0;
        foreach (var contour in region.Contours)
        {
            var (x, y) = PlanarRegionTopologyValidator.ToOpenLoop(contour.X, contour.Y);
            var points = Enumerable.Range(point, x.Length).ToArray();
            for (var i = 0; i < points.Length; i++)
                result.AppendLine($"Point({points[i]}) = {{{Fmt(x[i])}, {Fmt(y[i])}, 0, {Fmt(settings.MaxElementSizeM)}}};");

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
        foreach (var constraint in region.ConstraintObjects.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            var group = physicalGroup++;
            var name = SafeName(constraint.Id);
            switch (constraint.Geometry.Kind)
            {
                case PlanarConstraintGeometryKind.Point:
                {
                    var p = constraint.Geometry.Points[0];
                    result.AppendLine($"Point({nextPoint}) = {{{Fmt(p.U)}, {Fmt(p.V)}, 0, {Fmt(settings.MaxElementSizeM)}}};");
                    result.AppendLine($"Point {{{nextPoint}}} In Surface {{1}};");
                    result.AppendLine($"Physical Point(\"constraint:{name}:point\", {group}) = {{{nextPoint}}};");
                    nextPoint++;
                    break;
                }
                case PlanarConstraintGeometryKind.Curve:
                {
                    var points = constraint.Geometry.Points;
                    var pointIds = Enumerable.Range(nextPoint, points.Count).ToArray();
                    for (var i = 0; i < points.Count; i++)
                        result.AppendLine($"Point({pointIds[i]}) = {{{Fmt(points[i].U)}, {Fmt(points[i].V)}, 0, {Fmt(settings.MaxElementSizeM)}}};");
                    var lineIds = Enumerable.Range(nextLine, points.Count - 1).ToArray();
                    for (var i = 0; i < lineIds.Length; i++)
                        result.AppendLine($"Line({lineIds[i]}) = {{{pointIds[i]}, {pointIds[i + 1]}}};");
                    result.AppendLine($"Line {{{string.Join(", ", lineIds)}}} In Surface {{1}};");
                    result.AppendLine($"Physical Curve(\"constraint:{name}:curve\", {group}) = {{{string.Join(", ", lineIds)}}};");
                    nextPoint += pointIds.Length;
                    nextLine += lineIds.Length;
                    break;
                }
                case PlanarConstraintGeometryKind.Region:
                {
                    var points = constraint.Geometry.Points;
                    var pointIds = Enumerable.Range(nextPoint, points.Count).ToArray();
                    for (var i = 0; i < points.Count; i++)
                        result.AppendLine($"Point({pointIds[i]}) = {{{Fmt(points[i].U)}, {Fmt(points[i].V)}, 0, {Fmt(settings.MaxElementSizeM)}}};");
                    var lineIds = Enumerable.Range(nextLine, points.Count).ToArray();
                    for (var i = 0; i < lineIds.Length; i++)
                        result.AppendLine($"Line({lineIds[i]}) = {{{pointIds[i]}, {pointIds[(i + 1) % pointIds.Length]}}};");
                    result.AppendLine($"Curve Loop({nextLoop}) = {{{string.Join(", ", lineIds)}}};");
                    result.AppendLine($"Line {{{string.Join(", ", lineIds)}}} In Surface {{1}};");
                    result.AppendLine($"Physical Curve(\"constraint:{name}:region\", {group}) = {{{string.Join(", ", lineIds)}}};");
                    nextPoint += pointIds.Length;
                    nextLine += lineIds.Length;
                    nextLoop++;
                    break;
                }
            }
        }

        return result.ToString();
    }

    static string SafeName(string value) => value.Replace("\"", "_");
    static string Fmt(double value) => value.ToString("G17", CultureInfo.InvariantCulture);

    sealed record LoopInfo(int LoopId, int[] PointIds, int[] LineIds, ContourType Type);
}
