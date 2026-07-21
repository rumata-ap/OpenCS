using System.Text.Json;
using System.Windows.Media.Media3D;
using CScore;
using CScore.Fem;
using OpenCS.OpenSees.Structural;

namespace OpenCS.ViewModels;

/// <summary>Строит описания контуров сечений конструктивных стержней для 3D-вида.</summary>
public static class FemSectionGlyphFactory
{
    /// <summary>Создаёт по одному глифу в середине каждого корректного beam-стержня.</summary>
    public static IReadOnlyList<FemSectionGlyph> Create(
        IReadOnlyList<FemMember> members,
        IReadOnlyList<CrossSection> sections,
        IReadOnlyDictionary<string, Point3D> nodeMap)
    {
        var sectionById = sections.ToDictionary(section => section.Id);
        var result = new List<FemSectionGlyph>();

        foreach (var member in members.Where(member => member.ElemType == "beam"))
        {
            var nodeIds = JsonSerializer.Deserialize<int[]>(member.NodeIdsJson ?? "[]") ?? [];
            if (nodeIds.Length < 2 ||
                !nodeMap.TryGetValue(nodeIds[0].ToString(), out var first) ||
                !nodeMap.TryGetValue(nodeIds[1].ToString(), out var second))
                continue;

            var delta = second - first;
            double length = delta.Length;
            if (!double.IsFinite(length) || length < 1e-12)
                continue;

            var frame = FemLocalAxis.LocalFrame(
                new FemLinearNode(nodeIds[0], first.X, first.Y, first.Z, new bool[6]),
                new FemLinearNode(nodeIds[1], second.X, second.Y, second.Z, new bool[6]),
                member.RotationDeg);

            var contours = member.CrossSectionId is { } sectionId && sectionById.TryGetValue(sectionId, out var section)
                ? ReadContours(section)
                : [];

            result.Add(new FemSectionGlyph(
                member.ElemTag,
                new Point3D((first.X + second.X) / 2, (first.Y + second.Y) / 2, (first.Z + second.Z) / 2),
                ToVector(frame.X), ToVector(frame.Y), ToVector(frame.Z),
                member.RotationDeg,
                contours,
                Math.Clamp(length * 0.08, 0.05, 0.24)));
        }

        return result;
    }

    static IReadOnlyList<IReadOnlyList<(double Y, double Z)>> ReadContours(CrossSection section)
    {
        var contours = new List<IReadOnlyList<(double Y, double Z)>>();
        foreach (var contour in section.Areas.SelectMany(area => area.Contours))
        {
            if (contour.X.Count != contour.Y.Count || contour.X.Count < 2)
                continue;

            var points = new List<(double Y, double Z)>(contour.X.Count);
            for (int i = 0; i < contour.X.Count; i++)
                points.Add((contour.Y[i], contour.X[i]));
            contours.Add(points);
        }

        return contours;
    }

    static Vector3D ToVector((double X, double Y, double Z) vector) =>
        new(vector.X, vector.Y, vector.Z);
}
