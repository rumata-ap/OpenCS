using System.Globalization;
using CScore.Fem;

namespace CScore.Import;

/// <summary>Итог переноса нагрузок SCAD: полные списки загружений и нагрузок сеточного уровня схемы и журнал.</summary>
/// <param name="LoadCases">Все загружения схемы после переноса (существующие объекты обновлены, новые — с временными Id).</param>
/// <param name="ElementLoads">Все нагрузки на КЭ: не из SCAD — без изменений, из SCAD — новые.</param>
/// <param name="MeshNodeLoads">Все узловые нагрузки на узлы сетки, по тому же правилу.</param>
/// <param name="Report">Журнал: что перенесено, что нет и почему.</param>
public sealed record ScadLoadTransferResult(
    IReadOnlyList<FemLoadCase> LoadCases,
    IReadOnlyList<FemElementLoad> ElementLoads,
    IReadOnlyList<FemMeshNodeLoad> MeshNodeLoads,
    IReadOnlyList<string> Report,
    int CasesCreated, int ImportedElementLoads, int ImportedNodeLoads, int SkippedRecords);

/// <summary>
/// Перенос нагрузок из вложения SCAD (<see cref="ScadAnalysisModel"/>) в нагрузки сеточного уровня схемы. Виды —
/// <c>ApiForceType</c> SCADAPIX (ScadStructHelpAPI.hxx); Qn 1–3 — оси X, Y, Z (в местной системе — x, y, z КЭ),
/// 4–6 — моменты. Правило знаков SCAD (справка, «Правило знаков при задании нагрузок»): положительные силы действуют
/// против осей, моменты — по часовой стрелке с конца оси, поэтому силовые значения переносятся с обратным знаком. Повторный перенос заменяет всё, что перенесено раньше (происхождение «import:scad»), загружения
/// сопоставляются по номеру SCAD; заданное вручную не трогается.
/// </summary>
public static class ScadLoadTransfer
{
    public static readonly string Origin = FemLoadOrigin.Import("scad");

    const int Node = 0, PointLocal = 5, EvenlyLocal = 6, TrapezLocal = 7, PointGlobal = 15, EvenlyGlobal = 16,
        TrapezGlobal = 17, EvenlyGlobalIns = 56, EvenlyLocalIns = 46, Weight = 96, WeightIns = 116;

    /// <param name="model">Вложение SCAD схемы.</param>
    /// <param name="elementTypes">Тег КЭ сетки → тип («beam» | «shell»).</param>
    /// <param name="loadCases">Загружения схемы.</param>
    /// <param name="elementLoads">Нагрузки на КЭ схемы.</param>
    /// <param name="meshNodeLoads">Узловые нагрузки на узлы сетки схемы.</param>
    /// <param name="allocateLoadCaseId">Временный Id нового загружения (отрицательный, до сохранения).</param>
    public static ScadLoadTransferResult Transfer(ScadAnalysisModel model, IReadOnlyDictionary<string, string> elementTypes,
        IReadOnlyList<FemLoadCase> loadCases, IReadOnlyList<FemElementLoad> elementLoads,
        IReadOnlyList<FemMeshNodeLoad> meshNodeLoads, Func<int> allocateLoadCaseId)
    {
        double fu = model.ForceUnitN, lu = model.LengthUnitM;
        var report = new List<string>();
        var cases = loadCases.ToList();
        var bySource = cases.Where(c => c.Origin == Origin && c.SourceLoadNum != null)
            .GroupBy(c => c.SourceLoadNum!.Value).ToDictionary(g => g.Key, g => g.First());
        var newElementLoads = new List<FemElementLoad>();
        var newNodeLoads = new List<FemMeshNodeLoad>();
        int created = 0, skippedRecords = 0;

        foreach (var lc in model.LoadCases)
        {
            if (!bySource.TryGetValue(lc.Num, out var target))
            {
                target = new FemLoadCase
                {
                    Id = allocateLoadCaseId(), Origin = Origin, SourceLoadNum = lc.Num,
                    Tag = UniqueTag(CaseTag(lc), cases),
                };
                cases.Add(target);
                created++;
            }
            var skipped = new SortedDictionary<string, int>(StringComparer.Ordinal);
            void Skip(string what, int count) { skipped[what] = skipped.GetValueOrDefault(what) + count; skippedRecords++; }

            var merged = new Dictionary<string, FemElementLoad>(StringComparer.Ordinal);
            void AddElements(string kind, string cs, string axis, double[] values, IEnumerable<string> tags)
            {
                string key = string.Join("|", kind, cs, axis,
                    string.Join(";", values.Select(v => v.ToString("R", CultureInfo.InvariantCulture))));
                if (!merged.TryGetValue(key, out var load))
                {
                    load = new FemElementLoad
                    {
                        LoadCaseId = target.Id, Origin = Origin, TargetKind = FemLoadTargetKinds.Elements,
                        LoadKind = kind, CoordinateSystem = cs, Axis = axis,
                    };
                    load.SetValues(values);
                    merged[key] = load;
                    newElementLoads.Add(load);
                }
                load.SetTargetTags(load.TargetTags.Concat(tags));
            }

            foreach (var r in lc.NodeLoads)
            {
                if (r.Qw != Node || r.Qn is < 1 or > 6 || r.Data.Length < 1) { Skip($"узловые Qw {r.Qw} Qn {r.Qn}", r.Ids.Length); continue; }
                double v = -r.Data[0] * fu * (r.Qn > 3 ? lu : 1);
                foreach (int id in r.Ids)
                {
                    var load = new FemMeshNodeLoad
                    {
                        LoadCaseId = target.Id, Origin = Origin,
                        MeshNodeTag = id.ToString(CultureInfo.InvariantCulture),
                    };
                    switch (r.Qn)
                    {
                        case 1: load.Fx = v; break;
                        case 2: load.Fy = v; break;
                        case 3: load.Fz = v; break;
                        case 4: load.Mx = v; break;
                        case 5: load.My = v; break;
                        default: load.Mz = v; break;
                    }
                    newNodeLoads.Add(load);
                }
            }

            foreach (var r in lc.ElementLoads)
            {
                var ids = r.Ids.Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray();
                if (r.Qw is Weight or WeightIns)
                {
                    if (r.Data.Length < 1) { Skip("с. в. без коэффициента", ids.Length); continue; }
                    AddElements(FemElementLoadKinds.SelfWeight, "global", "z", [r.Data[0]], ids);
                    if (r.Qw == WeightIns) Note(report, lc.Num, "Qw 116: собственный вес, жёсткие вставки не учитываются");
                    continue;
                }
                bool local = r.Qw is PointLocal or EvenlyLocal or TrapezLocal or EvenlyLocalIns;
                if (r.Qn is < 1 or > 3 || r.Data.Length < 1 ||
                    r.Qw is not (PointLocal or EvenlyLocal or TrapezLocal or PointGlobal or EvenlyGlobal or TrapezGlobal
                        or EvenlyGlobalIns or EvenlyLocalIns))
                {
                    Skip(Describe(r), ids.Length);
                    continue;
                }
                string cs = local ? "local" : "global";
                string axis = r.Qn switch { 1 => "x", 2 => "y", _ => "z" };
                foreach (var byType in ids.GroupBy(t => elementTypes.GetValueOrDefault(t)))
                {
                    var tags = byType.ToArray();
                    bool shell = byType.Key == "shell", bar = byType.Key == "beam";
                    if (!shell && !bar) { Skip($"{Describe(r)} на КЭ, которых нет в сетке или не стержень/пластина", tags.Length); continue; }
                    switch (r.Qw)
                    {
                        case EvenlyGlobal or EvenlyLocal or EvenlyGlobalIns or EvenlyLocalIns:
                            AddElements(FemElementLoadKinds.Uniform, cs, axis,
                                [-r.Data[0] * fu / (shell ? lu * lu : lu)], tags);
                            if (bar && r.Qw is EvenlyGlobalIns or EvenlyLocalIns)
                                Note(report, lc.Num, $"Qw {r.Qw}: равномерная по длине КЭ, жёсткие вставки не учитываются");
                            break;
                        case TrapezGlobal or TrapezLocal when shell:
                            AddElements(FemElementLoadKinds.Nodal, cs, axis, r.Data.Select(q => -q * fu / (lu * lu)).ToArray(), tags);
                            break;
                        case PointGlobal or PointLocal when shell && r.Data.Length >= 3:
                            AddElements(FemElementLoadKinds.Point, cs, axis, [-r.Data[0] * fu, r.Data[1] * lu, r.Data[2] * lu], tags);
                            break;
                        case PointGlobal or PointLocal when bar && r.Data.Length >= 2:
                            AddElements(FemElementLoadKinds.Point, cs, axis, [-r.Data[0] * fu, r.Data[1] * lu], tags);
                            break;
                        default:
                            Skip($"{Describe(r)} на {(shell ? "пластины" : "стержни")} (Data: {r.Data.Length})", tags.Length);
                            break;
                    }
                }
            }
            foreach (var r in lc.AreaLoads) Skip($"нагрузка по области Qw {r.Qw} (штамп — позже)", 1);

            foreach (var (what, count) in skipped)
                report.Add($"Загружение {lc.Num} «{lc.Name}»: не перенесено — {what}, объектов {count}.");
        }

        foreach (var orphan in cases.Where(c => c.Origin == Origin && c.SourceLoadNum is { } n && model.LoadCases.All(l => l.Num != n)))
            report.Add($"Загружение «{orphan.Tag}» (SCAD {orphan.SourceLoadNum}) в SCAD отсутствует — перенесённые в него нагрузки удалены.");

        var keptElement = elementLoads.Where(l => l.Origin != Origin);
        var keptNode = meshNodeLoads.Where(l => l.Origin != Origin);
        report.Insert(0, $"Перенесено из SCAD: загружений {model.LoadCases.Count} (новых {created}), нагрузок на КЭ " +
            $"{newElementLoads.Count}, узловых {newNodeLoads.Count}; не перенесено записей {skippedRecords}.");
        return new ScadLoadTransferResult(cases, keptElement.Concat(newElementLoads).ToList(),
            keptNode.Concat(newNodeLoads).ToList(), report, created, newElementLoads.Count, newNodeLoads.Count, skippedRecords);
    }

    static string CaseTag(ScadLoadCase lc) =>
        string.IsNullOrWhiteSpace(lc.Name) ? $"L{lc.Num}" : lc.Name.Trim();

    static string UniqueTag(string tag, IReadOnlyList<FemLoadCase> cases)
    {
        if (cases.All(c => c.Tag != tag)) return tag;
        for (int k = 2; ; k++)
            if (cases.All(c => c.Tag != $"{tag} ({k})")) return $"{tag} ({k})";
    }

    static string Describe(ScadLoadRecord r) => r.Qw switch
    {
        1 or 2 or 11 => $"заданные перемещения Qw {r.Qw}",
        8 or 18 or 88 => $"температура Qw {r.Qw}",
        85 or 86 => $"массы Qw {r.Qw}",
        45 or 55 => $"сосредоточенная в долях длины Qw {r.Qw}",
        47 or 57 => $"трапеция по отрезку стержня Qw {r.Qw}",
        _ when r.Qn is < 1 or > 3 => $"Qw {r.Qw} Qn {r.Qn} (моменты на КЭ)",
        _ => $"Qw {r.Qw} Qn {r.Qn}",
    };

    static void Note(List<string> report, int num, string text)
    {
        string line = $"Загружение {num}: {text}.";
        if (!report.Contains(line)) report.Add(line);
    }
}
