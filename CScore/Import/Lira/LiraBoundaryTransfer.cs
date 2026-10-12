using System.Globalization;
using CScore.Fem;

namespace CScore.Import;

/// <summary>
/// Перенос граничных условий из текстового файла ЛИРЫ (<see cref="LiraTextSchema"/>) в сеточный уровень схемы:
/// связи документа 5 → <see cref="FemMeshNodeSupport"/>, одноузловые КЭ 51, 56 и 57 → <see cref="FemSpring"/>,
/// шарниры документа 2 → <see cref="FemElement.ReleaseI"/>/<see cref="FemElement.ReleaseJ"/>, АЖТ документа 25 (код −1)
/// и объединения перемещений документа 5 → <see cref="FemRigidBody"/> (виды <see cref="FemRigidBodyKinds.Rigid"/> и
/// <see cref="FemRigidBodyKinds.Coupled"/>). Узлы и КЭ сопоставляются по номерам: импорт ЛИРЫ даёт тегам узлов и КЭ
/// сетки номера схемы. Результат целиком заменяет ГУ происхождения «import:lira» (запись — <c>SaveFemBoundary</c>),
/// ручные не трогаются; C1 пластин из таблицы 5 API, если передан, сохраняется (запись заменяет ГУ всех импортных КЭ).
/// Упругие шарниры, КЭ 55 и физически нелинейные связи, АЖТ с кодом не −1, вставки документа 14 — в сводку
/// «не перенесено».
/// </summary>
public static class LiraBoundaryTransfer
{
    public static readonly string Origin = LiraSchemaConverter.BoundaryOrigin;

    /// <summary>КЭ 51 — связь конечной жёсткости: жёсткость — пары «DOF величина» (<c>3 2000</c>).</summary>
    public const int SpringFe51 = 51;

    /// <summary>
    /// КЭ 56 — одноузловая упругая связь; жёсткость читается как у КЭ 57. Поля по описанию не подтверждены, но во всех
    /// образцах (ЛИРА 2022–2025: ЗАГС, MIL-003, Turkestan, FRAME) форма одна — <c>Rx Ry 0 0 0 0</c>: горизонтальные
    /// пружины узлов фундаментной плиты (вертикаль — C1), как у КЭ 57 «Мирной».
    /// </summary>
    public const int SpringFe56 = 56;

    /// <summary>КЭ 57 — одноузловая упругая связь: жёсткость <c>Rx Ry Rz Rux Ruy Ruz</c> в общих осях.</summary>
    public const int SpringFe57 = 57;

    /// <param name="schema">Разобранный текстовый файл схемы.</param>
    /// <param name="meshNodeTags">Теги узлов сетки схемы (номера узлов ЛИРЫ).</param>
    /// <param name="elementTypes">Тег КЭ сетки (номер КЭ ЛИРЫ) → тип («beam» | «shell»).</param>
    /// <param name="tonToKn">Сколько кН в тонне-силе (настройка импорта): жёсткости связей — в единицах документа 0.</param>
    /// <param name="foundationC1">C1 пластин по тегу КЭ, Н/м³, прочитанный импортом из API; null — не переносится.</param>
    public static FemBoundaryTransferResult Transfer(LiraTextSchema schema, IReadOnlySet<string> meshNodeTags,
        IReadOnlyDictionary<string, string> elementTypes, double tonToKn,
        IReadOnlyDictionary<string, double>? foundationC1 = null)
    {
        var report = new List<string>();
        static string T(int id) => id.ToString(CultureInfo.InvariantCulture);
        static int Bits(IEnumerable<int> dofs) => dofs.Where(d => d is >= 1 and <= 6).Aggregate(0, (m, d) => m | 1 << (d - 1));

        // ── Связи документа 5 (записи одного узла объединяются по маске) ──
        var supports = new List<FemMeshNodeSupport>();
        int missingSupports = 0;
        foreach (var byNode in schema.Supports.GroupBy(s => s.Node).OrderBy(g => g.Key))
        {
            int mask = Bits(byNode.SelectMany(s => s.Dofs));
            if (mask == 0) continue;
            if (!meshNodeTags.Contains(T(byNode.Key))) { missingSupports++; continue; }
            supports.Add(new FemMeshNodeSupport { NodeTag = T(byNode.Key), Mask = mask, Origin = Origin });
        }
        if (missingSupports > 0) report.Add($"Связи: узлов нет в сетке — {missingSupports}, пропущены.");

        // ── КЭ 51, 56 и 57 → пружины узлов (КЭ одного узла складываются) ──
        var units = schema.Units;
        double forceN = units.ForceUnitN(tonToKn), lengthM = units.LengthUnitM;
        double linear = forceN / lengthM, angular = forceN * lengthM;
        var stiffnesses = new Dictionary<int, LiraTextStiffness>();
        foreach (var s in schema.Stiffnesses) stiffnesses.TryAdd(s.Number, s);

        var springElems = new List<(LiraTextElement Element, double[] K)>();
        int unparsedSprings = 0;
        foreach (var e in schema.Elements.Where(e => e.Type is SpringFe51 or SpringFe56 or SpringFe57))
        {
            var k = e.Nodes.Length == 1 && stiffnesses.TryGetValue(e.Stiffness, out var st)
                ? SpringStiffness(e.Type, st.NumericValues) : null;
            if (k == null) { unparsedSprings++; continue; }
            for (int i = 0; i < 6; i++) k[i] *= i < 3 ? linear : angular;
            springElems.Add((e, k));
        }
        var springs = new List<FemSpring>();
        int missingSprings = 0, mergedSprings = 0, zeroSprings = 0, transferredSpringElems = 0;
        foreach (var byNode in springElems.GroupBy(s => s.Element.Nodes[0]).OrderBy(g => g.Key))
        {
            if (!meshNodeTags.Contains(T(byNode.Key))) { missingSprings += byNode.Count(); continue; }
            var k = new double[6];
            foreach (var (_, ke) in byNode)
                for (int i = 0; i < 6; i++) k[i] += ke[i];
            if (k.All(v => v == 0)) { zeroSprings += byNode.Count(); continue; }
            if (byNode.Count() > 1) mergedSprings += byNode.Count();
            transferredSpringElems += byNode.Count();
            var spring = new FemSpring
            {
                TargetKind = FemSpringTargetKinds.MeshNode, NodeTag = T(byNode.Key), Origin = Origin,
                SourceElemTag = string.Join(",", byNode.Select(s => T(s.Element.Number))),
            };
            spring.SetStiffnesses(k);
            springs.Add(spring);
        }
        if (missingSprings > 0) report.Add($"Упругие связи (КЭ 51, 56, 57): узлов нет в сетке — КЭ {missingSprings}, пропущены.");
        if (unparsedSprings > 0)
            report.Add($"Упругие связи (КЭ 51, 56, 57): у {unparsedSprings} КЭ жёсткость не распознана или узел не один — пропущены.");
        if (mergedSprings > 0) report.Add($"Упругие связи (КЭ 51, 56, 57): {mergedSprings} КЭ на общих узлах сложены в одну пружину узла.");
        if (zeroSprings > 0) report.Add($"Упругие связи (КЭ 51, 56, 57): у {zeroSprings} КЭ все жёсткости нулевые — пропущены.");

        // ── АЖТ документа 25: номер тела — порядковый номер записи ──
        var bodies = new List<FemRigidBody>();
        int missingBodies = 0, missingSlaves = 0, otherCodeBodies = 0;
        for (int i = 0; i < schema.RigidBodies.Count; i++)
        {
            var b = schema.RigidBodies[i];
            if (b.Code != -1) { otherCodeBodies++; continue; }
            var slaves = b.Slaves.Select(T).Where(t => t != T(b.Master)).ToList();
            int present = slaves.Count(meshNodeTags.Contains);
            if (!meshNodeTags.Contains(T(b.Master)) || present == 0) { missingBodies++; continue; }
            missingSlaves += slaves.Count - present;
            var body = new FemRigidBody
            {
                MasterNodeTag = T(b.Master), Mask = FemBoundaryDofs.All, Origin = Origin, SourceElemTag = T(i + 1),
            };
            body.SetSlaveNodeTags(slaves.Where(meshNodeTags.Contains));
            bodies.Add(body);
        }
        if (missingBodies > 0) report.Add($"АЖТ: {missingBodies} без ведущего или ведомых узлов в сетке, пропущены.");
        if (missingSlaves > 0) report.Add($"АЖТ: ведомых узлов нет в сетке — {missingSlaves}, исключены из тел.");
        if (otherCodeBodies > 0)
            report.Add($"Не перенесено: АЖТ с кодом не −1 (−2 или без кода, смысл не установлен) — {otherCodeBodies}.");

        // ── Объединения перемещений документа 5: ведущий — первый узел группы в сетке, номер — порядковый номер группы ──
        var couplings = new List<FemRigidBody>();
        int missingCouplings = 0, missingCoupled = 0, noDofCouplings = 0, couplingNodes = 0;
        for (int i = 0; i < schema.Couplings.Count; i++)
        {
            var c = schema.Couplings[i];
            int mask = Bits(c.Dofs);
            if (mask == 0) { noDofCouplings++; continue; }
            var group = c.Nodes.Prepend(c.Head).Distinct().Select(T).ToList();
            var present = group.Where(meshNodeTags.Contains).ToList();
            missingCoupled += group.Count - present.Count;
            if (present.Count < 2) { missingCouplings++; continue; }
            var coupling = new FemRigidBody
            {
                MasterNodeTag = present[0], Mask = mask, Kind = FemRigidBodyKinds.Coupled, Origin = Origin,
                SourceElemTag = T(i + 1),
            };
            coupling.SetSlaveNodeTags(present.Skip(1));
            couplings.Add(coupling);
            couplingNodes += present.Count;
        }
        if (missingCoupled > 0) report.Add($"Объединения перемещений: узлов нет в сетке — {missingCoupled}, исключены из групп.");
        if (missingCouplings > 0)
            report.Add($"Объединения перемещений: {missingCouplings} групп, где в сетке меньше двух узлов, пропущены.");
        if (noDofCouplings > 0) report.Add($"Объединения перемещений: {noDofCouplings} групп без направлений 1–6, пропущены.");

        // ── Шарниры документа 2: DOF 1–6 местных осей ЛИРЫ = биты N Qy Qz T My Mz (местная Y = Y1) ──
        var props = new Dictionary<string, FemElementBoundaryProps>(StringComparer.Ordinal);
        int releasedEnds = 0, missingJoints = 0, badEnds = 0;
        var elasticEnds = new HashSet<(int, int)>();
        foreach (var byElem in schema.Hinges.GroupBy(h => h.Element).OrderBy(g => g.Key))
        {
            string tag = T(byElem.Key);
            if (elementTypes.GetValueOrDefault(tag) != "beam") { missingJoints++; continue; }
            int maskI = 0, maskJ = 0;
            foreach (var h in byElem)
            {
                if (h.Value != 0) { elasticEnds.Add((h.Element, h.End)); continue; }
                switch (h.End)
                {
                    case 1: maskI |= Bits(h.Dofs); break;
                    case 2: maskJ |= Bits(h.Dofs); break;
                    default: badEnds++; break;
                }
            }
            int? ri = maskI != 0 ? maskI : null, rj = maskJ != 0 ? maskJ : null;
            if (ri == null && rj == null) continue;
            releasedEnds += (ri != null ? 1 : 0) + (rj != null ? 1 : 0);
            props[tag] = new FemElementBoundaryProps(ri, rj, null);
        }
        int jointed = props.Count;
        if (missingJoints > 0) report.Add($"Шарниры: {missingJoints} КЭ нет в сетке среди стержней, пропущены.");
        if (badEnds > 0) report.Add($"Шарниры: {badEnds} записей с концом не 1 и не 2, пропущены.");
        if (elasticEnds.Count > 0)
            report.Add($"Не перенесено: упругие шарниры (последнее поле записи не 0), концов стержней — {elasticEnds.Count}.");

        // ── C1 пластин из API: сохраняется, т. к. запись ГУ КЭ заменяет освобождения и C1 всех импортных КЭ ──
        int c1Count = 0;
        if (foundationC1 != null)
            foreach (var (tag, c1) in foundationC1)
            {
                if (!(c1 > 0) || elementTypes.GetValueOrDefault(tag) != "shell") continue;
                props[tag] = props.TryGetValue(tag, out var p) ? p with { FoundationC1 = c1 } : new FemElementBoundaryProps(null, null, c1);
                c1Count++;
            }

        // ── Не переносится ──
        var otherLinks = schema.Elements.Where(e => IsOtherLink(e.Type)).GroupBy(e => e.Type).OrderBy(g => g.Key)
            .Select(g => (Type: g.Key, Count: g.Count())).ToList();
        foreach (var (type, count) in otherLinks)
            report.Add($"Не перенесено: КЭ {type} ({DescribeLink(type)}) — {count}.");
        int inserts = schema.SkippedDocuments.Where(d => d.Number == 14).Sum(d => d.Records);
        if (inserts > 0) report.Add($"Не перенесено: жёсткие вставки (документ 14), записей — {inserts}.");

        var summary = new List<FemBoundaryTransferRow>
        {
            new("Связи", "узлы", supports.Count, missingSupports),
            new("Упругие связи (КЭ 51, 56, 57)", "КЭ", transferredSpringElems, missingSprings + unparsedSprings,
                Join(mergedSprings > 0 ? $"сложены на общих узлах: {mergedSprings}" : "",
                    unparsedSprings > 0 ? $"жёсткость не распознана: {unparsedSprings}" : "",
                    zeroSprings > 0 ? $"нулевая жёсткость: {zeroSprings}" : "")),
            new("Абсолютно жёсткие тела", "тела", bodies.Count, missingBodies + otherCodeBodies,
                Join(missingSlaves > 0 ? $"ведомых узлов нет в сетке: {missingSlaves}" : "",
                    otherCodeBodies > 0 ? $"код не −1: {otherCodeBodies}" : "")),
            new("Шарниры стержней", "КЭ", jointed, missingJoints, releasedEnds > 0 ? $"освобождённых концов: {releasedEnds}" : ""),
        };
        if (schema.Couplings.Count > 0)
            summary.Add(new("Объединения перемещений", "группы", couplings.Count, missingCouplings + noDofCouplings,
                Join($"узлов: {couplingNodes}", missingCoupled > 0 ? $"узлов нет в сетке: {missingCoupled}" : "")));
        if (foundationC1 != null) summary.Add(new("Упругое основание пластин (C1)", "КЭ", c1Count, 0, "из API ЛИРЫ"));
        void Optional(string kind, string unit, int count, string note = "") { if (count > 0) summary.Add(new(kind, unit, 0, count, note)); }
        Optional("Упругие шарниры", "концы", elasticEnds.Count);
        foreach (var (type, count) in otherLinks) Optional($"КЭ {type} ({DescribeLink(type)})", "КЭ", count);
        Optional("Жёсткие вставки (документ 14)", "записи", inserts);

        report.Insert(0, $"Перенесено из ЛИРЫ граничных условий: связей {supports.Count}, пружин {springs.Count}, " +
            $"жёстких тел {bodies.Count}, объединений перемещений {couplings.Count}, стержней с шарнирами {jointed} " +
            $"(концов {releasedEnds})" + (foundationC1 != null ? $", пластин на упругом основании {c1Count}." : "."));
        return new FemBoundaryTransferResult(supports, springs, [.. bodies, .. couplings], props, report, summary);
    }

    /// <summary>
    /// Жёсткости X Y Z UX UY UZ в единицах файла по численной жёсткости документа 3: КЭ 56 и 57 — первые шесть чисел
    /// (<c>Rx Ry Rz Rux Ruy Ruz</c>), КЭ 51 — пары «DOF 1–6, величина» (повтор DOF складывается); null — не распознано.
    /// </summary>
    public static double[]? SpringStiffness(int type, double[]? values)
    {
        if (values == null) return null;
        var k = new double[6];
        switch (type)
        {
            case SpringFe56:
            case SpringFe57:
                if (values.Length < 6) return null;
                Array.Copy(values, k, 6);
                return k;
            case SpringFe51:
                if (values.Length == 0 || values.Length % 2 != 0) return null;
                for (int i = 0; i < values.Length; i += 2)
                {
                    double dof = values[i];
                    if (dof != Math.Floor(dof) || dof is < 1 or > 6) return null;
                    k[(int)dof - 1] += values[i + 1];
                }
                return k;
            default:
                return null;
        }
    }

    /// <summary>Связи, которые не переносятся: КЭ 5x и физически нелинейные 25x, кроме 51, 56 и 57.</summary>
    static bool IsOtherLink(int type) => type is (>= 50 and <= 59) or (>= 250 and <= 259) && type is not (SpringFe51 or SpringFe56 or SpringFe57);

    static string DescribeLink(int type) => type switch
    {
        55 => "упругая связь двух узлов",
        >= 250 => "физически нелинейная связь",
        _ => "связь",
    };

    static string Join(params string[] parts) => string.Join("; ", parts.Where(p => p.Length > 0));
}
