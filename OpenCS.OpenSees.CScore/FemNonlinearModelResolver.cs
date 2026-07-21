using System.Text.Json;
using CScore;
using CScore.Fem;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.CScore;

/// <summary>Собирает FemNonlinearModel из снимка сетки, свежего конструктивного слоя и проектных
/// fiber-сечений (CrossSectionToOpenSeesAdapter).</summary>
public sealed class FemNonlinearModelResolver
{
    public FemNonlinearResolveResult Resolve(
        IReadOnlyList<FemMeshNode> meshNodes,
        IReadOnlyList<FemElement> meshElements,
        IReadOnlyList<FemNode> sourceNodes,
        IReadOnlyList<FemMember> sourceMembers,
        IReadOnlyList<FemNodeLoad> resolvedLoads,
        IReadOnlyDictionary<int, CrossSection> sections,
        IReadOnlyDictionary<int, Material> materials,
        IReadOnlyList<Diagramm>? customDiagramPool,
        CalcType calcType,
        FemNonlinearAnalysisOptions options)
    {
        var errors = new List<string>();

        if (meshElements.Count == 0)
            errors.Add("У схемы нет снимка сетки — сначала выполните дискретизацию.");

        var srcNodeByTag = new Dictionary<string, FemNode>(StringComparer.Ordinal);
        foreach (var n in sourceNodes) if (n.NodeTag is { Length: > 0 }) srcNodeByTag[n.NodeTag] = n;
        var srcNodeById = sourceNodes.ToDictionary(n => n.Id);
        var srcMemberByTag = new Dictionary<string, FemMember>(StringComparer.Ordinal);
        foreach (var m in sourceMembers) if (m.ElemTag is { Length: > 0 }) srcMemberByTag[m.ElemTag] = m;

        // Узлы + перенос закреплений (идентично FemLinearModelResolver)
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

        // Fiber-сечения: дедупликация по (CrossSectionId, GjManualValue) — GJ встроен в section Fiber.
        var sectionsByKey = new Dictionary<(int CrossSectionId, double Gj), (int Tag, OpenSeesSectionModel Model)>();
        int nextSectionTag = 1;
        int nextMaterialTag = 1;

        (int Tag, OpenSeesSectionModel Model)? BuildSection(int csId, double gj, string memberTag)
        {
            var key = (csId, gj);
            if (sectionsByKey.TryGetValue(key, out var cached)) return cached;

            if (!sections.TryGetValue(csId, out var section))
            {
                errors.Add($"Стержень {memberTag}: сечение #{csId} не готово (нет фибр).");
                return null;
            }
            OpenSeesSectionModel model;
            try
            {
                model = CrossSectionToOpenSeesAdapter.Build(section, calcType, materials, customDiagramPool,
                    new CrossSectionToOpenSeesAdapter.Options { GJ = gj, FirstMaterialTag = nextMaterialTag });
            }
            catch (CScoreMappingException ex)
            {
                errors.Add($"Стержень {memberTag}: {ex.Message}");
                return null;
            }
            nextMaterialTag += model.Materials.Count;
            var entry = (Tag: nextSectionTag++, Model: model);
            sectionsByKey[key] = entry;
            return entry;
        }

        // Элементы
        var elements = new List<FemNonlinearElement>();
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
            if (member.GjStrategy != "manual")
            {
                errors.Add($"Стержень {member.ElemTag}: стратегия GJ «{member.GjStrategy}» отложена (срез 5 поддерживает только manual).");
                continue;
            }
            if (member.GjManualValue is not { } gj || gj <= 0)
            {
                errors.Add($"Стержень {member.ElemTag}: не задано ручное значение GJ (>0).");
                continue;
            }

            var built = BuildSection(csId, gj, member.ElemTag);
            if (built is not { } sectionEntry) continue;

            (double, double, double) vecxz;
            try { vecxz = FemLocalAxis.Vecxz(ni, nj, member.RotationDeg); }
            catch (InvalidOperationException ex) { errors.Add($"Элемент {el.ElemTag}: {ex.Message}"); continue; }

            if (!int.TryParse(el.ElemTag, out int etag))
            {
                errors.Add($"Элемент с нечисловым тегом «{el.ElemTag}».");
                continue;
            }
            elements.Add(new FemNonlinearElement(etag, ni.Tag, nj.Tag, sectionEntry.Tag, options.IntegrationPoints, vecxz));
        }

        // Нагрузки (идентично FemLinearModelResolver)
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

        if (errors.Count > 0)
            return new FemNonlinearResolveResult(null, errors);

        var model = new FemNonlinearModel
        {
            Nodes = nodes,
            Sections = sectionsByKey.Values.ToDictionary(v => v.Tag, v => v.Model),
            Elements = elements,
            Loads = loads,
            LoadSteps = options.LoadSteps,
            Tolerance = options.Tolerance,
            MaxIterations = options.MaxIterations,
            GeomTransfKind = options.GeomTransfKind,
            ConvergenceTest = options.ConvergenceTest
        };
        try { model.Validate(); }
        catch (InvalidOperationException ex) { return new FemNonlinearResolveResult(null, [ex.Message]); }
        return new FemNonlinearResolveResult(model, []);
    }
}
