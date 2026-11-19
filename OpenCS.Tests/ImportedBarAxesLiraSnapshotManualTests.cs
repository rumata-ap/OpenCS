using System.Diagnostics;
using CScore.Fem;
using CScore.Fem.Fragment.Masks;
using CScore.Fem.Loads;
using CScore.Sp63Punching;
using CScore.Sp63Punching.Fem;
using CScore.Submodel;
using Microsoft.Data.Sqlite;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;
using Xunit.Abstractions;

namespace OpenCS.Tests;

/// <summary>
/// Ручная сверка осей импортных стержней ЛИРЫ (местная Y = Z1, спека 2026-11-18, срез 2): копия базы импортной схемы со
/// снимком результатов ЛИРЫ (OPENCS_AXES_CMP_DB; при открытии — миграция v102), линейная постановка CSfea родителя
/// (OPENCS_AXES_CMP_ANALYSIS — Id, по умолчанию первая линейная с результатом до миграции) пересчитывается, затем:
/// <list type="number">
/// <item>силы концов вертикальных стержней (колонн) снимка против CSfea в осях сетки — по компонентам и |M| = √(My²+Mz²);</item>
/// <item>маски фрагментов из CSfea и из снимка: невязка равновесия и расхождение сил по разрезам;</item>
/// <item>продавливание «по колоннам» от снимка и от CSfea на тех же ключах; отступы контуров ЛИРЫ (*.asp) от граней.</item>
/// </list>
/// Без переменной — выход. Сравнение — масштабом s = Σc·l / Σl² и невязкой ‖c − sign(s)·l‖ / ‖l‖ (c — CSfea, l — снимок).
/// </summary>
public class ImportedBarAxesLiraSnapshotManualTests(ITestOutputHelper output)
{
    [Fact]
    public async Task CompareCsfeaWithLiraSnapshot()
    {
        string? source = Environment.GetEnvironmentVariable("OPENCS_AXES_CMP_DB");
        if (string.IsNullOrWhiteSpace(source)) return;
        var root = Path.Combine(Path.GetTempPath(), $"opencs-axes-cmp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "source.db");
        File.Copy(source, path);
        try { await Run(path, root); }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// Пересборка фрагмента импортной схемы (без расчёта): поворот сечения частей колонн = поворот КЭ родителя на той же
    /// оси в плане (оси ЛИРЫ — у КЭ, у КонЭ «КЭ без КонЭ» — 0; до 19.11 части теряли поворот).
    /// </summary>
    [Fact]
    public void FragmentRebuild_ColumnRotationsMatchParent()
    {
        string? source = Environment.GetEnvironmentVariable("OPENCS_AXES_CMP_DB");
        if (string.IsNullOrWhiteSpace(source)) return;
        var root = Path.Combine(Path.GetTempPath(), $"opencs-axes-frag-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "source.db");
        File.Copy(source, path);
        try
        {
            using var db = new DatabaseService(path);
            db.LoadAll();
            var parent = db.FemSchemas.First(s => s.SourceType == "lira");
            var fragment = db.FemSchemas.First(s => s.ParentSchemaId == parent.Id);
            var request = StoreyFragmentService.ParentRequest(fragment)!;
            var draft = StoreyFragmentService.Prepare(db, parent.Id, request).Draft;
            Assert.False(draft.HasErrors, string.Join("; ", draft.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));

            static (double, double) Key(double x, double y) => (Math.Round(x, 2), Math.Round(y, 2));
            var nodes = db.GetFemMeshNodes(parent.Id).ToDictionary(n => n.NodeTag);
            var parentRotation = new Dictionary<(double, double), HashSet<double>>();
            foreach (var e in db.GetFemMeshElements(parent.Id).Where(e => e.ElemType == "beam"))
            {
                var t = FemMeshTopology.ReadNodeTags(e)!.ToArray();
                var (a, b) = (nodes[t[0]], nodes[t[1]]);
                if (Math.Abs(a.X - b.X) > 1e-6 || Math.Abs(a.Y - b.Y) > 1e-6) continue;
                var k = Key(a.X, a.Y);
                if (!parentRotation.TryGetValue(k, out var set)) parentRotation[k] = set = [];
                set.Add(Math.Round(a.Z < b.Z ? e.BeamRotationDeg ?? 0 : -(e.BeamRotationDeg ?? 0), 6));
            }
            var fnodes = draft.Nodes.ToDictionary(n => n.NodeTag);
            int same = 0, differ = 0, unknown = 0;
            foreach (var m in draft.Members.Select(m => m.Member).Where(m => m.ElemType == "beam"))
            {
                var ids = System.Text.Json.JsonSerializer.Deserialize<int[]>(m.NodeIdsJson)!;
                var (a, b) = (fnodes[ids[0].ToString()], fnodes[ids[^1].ToString()]);
                if (Math.Abs(a.X - b.X) > 1e-6 || Math.Abs(a.Y - b.Y) > 1e-6) continue;
                if (!parentRotation.TryGetValue(Key(a.X, a.Y), out var set)) { unknown++; continue; }
                double rot = Math.Round(a.Z < b.Z ? m.RotationDeg : -m.RotationDeg, 6);
                bool ok = set.Any(r => Math.Abs(Math.IEEERemainder(r - rot, 360)) < 1e-6);
                if (ok) same++;
                else
                {
                    differ++;
                    if (differ <= 5) output.WriteLine($"  {m.ElemTag}: фрагмент {rot}°, родитель {string.Join("/", set)}°");
                }
            }
            output.WriteLine($"колонны фрагмента «{fragment.Tag}»: совпал поворот {same}, разный {differ}, без пары {unknown}");
            Assert.Equal(0, differ);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    async Task Run(string path, string root)
    {
        var sw = Stopwatch.StartNew();
        using var db = new DatabaseService(path);
        db.LoadAll();
        var log = new OutputLog(output);
        var schema = db.FemSchemas.First(s => s.SourceType == "lira");
        var analyses = db.GetFemAnalyses(schema.Id).Where(a => a.Kind == FemCsfeaRunner.LinearAnalysisKind).ToList();
        var analysis = Environment.GetEnvironmentVariable("OPENCS_AXES_CMP_ANALYSIS") is { } aid
            ? analyses.First(a => a.Id == int.Parse(aid))
            : analyses.First(a => a.Status == "created" && a.Tag.Contains("оси", StringComparison.OrdinalIgnoreCase));
        output.WriteLine($"схема «{schema.Tag}», постановка {analysis.Id} «{analysis.Tag}»; открытие {sw.Elapsed.TotalSeconds:0} с");

        sw.Restart();
        var gmsh = new GmshSettings { ExecutablePath = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe", ArtifactsPath = Path.Combine(root, "gmsh") };
        var result = await FemCsfeaRunner.RunAsync(new FemCsfeaRunContext(db, gmsh, log), schema, analysis, null, CancellationToken.None);
        db.SaveCalcResult(result);
        analysis.ResultId = result.Id;
        analysis.Status = result.Status;
        db.SaveFemAnalysis(analysis);
        output.WriteLine($"линейный CSfea: {result.Status}, {sw.Elapsed.TotalSeconds:0} с");
        Assert.Equal("ok", result.Status);

        CompareColumns(db, schema, analysis, result);
        ComparePunching(db, schema, analysis);
        CompareMasks(db, schema, analysis);
    }

    // ---------------------------------------------------------------- колонны

    void CompareColumns(DatabaseService db, FemSchema schema, FemAnalysis analysis, CScore.CalcResult result)
    {
        var loadCases = db.GetFemLoadCases(schema.Id);
        var summary = FemCsfeaResultSummary.Parse(result.DataJson)!;
        var stages = FemAnalysisParams.Parse(analysis.ParamsJson).ResolveStages(analysis);
        var warnings = new List<string>();
        var identity = loadCases.ToDictionary(c => c.Id, c => c.Id);
        var steps = FragmentMaskSourceKeys.Select(summary, stages, loadCases, identity, warnings)
            .Select(s => (s.Key, FemCsfeaStepFieldsCodec.Unpack(db.GetFemResultStep(result.Id, s.StepN)!))).ToList();
        foreach (var w in warnings) output.WriteLine("  ключи: " + w);

        var nodes = new Dictionary<string, FemMeshNode>(StringComparer.Ordinal);
        foreach (var n in db.GetFemMeshNodes(schema.Id)) nodes.TryAdd(n.NodeTag, n);
        var elements = db.GetFemMeshElements(schema.Id);
        var members = db.GetFemMembers(schema.Id);
        var csfea = new CsfeaFieldsParentResult(steps, nodes, elements, members, null);

        var file = db.GetFemSchemaSourceFile(schema.Id, StoreyFragmentMaskService.SnapshotFileKind(FragmentMaskSet.SourceLira))!;
        var snapshot = ImportedResultSnapshotCodec.Decode(file.Value.Data);
        var byNum = FragmentMaskSourceKeys.ImportedLoadCaseMap(snapshot.Loads, loadCases, FemLoadOrigin.Import(FragmentMaskSet.SourceLira), identity, warnings);
        var lira = new ImportedParentResult(snapshot, byNum, nodes, elements, ImportedForceConvention.Lira);
        output.WriteLine($"снимок: загружений {snapshot.Loads.Count}, сопоставлено {byNum.Count}; стержней без осей {lira.BarsWithoutAxes.Count}");
        var (ry, rz, bars) = lira.ShearConsistency();
        output.WriteLine($"согласованность Q и dM/dx снимка в осях сетки: XY {ry:0.000}, XZ {rz:0.000} ({bars} стержне-ключей)");

        var columns = elements.Where(e => e.ElemType == "beam" && FemElementGeometry.Of(e, nodes) is { IsBar: true } g
            && g.Length > 1e-6 && Math.Abs((g.Points[1] - g.Points[0]).Z) / g.Length > 0.99).Select(e => e.ElemTag).ToList();
        var beams = elements.Where(e => e.ElemType == "beam").Select(e => e.ElemTag).Except(columns).ToList();
        output.WriteLine($"стержней: вертикальных {columns.Count}, прочих {beams.Count}");
        string[] names = ["N", "Qy", "Qz", "Mк", "My", "Mz"];

        // Показанные OpenCS усилия сечения в начале КЭ (−f_I, оси сетки) против сырых компонент ЛИРЫ сечения 1.
        output.WriteLine("=== колонны: показанное OpenCS (−f_I) против сырого снимка ЛИРЫ (сечение 1)");
        foreach (var load in snapshot.Loads.Take(2))
        {
            if (!byNum.TryGetValue(load.Num, out int lcId)) continue;
            var key = FragmentMaskKey.OfLoadCase(lcId);
            var index = new Dictionary<int, int>();
            for (int i = 0; i < load.BarNums.Length; i++) index.TryAdd(load.BarNums[i], i);
            var shown = new List<double>[6];
            var raw = new List<double>[6];
            for (int k = 0; k < 6; k++) { shown[k] = []; raw[k] = []; }
            foreach (var tag in columns)
            {
                if (!csfea.TryBeamEndForces(key, tag, out var fc) || !index.TryGetValue(int.Parse(tag), out int bi)) continue;
                double[] sh = [-fc.I.X, -fc.I.Y, -fc.I.Z, -fc.I.Rx, -fc.I.Ry, -fc.I.Rz];
                for (int k = 0; k < 6; k++)
                {
                    shown[k].Add(sh[k] / 1e3);
                    raw[k].Add(load.BarForces[bi * ImportedResultLoad.BarComponents + k] / 1e3);
                }
            }
            output.WriteLine($"  загружение {load.Num}");
            foreach (var (a, b) in new[] { (0, 0), (3, 3), (1, 2), (2, 1), (4, 5), (5, 4) })
                Fit($"OpenCS {names[a]} ↔ ЛИРА {names[b]}", shown[a], raw[b]);
        }

        // Прочие — по жёсткостям программы: у «Мирной» швеллер 20П (7) и связи, близкие к АЖТ (6), ведут себя по-разному.
        var groups = new List<(string, List<string>)> { ("колонны", columns) };
        var byTag = elements.Where(e => e.ElemType == "beam").ToDictionary(e => e.ElemTag);
        foreach (var g in beams.GroupBy(t => byTag[t].StiffnessNum).OrderBy(g => g.Key))
            groups.Add(($"прочие стержни, жёсткость {g.Key?.ToString() ?? "—"} ({g.Count()} КЭ)", g.ToList()));
        foreach (var (what, tags) in groups)
        {
            output.WriteLine($"=== {what}");
            foreach (var key in steps.Select(s => s.Key))
            {
                var c = new List<double>[7];
                var l = new List<double>[7];
                for (int k = 0; k < 7; k++) { c[k] = []; l[k] = []; }
                foreach (var tag in tags)
                {
                    if (!csfea.TryBeamEndForces(key, tag, out var fc) || !lira.TryBeamEndForces(key, tag, out var fl)) continue;
                    foreach (var (a, b) in new[] { (fc.I, fl.I), (fc.J, fl.J) })
                    {
                        double[] ac = [a.X, a.Y, a.Z, a.Rx, a.Ry, a.Rz], bl = [b.X, b.Y, b.Z, b.Rx, b.Ry, b.Rz];
                        for (int k = 0; k < 6; k++) { c[k].Add(ac[k] / 1e3); l[k].Add(bl[k] / 1e3); }
                        c[6].Add(Math.Sqrt(a.Ry * a.Ry + a.Rz * a.Rz) / 1e3);
                        l[6].Add(Math.Sqrt(b.Ry * b.Ry + b.Rz * b.Rz) / 1e3);
                    }
                }
                string label = loadCases.FirstOrDefault(x => key == FragmentMaskKey.OfLoadCase(x.Id))?.Tag ?? key.ToString();
                output.WriteLine($"  {label}: концов {c[0].Count}");
                for (int k = 0; k < 7; k++) Fit(k < 6 ? names[k] : "|M|", c[k], l[k]);
                // Равновесие КЭ в плоскости XY без нагрузки на КЭ: Mz_I + Mz_J + L·Fy_J = 0, ΣFy = 0 (силы узлов на КЭ).
                double[] rm = new double[2], rf = new double[2], mm = new double[2], ff = new double[2];
                foreach (var tag in tags)
                {
                    if (FemElementGeometry.Of(byTag[tag], nodes) is not { IsBar: true } g) continue;
                    double len = g.Length;
                    int src = 0;
                    foreach (var res in new IFragmentParentResult[] { csfea, lira })
                    {
                        if (res.TryBeamEndForces(key, tag, out var f))
                        {
                            rm[src] += Math.Pow(f.I.Rz + f.J.Rz + len * f.J.Y, 2);
                            mm[src] += f.I.Rz * f.I.Rz + f.J.Rz * f.J.Rz;
                            rf[src] += Math.Pow(f.I.Y + f.J.Y, 2);
                            ff[src] += f.I.Y * f.I.Y + f.J.Y * f.J.Y;
                        }
                        src++;
                    }
                }
                output.WriteLine($"     равновесие XY: CSfea ΣF {Math.Sqrt(rf[0] / Math.Max(ff[0], 1e-30)):0.000}, ΣM {Math.Sqrt(rm[0] / Math.Max(mm[0], 1e-30)):0.000}; " +
                                 $"снимок ΣF {Math.Sqrt(rf[1] / Math.Max(ff[1], 1e-30)):0.000}, ΣM {Math.Sqrt(rm[1] / Math.Max(mm[1], 1e-30)):0.000}");
            }
        }
    }

    // ---------------------------------------------------------------- продавливание

    void ComparePunching(DatabaseService db, FemSchema schema, FemAnalysis analysis)
    {
        output.WriteLine("=== продавливание «по колоннам»");
        var data = PunchingFemService.Load(db, schema, new PunchingFemOptions());
        var diag = new List<FemValidationDiagnostic>();
        var csfea = PunchingFemService.OpenCsfea(db, schema, analysis.Id, diag);
        var lira = PunchingFemService.OpenImported(db, schema, FragmentMaskSet.SourceLira, diag);
        foreach (var d in diag.Where(d => d.IsError)) output.WriteLine("  ошибка: " + d.Message);
        Assert.NotNull(csfea);
        Assert.NotNull(lira);
        lira = lira.WithKeys(csfea.Keys);
        output.WriteLine($"  узлов {data.Joints.Count}, ключей {csfea.Keys.Count}");

        var cF = new List<double>(); var lF = new List<double>();
        var cM = new List<double>(); var lM = new List<double>();
        var cMx = new List<double>(); var lMx = new List<double>();
        var cMy = new List<double>(); var lMy = new List<double>();
        var worst = new List<(string Node, string Key, PunchingFemForces C, PunchingFemForces L)>();
        foreach (var joint in data.Joints.Where(j => !j.IsWall && j.Bars.Count > 0))
        {
            var fc = PunchingFemService.Evaluate(data, csfea, joint, PunchingFemForceMethod.Column);
            var fl = PunchingFemService.Evaluate(data, lira, joint, PunchingFemForceMethod.Column);
            foreach (var (key, label) in csfea.Keys)
            {
                if (!fc.ByKey.TryGetValue(key, out var c) || !fl.ByKey.TryGetValue(key, out var l)) continue;
                cF.Add(c.F); lF.Add(l.F);
                cMx.Add(c.Mx); lMx.Add(l.Mx); cMy.Add(c.My); lMy.Add(l.My);
                cM.Add(Math.Sqrt(c.Mx * c.Mx + c.My * c.My)); lM.Add(Math.Sqrt(l.Mx * l.Mx + l.My * l.My));
                worst.Add((joint.NodeTag, label, c, l));
            }
        }
        Fit("F", cF, lF);
        Fit("Mx", cMx, lMx);
        Fit("My", cMy, lMy);
        Fit("|M|", cM, lM);
        foreach (var w in worst.OrderByDescending(w => Math.Abs(w.C.F - w.L.F)).Take(6))
        {
            var j = data.Joints.First(x => x.NodeTag == w.Node);
            output.WriteLine($"    ΔF: узел {w.Node} z {j.Origin.Z:0.000}, h {j.H * 1e3:0}, стержней {j.Bars.Count}, «{w.Key}»: CSfea F {w.C.F:0.0} " +
                $"(стержни {w.C.ColumnAction:0.0}, основание {w.C.BaseLoad:0.0}); снимок F {w.L.F:0.0} (стержни {w.L.ColumnAction:0.0}, основание {w.L.BaseLoad:0.0})");
        }
        foreach (var w in worst.OrderByDescending(w => Math.Abs(Math.Sqrt(w.C.Mx * w.C.Mx + w.C.My * w.C.My) - Math.Sqrt(w.L.Mx * w.L.Mx + w.L.My * w.L.My))).Take(5))
            output.WriteLine($"    узел {w.Node}, «{w.Key}»: CSfea F {w.C.F:0.0} Mx {w.C.Mx:0.00} My {w.C.My:0.00}; снимок F {w.L.F:0.0} Mx {w.L.Mx:0.00} My {w.L.My:0.00} ({w.C.Method}/{w.L.Method})");

        // Контуры ЛИРЫ: отступы от граней колонны в осях узла.
        var asp = PunchingFemService.LoadLiraAsp(db, schema.Id);
        if (asp?.File is not { } file) { output.WriteLine("  *.asp не приложен"); return; }
        var match = PunchingFemService.MatchLira(file, data.Joints);
        int closed = 0, equal = 0;
        foreach (var joint in data.Joints)
        {
            if (!match.ByNode.TryGetValue(joint.NodeTag, out var rec) || !rec.IsClosed) continue;
            if (joint.Area is not PunchingLoadArea.Rectangle rect)
            {
                output.WriteLine($"  узел {joint.NodeTag}: замкнутый контур ЛИРЫ, но грузовая площадь {joint.Area?.GetType().Name ?? "нет"} ({joint.SectionLabel})");
                continue;
            }
            if (PunchingFemLiraContour.ToLocal(joint, rec.Contour) is not { } local)
            {
                output.WriteLine($"  узел {joint.NodeTag}: контур ЛИРЫ не сцепился");
                continue;
            }
            closed++;
            double minX = local.Points.Min(p => p.X), maxX = local.Points.Max(p => p.X);
            double minY = local.Points.Min(p => p.Y), maxY = local.Points.Max(p => p.Y);
            double[] off = [maxX - rect.Bx / 2, -minX - rect.Bx / 2, maxY - rect.By / 2, -minY - rect.By / 2];
            if (off.Max() - off.Min() < 2e-3) { equal++; continue; }
            output.WriteLine($"  узел {joint.NodeTag} ({joint.SectionLabel}, {rect.Bx * 1e3:0}×{rect.By * 1e3:0}, h {joint.H * 1e3:0}, h0/2 " +
                $"{(joint.H - (joint.Ax + joint.Ay) / 2) / 2 * 1e3:0}): отступы ЛИРЫ +x {off[0] * 1e3:0} −x {off[1] * 1e3:0} +y {off[2] * 1e3:0} −y {off[3] * 1e3:0}; " +
                $"u ЛИРЫ {rec.Perimeter * 1e3:0}; стержней {joint.Bars.Count}, капитель {(joint.Capital != null ? "есть" : "нет")}, " +
                $"краёв {joint.Edges.Count}, отверстий {joint.Holes.Count}; точка ({joint.Origin.X:0.000}, {joint.Origin.Y:0.000}, {joint.Origin.Z:0.000}); " +
                $"ось x ({joint.AxisX.X:0.00}, {joint.AxisX.Y:0.00})");
            output.WriteLine("    контур в осях узла: " + string.Join(" ", local.Points.Select(p => $"({p.X * 1e3:0}, {p.Y * 1e3:0})")));
            foreach (var b in joint.Bars) output.WriteLine($"    стержень {b}");
        }
        output.WriteLine($"  замкнутых контуров ЛИРЫ {closed}, равные отступы у {equal}");
        int allClosed = file.Punching.Count(r => r.IsClosed);
        output.WriteLine($"  записей ЛИРЫ {file.Punching.Count}, замкнутых {allClosed}; не сопоставлено {match.Unmatched.Count}");
        foreach (var r in match.Unmatched.Where(r => r.IsClosed))
            output.WriteLine($"    не сопоставлена: узел {r.Node}, точка ({r.Point.X:0.000}, {r.Point.Y:0.000}, {r.Point.Z:0.000}), u {r.Perimeter * 1e3:0}, " +
                "отрезки " + string.Join(" ", r.Contour.Select(c => $"({c.X1:0.000},{c.Y1:0.000})-({c.X2:0.000},{c.Y2:0.000})")));
        foreach (var (tag, r) in match.ByNode.Where(kv => kv.Value.IsClosed && data.Joints.All(j => j.NodeTag != kv.Key)))
            output.WriteLine($"    сопоставлена узлу {tag} вне найденных");
    }

    // ---------------------------------------------------------------- маски

    void CompareMasks(DatabaseService db, FemSchema schema, FemAnalysis analysis)
    {
        foreach (var fragment in db.FemSchemas.Where(s => s.ParentSchemaId == schema.Id).ToList())
        {
            output.WriteLine($"=== маски фрагмента «{fragment.Tag}»");
            var sw = Stopwatch.StartNew();
            var fromCsfea = StoreyFragmentMaskService.Extract(db, fragment, FragmentMaskSet.SourceCsfeaLinear,
                analysis.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), save: false);
            output.WriteLine($"  из CSfea: {(fromCsfea.HasErrors ? "отказ" : "ok")}, невязка {fromCsfea.BalanceResidual:P2}, {sw.Elapsed.TotalSeconds:0} с");
            Report(fromCsfea);
            sw.Restart();
            var fromLira = StoreyFragmentMaskService.Extract(db, fragment, FragmentMaskSet.SourceLira, "", save: false);
            output.WriteLine($"  из снимка ЛИРЫ: {(fromLira.HasErrors ? "отказ" : "ok")}, невязка {fromLira.BalanceResidual:P2}, {sw.Elapsed.TotalSeconds:0} с");
            Report(fromLira);
            if (fromCsfea.Set == null || fromLira.Set == null) continue;

            // Силы по разрезам на общих ключах: по компонентам силы и момента.
            var lira = fromLira.Set.Items.ToDictionary(i => (i.CutId, i.Key));
            var byKey = new Dictionary<FragmentMaskKey, (List<double> C, List<double> L)>();
            foreach (var item in fromCsfea.Set.Items)
            {
                if (!lira.TryGetValue((item.CutId, item.Key), out var other) || other.Samples.Count != item.Samples.Count) continue;
                if (!byKey.TryGetValue(item.Key, out var acc)) byKey[item.Key] = acc = ([], []);
                for (int s = 0; s < item.Samples.Count; s++)
                    if (item.Samples[s].F is { } a && other.Samples[s].F is { } b)
                    {
                        acc.C.AddRange([a.X, a.Y, a.Z, a.Rx, a.Ry, a.Rz]);
                        acc.L.AddRange([b.X, b.Y, b.Z, b.Rx, b.Ry, b.Rz]);
                    }
            }
            var cases = db.GetFemLoadCases(fragment.Id);
            foreach (var (key, (c, l)) in byKey)
                Fit("силы разрезов " + (cases.FirstOrDefault(x => key == FragmentMaskKey.OfLoadCase(x.Id))?.Tag ?? key.ToString()), c, l);
        }
    }

    void Report(FragmentMaskExtractResult r)
    {
        foreach (var d in r.Diagnostics.Where(d => d.IsError || d.Code is "fragment_mask_balance" or "fragment_mask_summary"
            or "fragment_mask_shear_sign" or "fragment_mask_bar_axes"))
            output.WriteLine($"    [{d.Code}] {d.Message}");
    }

    // ---------------------------------------------------------------- общее

    /// <summary>s = Σc·l / Σl²; невязка ‖c − sign(s)·l‖ / ‖l‖; max |c|, max |l|.</summary>
    void Fit(string what, IReadOnlyList<double> c, IReadOnlyList<double> l)
    {
        double cl = 0, ll = 0;
        for (int i = 0; i < c.Count; i++) { cl += c[i] * l[i]; ll += l[i] * l[i]; }
        double s = ll > 0 ? cl / ll : double.NaN;
        double sg = s < 0 ? -1 : 1, rr = 0;
        for (int i = 0; i < c.Count; i++) rr += (c[i] - sg * l[i]) * (c[i] - sg * l[i]);
        double maxC = c.Count > 0 ? c.Max(Math.Abs) : 0, maxL = l.Count > 0 ? l.Max(Math.Abs) : 0;
        output.WriteLine($"    {what}: n = {c.Count}, s = {s:0.0000}, невязка {(ll > 0 ? Math.Sqrt(rr / ll) : double.NaN):0.0000}, " +
            $"max|CSfea| = {maxC:0.###}, max|снимок| = {maxL:0.###}");
    }

    /// <summary>Журнал исполнителя — в вывод теста.</summary>
    sealed class OutputLog(ITestOutputHelper output) : ILogService
    {
        public System.Collections.ObjectModel.ObservableCollection<LogEntry> LogEntries { get; } = [];
        public void Info(string message) => output.WriteLine("журнал: " + message);
        public void Warning(string message) => output.WriteLine("журнал (!): " + message);
        public void Error(string message) => output.WriteLine("журнал (ошибка): " + message);
    }
}
