using System.Globalization;
using CScore;
using CScore.Fem;
using CScore.Import;
using CSfea.CScoreBridge.Structural;
using CSfea.Core;

namespace OpenCS.OpenSees.CScore;

/// <summary>Вход адаптера схемы SCAD → <see cref="RcStructuralModel"/> (расчёты CSfea).</summary>
public sealed class ScadRcModelInput
{
    /// <summary>Схема SCAD с расчётной моделью (опоры, жёсткие тела, загружения).</summary>
    public required ScadSchemaData Data { get; init; }

    /// <summary>Сечение пластинчатого КЭ по номеру; null — КЭ пропускается (в отчёт).</summary>
    public required Func<int, ScadShellElementSection?> PlateSection { get; init; }

    /// <summary>
    /// Перевод сечения пластины в сечение модели CSfea (упругий ламинат или ЖБ-сечение с диаграммами); вызывается
    /// один раз на ключ сечения. Упругий вариант — <see cref="ScadRcModelAdapter.ElasticShells"/>.
    /// </summary>
    public required Func<ScadShellElementSection, RcShellSection> ShellSection { get; init; }

    /// <summary>Сечение CScore стержня по номеру КЭ (нелинейный расчёт); null — только упругое по жёсткости SCAD.</summary>
    public Func<int, (CrossSection Section, string Key)?>? BeamSection { get; init; }

    /// <summary>Вид расчёта для сечений CScore стержней.</summary>
    public CalcType BeamCalc { get; init; } = CalcType.N;

    /// <summary>
    /// Стальные профили жёсткостей STZ (сортамент SCAD, <see cref="ScadSteelProfiles"/>): номер жёсткости → профиль;
    /// null — сортаментные стержни пропускаются. E = 2,06·10¹¹ Па, ν = 0,3, удельный вес без RO — 7,85 т/м³.
    /// </summary>
    public IReadOnlyDictionary<int, ImportedSteelShape>? SteelShapes { get; init; }

    /// <summary>Стадии нагружения.</summary>
    public required IReadOnlyList<ScadShellStage> Stages { get; init; }
}

/// <summary>Итог адаптации: модель, суммарная вертикальная нагрузка стадий (Н, вниз — плюс), отчёт.</summary>
public sealed record ScadRcModelResult(RcStructuralModel Model, IReadOnlyList<double> StageTotalDownN,
    IReadOnlyList<string> Report);

/// <summary>
/// Адаптер схемы SCAD (.SPR, <see cref="ScadSchemaData"/>) → нейтральная модель <see cref="RcStructuralModel"/> тем же
/// путём, что схема FEM из БД: сетка (<see cref="ScadSchemaConverter"/>, оси пластин и стержней SCAD), ГУ
/// (<see cref="ScadBoundaryTransfer"/>: закрепления, КЭ 51, жёсткие тела, шарниры, C1), загружения
/// (<see cref="ScadLoadTransfer"/>), упругие свойства и собственный вес по жёсткостям
/// (<see cref="ScadElementStiffnessSource"/>) → <see cref="FemRcModelAdapter"/>. Пластины без сечения и стержни без
/// сечения или с вырожденными осями пропускаются (в отчёт). Номера узлов, КЭ и загружений — номера SCAD.
/// </summary>
public static class ScadRcModelAdapter
{
    /// <summary>Упругие пластины: изотропный слой (E, ν) толщиной сечения SCAD.</summary>
    public static Func<ScadShellElementSection, RcShellSection> ElasticShells(double e, double nu) =>
        s => new RcShellSection($"elastic|{s.Section.H}")
        {
            Elastic = new Laminate(new[] { new Ply(new OrthotropicMaterial(e, e, nu, e / (2 * (1 + nu))), 0.0, s.Section.H) }),
        };

    /// <summary>Коды диагностик общего адаптера, которые здесь заменены строками отчёта в терминах SCAD.</summary>
    static readonly HashSet<string> ReplacedDiagnostics = new(StringComparer.Ordinal)
        { "shell_foundation", "beam_releases", "spring_without_elements" };

    public static ScadRcModelResult Adapt(ScadRcModelInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var data = input.Data;
        var am = data.AnalysisModel ?? throw new CScoreMappingException(
            "У схемы SCAD нет расчётной модели (опоры, жёсткие тела, нагрузки) — перечитайте схему из .SPR.");
        foreach (var st in input.Stages)
            foreach (var (lc, _) in st.Loads)
                if (!am.LoadCases.Any(c => c.Num == lc)) throw new CScoreMappingException($"Нет загружения {lc}.");
        var report = new List<string>();
        var stiffness = ScadSchemaConverter.ToSchemaStiffnesses(data).ToDictionary(s => s.Id);
        var steel = input.SteelShapes == null ? null
            : new SteelProfileIndex(input.SteelShapes.Select(kv => new SteelProfileEntry(kv.Key, "STZ", kv.Value, null)));
        var properties = new ScadElementStiffnessSource(stiffness, am.ForceUnitN, am.LengthUnitM, steel);

        // Сетка: только пластины и стержни SCAD; сечения — по номеру КЭ, у пластин — готовые сечения модели по ключу.
        var kinds = data.Elements.ToDictionary(e => T(e.Id), e => ScadElementKinds.Classify(e.TypeCode, e.NodeIds.Length));
        var elements = new List<FemElement>();
        var shellByKey = new Dictionary<string, RcShellSection>(StringComparer.Ordinal);
        var shellSections = new Dictionary<string, RcShellSection>(StringComparer.Ordinal);
        var beamSections = new Dictionary<string, FemRcBeamCross>(StringComparer.Ordinal);
        var noBarSection = new Dictionary<int, int>();
        int noSection = 0, rotatedAxes = 0;
        foreach (var e in ScadSchemaConverter.ToFemMeshElements(data, 0))
        {
            int id = int.Parse(e.ElemTag, CultureInfo.InvariantCulture);
            switch (kinds[e.ElemTag])
            {
                case ScadElementKind.Shell:
                    if (input.PlateSection(id) is not { } sec) { noSection++; continue; }
                    if (!shellByKey.TryGetValue(sec.Key, out var rs)) shellByKey[sec.Key] = rs = input.ShellSection(sec);
                    shellSections[e.ElemTag] = rs;
                    break;
                case ScadElementKind.Beam:
                    var bar = properties.Bar(e);
                    var cross = input.BeamSection?.Invoke(id);
                    if (bar == null && cross == null)
                    {
                        int sid = e.StiffnessNum ?? 0;
                        noBarSection[sid] = noBarSection.GetValueOrDefault(sid) + 1;
                        continue;
                    }
                    if (e.BeamRotationDeg == null)
                    {
                        report.Add($"Стержень {id}: нулевая длина или вырожденная ориентация осей — пропущен.");
                        continue;
                    }
                    if (data.RodAxes.ContainsKey(id)) rotatedAxes++;
                    if (cross is { } c)
                        beamSections[e.ElemTag] = new FemRcBeamCross(c.Section, c.Key, bar == null ? 0 : bar.G * bar.J);
                    break;
                default:
                    continue;
            }
            elements.Add(e);
        }
        if (noSection > 0) report.Add($"Пропущено {noSection} пластин без сечения.");
        foreach (var (sid, count) in noBarSection.OrderBy(x => x.Key))
            report.Add($"Пропущено стержней {count}: нет сечения (жёсткость {sid} — не S0/S3/S6 и не профиль сортамента: " +
                $"{stiffness.GetValueOrDefault(sid)?.Params.Split(' ')[0] ?? "?"}).");
        if (rotatedAxes > 0) report.Add($"Местные оси стержней по ориентации SCAD: {rotatedAxes} КЭ.");

        // ГУ и нагрузки — переносами импорта; загружения получают номера SCAD.
        var types = elements.ToDictionary(e => e.ElemTag, e => e.ElemType, StringComparer.Ordinal);
        var nodes = ScadSchemaConverter.ToFemMeshNodes(data, 0);
        var bc = ScadBoundaryTransfer.Transfer(am, nodes.Select(n => n.NodeTag).ToHashSet(StringComparer.Ordinal), types);
        foreach (var e in elements)
            if (bc.ElementProps.TryGetValue(e.ElemTag, out var p))
                (e.ReleaseI, e.ReleaseJ, e.FoundationC1) = (p.ReleaseI, p.ReleaseJ, p.FoundationC1);
        var caseIds = new Queue<int>(am.LoadCases.Select(c => c.Num));
        var loads = ScadLoadTransfer.Transfer(am, types, [], [], [], caseIds.Dequeue);
        report.AddRange(loads.Report.Skip(1));

        var adapted = FemRcModelAdapter.Adapt(new FemRcModelInput
        {
            MeshNodes = nodes, MeshElements = elements,
            Supports = bc.Supports, Springs = bc.Springs, RigidBodies = bc.RigidBodies,
            LoadCases = loads.LoadCases, ElementLoads = loads.ElementLoads, MeshNodeLoads = loads.MeshNodeLoads,
            Properties = properties,
            ShellModelSection = e => shellSections.GetValueOrDefault(e.ElemTag),
            BeamSection = e => beamSections.GetValueOrDefault(e.ElemTag),
            Calc = input.BeamCalc,
            AllLoadCases = true,
            Stages = input.Stages.Select(st => new FemRcStage(st.Tag,
                st.Loads.Select(l => (l.LoadCase, l.Factor * st.MaxLoadFactor)).ToList(),
                Math.Max(1, (int)Math.Round(st.MaxLoadFactor / st.LoadFactorStep)))).ToList(),
        });
        var model = adapted.Model;

        int onBed = model.Shells.Count(s => s.FoundationC1 != null);
        if (onBed > 0) report.Add($"Упругое основание C1: {onBed} пластин.");
        int beyondC1 = am.Beds.Where(b => b.C1 > 0 && b.HasBeyondC1).Sum(b => b.Elements.Count());
        if (beyondC1 > 0) report.Add($"Упругое основание: C2 и прочие коэффициенты кроме C1 не учтены ({beyondC1} КЭ).");
        int releasedEnds = model.Beams.Sum(b => (b.ReleaseI != 0 ? 1 : 0) + (b.ReleaseJ != 0 ? 1 : 0));
        if (releasedEnds > 0) report.Add($"Шарниры: освобождено концов стержней {releasedEnds}.");
        var used = model.Nodes.Select(n => n.Id).ToHashSet();
        int springs = am.Springs.Count(s => used.Contains(s.Node)), orphanSprings = am.Springs.Count - springs;
        if (springs > 0) report.Add($"Связи конечной жёсткости (КЭ 51): {springs}.");
        if (orphanSprings > 0) report.Add($"Связи конечной жёсткости (КЭ 51): {orphanSprings} на узлах без КЭ модели — пропущены.");
        foreach (var line in adapted.Diagnostics.Where(d => !ReplacedDiagnostics.Contains(d.Code))
                     .OrderByDescending(d => d.IsError).Select(d => (d.IsError ? "Ошибка: " : "") + d.Message))
            if (!report.Contains(line)) report.Add(line);

        return new ScadRcModelResult(model, adapted.StageTotals.Select(t => -t.Fz).ToList(), report);
    }

    static string T(int id) => id.ToString(CultureInfo.InvariantCulture);
}
