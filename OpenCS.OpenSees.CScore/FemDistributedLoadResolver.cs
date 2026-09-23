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
    /// <summary>Разрешает канонические нагрузки в нагрузки отдельных 3D beam-элементов.</summary>
    public FemDistributedLoadResolution Resolve(
        IReadOnlyList<FemMeshNode> meshNodes,
        IReadOnlyList<FemElement> meshElements,
        IReadOnlyList<FemNode> sourceNodes,
        IReadOnlyList<FemMember> sourceMembers,
        IReadOnlyList<FemMemberLoad> memberLoads)
    {
        var segmentation = FemMemberLoadSegmenter.SegmentDistributed(
            meshNodes, meshElements, sourceNodes, sourceMembers, memberLoads);
        var frames = new FemLoadFrames(meshNodes, meshElements, sourceNodes, sourceMembers);
        var result = new List<FemLinearDistributedLoad>();

        foreach (var piece in segmentation.Distributed)
        {
            var sourceFrame = frames.MemberFrame(piece.MemberTag);
            var elementFrame = frames.ElementFrame(piece.MeshElementTag, piece.MemberTag);
            var qStart = FemLoadFrames.ToElementLocal(piece.QAtA, piece.CoordinateSystem, sourceFrame, elementFrame);
            var qEnd = FemLoadFrames.ToElementLocal(piece.QAtB, piece.CoordinateSystem, sourceFrame, elementFrame);
            result.Add(new FemLinearDistributedLoad(
                FemLoadFrames.ParseElementTag(piece.MeshElementTag, piece.MemberTag),
                qStart.Y, qStart.Z, qStart.X,
                qEnd.Y, qEnd.Z, qEnd.X,
                piece.AOverL, piece.BOverL));
        }

        return new FemDistributedLoadResolution(result, segmentation.Errors.Select(e => e.Message).ToList());
    }
}
