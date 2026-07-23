using System.Text.Json;
using CScore.Fem;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.CScore;

/// <summary>Результат переноса распределённых нагрузок на элементы расчётной сетки.</summary>
public sealed record FemDistributedLoadResolution(
    IReadOnlyList<FemLinearDistributedLoad> Loads,
    IReadOnlyList<string> Errors);

/// <summary>Разрезает нагрузки конструктивных стержней по mesh-элементам и переводит их в локальные оси.</summary>
public sealed class FemDistributedLoadResolver
{
    const double Epsilon = 1e-10;

    /// <summary>Разрешает канонические нагрузки в нагрузки отдельных 3D beam-элементов.</summary>
    public FemDistributedLoadResolution Resolve(
        IReadOnlyList<FemMeshNode> meshNodes,
        IReadOnlyList<FemElement> meshElements,
        IReadOnlyList<FemNode> sourceNodes,
        IReadOnlyList<FemMember> sourceMembers,
        IReadOnlyList<FemMemberLoad> memberLoads)
    {
        var errors = new List<string>();
        var result = new List<FemLinearDistributedLoad>();
        var sourceNodeByTag = sourceNodes
            .Where(node => !string.IsNullOrWhiteSpace(node.NodeTag))
            .ToDictionary(node => node.NodeTag, StringComparer.Ordinal);
        var sourceMemberById = sourceMembers.ToDictionary(member => member.Id);
        var meshNodeByTag = meshNodes
            .Where(node => int.TryParse(node.NodeTag, out _))
            .ToDictionary(node => int.Parse(node.NodeTag));

        foreach (var load in memberLoads)
        {
            if (!sourceMemberById.TryGetValue(load.MemberId, out var member))
            {
                errors.Add($"Распределённая нагрузка {load.Id} ссылается на неизвестный стержень {load.MemberId}.");
                continue;
            }
            if (load.DistributionType.Equals("point", StringComparison.OrdinalIgnoreCase)) continue;
            if (load.CoordinateSystem is not ("local" or "global"))
            {
                errors.Add($"Нагрузка {load.Id} стержня {member.ElemTag}: неизвестная система координат '{load.CoordinateSystem}'.");
                continue;
            }
            if (load.DistributionType is not ("uniform" or "trapezoidal"))
            {
                errors.Add($"Нагрузка {load.Id} стержня {member.ElemTag}: неизвестный тип '{load.DistributionType}'.");
                continue;
            }

            int[] memberEnds = JsonSerializer.Deserialize<int[]>(member.NodeIdsJson) ?? [];
            if (memberEnds.Length != 2 ||
                !sourceNodeByTag.TryGetValue(memberEnds[0].ToString(), out var sourceNodeI) ||
                !sourceNodeByTag.TryGetValue(memberEnds[1].ToString(), out var sourceNodeJ))
            {
                errors.Add($"Стержень {member.ElemTag}: не найдены узлы для распределённой нагрузки {load.Id}.");
                continue;
            }

            var sourceI = ToLinearNode(sourceNodeI);
            var sourceJ = ToLinearNode(sourceNodeJ);
            var sourceFrame = FemLocalAxis.LocalFrame(sourceI, sourceJ, member.RotationDeg);
            var sourceX = sourceFrame.X;
            double length = Distance(sourceI, sourceJ);
            if (!double.IsFinite(length) || length <= Epsilon)
            {
                errors.Add($"Стержень {member.ElemTag}: нулевая или некорректная длина.");
                continue;
            }

            double loadStart = load.StartOffsetM;
            double loadEnd = length - load.EndOffsetM;
            if (!double.IsFinite(loadStart) || !double.IsFinite(loadEnd) ||
                loadStart < 0 || loadEnd > length || loadEnd - loadStart <= Epsilon)
            {
                errors.Add($"Нагрузка {load.Id} стержня {member.ElemTag}: участок выходит за длину стержня или пуст.");
                continue;
            }

            bool matchedElement = false;
            foreach (var meshElement in meshElements.Where(element =>
                         string.Equals(element.SourceMemberTag, member.ElemTag, StringComparison.Ordinal)))
            {
                int[] ends = JsonSerializer.Deserialize<int[]>(meshElement.NodeIdsJson) ?? [];
                if (ends.Length != 2 || !meshNodeByTag.TryGetValue(ends[0], out var meshI) ||
                    !meshNodeByTag.TryGetValue(ends[1], out var meshJ))
                {
                    errors.Add($"Элемент сетки {meshElement.ElemTag}: некорректная топология для стержня {member.ElemTag}.");
                    continue;
                }

                var meshLinearI = ToLinearNode(meshI);
                var meshLinearJ = ToLinearNode(meshJ);
                double sI = ProjectOnSource(meshI, sourceI, sourceX);
                double sJ = ProjectOnSource(meshJ, sourceI, sourceX);
                double overlapStart = Math.Max(loadStart, Math.Min(sI, sJ));
                double overlapEnd = Math.Min(loadEnd, Math.Max(sI, sJ));
                if (overlapEnd - overlapStart <= Epsilon) continue;

                matchedElement = true;
                double uStart = (overlapStart - sI) / (sJ - sI);
                double uEnd = (overlapEnd - sI) / (sJ - sI);
                double aOverL = Math.Min(uStart, uEnd);
                double bOverL = Math.Max(uStart, uEnd);
                var meshFrame = FemLocalAxis.LocalFrame(meshLinearI, meshLinearJ, member.RotationDeg);
                var qAtFirst = ConvertToElementLocal(
                    Evaluate(load, loadStart, loadEnd, overlapStart), load.CoordinateSystem,
                    sourceFrame, meshFrame);
                var qAtSecond = ConvertToElementLocal(
                    Evaluate(load, loadStart, loadEnd, overlapEnd), load.CoordinateSystem,
                    sourceFrame, meshFrame);
                var qStart = uStart <= uEnd ? qAtFirst : qAtSecond;
                var qEnd = uStart <= uEnd ? qAtSecond : qAtFirst;

                result.Add(new FemLinearDistributedLoad(
                    ParseElementTag(meshElement.ElemTag, member.ElemTag),
                    qStart.Y, qStart.Z, qStart.X,
                    qEnd.Y, qEnd.Z, qEnd.X,
                    aOverL, bOverL));
            }

            if (!matchedElement)
                errors.Add($"Нагрузка {load.Id} стержня {member.ElemTag}: не найдено пересечение с mesh-элементами.");
        }

        return new FemDistributedLoadResolution(result, errors);
    }

    static int ParseElementTag(string tag, string memberTag)
    {
        if (int.TryParse(tag, out int value)) return value;
        throw new InvalidOperationException($"Элемент сетки '{tag}' стержня '{memberTag}' имеет нечисловой тег.");
    }

    static FemLinearNode ToLinearNode(FemNode node) =>
        new(int.TryParse(node.NodeTag, out int tag) ? tag : node.Id, node.X, node.Y, node.Z, new bool[6]);

    static FemLinearNode ToLinearNode(FemMeshNode node) =>
        new(int.Parse(node.NodeTag), node.X, node.Y, node.Z, new bool[6]);

    static double Distance(FemLinearNode i, FemLinearNode j)
    {
        double dx = j.X - i.X, dy = j.Y - i.Y, dz = j.Z - i.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    static double ProjectOnSource(FemMeshNode node, FemLinearNode sourceI,
        (double X, double Y, double Z) sourceX)
    {
        double dx = node.X - sourceI.X, dy = node.Y - sourceI.Y, dz = node.Z - sourceI.Z;
        return dx * sourceX.X + dy * sourceX.Y + dz * sourceX.Z;
    }

    static (double X, double Y, double Z) Evaluate(
        FemMemberLoad load, double loadStart, double loadEnd, double coordinate)
    {
        if (load.DistributionType.Equals("uniform", StringComparison.OrdinalIgnoreCase))
            return (load.QxStart, load.QyStart, load.QzStart);
        double t = (coordinate - loadStart) / (loadEnd - loadStart);
        return (
            load.QxStart + (load.QxEnd - load.QxStart) * t,
            load.QyStart + (load.QyEnd - load.QyStart) * t,
            load.QzStart + (load.QzEnd - load.QzStart) * t);
    }

    static (double X, double Y, double Z) ConvertToElementLocal(
        (double X, double Y, double Z) value,
        string coordinateSystem,
        ((double X, double Y, double Z) X,
         (double X, double Y, double Z) Y,
         (double X, double Y, double Z) Z) sourceFrame,
        ((double X, double Y, double Z) X,
         (double X, double Y, double Z) Y,
         (double X, double Y, double Z) Z) elementFrame)
    {
        var global = coordinateSystem.Equals("local", StringComparison.OrdinalIgnoreCase)
            ? Add(Add(Scale(sourceFrame.X, value.X), Scale(sourceFrame.Y, value.Y)), Scale(sourceFrame.Z, value.Z))
            : value;
        return (Dot(global, elementFrame.X), Dot(global, elementFrame.Y), Dot(global, elementFrame.Z));
    }

    static (double X, double Y, double Z) Add(
        (double X, double Y, double Z) a, (double X, double Y, double Z) b) =>
        (a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    static (double X, double Y, double Z) Scale(
        (double X, double Y, double Z) value, double factor) =>
        (value.X * factor, value.Y * factor, value.Z * factor);

    static double Dot(
        (double X, double Y, double Z) a, (double X, double Y, double Z) b) =>
        a.X * b.X + a.Y * b.Y + a.Z * b.Z;
}
