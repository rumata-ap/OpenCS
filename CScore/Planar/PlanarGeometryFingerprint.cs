using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CScore.Planar;

/// <summary>Детерминированный SHA-256 отпечаток геометрии PlanarRegion (контуры + Frame3D +
/// разметка границ) — для инвалидации производных данных (будущий MeshSnapshot). Идиома
/// идентична PlateSectionOpenSeesMapper.Fingerprint (OpenCS.OpenSees.CScore).</summary>
public static class PlanarGeometryFingerprint
{
    public static string Compute(
        IReadOnlyList<Contour> contours,
        Frame3D frame,
        IReadOnlyList<BoundarySegment> boundarySegments,
        IReadOnlyList<PlanarConstraintObject>? constraintObjects = null)
    {
        var parts = new List<string>();

        foreach (var c in contours)
        {
            parts.Add(c.Type.ToString());
            for (int i = 0; i < c.X.Count; i++)
                parts.Add($"{Fmt(c.X[i])},{Fmt(c.Y[i])}");
        }

        parts.Add(FrameFingerprint(frame));

        foreach (var seg in boundarySegments)
            parts.Add($"{seg.Loop}:{seg.HoleIndex}:{seg.StartVertex}:{seg.EndVertex}:{seg.Role}");

        foreach (var constraint in (constraintObjects ?? []).OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            parts.Add($"constraint:{constraint.Id}:{constraint.Tag}:{constraint.Geometry.Kind}:{Fmt(constraint.ToleranceM)}");
            if (constraint.IsDerived)
                parts.Add("derived:true");
            foreach (var point in constraint.Geometry.Points)
                parts.Add($"point:{Fmt(point.U)},{Fmt(point.V)}");

            var structural = constraint.StructuralFacet;
            parts.Add($"structural:{structural.Kind}:{structural.DofMask}");
            if (structural.MasterReference is not null)
                parts.Add($"master:{structural.MasterReference.Provider}:{structural.MasterReference.Key}");
            if (structural.Frame is not null)
                parts.Add($"structural-frame:{FrameFingerprint(structural.Frame)}");

            parts.Add($"mesh:{constraint.MeshFacet.Kind}");
            if (constraint.MasterReference is not null)
                parts.Add($"object-master:{constraint.MasterReference.Provider}:{constraint.MasterReference.Key}");
            parts.Add($"object-dof:{constraint.DofMask}");
            if (constraint.StructuralFrame is not null)
                parts.Add($"object-frame:{FrameFingerprint(constraint.StructuralFrame)}");
            parts.Add($"source:{constraint.Provenance ?? ""}");

            foreach (var source in (constraint.SourceReferences ?? []).OrderBy(item => item.MemberId).ThenBy(item => item.MemberTag, StringComparer.Ordinal))
            {
                parts.Add($"source-ref:{source.MemberId}:{source.MemberTag}");
                foreach (var id in source.ElementIds.OrderBy(id => id)) parts.Add($"source-element-id:{id}");
                foreach (var tag in source.ElementTags.OrderBy(tag => tag, StringComparer.Ordinal)) parts.Add($"source-element-tag:{tag}");
                foreach (var id in source.NodeIds.OrderBy(id => id)) parts.Add($"source-node-id:{id}");
                foreach (var tag in source.NodeTags.OrderBy(tag => tag, StringComparer.Ordinal)) parts.Add($"source-node-tag:{tag}");
            }

            foreach (var relation in (constraint.StructuralRelations ?? [])
                         .OrderBy(item => item.SourceMemberId)
                         .ThenBy(item => item.SourceMemberTag, StringComparer.Ordinal))
            {
                parts.Add($"relation:{relation.SourceMemberId}:{relation.SourceMemberTag}:{relation.Kind}:{relation.DofMask}");
                foreach (var id in relation.SourceElementIds.OrderBy(id => id)) parts.Add($"relation-element-id:{id}");
                foreach (var tag in relation.SourceElementTags.OrderBy(tag => tag, StringComparer.Ordinal)) parts.Add($"relation-element-tag:{tag}");
                if (relation.MasterReference is not null)
                    parts.Add($"relation-master:{relation.MasterReference.Provider}:{relation.MasterReference.Key}");
            }
        }

        return Hash(string.Join("|", parts));
    }

    static string FrameFingerprint(Frame3D f) => string.Join(",",
        Fmt(f.Origin.X), Fmt(f.Origin.Y), Fmt(f.Origin.Z),
        Fmt(f.LocalX.X), Fmt(f.LocalX.Y), Fmt(f.LocalX.Z),
        Fmt(f.LocalY.X), Fmt(f.LocalY.Y), Fmt(f.LocalY.Z),
        Fmt(f.LocalZ.X), Fmt(f.LocalZ.Y), Fmt(f.LocalZ.Z));

    static string Fmt(double v) => v.ToString("G17", CultureInfo.InvariantCulture);

    static string Hash(string input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
}
