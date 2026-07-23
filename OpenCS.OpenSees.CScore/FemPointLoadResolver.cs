using System.Text.Json;
using CScore.Fem;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.CScore;

/// <summary>Результат переноса сосредоточенных нагрузок конструктивных стержней на узлы/элементы сетки.</summary>
public sealed record FemPointLoadResolution(
    IReadOnlyList<FemLinearNodalLoad> NodalLoads,
    IReadOnlyList<FemLinearPointLoad> ElementLoads,
    IReadOnlyList<string> Errors);

/// <summary>Переносит сосредоточенные нагрузки (DistributionType="point") на узлы или элементы
/// расчётной сетки. Точка, совпадающая с узлом сетки, — узловая нагрузка (сила и момент).
/// Точка внутри элемента — eleLoad -type -beamPoint (только сила; момент внутри элемента —
/// ошибка разрешения, OpenSees не поддерживает сосредоточенный момент внутри пролёта).</summary>
public sealed class FemPointLoadResolver
{
    const double Epsilon = 1e-10;
    const double NodeMatchToleranceM = 1e-6;

    public FemPointLoadResolution Resolve(
        IReadOnlyList<FemMeshNode> meshNodes,
        IReadOnlyList<FemElement> meshElements,
        IReadOnlyList<FemNode> sourceNodes,
        IReadOnlyList<FemMember> sourceMembers,
        IReadOnlyList<FemMemberLoad> memberLoads)
    {
        var errors = new List<string>();
        var nodalLoads = new List<FemLinearNodalLoad>();
        var elementLoads = new List<FemLinearPointLoad>();
        var sourceNodeByTag = sourceNodes
            .Where(node => !string.IsNullOrWhiteSpace(node.NodeTag))
            .ToDictionary(node => node.NodeTag, StringComparer.Ordinal);
        var sourceMemberById = sourceMembers.ToDictionary(member => member.Id);
        var meshNodeByTag = meshNodes
            .Where(node => int.TryParse(node.NodeTag, out _))
            .ToDictionary(node => int.Parse(node.NodeTag));

        foreach (var load in memberLoads)
        {
            if (!load.DistributionType.Equals("point", StringComparison.OrdinalIgnoreCase)) continue;
            if (!sourceMemberById.TryGetValue(load.MemberId, out var member))
            {
                errors.Add($"Сосредоточенная нагрузка {load.Id} ссылается на неизвестный стержень {load.MemberId}.");
                continue;
            }
            if (load.CoordinateSystem is not ("local" or "global"))
            {
                errors.Add($"Нагрузка {load.Id} стержня {member.ElemTag}: неизвестная система координат '{load.CoordinateSystem}'.");
                continue;
            }

            int[] memberEnds = JsonSerializer.Deserialize<int[]>(member.NodeIdsJson) ?? [];
            if (memberEnds.Length != 2 ||
                !sourceNodeByTag.TryGetValue(memberEnds[0].ToString(), out var sourceNodeI) ||
                !sourceNodeByTag.TryGetValue(memberEnds[1].ToString(), out var sourceNodeJ))
            {
                errors.Add($"Стержень {member.ElemTag}: не найдены узлы для сосредоточенной нагрузки {load.Id}.");
                continue;
            }

            var sourceI = ToLinearNode(sourceNodeI);
            var sourceJ = ToLinearNode(sourceNodeJ);
            var sourceFrame = FemLocalAxis.LocalFrame(sourceI, sourceJ, member.RotationDeg);
            double length = Distance(sourceI, sourceJ);
            if (!double.IsFinite(length) || length <= Epsilon)
            {
                errors.Add($"Стержень {member.ElemTag}: нулевая или некорректная длина.");
                continue;
            }
            if (!double.IsFinite(load.StartOffsetM) || load.StartOffsetM < -Epsilon || load.StartOffsetM > length + Epsilon)
            {
                errors.Add($"Сосредоточенная нагрузка {load.Id} стержня {member.ElemTag}: точка приложения вне длины стержня.");
                continue;
            }
            double position = Math.Clamp(load.StartOffsetM, 0, length);

            var memberElements = meshElements
                .Where(element => string.Equals(element.SourceMemberTag, member.ElemTag, StringComparison.Ordinal))
                .ToArray();
            if (memberElements.Length == 0)
            {
                errors.Add($"Сосредоточенная нагрузка {load.Id} стержня {member.ElemTag}: нет элементов сетки.");
                continue;
            }

            var nodePositions = new Dictionary<int, double>();
            var endpointsByElement = new List<(int ElemTag, int NodeI, int NodeJ, double SI, double SJ)>();
            bool topologyError = false;
            foreach (var meshElement in memberElements)
            {
                int[] ends = JsonSerializer.Deserialize<int[]>(meshElement.NodeIdsJson) ?? [];
                if (ends.Length != 2 || !meshNodeByTag.TryGetValue(ends[0], out var meshI) ||
                    !meshNodeByTag.TryGetValue(ends[1], out var meshJ))
                {
                    errors.Add($"Элемент сетки {meshElement.ElemTag}: некорректная топология для стержня {member.ElemTag}.");
                    topologyError = true;
                    continue;
                }
                double sI = ProjectOnSource(meshI, sourceI, sourceFrame.X);
                double sJ = ProjectOnSource(meshJ, sourceI, sourceFrame.X);
                nodePositions.TryAdd(ends[0], sI);
                nodePositions.TryAdd(ends[1], sJ);
                endpointsByElement.Add((ParseElementTag(meshElement.ElemTag, member.ElemTag), ends[0], ends[1], sI, sJ));
            }
            if (topologyError) continue;

            var matches = nodePositions.Where(kv => Math.Abs(kv.Value - position) <= NodeMatchToleranceM).ToArray();
            if (matches.Length > 0)
            {
                var force = ToGlobal((load.QxStart, load.QyStart, load.QzStart), load.CoordinateSystem, sourceFrame);
                var moment = ToGlobal((load.Mx, load.My, load.Mz), load.CoordinateSystem, sourceFrame);
                nodalLoads.Add(new FemLinearNodalLoad(matches[0].Key, force.X, force.Y, force.Z, moment.X, moment.Y, moment.Z));
                continue;
            }

            if (load.Mx != 0 || load.My != 0 || load.Mz != 0)
            {
                errors.Add($"Сосредоточенная нагрузка {load.Id} стержня {member.ElemTag}: момент допустим только в узле расчётной сетки — переместите точку приложения или уменьшите шаг разбиения стержня.");
                continue;
            }

            var candidates = endpointsByElement
                .Where(e => position > Math.Min(e.SI, e.SJ) + Epsilon && position < Math.Max(e.SI, e.SJ) - Epsilon)
                .ToArray();
            if (candidates.Length == 0)
            {
                errors.Add($"Сосредоточенная нагрузка {load.Id} стержня {member.ElemTag}: не найден элемент сетки, содержащий точку приложения.");
                continue;
            }
            var containing = candidates[0];

            var meshLinearI = ToLinearNode(meshNodeByTag[containing.NodeI]);
            var meshLinearJ = ToLinearNode(meshNodeByTag[containing.NodeJ]);
            var meshFrame = FemLocalAxis.LocalFrame(meshLinearI, meshLinearJ, member.RotationDeg);
            var forceLocal = ConvertToElementLocal(
                (load.QxStart, load.QyStart, load.QzStart), load.CoordinateSystem, sourceFrame, meshFrame);
            double xL = (position - containing.SI) / (containing.SJ - containing.SI);
            elementLoads.Add(new FemLinearPointLoad(containing.ElemTag, forceLocal.Y, forceLocal.Z, forceLocal.X, xL));
        }

        return new FemPointLoadResolution(nodalLoads, elementLoads, errors);
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

    static (double X, double Y, double Z) ToGlobal(
        (double X, double Y, double Z) value,
        string coordinateSystem,
        ((double X, double Y, double Z) X,
         (double X, double Y, double Z) Y,
         (double X, double Y, double Z) Z) sourceFrame) =>
        coordinateSystem.Equals("local", StringComparison.OrdinalIgnoreCase)
            ? Add(Add(Scale(sourceFrame.X, value.X), Scale(sourceFrame.Y, value.Y)), Scale(sourceFrame.Z, value.Z))
            : value;

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
        var global = ToGlobal(value, coordinateSystem, sourceFrame);
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
