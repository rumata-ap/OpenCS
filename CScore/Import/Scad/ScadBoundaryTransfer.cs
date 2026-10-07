using System.Globalization;
using CScore.Fem;

namespace CScore.Import;

/// <summary>Итог переноса ГУ SCAD: объекты сеточного уровня происхождения «import:scad» и журнал.</summary>
/// <param name="Supports">Закрепления узлов сетки.</param>
/// <param name="Springs">Пружины на узлах сетки (одна запись на узел, КЭ 51 одного узла суммируются).</param>
/// <param name="RigidBodies">Жёсткие тела.</param>
/// <param name="ElementProps">ГУ КЭ по тегу: освобождения концов стержней и C1 пластин (только КЭ, где они есть).</param>
/// <param name="Report">Журнал: первая строка — сводка, далее — что не перенесено и почему.</param>
public sealed record ScadBoundaryTransferResult(
    IReadOnlyList<FemMeshNodeSupport> Supports,
    IReadOnlyList<FemSpring> Springs,
    IReadOnlyList<FemRigidBody> RigidBodies,
    IReadOnlyDictionary<string, FemElementBoundaryProps> ElementProps,
    IReadOnlyList<string> Report);

/// <summary>
/// Перенос граничных условий из вложения SCAD (<see cref="ScadAnalysisModel"/>) в сеточный уровень схемы:
/// закрепления (ApiGetBound) → <see cref="FemMeshNodeSupport"/>, КЭ 51 → <see cref="FemSpring"/>, КЭ 100 →
/// <see cref="FemRigidBody"/>, шарниры стержней → <see cref="FemElement.ReleaseI"/>/<see cref="FemElement.ReleaseJ"/>,
/// C1 упругого основания пластин (ApiGetBed) → <see cref="FemElement.FoundationC1"/>.
/// Результат целиком заменяет ГУ своего происхождения (запись — <c>SaveFemBoundary</c>), ручные не трогаются.
/// Объекты на узлах/КЭ, которых нет в сетке, пропускаются с записью в журнал.
/// </summary>
public static class ScadBoundaryTransfer
{
    public static readonly string Origin = ScadLoadTransfer.Origin;

    /// <param name="model">Вложение SCAD схемы.</param>
    /// <param name="meshNodeTags">Теги узлов сетки схемы.</param>
    /// <param name="elementTypes">Тег КЭ сетки → тип («beam» | «shell»).</param>
    public static ScadBoundaryTransferResult Transfer(ScadAnalysisModel model, IReadOnlySet<string> meshNodeTags,
        IReadOnlyDictionary<string, string> elementTypes)
    {
        var report = new List<string>();
        static string T(int id) => id.ToString(CultureInfo.InvariantCulture);

        var supports = new List<FemMeshNodeSupport>();
        int missingSupports = 0;
        foreach (var (node, mask) in model.Bounds.OrderBy(b => b.Key))
        {
            if ((mask & FemBoundaryDofs.All) == 0) continue;
            if (!meshNodeTags.Contains(T(node))) { missingSupports++; continue; }
            supports.Add(new FemMeshNodeSupport { NodeTag = T(node), Mask = mask & FemBoundaryDofs.All, Origin = Origin });
        }
        if (missingSupports > 0) report.Add($"Закрепления: узлов нет в сетке — {missingSupports}, пропущены.");

        var springs = new List<FemSpring>();
        int missingSprings = 0, mergedSprings = 0;
        foreach (var byNode in model.Springs.GroupBy(s => s.Node).OrderBy(g => g.Key))
        {
            if (!meshNodeTags.Contains(T(byNode.Key))) { missingSprings += byNode.Count(); continue; }
            var k = new double[6];
            foreach (var s in byNode)
                for (int i = 0; i < 6 && i < s.K.Length; i++) k[i] += s.K[i];
            if (k.All(v => v == 0)) continue;
            if (byNode.Count() > 1) mergedSprings += byNode.Count();
            var spring = new FemSpring
            {
                TargetKind = FemSpringTargetKinds.MeshNode, NodeTag = T(byNode.Key), Origin = Origin,
                SourceElemTag = string.Join(",", byNode.Select(s => T(s.ElemId))),
            };
            spring.SetStiffnesses(k);
            springs.Add(spring);
        }
        if (missingSprings > 0) report.Add($"Связи конечной жёсткости (КЭ 51): узлов нет в сетке — КЭ {missingSprings}, пропущены.");
        if (mergedSprings > 0) report.Add($"Связи конечной жёсткости (КЭ 51): {mergedSprings} КЭ на общих узлах сложены в одну пружину узла.");

        var bodies = new List<FemRigidBody>();
        int missingBodies = 0, missingSlaves = 0;
        foreach (var b in model.RigidBodies.OrderBy(b => b.ElemId))
        {
            var slaves = b.SlaveNodes.Select(T).Where(t => t != T(b.MasterNode)).ToList();
            int present = slaves.Count(meshNodeTags.Contains);
            if (!meshNodeTags.Contains(T(b.MasterNode)) || present == 0 || (b.Mask & FemBoundaryDofs.All) == 0)
            {
                missingBodies++;
                continue;
            }
            missingSlaves += slaves.Count - present;
            var body = new FemRigidBody
            {
                MasterNodeTag = T(b.MasterNode), Mask = b.Mask & FemBoundaryDofs.All, Origin = Origin,
                SourceElemTag = T(b.ElemId),
            };
            body.SetSlaveNodeTags(slaves.Where(meshNodeTags.Contains));
            bodies.Add(body);
        }
        if (missingBodies > 0) report.Add($"Жёсткие тела (КЭ 100): {missingBodies} без ведущего или ведомых узлов в сетке, пропущены.");
        if (missingSlaves > 0) report.Add($"Жёсткие тела (КЭ 100): ведомых узлов нет в сетке — {missingSlaves}, исключены из тел.");

        var props = new Dictionary<string, FemElementBoundaryProps>(StringComparer.Ordinal);
        int releasedEnds = 0, missingJoints = 0;
        foreach (var j in model.Joints.OrderBy(j => j.ElemId))
        {
            string tag = T(j.ElemId);
            if (elementTypes.GetValueOrDefault(tag) != "beam") { missingJoints++; continue; }
            int? ri = (j.MaskI & FemBoundaryDofs.All) is var mi and not 0 ? mi : null;
            int? rj = (j.MaskJ & FemBoundaryDofs.All) is var mj and not 0 ? mj : null;
            if (ri == null && rj == null) continue;
            releasedEnds += (ri != null ? 1 : 0) + (rj != null ? 1 : 0);
            props[tag] = new FemElementBoundaryProps(ri, rj, null);
        }
        if (missingJoints > 0) report.Add($"Шарниры: {missingJoints} КЭ нет в сетке среди стержней, пропущены.");

        int jointed = props.Count;

        // Упругое основание: C1 пластин (Винклер); C2 и прочие коэффициенты, основание стержней — в журнал.
        var c1 = new Dictionary<string, double>(StringComparer.Ordinal);
        int beyondC1 = 0, beamBeds = 0, missingBeds = 0, repeatedBeds = 0;
        foreach (var bed in model.Beds)
            foreach (int id in bed.Elements)
            {
                string tag = T(id);
                switch (elementTypes.GetValueOrDefault(tag))
                {
                    case "shell":
                        if (!(bed.C1 > 0)) continue;
                        if (c1.TryGetValue(tag, out double prev)) { repeatedBeds++; c1[tag] = prev + bed.C1; }
                        else c1[tag] = bed.C1;
                        if (bed.HasBeyondC1) beyondC1++;
                        break;
                    case "beam": beamBeds++; break;
                    default: missingBeds++; break;
                }
            }
        foreach (var (tag, value) in c1)
            props[tag] = props.TryGetValue(tag, out var p) ? p with { FoundationC1 = value }
                : new FemElementBoundaryProps(null, null, value);
        if (beyondC1 > 0)
            report.Add($"Упругое основание: у {beyondC1} пластин заданы C2 или другие коэффициенты кроме C1 — не учтены " +
                "(модель Винклера, только C1).");
        if (repeatedBeds > 0) report.Add($"Упругое основание: {repeatedBeds} пластин входят в несколько групп — C1 сложены.");
        if (beamBeds > 0) report.Add($"Не перенесено: упругое основание стержней, КЭ — {beamBeds}.");
        if (missingBeds > 0) report.Add($"Упругое основание: {missingBeds} КЭ нет в сетке среди пластин и стержней, пропущены.");

        foreach (var (kind, count) in model.NotTransferred.OrderBy(x => x.Key, StringComparer.Ordinal))
            if (count > 0) report.Add($"Не перенесено: {DescribeNotTransferred(kind)} — {count}.");
        if (!model.HasBoundaryV2)
            report.Add("Вложение SCAD прочитано до поддержки пружин и шарниров: перенесены только закрепления и жёсткие тела; " +
                "дочитайте граничные условия из .SPR.");
        else if (!model.HasBeds)
            report.Add("Вложение SCAD прочитано до поддержки упругого основания: C1 пластин не перенесён; " +
                "дочитайте граничные условия из .SPR.");

        report.Insert(0, $"Перенесено из SCAD граничных условий: закреплений {supports.Count}, пружин {springs.Count}, " +
            $"жёстких тел {bodies.Count}, стержней с шарнирами {jointed} " +
            $"(концов {releasedEnds}), пластин на упругом основании {c1.Count}.");
        return new ScadBoundaryTransferResult(supports, springs, bodies, props, report);
    }

    /// <summary>Подпись вида непереносимого для журнала.</summary>
    public static string DescribeNotTransferred(string kind) => kind switch
    {
        ScadNotTransferredKinds.Fe55 => "упругие связи двух узлов (КЭ 55), КЭ",
        ScadNotTransferredKinds.BoundUnite => "объединения перемещений, групп",
        ScadNotTransferredKinds.ElasticJoint => "упругие шарниры, концов стержней",
        ScadNotTransferredKinds.Insert => "жёсткие вставки, КЭ",
        ScadNotTransferredKinds.Bed => "упругое основание, КЭ",
        ScadNotTransferredKinds.SpringUnparsed => "КЭ 51 с нераспознанной жёсткостью",
        _ => kind,
    };
}
