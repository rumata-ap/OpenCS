using CScore;
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

    /// <summary>Стадии нагружения.</summary>
    public required IReadOnlyList<ScadShellStage> Stages { get; init; }
}

/// <summary>Итог адаптации: модель, суммарная вертикальная нагрузка стадий (Н, вниз — плюс), отчёт.</summary>
public sealed record ScadRcModelResult(RcStructuralModel Model, IReadOnlyList<double> StageTotalDownN,
    IReadOnlyList<string> Report);

/// <summary>
/// Адаптер схемы SCAD (.SPR, <see cref="ScadSchemaData"/>) → нейтральная модель <see cref="RcStructuralModel"/>:
/// пластины Q4/T3 (порядок SCAD «1 2 4 3» → обход контура), ось x сечения — ось X1 КЭ, повёрнутая на угол осей
/// SCAD (<see cref="ScadSchemaData.PlateAxisAngles"/>), стержни (упругие по жёсткости SCAD, при наличии — сечение
/// CScore), жёсткие тела с любой маской DOF, закрепления, загружения — узловыми силами
/// (<see cref="ScadShellModelAssembler.NodalLoads"/>, как в сборке OpenSees), стадии. Номера узлов и КЭ — номера SCAD.
/// </summary>
public static class ScadRcModelAdapter
{
    /// <summary>Упругие пластины: изотропный слой (E, ν) толщиной сечения SCAD.</summary>
    public static Func<ScadShellElementSection, RcShellSection> ElasticShells(double e, double nu) =>
        s => new RcShellSection($"elastic|{s.Section.H}")
        {
            Elastic = new Laminate(new[] { new Ply(new OrthotropicMaterial(e, e, nu, e / (2 * (1 + nu))), 0.0, s.Section.H) }),
        };

    public static ScadRcModelResult Adapt(ScadRcModelInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var data = input.Data;
        var am = data.AnalysisModel ?? throw new CScoreMappingException(
            "У схемы SCAD нет расчётной модели (опоры, жёсткие тела, нагрузки) — перечитайте схему из .SPR.");
        var report = new List<string>();
        var stiff = data.Stiffnesses.ToDictionary(s => s.Id);
        double fu = am.ForceUnitN, lu = am.LengthUnitM;
        var nodes = data.Nodes.ToDictionary(n => n.Id);
        var used = new HashSet<int>();
        var model = new RcStructuralModel();

        // Пластины.
        var sectionByKey = new Dictionary<string, RcShellSection>(StringComparer.Ordinal);
        int noSection = 0;
        foreach (var e in data.Elements)
        {
            if (ScadElementKinds.Classify(e.TypeCode, e.NodeIds.Length) != ScadElementKind.Shell) continue;
            int[] contour = e.NodeIds.Length == 4 ? [e.NodeIds[0], e.NodeIds[1], e.NodeIds[3], e.NodeIds[2]] : e.NodeIds;
            if (input.PlateSection(e.Id) is not { } sec) { noSection++; continue; }
            if (!sectionByKey.TryGetValue(sec.Key, out var rs)) sectionByKey[sec.Key] = rs = input.ShellSection(sec);
            var frame = ScadShellModelAssembler.Frame(contour.Select(id => nodes[id]).ToArray(),
                data.PlateAxisAngles.GetValueOrDefault(e.Id));
            model.Shells.Add(new RcShell(e.Id, contour, rs, [frame.Ex.X, frame.Ex.Y, frame.Ex.Z]));
            foreach (int id in contour) used.Add(id);
        }
        if (noSection > 0) report.Add($"Пропущено {noSection} пластин без сечения.");

        // Стержни.
        var beamSectionByKey = new Dictionary<string, RcBeamSection>(StringComparer.Ordinal);
        foreach (var e in data.Elements)
        {
            if (ScadElementKinds.Classify(e.TypeCode, e.NodeIds.Length) != ScadElementKind.Beam) continue;
            var (ni, nj) = (nodes[e.NodeIds[0]], nodes[e.NodeIds[1]]);
            var vecxz = ScadShellModelAssembler.Vecxz(ni, nj);
            var lin = ScadShellModelAssembler.ElasticBeam(e, stiff, fu, lu, vecxz, ni, nj);
            var cross = input.BeamSection?.Invoke(e.Id);
            if (lin == null && cross == null)
            {
                report.Add($"Стержень {e.Id}: нет сечения (жёсткость {e.StiffnessId} — не брус S0).");
                continue;
            }
            string key = $"{cross?.Key ?? "-"}|{(lin == null ? "-" : $"{lin.A}|{lin.E}|{lin.Iy}|{lin.Iz}|{lin.J}|{lin.G}")}";
            if (!beamSectionByKey.TryGetValue(key, out var bs))
                beamSectionByKey[key] = bs = new RcBeamSection(key)
                {
                    Elastic = lin == null ? null : new BeamSection(lin.E, lin.A, lin.Iy, lin.Iz, lin.J, lin.G),
                    Cross = cross?.Section,
                    Calc = input.BeamCalc,
                    TorsionGJ = lin == null ? 0 : lin.G * lin.J,
                };
            model.Beams.Add(new RcBeam(e.Id, ni.Id, nj.Id, bs, RefVec(ni, nj, vecxz)));
            used.Add(ni.Id);
            used.Add(nj.Id);
        }

        // Жёсткие тела — любая маска DOF.
        foreach (var b in am.RigidBodies)
        {
            int mask = b.Mask & RigidLink.All;
            if (mask == 0) { report.Add($"Жёсткое тело {b.ElemId}: пустая маска — пропущено."); continue; }
            model.RigidBodies.Add(new RcRigidBody(b.ElemId, b.MasterNode, b.SlaveNodes.ToArray(), mask));
            used.Add(b.MasterNode);
            foreach (int s in b.SlaveNodes) used.Add(s);
        }

        foreach (var n in data.Nodes.Where(n => used.Contains(n.Id)))
        {
            model.Nodes.Add(new RcNode(n.Id, n.X, n.Y, n.Z));
            int mask = am.Bounds.GetValueOrDefault(n.Id) & RigidLink.All;
            if (mask != 0) model.Supports.Add(new RcSupport(n.Id, mask));
        }

        // Загружения: узловые силы (Fz вниз — плюс в NodalLoads).
        foreach (var lc in am.LoadCases)
        {
            var f = ScadShellModelAssembler.NodalLoads(lc, data, stiff, nodes, fu, lu, report);
            var rc = new RcLoadCase(lc.Num, lc.Name ?? $"L{lc.Num}");
            foreach (var (node, fz) in f.OrderBy(kv => kv.Key))
                if (used.Contains(node)) rc.Nodal.Add(new RcNodalLoad(node, [0, 0, -fz, 0, 0, 0]));
            model.LoadCases.Add(rc);
        }

        var totals = new List<double>();
        var byCase = model.LoadCases.ToDictionary(l => l.Id);
        foreach (var st in input.Stages)
        {
            int steps = Math.Max(1, (int)Math.Round(st.MaxLoadFactor / st.LoadFactorStep));
            var loads = st.Loads.Select(l => (l.LoadCase, l.Factor * st.MaxLoadFactor)).ToList();
            double total = 0;
            foreach (var (lc, k) in loads)
            {
                if (!byCase.TryGetValue(lc, out var c)) throw new CScoreMappingException($"Нет загружения {lc}.");
                total -= k * c.Nodal.Sum(p => p.Force[2]);
            }
            model.Stages.Add(new RcStage(st.Tag, loads, steps));
            totals.Add(total);
        }
        return new ScadRcModelResult(model, totals, report);
    }

    /// <summary>
    /// Локальная ось y CSfea по vecxz OpenSees (вектор в плоскости xz): y = vecxz × x — оси КЭ совпадают с осями
    /// упругого стержня сборки OpenSees (Iy, Iz — те же).
    /// </summary>
    static double[] RefVec(ScadNodeRecord a, ScadNodeRecord b, (double X, double Y, double Z) v)
    {
        double ax = b.X - a.X, ay = b.Y - a.Y, az = b.Z - a.Z;
        double l = Math.Sqrt(ax * ax + ay * ay + az * az);
        ax /= l; ay /= l; az /= l;
        return [v.Y * az - v.Z * ay, v.Z * ax - v.X * az, v.X * ay - v.Y * ax];
    }
}
