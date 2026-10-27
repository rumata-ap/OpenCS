using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CScore.Fem.Fragment;

/// <summary>
/// Отпечаток снимка полосы родителя (спека разд. 6): SHA-256 по каноническим строкам узлов, КонЭ, сетки, загружений,
/// определений, нагрузок, АЖТ, пружин и закреплений узлов сетки. Id БД не входят — загружение, узел, КонЭ и группа представлены тегами, строки
/// каждого вида упорядочены, поэтому перечитывание той же схемы даёт тот же отпечаток.
/// </summary>
public static class StoreyFragmentFingerprint
{
    public static string Compute(StoreyFragmentParentSnapshot s)
    {
        ArgumentNullException.ThrowIfNull(s);
        // Загружения — по тегу; при совпадении тегов — в порядке Id (ранг одинаков при перечитывании).
        var lcRank = s.LoadCases.OrderBy(c => c.Tag, StringComparer.Ordinal).ThenBy(c => c.Id)
            .Select((c, k) => (c.Id, k)).GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First().k);
        string Lc(int id) => lcRank.TryGetValue(id, out int k) ? "#" + k.ToString(CultureInfo.InvariantCulture) : "?" + id;
        var nodeTag = s.Nodes.GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First().NodeTag);
        var memberTag = s.Members.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First().ElemTag);
        var groupTag = s.Groups.GroupBy(g => g.Id).ToDictionary(g => g.Key, g => g.First().Tag);
        var regionPrint = s.Regions.GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First().GeometryFingerprint);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Section(string name, IEnumerable<string> lines)
        {
            hash.AppendData(Encoding.UTF8.GetBytes("[" + name + "]\n"));
            foreach (var line in lines.OrderBy(l => l, StringComparer.Ordinal))
                hash.AppendData(Encoding.UTF8.GetBytes(line + "\n"));
        }

        Section("nodes", s.Nodes.Select(n => J(n.NodeTag, D(n.X), D(n.Y), D(n.Z), I(n.DofMask))));
        Section("members", s.Members.Select(m => J(m.ElemTag, m.ElemType, m.NodeIdsJson, m.SectionTag, m.MaterialTag,
            D(m.ThicknessM), I(m.CrossSectionId), m.GjStrategy, D(m.GjManualValue), D(m.TargetMeshLengthM), D(m.RotationDeg),
            m.PlanarRegionId is int r ? regionPrint.GetValueOrDefault(r, "?") : "", m.Kind, m.RigidJunctionMode,
            I(m.RigidJunctionMask), m.RigidJunctionKind, m.RigidJunctionTraceJson)));
        Section("mesh_nodes", s.MeshNodes.Select(n => J(n.NodeTag, D(n.X), D(n.Y), D(n.Z))));
        Section("mesh_elements", s.MeshElements.Select(e => J(e.ElemTag, e.ElemType, e.NodeIdsJson, e.SourceMemberTag,
            e.SectionTag, I(e.StiffnessNum), e.MaterialTag, D(e.ThicknessM), e.ReinforcementTypeIds, D(e.LocalAxisAngleDeg),
            D(e.BeamRotationDeg), I(e.ReleaseI), I(e.ReleaseJ), D(e.FoundationC1), I(e.CrossSectionId))));
        Section("rigid_bodies", s.RigidBodies.Select(b => J(b.MasterNodeTag, I(b.Mask), b.Kind, b.SourceElemTag)));
        Section("load_cases", s.LoadCases.Select(c => J(Lc(c.Id), c.Tag, c.LoadType, c.Sp20Type, c.Sp20Group,
            D(c.GammaFUnfav), D(c.GammaFFav), D(c.Psi1), D(c.Psi2), D(c.LongTermShare), D(c.SelfWeightFactor))));
        var rankMap = lcRank.ToDictionary(p => p.Key, p => -1 - p.Value);
        Section("definitions", s.LoadDefinitions.Select(d => J(d.Tag, d.Description, d.SourceKind, d.CombinationType,
            FragmentLoadCopy.RemapLoadCases(d, rankMap).ExpressionJson)));
        Section("member_loads", s.MemberLoads.Select(l => J(Lc(l.LoadCaseId), memberTag.GetValueOrDefault(l.MemberId, "?"),
            l.CoordinateSystem, l.DistributionType, D(l.StartOffsetM), D(l.EndOffsetM), D(l.QxStart), D(l.QyStart),
            D(l.QzStart), D(l.QxEnd), D(l.QyEnd), D(l.QzEnd), D(l.Mx), D(l.My), D(l.Mz))));
        Section("node_loads", s.NodeLoads.Select(l => J(Lc(l.LoadCaseId), nodeTag.GetValueOrDefault(l.NodeId, "?"),
            D(l.Fx), D(l.Fy), D(l.Fz), D(l.Mx), D(l.My), D(l.Mz))));
        Section("kinematic_loads", s.KinematicLoads.Select(l => J(Lc(l.LoadCaseId), nodeTag.GetValueOrDefault(l.NodeId, "?"),
            I(l.Dof), D(l.Value))));
        Section("element_loads", s.ElementLoads.Select(l => J(Lc(l.LoadCaseId), l.TargetKind, l.TargetTagsJson,
            l.GroupId is int g ? groupTag.GetValueOrDefault(g, "?") : "", l.LoadKind, l.CoordinateSystem, l.Axis, l.ValuesJson)));
        Section("mesh_node_loads", s.MeshNodeLoads.Select(l => J(Lc(l.LoadCaseId), l.MeshNodeTag,
            D(l.Fx), D(l.Fy), D(l.Fz), D(l.Mx), D(l.My), D(l.Mz))));
        Section("springs", s.Springs.Select(p => J(p.TargetKind, p.NodeTag,
            D(p.Kx), D(p.Ky), D(p.Kz), D(p.Kux), D(p.Kuy), D(p.Kuz), p.Origin, p.SourceElemTag)));
        Section("mesh_node_supports", s.MeshNodeSupports.Select(p => J(p.NodeTag, I(p.Mask), p.Origin)));
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    static string J(params string?[] parts) => string.Join("|", parts.Select(p => p ?? "∅"));
    static string D(double v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string D(double? v) => v is double x ? D(x) : "∅";
    static string I(int? v) => v?.ToString(CultureInfo.InvariantCulture) ?? "∅";
}
