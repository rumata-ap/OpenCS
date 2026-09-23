using System.Globalization;
using System.Text.Json;
using CScore.Planar;

namespace CScore.Fem;

/// <summary>Участок распределённой нагрузки стержня на одном mesh-элементе.
/// <see cref="AOverL"/>/<see cref="BOverL"/> — доли длины элемента от его узла i;
/// <see cref="QAtA"/>/<see cref="QAtB"/> — интенсивности в этих точках в исходной системе нагрузки.</summary>
public sealed record MemberLoadDistributedPiece(int LoadId, string MemberTag, string MeshElementTag,
    double AOverL, double BOverL, PlanarVector3 QAtA, PlanarVector3 QAtB, string CoordinateSystem);

/// <summary>Сосредоточенная нагрузка стержня, совпавшая с mesh-узлом (сила и момент в исходной системе).</summary>
public sealed record MemberLoadPointOnNode(int LoadId, string MemberTag, string MeshNodeTag,
    PlanarVector3 Force, PlanarVector3 Moment, string CoordinateSystem);

/// <summary>Сосредоточенная сила стержня внутри mesh-элемента; <see cref="XOverL"/> — доля длины от узла i.</summary>
public sealed record MemberLoadPointInElement(int LoadId, string MemberTag, string MeshElementTag,
    double XOverL, PlanarVector3 Force, string CoordinateSystem);

/// <summary>Ошибка разрешения нагрузки; <see cref="MemberTag"/> — null, если стержень не найден.</summary>
public sealed record MemberLoadSegmentError(int LoadId, string? MemberTag, string Message);

/// <summary>Результат геометрического разрезания нагрузок стержней по mesh-элементам.</summary>
public sealed record MemberLoadSegmentation(
    IReadOnlyList<MemberLoadDistributedPiece> Distributed,
    IReadOnlyList<MemberLoadPointOnNode> OnNodes,
    IReadOnlyList<MemberLoadPointInElement> InElements,
    IReadOnlyList<MemberLoadSegmentError> Errors);

/// <summary>
/// Разрезает нагрузки конструктивных стержней по элементам расчётной сетки без перевода в локальные оси:
/// значения остаются в системе координат исходной нагрузки. Перевод в оси элемента — задача потребителя.
/// </summary>
public static class FemMemberLoadSegmenter
{
    const double Epsilon = 1e-10;
    const double NodeMatchToleranceM = 1e-6;

    /// <summary>Распределённые и сосредоточенные нагрузки вместе; повторяющиеся ошибки не дублируются.</summary>
    public static MemberLoadSegmentation Segment(
        IReadOnlyList<FemMeshNode> meshNodes,
        IReadOnlyList<FemElement> meshElements,
        IReadOnlyList<FemNode> sourceNodes,
        IReadOnlyList<FemMember> sourceMembers,
        IReadOnlyList<FemMemberLoad> memberLoads)
    {
        var distributed = SegmentDistributed(meshNodes, meshElements, sourceNodes, sourceMembers, memberLoads);
        var points = SegmentPoints(meshNodes, meshElements, sourceNodes, sourceMembers, memberLoads);
        return new MemberLoadSegmentation(
            distributed.Distributed, points.OnNodes, points.InElements,
            distributed.Errors.Concat(points.Errors).Distinct().ToList());
    }

    /// <summary>Только распределённые нагрузки (uniform/trapezoidal).</summary>
    public static MemberLoadSegmentation SegmentDistributed(
        IReadOnlyList<FemMeshNode> meshNodes,
        IReadOnlyList<FemElement> meshElements,
        IReadOnlyList<FemNode> sourceNodes,
        IReadOnlyList<FemMember> sourceMembers,
        IReadOnlyList<FemMemberLoad> memberLoads)
    {
        var errors = new List<MemberLoadSegmentError>();
        var pieces = new List<MemberLoadDistributedPiece>();
        var ctx = new Context(meshNodes, sourceNodes, sourceMembers);

        foreach (var load in memberLoads)
        {
            if (!ctx.MemberById.TryGetValue(load.MemberId, out var member))
            {
                errors.Add(new(load.Id, null, $"Распределённая нагрузка {load.Id} ссылается на неизвестный стержень {load.MemberId}."));
                continue;
            }
            if (load.DistributionType.Equals("point", StringComparison.OrdinalIgnoreCase)) continue;
            if (load.CoordinateSystem is not ("local" or "global"))
            {
                errors.Add(new(load.Id, member.ElemTag, $"Нагрузка {load.Id} стержня {member.ElemTag}: неизвестная система координат '{load.CoordinateSystem}'."));
                continue;
            }
            if (load.DistributionType is not ("uniform" or "trapezoidal"))
            {
                errors.Add(new(load.Id, member.ElemTag, $"Нагрузка {load.Id} стержня {member.ElemTag}: неизвестный тип '{load.DistributionType}'."));
                continue;
            }
            if (!ctx.TryMemberEnds(member, out var sourceI, out var sourceJ))
            {
                errors.Add(new(load.Id, member.ElemTag, $"Стержень {member.ElemTag}: не найдены узлы для распределённой нагрузки {load.Id}."));
                continue;
            }
            double length = (sourceJ - sourceI).Length;
            if (!double.IsFinite(length) || length <= Epsilon)
            {
                errors.Add(new(load.Id, member.ElemTag, $"Стержень {member.ElemTag}: нулевая или некорректная длина."));
                continue;
            }
            var sourceX = (sourceJ - sourceI) * (1.0 / length);

            double loadStart = load.StartOffsetM;
            double loadEnd = length - load.EndOffsetM;
            if (!double.IsFinite(loadStart) || !double.IsFinite(loadEnd) ||
                loadStart < 0 || loadEnd > length || loadEnd - loadStart <= Epsilon)
            {
                errors.Add(new(load.Id, member.ElemTag, $"Нагрузка {load.Id} стержня {member.ElemTag}: участок выходит за длину стержня или пуст."));
                continue;
            }

            bool matchedElement = false;
            foreach (var meshElement in meshElements.Where(element =>
                         string.Equals(element.SourceMemberTag, member.ElemTag, StringComparison.Ordinal)))
            {
                if (!ctx.TryElementEnds(meshElement, out _, out var meshI, out _, out var meshJ))
                {
                    errors.Add(new(load.Id, member.ElemTag, $"Элемент сетки {meshElement.ElemTag}: некорректная топология для стержня {member.ElemTag}."));
                    continue;
                }

                double sI = (meshI - sourceI).Dot(sourceX);
                double sJ = (meshJ - sourceI).Dot(sourceX);
                double overlapStart = Math.Max(loadStart, Math.Min(sI, sJ));
                double overlapEnd = Math.Min(loadEnd, Math.Max(sI, sJ));
                if (overlapEnd - overlapStart <= Epsilon) continue;

                matchedElement = true;
                double uStart = (overlapStart - sI) / (sJ - sI);
                double uEnd = (overlapEnd - sI) / (sJ - sI);
                var qAtFirst = Evaluate(load, loadStart, loadEnd, overlapStart);
                var qAtSecond = Evaluate(load, loadStart, loadEnd, overlapEnd);
                pieces.Add(new MemberLoadDistributedPiece(
                    load.Id, member.ElemTag, meshElement.ElemTag,
                    Math.Min(uStart, uEnd), Math.Max(uStart, uEnd),
                    uStart <= uEnd ? qAtFirst : qAtSecond,
                    uStart <= uEnd ? qAtSecond : qAtFirst,
                    load.CoordinateSystem));
            }

            if (!matchedElement)
                errors.Add(new(load.Id, member.ElemTag, $"Нагрузка {load.Id} стержня {member.ElemTag}: не найдено пересечение с mesh-элементами."));
        }

        return new MemberLoadSegmentation(pieces, [], [], errors);
    }

    /// <summary>Только сосредоточенные нагрузки (DistributionType="point").</summary>
    public static MemberLoadSegmentation SegmentPoints(
        IReadOnlyList<FemMeshNode> meshNodes,
        IReadOnlyList<FemElement> meshElements,
        IReadOnlyList<FemNode> sourceNodes,
        IReadOnlyList<FemMember> sourceMembers,
        IReadOnlyList<FemMemberLoad> memberLoads)
    {
        var errors = new List<MemberLoadSegmentError>();
        var onNodes = new List<MemberLoadPointOnNode>();
        var inElements = new List<MemberLoadPointInElement>();
        var ctx = new Context(meshNodes, sourceNodes, sourceMembers);

        foreach (var load in memberLoads)
        {
            if (!load.DistributionType.Equals("point", StringComparison.OrdinalIgnoreCase)) continue;
            if (!ctx.MemberById.TryGetValue(load.MemberId, out var member))
            {
                errors.Add(new(load.Id, null, $"Сосредоточенная нагрузка {load.Id} ссылается на неизвестный стержень {load.MemberId}."));
                continue;
            }
            if (load.CoordinateSystem is not ("local" or "global"))
            {
                errors.Add(new(load.Id, member.ElemTag, $"Нагрузка {load.Id} стержня {member.ElemTag}: неизвестная система координат '{load.CoordinateSystem}'."));
                continue;
            }
            if (!ctx.TryMemberEnds(member, out var sourceI, out var sourceJ))
            {
                errors.Add(new(load.Id, member.ElemTag, $"Стержень {member.ElemTag}: не найдены узлы для сосредоточенной нагрузки {load.Id}."));
                continue;
            }
            double length = (sourceJ - sourceI).Length;
            if (!double.IsFinite(length) || length <= Epsilon)
            {
                errors.Add(new(load.Id, member.ElemTag, $"Стержень {member.ElemTag}: нулевая или некорректная длина."));
                continue;
            }
            var sourceX = (sourceJ - sourceI) * (1.0 / length);
            if (!double.IsFinite(load.StartOffsetM) || load.StartOffsetM < -Epsilon || load.StartOffsetM > length + Epsilon)
            {
                errors.Add(new(load.Id, member.ElemTag, $"Сосредоточенная нагрузка {load.Id} стержня {member.ElemTag}: точка приложения вне длины стержня."));
                continue;
            }
            double position = Math.Clamp(load.StartOffsetM, 0, length);

            var memberElements = meshElements
                .Where(element => string.Equals(element.SourceMemberTag, member.ElemTag, StringComparison.Ordinal))
                .ToArray();
            if (memberElements.Length == 0)
            {
                errors.Add(new(load.Id, member.ElemTag, $"Сосредоточенная нагрузка {load.Id} стержня {member.ElemTag}: нет элементов сетки."));
                continue;
            }

            var nodePositions = new List<(string Tag, double S)>();
            var endpoints = new List<(string ElemTag, double SI, double SJ)>();
            bool topologyError = false;
            foreach (var meshElement in memberElements)
            {
                if (!ctx.TryElementEnds(meshElement, out var tagI, out var meshI, out var tagJ, out var meshJ))
                {
                    errors.Add(new(load.Id, member.ElemTag, $"Элемент сетки {meshElement.ElemTag}: некорректная топология для стержня {member.ElemTag}."));
                    topologyError = true;
                    continue;
                }
                double sI = (meshI - sourceI).Dot(sourceX);
                double sJ = (meshJ - sourceI).Dot(sourceX);
                if (nodePositions.All(p => p.Tag != tagI)) nodePositions.Add((tagI, sI));
                if (nodePositions.All(p => p.Tag != tagJ)) nodePositions.Add((tagJ, sJ));
                endpoints.Add((meshElement.ElemTag, sI, sJ));
            }
            if (topologyError) continue;

            var force = new PlanarVector3(load.QxStart, load.QyStart, load.QzStart);
            var moment = new PlanarVector3(load.Mx, load.My, load.Mz);
            var match = nodePositions.FirstOrDefault(p => Math.Abs(p.S - position) <= NodeMatchToleranceM);
            if (match.Tag is not null)
            {
                onNodes.Add(new MemberLoadPointOnNode(load.Id, member.ElemTag, match.Tag, force, moment, load.CoordinateSystem));
                continue;
            }

            if (load.Mx != 0 || load.My != 0 || load.Mz != 0)
            {
                errors.Add(new(load.Id, member.ElemTag, $"Сосредоточенная нагрузка {load.Id} стержня {member.ElemTag}: момент допустим только в узле расчётной сетки — переместите точку приложения или уменьшите шаг разбиения стержня."));
                continue;
            }

            var containing = endpoints.FirstOrDefault(e =>
                position > Math.Min(e.SI, e.SJ) + Epsilon && position < Math.Max(e.SI, e.SJ) - Epsilon);
            if (containing.ElemTag is null)
            {
                errors.Add(new(load.Id, member.ElemTag, $"Сосредоточенная нагрузка {load.Id} стержня {member.ElemTag}: не найден элемент сетки, содержащий точку приложения."));
                continue;
            }
            double xOverL = (position - containing.SI) / (containing.SJ - containing.SI);
            inElements.Add(new MemberLoadPointInElement(load.Id, member.ElemTag, containing.ElemTag, xOverL, force, load.CoordinateSystem));
        }

        return new MemberLoadSegmentation([], onNodes, inElements, errors);
    }

    static PlanarVector3 Evaluate(FemMemberLoad load, double loadStart, double loadEnd, double coordinate)
    {
        if (load.DistributionType.Equals("uniform", StringComparison.OrdinalIgnoreCase))
            return new PlanarVector3(load.QxStart, load.QyStart, load.QzStart);
        double t = (coordinate - loadStart) / (loadEnd - loadStart);
        return new PlanarVector3(
            load.QxStart + (load.QxEnd - load.QxStart) * t,
            load.QyStart + (load.QyEnd - load.QyStart) * t,
            load.QzStart + (load.QzEnd - load.QzStart) * t);
    }

    sealed class Context
    {
        readonly Dictionary<string, FemNode> _sourceNodeByTag;
        readonly Dictionary<string, FemMeshNode> _meshNodeByTag;
        public Dictionary<int, FemMember> MemberById { get; }

        public Context(IReadOnlyList<FemMeshNode> meshNodes, IReadOnlyList<FemNode> sourceNodes,
            IReadOnlyList<FemMember> sourceMembers)
        {
            _sourceNodeByTag = sourceNodes
                .Where(node => !string.IsNullOrWhiteSpace(node.NodeTag))
                .ToDictionary(node => node.NodeTag, StringComparer.Ordinal);
            MemberById = sourceMembers.ToDictionary(member => member.Id);
            _meshNodeByTag = new Dictionary<string, FemMeshNode>(StringComparer.Ordinal);
            foreach (var node in meshNodes)
                if (FemMeshTopology.CanonicalNodeTag(node.NodeTag) is string tag)
                    _meshNodeByTag[tag] = node;
        }

        public bool TryMemberEnds(FemMember member, out PlanarVector3 i, out PlanarVector3 j)
        {
            i = j = PlanarVector3.Zero;
            int[]? ends;
            try { ends = JsonSerializer.Deserialize<int[]>(member.NodeIdsJson); }
            catch (JsonException) { return false; }
            if (ends is null || ends.Length != 2 ||
                !_sourceNodeByTag.TryGetValue(ends[0].ToString(CultureInfo.InvariantCulture), out var nodeI) ||
                !_sourceNodeByTag.TryGetValue(ends[1].ToString(CultureInfo.InvariantCulture), out var nodeJ))
                return false;
            i = new PlanarVector3(nodeI.X, nodeI.Y, nodeI.Z);
            j = new PlanarVector3(nodeJ.X, nodeJ.Y, nodeJ.Z);
            return true;
        }

        public bool TryElementEnds(FemElement element, out string tagI, out PlanarVector3 i,
            out string tagJ, out PlanarVector3 j)
        {
            tagI = tagJ = "";
            i = j = PlanarVector3.Zero;
            var tags = FemMeshTopology.ReadNodeTags(element, 2);
            if (tags is null ||
                !_meshNodeByTag.TryGetValue(tags[0], out var nodeI) ||
                !_meshNodeByTag.TryGetValue(tags[1], out var nodeJ))
                return false;
            tagI = tags[0];
            tagJ = tags[1];
            i = new PlanarVector3(nodeI.X, nodeI.Y, nodeI.Z);
            j = new PlanarVector3(nodeJ.X, nodeJ.Y, nodeJ.Z);
            return true;
        }
    }
}
