using System.Text.Json;
using CScore;
using CScore.Fem;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.CScore;

/// <summary>Собирает FemLinearModel из снимка сетки и свежего конструктивного слоя.</summary>
public sealed class FemLinearModelResolver
{
    /// <summary>Разрешает свойства сетки в типизированную линейную модель.</summary>
    public FemLinearResolveResult Resolve(
        IReadOnlyList<FemMeshNode> meshNodes,
        IReadOnlyList<FemElement> meshElements,
        IReadOnlyList<FemNode> sourceNodes,
        IReadOnlyList<FemMember> sourceMembers,
        IReadOnlyList<FemNodeLoad> resolvedLoads,
        IReadOnlyDictionary<int, GeoProps> sectionProps,
        IReadOnlyList<FemMemberLoad>? resolvedMemberLoads = null,
        IReadOnlyList<FemKinematicLoad>? resolvedKinematicLoads = null)
    {
        var errors = new List<string>();

        if (meshElements.Count == 0)
            errors.Add("У схемы нет снимка сетки — сначала выполните дискретизацию.");

        var srcNodeByTag = new Dictionary<string, FemNode>(StringComparer.Ordinal);
        foreach (var n in sourceNodes) if (n.NodeTag is { Length: > 0 }) srcNodeByTag[n.NodeTag] = n;
        var srcNodeById = sourceNodes.ToDictionary(n => n.Id);
        var srcMemberByTag = new Dictionary<string, FemMember>(StringComparer.Ordinal);
        foreach (var m in sourceMembers) if (m.ElemTag is { Length: > 0 }) srcMemberByTag[m.ElemTag] = m;

        // Узлы + перенос закреплений
        var nodes = new List<FemLinearNode>();
        var meshNodeBySourceTag = new Dictionary<string, FemLinearNode>(StringComparer.Ordinal);
        foreach (var mnode in meshNodes)
        {
            if (!int.TryParse(mnode.NodeTag, out int tag))
            {
                errors.Add($"Узел сетки с нечисловым тегом «{mnode.NodeTag}».");
                continue;
            }
            var fixedMask = new bool[6];
            if (mnode.SourceNodeTag is { Length: > 0 } srcTag &&
                srcNodeByTag.TryGetValue(srcTag, out var srcNode))
                for (int bit = 0; bit < 6; bit++)
                    fixedMask[bit] = (srcNode.DofMask & (1 << bit)) != 0;

            var ln = new FemLinearNode(tag, mnode.X, mnode.Y, mnode.Z, fixedMask);
            nodes.Add(ln);
            if (mnode.SourceNodeTag is { Length: > 0 } s) meshNodeBySourceTag[s] = ln;
        }
        var nodeByTag = nodes.ToDictionary(n => n.Tag);

        // Элементы
        var elements = new List<FemLinearElement>();
        foreach (var el in meshElements)
        {
            int[] ends = JsonSerializer.Deserialize<int[]>(el.NodeIdsJson) ?? [];
            if (ends.Length != 2 || !nodeByTag.TryGetValue(ends[0], out var ni) || !nodeByTag.TryGetValue(ends[1], out var nj))
            {
                errors.Add($"Элемент {el.ElemTag}: некорректная топология.");
                continue;
            }
            if (el.SourceMemberTag is not { Length: > 0 } memTag || !srcMemberByTag.TryGetValue(memTag, out var member))
            {
                errors.Add($"Элемент {el.ElemTag}: не найден конструктивный стержень.");
                continue;
            }
            if (member.CrossSectionId is not { } csId)
            {
                errors.Add($"Стержень {member.ElemTag}: не назначено сечение.");
                continue;
            }
            if (!sectionProps.TryGetValue(csId, out var gp))
            {
                errors.Add($"Стержень {member.ElemTag}: сечение #{csId} не готово (нет геометрии).");
                continue;
            }
            FemSectionStiffness st;
            try { st = FemSectionStiffness.FromGeoProps(gp); }
            catch (InvalidOperationException ex) { errors.Add($"Стержень {member.ElemTag}: {ex.Message}"); continue; }

            double g, j;
            if (member.GjStrategy == "manual")
            {
                if (member.GjManualValue is not { } gj || gj <= 0)
                {
                    errors.Add($"Стержень {member.ElemTag}: не задано ручное значение GJ (>0).");
                    continue;
                }
                g = gj; j = 1.0;
            }
            else
            {
                errors.Add($"Стержень {member.ElemTag}: стратегия GJ «{member.GjStrategy}» отложена (срез 4 поддерживает только manual).");
                continue;
            }

            (double, double, double) vecxz;
            try { vecxz = FemLocalAxis.Vecxz(ni, nj, member.RotationDeg); }
            catch (InvalidOperationException ex) { errors.Add($"Элемент {el.ElemTag}: {ex.Message}"); continue; }

            if (!int.TryParse(el.ElemTag, out int etag))
            {
                errors.Add($"Элемент с нечисловым тегом «{el.ElemTag}».");
                continue;
            }
            elements.Add(new FemLinearElement(etag, ni.Tag, nj.Tag, st.A, st.E, g, j, st.Iy, st.Iz, vecxz));
        }

        // Нагрузки
        var loads = new List<FemLinearNodalLoad>();
        foreach (var l in resolvedLoads)
        {
            if (!srcNodeById.TryGetValue(l.NodeId, out var srcNode))
            {
                errors.Add($"Нагрузка ссылается на неизвестный конструктивный узел {l.NodeId}.");
                continue;
            }
            if (srcNode.NodeTag is not { Length: > 0 } srcTag || !meshNodeBySourceTag.TryGetValue(srcTag, out var meshNode))
            {
                errors.Add($"Нагруженный узел {srcNode.NodeTag} не имеет совпадающего узла сетки — приложить нагрузку невозможно.");
                continue;
            }
            loads.Add(new FemLinearNodalLoad(meshNode.Tag, l.Fx, l.Fy, l.Fz, l.Mx, l.My, l.Mz));
        }

        var distributed = new FemDistributedLoadResolver().Resolve(
            meshNodes, meshElements, sourceNodes, sourceMembers, resolvedMemberLoads ?? []);
        errors.AddRange(distributed.Errors);

        var points = new FemPointLoadResolver().Resolve(
            meshNodes, meshElements, sourceNodes, sourceMembers, resolvedMemberLoads ?? []);
        errors.AddRange(points.Errors);
        loads.AddRange(points.NodalLoads);

        var kinematicLoads = new List<FemLinearKinematicLoad>();
        foreach (var load in resolvedKinematicLoads ?? [])
        {
            if (!srcNodeById.TryGetValue(load.NodeId, out var srcNode))
            {
                errors.Add($"Кинематическая нагрузка ссылается на неизвестный конструктивный узел {load.NodeId}.");
                continue;
            }
            if (srcNode.NodeTag is not { Length: > 0 } srcTag || !meshNodeBySourceTag.TryGetValue(srcTag, out var meshNode))
            {
                errors.Add($"Кинематически нагруженный узел {srcNode.NodeTag} не имеет совпадающего узла сетки.");
                continue;
            }
            kinematicLoads.Add(new FemLinearKinematicLoad(meshNode.Tag, load.Dof, load.Value));
        }

        if (errors.Count > 0)
            return new FemLinearResolveResult(null, errors);

        var model = new FemLinearModel
        {
            Nodes = nodes, Elements = elements, Loads = loads, KinematicLoads = kinematicLoads, DistributedLoads = distributed.Loads,
            PointLoads = points.ElementLoads
        };
        try { model.Validate(); }
        catch (InvalidOperationException ex) { return new FemLinearResolveResult(null, [ex.Message]); }
        return new FemLinearResolveResult(model, []);
    }
}
