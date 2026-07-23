using System.Text.Json;
using System.Windows.Media.Media3D;
using CScore.Fem;
using OpenCS.OpenSees.Structural;

namespace OpenCS.ViewModels;

/// <summary>Преобразует канонические нагрузки стержней в данные для 3D-глифов.</summary>
public static class FemMemberLoadGlyphFactory
{
    /// <summary>Строит глифы по выбранному выражению нагрузки.</summary>
    public static IReadOnlyList<FemMemberLoadGlyph> Create(
        IReadOnlyList<FemMember> members,
        IReadOnlyList<FemNode> nodes,
        IReadOnlyList<FemMemberLoad> loads)
    {
        var nodeMap = nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.NodeTag))
            .ToDictionary(node => node.NodeTag, node => new Point3D(node.X, node.Y, node.Z), StringComparer.Ordinal);
        var memberById = members.ToDictionary(member => member.Id);
        var result = new List<FemMemberLoadGlyph>();

        foreach (var load in loads)
        {
            if (!memberById.TryGetValue(load.MemberId, out var member) || member.ElemType != "beam") continue;
            var ids = JsonSerializer.Deserialize<int[]>(member.NodeIdsJson ?? "[]") ?? [];
            if (ids.Length != 2 || !nodeMap.TryGetValue(ids[0].ToString(), out var first) ||
                !nodeMap.TryGetValue(ids[1].ToString(), out var second)) continue;

            var delta = second - first;
            double length = delta.Length;
            if (!double.IsFinite(length) || length < 1e-12) continue;

            if (load.DistributionType.Equals("point", StringComparison.OrdinalIgnoreCase))
            {
                double p = Math.Clamp(load.StartOffsetM / length, 0, 1);
                var point = first + delta * p;
                var force = ToGlobal(load, first, second, false);
                result.Add(new FemMemberLoadGlyph(
                    member.ElemTag, point, point, force, force,
                    $"{member.ElemTag}: {load.CoordinateSystem}, точка"));
                continue;
            }

            Vector3D qStart = ToGlobal(load, first, second, false);
            Vector3D qEnd = load.DistributionType.Equals("uniform", StringComparison.OrdinalIgnoreCase)
                ? qStart : ToGlobal(load, first, second, true);
            double a = Math.Clamp(load.StartOffsetM / length, 0, 1);
            double b = Math.Clamp(1 - load.EndOffsetM / length, 0, 1);
            if (b <= a) continue;

            result.Add(new FemMemberLoadGlyph(
                member.ElemTag,
                first + delta * a,
                first + delta * b,
                qStart, qEnd,
                $"{member.ElemTag}: {load.CoordinateSystem}, {load.DistributionType}"));
        }

        return result;

        Vector3D ToGlobal(FemMemberLoad load, Point3D i, Point3D j, bool end)
        {
            var value = end
                ? new Vector3D(load.QxEnd, load.QyEnd, load.QzEnd)
                : new Vector3D(load.QxStart, load.QyStart, load.QzStart);
            if (load.CoordinateSystem.Equals("global", StringComparison.OrdinalIgnoreCase)) return value;

            var frame = FemLocalAxis.LocalFrame(
                new FemLinearNode(0, i.X, i.Y, i.Z, new bool[6]),
                new FemLinearNode(0, j.X, j.Y, j.Z, new bool[6]),
                memberById[load.MemberId].RotationDeg);
            return new Vector3D(
                frame.X.X * value.X + frame.Y.X * value.Y + frame.Z.X * value.Z,
                frame.X.Y * value.X + frame.Y.Y * value.Y + frame.Z.Y * value.Z,
                frame.X.Z * value.X + frame.Y.Z * value.Y + frame.Z.Z * value.Z);
        }
    }
}
