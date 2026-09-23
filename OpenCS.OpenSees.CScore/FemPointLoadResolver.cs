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
    public FemPointLoadResolution Resolve(
        IReadOnlyList<FemMeshNode> meshNodes,
        IReadOnlyList<FemElement> meshElements,
        IReadOnlyList<FemNode> sourceNodes,
        IReadOnlyList<FemMember> sourceMembers,
        IReadOnlyList<FemMemberLoad> memberLoads)
    {
        var segmentation = FemMemberLoadSegmenter.SegmentPoints(
            meshNodes, meshElements, sourceNodes, sourceMembers, memberLoads);
        var frames = new FemLoadFrames(meshNodes, meshElements, sourceNodes, sourceMembers);
        var nodalLoads = new List<FemLinearNodalLoad>();
        var elementLoads = new List<FemLinearPointLoad>();

        foreach (var point in segmentation.OnNodes)
        {
            var sourceFrame = frames.MemberFrame(point.MemberTag);
            var force = FemLoadFrames.ToGlobal(point.Force, point.CoordinateSystem, sourceFrame);
            var moment = FemLoadFrames.ToGlobal(point.Moment, point.CoordinateSystem, sourceFrame);
            nodalLoads.Add(new FemLinearNodalLoad(int.Parse(point.MeshNodeTag),
                force.X, force.Y, force.Z, moment.X, moment.Y, moment.Z));
        }

        foreach (var point in segmentation.InElements)
        {
            var sourceFrame = frames.MemberFrame(point.MemberTag);
            var elementFrame = frames.ElementFrame(point.MeshElementTag, point.MemberTag);
            var forceLocal = FemLoadFrames.ToElementLocal(point.Force, point.CoordinateSystem, sourceFrame, elementFrame);
            elementLoads.Add(new FemLinearPointLoad(
                FemLoadFrames.ParseElementTag(point.MeshElementTag, point.MemberTag),
                forceLocal.Y, forceLocal.Z, forceLocal.X, point.XOverL));
        }

        return new FemPointLoadResolution(nodalLoads, elementLoads,
            segmentation.Errors.Select(e => e.Message).ToList());
    }
}
