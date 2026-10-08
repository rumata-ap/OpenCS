using System.Globalization;
using System.Text.Json;
using CScore;
using CScore.Fem;
using CScore.Fem.Loads;
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
/// SCAD (<see cref="ScadSchemaData.PlateAxisAngles"/>), C1 упругого основания пластин, стержни (упругие по жёсткости SCAD, при наличии — сечение
/// CScore), жёсткие тела с любой маской DOF, закрепления, загружения — узловыми силами
/// по всем осям (<see cref="ScadLoadTransfer"/> + <see cref="FemElementLoadNodalizer"/>, как у схемы FEM), стадии. Номера узлов и КЭ — номера SCAD.
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

        // Пластины; C1 упругого основания — из групп ApiGetBed (C2 и прочие коэффициенты не учитываются).
        var c1 = new Dictionary<int, double>();
        int beyondC1 = 0;
        foreach (var bed in am.Beds.Where(b => b.C1 > 0))
            foreach (int id in bed.Elements)
            {
                c1[id] = c1.GetValueOrDefault(id) + bed.C1;
                if (bed.HasBeyondC1) beyondC1++;
            }
        var sectionByKey = new Dictionary<string, RcShellSection>(StringComparer.Ordinal);
        int noSection = 0, onBed = 0;
        foreach (var e in data.Elements)
        {
            if (ScadElementKinds.Classify(e.TypeCode, e.NodeIds.Length) != ScadElementKind.Shell) continue;
            int[] contour = e.NodeIds.Length == 4 ? [e.NodeIds[0], e.NodeIds[1], e.NodeIds[3], e.NodeIds[2]] : e.NodeIds;
            if (input.PlateSection(e.Id) is not { } sec) { noSection++; continue; }
            if (!sectionByKey.TryGetValue(sec.Key, out var rs)) sectionByKey[sec.Key] = rs = input.ShellSection(sec);
            var frame = ScadShellModelAssembler.Frame(contour.Select(id => nodes[id]).ToArray(),
                data.PlateAxisAngles.GetValueOrDefault(e.Id));
            double? foundation = c1.TryGetValue(e.Id, out double k) ? k : null;
            if (foundation != null) onBed++;
            model.Shells.Add(new RcShell(e.Id, contour, rs, [frame.Ex.X, frame.Ex.Y, frame.Ex.Z], foundation));
            foreach (int id in contour) used.Add(id);
        }
        if (noSection > 0) report.Add($"Пропущено {noSection} пластин без сечения.");
        if (onBed > 0) report.Add($"Упругое основание C1: {onBed} пластин.");
        if (beyondC1 > 0) report.Add($"Упругое основание: C2 и прочие коэффициенты кроме C1 не учтены ({beyondC1} КЭ).");

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

        // Загружения: нагрузки SCAD → нагрузки сеточного уровня (ScadLoadTransfer) → согласованные узловые силы
        // (FemElementLoadNodalizer) — тот же путь, что у схемы FEM; собственный вес — по RO и толщине/сечению жёсткости.
        foreach (var rc in NodalLoadCases(data, model, stiff, fu, lu, report))
        {
            rc.Nodal.RemoveAll(p => !used.Contains(p.NodeId));
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
    /// Узловые силы загружений SCAD (глобальные оси, Н и Н·м) по КЭ модели: перенос <see cref="ScadLoadTransfer"/> и
    /// согласованное распределение <see cref="FemElementLoadNodalizer"/>. Непереносимое и пропущенное — в отчёт.
    /// </summary>
    static List<RcLoadCase> NodalLoadCases(ScadSchemaData data, RcStructuralModel model,
        IReadOnlyDictionary<int, ScadStiffnessRecord> stiff, double fu, double lu, List<string> report)
    {
        var am = data.AnalysisModel!;
        var inModel = model.Shells.Select(x => x.Id).Concat(model.Beams.Select(x => x.Id)).ToHashSet();
        string T(int id) => id.ToString(CultureInfo.InvariantCulture);
        var elements = data.Elements.Where(e => inModel.Contains(e.Id)).Select(e =>
        {
            bool shell = ScadElementKinds.Classify(e.TypeCode, e.NodeIds.Length) == ScadElementKind.Shell;
            return new FemElement
            {
                ElemTag = T(e.Id), ElemType = shell ? "shell" : "beam", NodeIdsJson = JsonSerializer.Serialize(e.NodeIds),
                StiffnessNum = e.StiffnessId, ThicknessM = shell ? stiff.GetValueOrDefault(e.StiffnessId)?.ThicknessM : null,
            };
        }).ToList();
        var meshNodes = data.Nodes.Select(n => new FemMeshNode { NodeTag = T(n.Id), X = n.X, Y = n.Y, Z = n.Z }).ToList();
        var mesh = new FemLoadMeshContext(meshNodes, elements, null, new StiffnessSelfWeight(stiff, fu, lu));

        int nextId = 0;
        var transfer = ScadLoadTransfer.Transfer(am, elements.ToDictionary(e => e.ElemTag, e => e.ElemType),
            [], [], [], () => --nextId);
        report.AddRange(transfer.Report.Skip(1));

        var cases = new List<RcLoadCase>();
        var diagnostics = new List<FemValidationDiagnostic>();
        foreach (var lc in am.LoadCases)
        {
            var target = transfer.LoadCases.Single(c => c.SourceLoadNum == lc.Num);
            var forces = new Dictionary<string, double[]>(StringComparer.Ordinal);
            foreach (var load in transfer.ElementLoads.Where(l => l.LoadCaseId == target.Id))
                FemElementLoadNodalizer.Accumulate(load, mesh, 1.0, forces, diagnostics);
            foreach (var p in transfer.MeshNodeLoads.Where(l => l.LoadCaseId == target.Id))
            {
                if (!forces.TryGetValue(p.MeshNodeTag, out var v)) forces[p.MeshNodeTag] = v = new double[6];
                v[0] += p.Fx; v[1] += p.Fy; v[2] += p.Fz; v[3] += p.Mx; v[4] += p.My; v[5] += p.Mz;
            }
            var rc = new RcLoadCase(lc.Num, lc.Name ?? $"L{lc.Num}");
            foreach (var (tag, v) in forces.OrderBy(kv => int.Parse(kv.Key, CultureInfo.InvariantCulture)))
                rc.Nodal.Add(new RcNodalLoad(int.Parse(tag, CultureInfo.InvariantCulture), v));
            cases.Add(rc);
        }
        foreach (var d in diagnostics) if (!report.Contains(d.Message)) report.Add(d.Message);
        return cases;
    }

    /// <summary>Удельный вес (RO жёсткости) и площадь бруса S0 для собственного веса.</summary>
    sealed class StiffnessSelfWeight(IReadOnlyDictionary<int, ScadStiffnessRecord> stiff, double fu, double lu)
        : IFemSelfWeightSource
    {
        public double? UnitWeight(FemElement element) =>
            element.StiffnessNum is { } id && ScadShellModelAssembler.Density(stiff.GetValueOrDefault(id), fu, lu) is > 0 and var g
                ? g : null;

        public double? BarArea(FemElement element) =>
            element.StiffnessNum is { } id && stiff.GetValueOrDefault(id)?.BarRect is { } r ? r.WidthM * r.HeightM : null;
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
