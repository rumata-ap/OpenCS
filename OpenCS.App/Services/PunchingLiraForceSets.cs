using System.Globalization;
using CScore;
using CScore.Fem.Fragment.Masks;
using CScore.Fem.Loads;
using CScore.Import;
using CScore.Sp63Punching;
using CScore.Sp63Punching.Fem;
using OpenCS.Tasks;

namespace OpenCS.Services;

/// <summary>Данные API ЛИРЫ для наборов продавливания: усилия загружений, строки РСУ и РСН в сечениях стержней колонн.</summary>
/// <param name="DocumentName">Имя документа ЛИРЫ.</param>
/// <param name="Cases">Сечение → номер загружения → N, Mk, My, Mz, Qz, Qy.</param>
/// <param name="Rsu">Строки РСУ (ПС1) сечений.</param>
/// <param name="Rsn">РСН (ПС1) с усилиями в сечениях.</param>
/// <param name="LoadCaseNames">Имена загружений ЛИРЫ по номеру.</param>
public sealed record PunchingLiraInput(
    string DocumentName,
    IReadOnlyDictionary<LiraBarSection, IReadOnlyDictionary<int, IReadOnlyList<double>>> Cases,
    IReadOnlyList<LiraRsuRow> Rsu,
    IReadOnlyList<LiraRsnRow> Rsn,
    IReadOnlyDictionary<int, string> LoadCaseNames);

/// <summary>Набор и пакетная задача узла.</summary>
/// <param name="Joint">Узел.</param>
/// <param name="Set">Набор (новый — Id = 0; прежний — с обновлёнными строками).</param>
/// <param name="Task">Пакетная задача продавливания (новая — Id = 0; ForceSetId выставить после сохранения набора).</param>
/// <param name="LoadCases">Строк загружений.</param>
/// <param name="Rsn">Строк РСН.</param>
/// <param name="Rsu">Строк РСУ (различных сочетаний).</param>
public sealed record PunchingLiraJointSet(PunchingFemJoint Joint, ForceSet Set, CalcTask Task, int LoadCases, int Rsn, int Rsu);

/// <summary>Наборы узлов и предупреждения.</summary>
public sealed record PunchingLiraOutcome(List<PunchingLiraJointSet> Joints, List<string> Warnings);

/// <summary>
/// Наборы усилий продавливания из ЛИРЫ (подпроект В): на узел — набор «Продавливание, узел N» со строками загружений,
/// РСН и РСУ ЛИРЫ и пакетная задача продавливания с геометрией узла. Сочетания ЛИРЫ раскладываются на загружения
/// (<see cref="LiraCombinationRows"/>), усилия узла по сочетанию — расчёт OpenCS по колоннам из снимка результатов
/// (<see cref="PunchingFemService.Evaluate"/>): F — со знаком стороны (+ снизу), Mx, My — в осях узла. Сохранение —
/// у вызывающего.
/// </summary>
public static class PunchingLiraForceSets
{
    /// <summary><see cref="ForceSet.SourceType"/> наборов продавливания из ЛИРЫ.</summary>
    public const string SourceType = "lira_punching";

    /// <summary>
    /// Стержни колонн узлов для запроса к ЛИРЕ: номер КЭ ЛИРЫ (тег стержня импорта) и конец у плиты (true — первый узел
    /// КЭ). Узлы без стержней и стержни с нечисловым тегом — предупреждением.
    /// </summary>
    public static List<(int Element, bool AtStart)> Bars(PunchingFemSchemaData data, IEnumerable<PunchingFemJoint> joints, List<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(joints);
        var list = new List<(int, bool)>();
        foreach (var j in joints)
        {
            if (j.Bars.Count == 0)
            {
                warnings.Add($"Узел {j.NodeTag}: нет стержней колонн (стена или пилон из пластин) — набор из ЛИРЫ не строится.");
                continue;
            }
            foreach (var b in j.Bars)
            {
                if (!int.TryParse(b.ElemTag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int num)
                    || !data.ElementsByTag.TryGetValue(b.ElemTag, out var e)
                    || FemElementGeometry.Of(e, data.Model.Nodes) is not { IsBar: true } g)
                {
                    warnings.Add($"Узел {j.NodeTag}: стержень «{b.ElemTag}» не сопоставляется с КЭ ЛИРЫ — пропущен.");
                    continue;
                }
                var item = (num, g.NodeTags[0] == b.NodeTag);
                if (!list.Contains(item)) list.Add(item);
            }
        }
        return list;
    }

    /// <summary>
    /// Наборы и задачи узлов <paramref name="joints"/> по данным ЛИРЫ <paramref name="lira"/>. Прежний набор узла (тот же
    /// источник, схема и узел) обновляется, его пакетная задача — тоже.
    /// </summary>
    public static PunchingLiraOutcome Build(PunchingFemSchemaData data, PunchingFemSource source, IReadOnlyList<PunchingFemJoint> joints,
        PunchingLiraInput lira, PunchingFemCheckParams check, IReadOnlyList<ForceSet> existingSets, IReadOnlyList<CalcTask> existingTasks)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(lira);
        ArgumentNullException.ThrowIfNull(check);
        var warnings = new List<string>();
        var map = source.ProgramLoadCases
            ?? throw new InvalidOperationException("Наборы из ЛИРЫ строятся только по снимку результатов импорта.");

        // Раскладка — один раз на все узлы: у РСУ общий пул сочетаний для неопределимых строк, РСН — глобальны.
        var rsu = LiraCombinationRows.Rsu(lira.Rsu, lira.Cases);
        var rsn = LiraCombinationRows.Rsn(lira.Rsn, lira.Cases);
        foreach (var f in rsn.Where(f => !f.Fit.Ok))
            warnings.Add($"{f.Label}: не разложено на загружения ({f.Fit.Failure}) — строка пропущена.");
        PunchingFemForceMethod? method = check.Method switch
        {
            "contour" => PunchingFemForceMethod.Contour,
            "column" => PunchingFemForceMethod.Column,
            _ => null,
        };

        var result = new List<PunchingLiraJointSet>();
        var tags = existingSets.Select(s => s.Tag).ToHashSet(StringComparer.Ordinal);
        foreach (var joint in joints)
        {
            if (joint.Bars.Count == 0) continue;
            var elements = joint.Bars.Select(b => int.TryParse(b.ElemTag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : -1)
                .ToHashSet();
            var sections = lira.Cases.Keys.Where(s => elements.Contains(s.Element)).ToHashSet();
            if (sections.Count == 0)
            {
                warnings.Add($"Узел {joint.NodeTag}: ЛИРА не вернула усилий стержней колонн — набор не построен.");
                continue;
            }

            // Ключи: загружения, РСН, РСУ колонн узла (без повторов).
            var keys = new List<(FragmentMaskKey Key, string Label, string Kind)>();
            var lcNums = sections.SelectMany(s => lira.Cases[s].Keys).Distinct().Order().ToList();
            foreach (int lc in lcNums)
            {
                if (!map.TryGetValue(lc, out int id))
                {
                    warnings.Add($"Узел {joint.NodeTag}: загружения ЛИРЫ {lc} нет в снимке схемы — строка пропущена.");
                    continue;
                }
                string name = lira.LoadCaseNames.GetValueOrDefault(lc, "");
                keys.Add((FragmentMaskKey.OfStageTerms([(id, 1.0)]),
                    string.IsNullOrWhiteSpace(name) ? $"Загр. {lc}" : $"Загр. {lc} «{name.Trim()}»", "lc"));
            }
            foreach (var f in rsn.Where(f => f.Fit.Ok)) AddCombination(f, "rsn");
            var jointRsu = rsu.Where((f, i) => sections.Contains(lira.Rsu[i].At)).ToList();
            int failed = jointRsu.Count(f => !f.Fit.Ok);
            if (failed > 0)
                warnings.Add($"Узел {joint.NodeTag}: строк РСУ не разложено на загружения — {failed} из {jointRsu.Count} " +
                    $"(например, {jointRsu.First(f => !f.Fit.Ok).Label}: {jointRsu.First(f => !f.Fit.Ok).Fit.Failure}) — пропущены.");
            foreach (var f in LiraCombinationRows.Distinct(jointRsu.Where(f => f.Fit.Ok))) AddCombination(f, "rsu");

            void AddCombination(LiraFittedCombination f, string kind)
            {
                var terms = new List<(int, double)>();
                foreach (var (lc, k) in f.Fit.Terms)
                {
                    if (!map.TryGetValue(lc, out int id))
                    {
                        warnings.Add($"Узел {joint.NodeTag}, {f.Label}: загружения ЛИРЫ {lc} нет в снимке схемы — строка пропущена.");
                        return;
                    }
                    terms.Add((id, k));
                }
                if (terms.Count == 0) return;
                var key = FragmentMaskKey.OfStageTerms(terms);
                if (keys.Any(x => x.Key == key)) return;
                keys.Add((key, f.Duplicates > 0 ? $"{f.Label} (+{f.Duplicates})" : f.Label, kind));
            }

            var forces = PunchingFemService.Evaluate(data, source.WithKeys(keys.Select(k => (k.Key, k.Label)).ToList()), joint, method);
            var items = new List<LoadItem>();
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            PunchingFemForces? first = null;
            foreach (var (key, label, kind) in keys)
            {
                if (!forces.ByKey.TryGetValue(key, out var f)) continue;
                first ??= f;
                counts[kind] = counts.GetValueOrDefault(kind) + 1;
                items.Add(new LoadItem
                {
                    Num = items.Count + 1, Label = label,
                    N = Math.Round(f.Side == PunchingLoadSide.FromBelow ? f.F : -f.F, 3),
                    Mx = Math.Round(f.Mx, 3), My = Math.Round(f.My, 3),
                });
            }
            if (items.Count == 0 || first == null)
            {
                warnings.Add($"Узел {joint.NodeTag}: нет усилий по колоннам ({forces.Missing ?? "нет строк"}) — набор не построен.");
                continue;
            }
            if (forces.Missing != null && items.Count < keys.Count)
                warnings.Add($"Узел {joint.NodeTag}: строк без усилий — {keys.Count - items.Count} ({forces.Missing}).");

            // Набор: прежний того же узла и схемы — обновить строки.
            var set = existingSets.FirstOrDefault(s => s.SourceType == SourceType && s.SourceSchemaId == data.Schema.Id
                && s.SourceElementTag == joint.NodeTag);
            if (set == null)
            {
                set = new ForceSet
                {
                    Tag = UniqueTag($"Продавливание, узел {joint.NodeTag}", data.Schema.Tag, tags),
                    Kind = "bar", SourceType = SourceType, SourceSchemaId = data.Schema.Id, SourceElementTag = joint.NodeTag,
                };
                tags.Add(set.Tag);
            }
            set.Description = string.Format(CultureInfo.InvariantCulture,
                "ЛИРА «{0}», схема «{1}», узел {2}: загружений {3}, РСН {4}, РСУ {5}. N — сила продавливания (+ снизу), " +
                "Mx, My — моменты колонн в осях узла, кН и кН·м.",
                lira.DocumentName, data.Schema.Tag, joint.NodeTag, counts.GetValueOrDefault("lc"), counts.GetValueOrDefault("rsn"),
                counts.GetValueOrDefault("rsu"));
            set.Items = items;

            // Пакетная задача: геометрия узла, сторона — по преобладающему знаку N набора.
            int sign = PunchingBatchRunner.DominantSign(items);
            var side = sign >= 0 ? PunchingLoadSide.FromBelow : PunchingLoadSide.FromAbove;
            var p = PunchingFemService.TaskParams(joint, first with { Side = side }, check, data.Schema.Tag, $"ЛИРА «{lira.DocumentName}»");
            var task = set.Id > 0
                ? existingTasks.FirstOrDefault(t => t.Kind == PunchingBatchRunner.Kind && t.ForceSetId == set.Id)
                : null;
            task ??= new CalcTask { Kind = PunchingBatchRunner.Kind, Tag = set.Tag, CalcType = CalcType.C, ForceSetId = set.Id };
            task.ParamsJson = p.ToJson();
            result.Add(new PunchingLiraJointSet(joint, set, task, counts.GetValueOrDefault("lc"), counts.GetValueOrDefault("rsn"),
                counts.GetValueOrDefault("rsu")));
        }
        return new PunchingLiraOutcome(result, warnings);
    }

    /// <summary>Свободное имя: само, затем с именем схемы, затем с номером.</summary>
    static string UniqueTag(string tag, string schema, ISet<string> taken)
    {
        if (!taken.Contains(tag)) return tag;
        string withSchema = $"{tag} ({schema})";
        if (!taken.Contains(withSchema)) return withSchema;
        for (int i = 2; ; i++)
            if (!taken.Contains($"{withSchema} {i}")) return $"{withSchema} {i}";
    }
}
