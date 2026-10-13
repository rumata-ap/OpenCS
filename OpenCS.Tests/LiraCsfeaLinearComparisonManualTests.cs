using System.Diagnostics;
using System.Text.Json;
using CScore.Fem;
using CScore.Import;
using CSfea.CScoreBridge.Structural;
using Microsoft.Data.Sqlite;
using OpenCS.Services;
using OpenCS.Utilites;
using Xunit;
using Xunit.Abstractions;

namespace OpenCS.Tests;

/// <summary>
/// Ручная сверка линейного расчёта CSfea схемы, импортированной из ЛИРЫ (ГУ и нагрузки из текстового файла), с
/// результатами той же схемы в открытой ЛИРЕ (срез 6 плана 4б ЛИРЫ + 4ж). OPENCS_LIRA_CMP_DB — путь к БД OpenCS с
/// посчитанным линейным результатом CSfea (читается копия); в ЛИРЕ должна быть открыта и посчитана та же схема. Без
/// переменной — выход.
/// <para>
/// По каждому загружению: ΣF нагрузки и ΣR по типам (поля шага CSfea) против ΣR ЛИРЫ (усилия КЭ 57 + отпор C1 пластин,
/// Rz·A); реакции пружин по узлам; отпор C1 по пластинам (у нас p = −C1·n·ū); перемещения всех узлов; усилия пластин
/// (в осях выдачи) и концов стержней. Поля сравниваются масштабом наилучшего приближения s = Σc·l / Σl² (знак и
/// множитель единиц видны сразу) и относительной невязкой ‖c − sign(s)·l‖ / ‖l‖; плюс значения в характерных точках.
/// Единицы вывода — кН, м, мм.
/// </para>
/// <para>
/// Необязательно: OPENCS_LIRA_CMP_CACHE — файл кэша результатов ЛИРЫ (нет — читается из API и записывается; чтение
/// «Мирной» ~7 мин); OPENCS_LIRA_CMP_RERUN=1 — вместо записанного результата линейный CSfea той же постановки с
/// понижающими коэффициентами жёсткости ЛИРЫ (<see cref="RerunWithLiraFactors"/>); OPENCS_LIRA_CMP_DUMP — каталог для
/// CSV перемещений узлов и усилий стержней (CSfea и ЛИРА рядом).
/// </para>
/// <para>
/// Итог 13.10 («Мирная», 8 загружений): ΣR = ΣF ЛИРЫ (C1 — до 0,03 %, пружины КЭ 57 — до сотых кН); без коэффициентов
/// ЛИРЫ осадки CSfea в 2–3 раза меньше; с ними uz — s ≈ 1,00, невязка 3–10 %, отпор C1 — 0,5–2 %, Mx/My пластин — знак
/// обратный, ~9 %. Знаки ЛИРЫ относительно полей шага: отпор Rz и усилия КЭ 57 — обратные; стержни — в начале N = −Nᵢ,
/// Q и M как есть, в конце N = Nⱼ, Q и M с обратным знаком.
/// </para>
/// </summary>
public class LiraCsfeaLinearComparisonManualTests(ITestOutputHelper output)
{
    const double TonToKn = 9.80665;

    [Fact]
    public void CompareWithLira()
    {
        string? dbPath = Environment.GetEnvironmentVariable("OPENCS_LIRA_CMP_DB");
        if (string.IsNullOrWhiteSpace(dbPath)) return;
        string copy = Path.Combine(Path.GetTempPath(), $"opencs-lira-cmp-{Guid.NewGuid():N}.db");
        File.Copy(dbPath, copy);
        try { Run(copy); }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(copy); } catch (IOException) { }
        }
    }

    void Run(string db)
    {
        var sw = Stopwatch.StartNew();
        var m = CsfeaData.Read(db);
        output.WriteLine($"БД: схема «{m.SchemaTitle}», узлов сетки {m.Nodes.Count}, КЭ {m.Elements.Count}, пружин {m.Springs.Count}, " +
            $"загружений {m.Cases.Count}, шагов с полями {m.Steps.Count}; {sw.Elapsed.TotalSeconds:0.0} с");

        var nodeIds = m.Steps.Values.First().NodeIds;
        var shells = m.Steps.Values.First().ShellIds;
        var beams = m.Steps.Values.First().BeamIds;
        var springElems = m.Springs.Select(s => s.LiraElem).Distinct().ToArray();
        string? cache = Environment.GetEnvironmentVariable("OPENCS_LIRA_CMP_CACHE");
        LiraData lr;
        if (cache != null && File.Exists(cache))
        {
            lr = JsonSerializer.Deserialize<LiraData>(File.ReadAllBytes(cache))!;
            output.WriteLine($"ЛИРА: из кэша {cache}");
        }
        else
        {
            lr = ReadLira(m, nodeIds, shells, beams, springElems);
            if (cache != null) File.WriteAllBytes(cache, JsonSerializer.SerializeToUtf8Bytes(lr));
        }
        var forces = lr.Forces;
        var disp = lr.Displacements;

        if (Environment.GetEnvironmentVariable("OPENCS_LIRA_CMP_RERUN") == "1")
        {
            sw.Restart();
            foreach (var (tag, f) in RerunWithLiraFactors(db)) m.Steps[tag] = f;
            output.WriteLine($"CSfea пересчитан с коэффициентами жёсткости ЛИРЫ (PLKE, численные EIy/EIz бруса): {sw.Elapsed.TotalSeconds:0.0} с");
        }

        foreach (var lc in m.Cases)
        {
            if (!m.Steps.TryGetValue(lc.Tag, out var f)) { output.WriteLine($"=== {lc.Tag}: нет поля шага"); continue; }
            output.WriteLine("");
            output.WriteLine($"=== ЗН {lc.LiraNum} «{lc.Tag}»");
            CompareSums(m, f, forces, lc.LiraNum);
            CompareSprings(m, f, forces, lc.LiraNum);
            CompareFoundation(m, f, forces, lc.LiraNum);
            CompareDisplacements(f, disp[lc.LiraNum]);
            CompareShells(m, f, forces, lc.LiraNum);
            CompareBeams(f, forces, lc.LiraNum);
        }
    }

    public sealed class LiraData
    {
        public LiraForces Forces { get; set; } = new();
        public Dictionary<int, double[]> Displacements { get; set; } = new();
    }

    LiraData ReadLira(CsfeaData m, int[] nodeIds, int[] shells, int[] beams, int[] springElems)
    {
        dynamic lira = LiraComConnector.ConnectApplication();
        dynamic doc = lira.ActiveDocument ?? throw new InvalidOperationException("В ЛИРЕ нет открытого документа.");
        string docName = Path.GetFileNameWithoutExtension((string)doc.PathName);
        dynamic mu = lira.MeasurementUnits;
        var units = LiraApiUnits.FromCodes((int)mu.Forces1, (int)mu.Forces2, TonToKn);
        output.WriteLine($"ЛИРА: «{docName}», сила → кН × {units.ForceToKn}, длина усилий → м × {units.ForceLengthToM}");
        var sw = Stopwatch.StartNew();
        var forces = ReadForces(LiraComConnector.CreateResultsAccess(), docName, units, m, shells, beams, springElems);
        output.WriteLine($"ЛИРА усилия: пластин {shells.Length}, стержней {beams.Length}, КЭ 57 {springElems.Length}, " +
            $"{sw.Elapsed.TotalSeconds:0.0} с; сечений у пластин: {string.Join(", ", forces.ShellSections.GroupBy(x => x).Select(g => $"{g.Key}×{g.Count()}"))}");
        // перемещения — после усилий и другим объектом: после запроса перемещений LiraResAPI 2024 отдаёт в усилиях
        // того же объекта мусор (денормализованные float ~1e-39), проверено 13.10
        sw.Restart();
        var disp = ReadDisplacements(LiraComConnector.CreateResultsAccess(), docName, nodeIds, m.Cases.Select(c => c.LiraNum).ToArray());
        output.WriteLine($"ЛИРА перемещения: {nodeIds.Length} узлов, {sw.Elapsed.TotalSeconds:0.0} с");
        return new LiraData { Forces = forces, Displacements = disp };
    }

    // ---------------------------------------------------------------- пересчёт CSfea с коэффициентами ЛИРЫ

    /// <summary>
    /// Линейный CSfea той же постановки по БД, но с понижающими коэффициентами жёсткости ЛИРЫ: у пластин изгиб × PLKE
    /// (мембрана без изменений: E′ = E/√k, h′ = h·√k — точно для изотропной упругой оболочки), у бруса — численные
    /// EIy/EIz ЛИРЫ вместо E·b·h³/12.
    /// </summary>
    Dictionary<string, RcSecantStepFields> RerunWithLiraFactors(string db)
    {
        using var dbs = new DatabaseService(db);
        dbs.LoadAll();
        var schema = dbs.FemSchemas.First();
        var analysis = dbs.GetFemAnalyses(schema.Id).First(a => a.Kind == FemCsfeaRunner.LinearAnalysisKind);
        var input = FemCsfeaInputBuilder.Build(dbs, schema.Id, FemCsfeaSetup.FromAnalysis(analysis));
        var stiff = dbs.GetFemSchemaStiffnesses(schema.Id);
        var props = new LiraFactorsSource(input.Properties!, stiff);
        typeof(FemRcModelInput).GetField("<Properties>k__BackingField",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(input, props);
        var adapted = FemRcModelAdapter.Adapt(input);
        foreach (var d in adapted.Diagnostics.Take(8)) output.WriteLine("  адаптер: " + d.Message);
        output.WriteLine($"  пластин с коэффициентом: {string.Join(", ", props.ShellFactors.GroupBy(x => x).Select(g => $"{g.Key}×{g.Count()}"))}; " +
            $"стержней с численными EIy/EIz: {props.BarsScaled}");
        var run = RcLinearAnalysis.Run(adapted.Model, new RcLinearOptions { ShellForceAngles = adapted.ShellForceAngles });
        return run.Cases.ToDictionary(c => c.Name, c => c.Fields);
    }

    sealed class LiraFactorsSource(IFemElementStiffnessSource inner, IReadOnlyDictionary<int, LiraStiffnessRecord> stiff)
        : IFemElementStiffnessSource
    {
        public List<double> ShellFactors { get; } = new();
        public int BarsScaled { get; private set; }

        LiraStiffnessRecord? Record(FemElement e) => e.StiffnessNum is int n && stiff.TryGetValue(n, out var s) ? s : null;

        public FemShellStiffness? Shell(FemElement e)
        {
            var b = inner.Shell(e);
            if (b == null || Record(e) is not { } s || LiraStiffnessParams.Value(s.Params, "PLKE") is not { } k || !(k > 0) || k == 1)
                return b;
            lock (ShellFactors) ShellFactors.Add(k);
            double r = Math.Sqrt(k);
            return new FemShellStiffness(b.E / r, b.Nu, b.H * r);
        }

        public FemBarStiffness? Bar(FemElement e)
        {
            var b = inner.Bar(e);
            if (b == null || Record(e) is not { } s || LiraStiffnessParams.BarRect(s) is null) return b;
            double? E = LiraStiffnessParams.Value(s.Params, "E"), eiy = LiraStiffnessParams.Value(s.Params, "EIy"),
                eiz = LiraStiffnessParams.Value(s.Params, "EIz"), bb = LiraStiffnessParams.Value(s.Params, "B"),
                hh = LiraStiffnessParams.Value(s.Params, "H");
            if (E is not > 0 || eiy is not > 0 || eiz is not > 0 || bb is not > 0 || hh is not > 0) return b;
            double u = s.SectionUnitM, bm = bb.Value * u, hm = hh.Value * u;
            double ky = eiy.Value / (E.Value * bm * hm * hm * hm / 12), kz = eiz.Value / (E.Value * hm * bm * bm * bm / 12);
            BarsScaled++;
            return b with { Iy = b.Iy * ky, Iz = b.Iz * kz };
        }

        public double? UnitWeight(FemElement e) => inner.UnitWeight(e);
        public double? BarArea(FemElement e) => inner.BarArea(e);
        public bool IsWithoutStiffness(FemElement e) => inner.IsWithoutStiffness(e);
    }

    // ---------------------------------------------------------------- суммы

    void CompareSums(CsfeaData m, RcSecantStepFields f, LiraForces lf, int lc)
    {
        double[] F = f.LoadSum.Select(v => v / 1e3).ToArray();
        double[] S = f.ReactionSums.Select(v => v / 1e3).ToArray();   // связи, пружины, C1, авто — по 3
        var sp = new double[3];
        foreach (var (_, r) in lf.Spring[lc]) for (int k = 0; k < 3; k++) sp[k] += r[k];
        double c1 = 0;
        foreach (var (e, p) in lf.PlateRz[lc]) c1 += p * m.Elements[e].Area;
        output.WriteLine("  | величина, кН | X | Y | Z |");
        output.WriteLine($"  | ΣF CSfea | {F[0]:0.00} | {F[1]:0.00} | {F[2]:0.00} |");
        output.WriteLine($"  | ΣR CSfea связи | {S[0]:0.00} | {S[1]:0.00} | {S[2]:0.00} |");
        output.WriteLine($"  | ΣR CSfea пружины | {S[3]:0.00} | {S[4]:0.00} | {S[5]:0.00} |");
        output.WriteLine($"  | ΣR CSfea C1 | {S[6]:0.00} | {S[7]:0.00} | {S[8]:0.00} |");
        output.WriteLine($"  | ΣR CSfea авто | {S[9]:0.00} | {S[10]:0.00} | {S[11]:0.00} |");
        output.WriteLine($"  | Σ усилий КЭ 57 ЛИРЫ (Rx, Ry, Rz) | {sp[0]:0.00} | {sp[1]:0.00} | {sp[2]:0.00} |");
        output.WriteLine($"  | Σ Rz·A пластин ЛИРЫ | — | — | {c1:0.00} |");
    }

    // ---------------------------------------------------------------- пружины КЭ 57

    void CompareSprings(CsfeaData m, RcSecantStepFields f, LiraForces lf, int lc)
    {
        var rIndex = Index(f.ReactionNodeIds);
        var lira = new Dictionary<int, double[]>();
        foreach (var s in m.Springs)
        {
            if (!lf.Spring[lc].TryGetValue(s.LiraElem, out var r)) continue;
            if (!lira.TryGetValue(s.Node, out var a)) lira[s.Node] = a = new double[3];
            for (int k = 0; k < 3; k++) a[k] += r[k];
        }
        var pairs = new List<(int Node, double[] C, double[] L)>();
        foreach (var (node, l) in lira)
        {
            var c = new double[3];
            if (rIndex.TryGetValue(node, out int i)) for (int k = 0; k < 3; k++) c[k] = f.Reactions[6 * i + k] / 1e3;
            pairs.Add((node, c, l));
        }
        for (int k = 0; k < 2; k++)
            Fit($"пружины R{"XY"[k]} по узлам", pairs.Select(p => p.C[k]), pairs.Select(p => p.L[k]));
        foreach (var p in pairs.OrderByDescending(p => Math.Abs(p.C[0]) + Math.Abs(p.C[1])).Take(3))
            output.WriteLine($"    узел {p.Node}: CSfea Rx, Ry = {p.C[0]:0.000}, {p.C[1]:0.000}; ЛИРА КЭ 57 Rx, Ry = {p.L[0]:0.000}, {p.L[1]:0.000} кН");
    }

    // ---------------------------------------------------------------- отпор C1

    void CompareFoundation(CsfeaData m, RcSecantStepFields f, LiraForces lf, int lc)
    {
        var nIndex = Index(f.NodeIds);
        var pairs = new List<(int E, double C, double L)>();
        foreach (var (e, l) in lf.PlateRz[lc])
        {
            var el = m.Elements[e];
            if (el.C1 <= 0) continue;
            // p = −C1·n·ū, n — нормаль КЭ, повёрнутая вверх; + — сжатие основания (кПа)
            var n = el.Normal(m.Nodes);
            if (n.Z < 0) n = (-n.X, -n.Y, -n.Z);
            double un = 0;
            foreach (int node in el.Nodes)
            {
                int i = nIndex[node];
                un += n.X * f.Displacements[6 * i] + n.Y * f.Displacements[6 * i + 1] + n.Z * f.Displacements[6 * i + 2];
            }
            un /= el.Nodes.Length;
            pairs.Add((e, -el.C1 * un / 1e3, l));
        }
        Fit("отпор C1 по пластинам, кПа", pairs.Select(p => p.C), pairs.Select(p => p.L));
        foreach (var p in pairs.OrderByDescending(p => Math.Abs(p.C)).Take(3))
            output.WriteLine($"    КЭ {p.E}: CSfea p = {p.C:0.000}; ЛИРА Rz = {p.L:0.000} кПа");
    }

    // ---------------------------------------------------------------- перемещения

    void CompareDisplacements(RcSecantStepFields f, double[] lira)
    {
        string[] names = ["ux", "uy", "uz", "rx", "ry", "rz"];
        int n = f.NodeIds.Length;
        if (Environment.GetEnvironmentVariable("OPENCS_LIRA_CMP_DUMP") is { } dump)
            File.WriteAllLines(Path.Combine(dump, $"nodes_{f.Stage}.csv"), Enumerable.Range(0, n).Select(i =>
                $"{f.NodeIds[i]};" + string.Join(";", Enumerable.Range(0, 6).Select(k => f.Displacements[6 * i + k].ToString("R", System.Globalization.CultureInfo.InvariantCulture))) + ";" +
                string.Join(";", Enumerable.Range(0, 6).Select(k => lira[6 * i + k].ToString("R", System.Globalization.CultureInfo.InvariantCulture)))));
        for (int k = 0; k < 6; k++)
        {
            double scale = k < 3 ? 1e3 : 1e3;   // мм, мрад
            Fit($"{names[k]} всех узлов, {(k < 3 ? "мм" : "мрад")}",
                Enumerable.Range(0, n).Select(i => f.Displacements[6 * i + k] * scale),
                Enumerable.Range(0, n).Select(i => lira[6 * i + k] * scale));
        }
        int iMinC = Enumerable.Range(0, n).MinBy(i => f.Displacements[6 * i + 2]);
        int iMinL = Enumerable.Range(0, n).MinBy(i => lira[6 * i + 2]);
        output.WriteLine($"    наибольшая осадка: CSfea {f.Displacements[6 * iMinC + 2] * 1e3:0.000} мм (узел {f.NodeIds[iMinC]}, у ЛИРЫ там " +
            $"{lira[6 * iMinC + 2] * 1e3:0.000}); ЛИРА {lira[6 * iMinL + 2] * 1e3:0.000} мм (узел {f.NodeIds[iMinL]}, у CSfea там " +
            $"{f.Displacements[6 * iMinL + 2] * 1e3:0.000})");
        for (int k = 0; k < 2; k++)
        {
            int i = Enumerable.Range(0, n).MaxBy(j => Math.Abs(f.Displacements[6 * j + k]));
            output.WriteLine($"    max |{names[k]}|: узел {f.NodeIds[i]}, CSfea {f.Displacements[6 * i + k] * 1e3:0.000} мм, ЛИРА {lira[6 * i + k] * 1e3:0.000} мм");
        }
    }

    // ---------------------------------------------------------------- пластины

    void CompareShells(CsfeaData m, RcSecantStepFields f, LiraForces lf, int lc)
    {
        string[] names = ["Nx", "Ny", "Nxy", "Mx", "My", "Mxy", "Qx", "Qy"];
        var l = lf.Shell[lc];
        int ns = f.ShellIds.Length, nc = RcSecantStepFields.ShellForceComponents;
        for (int k = 0; k < nc; k++)
            Fit($"пластины {names[k]}", Enumerable.Range(0, ns).Select(i => f.ShellForces[nc * i + k] / 1e3),
                Enumerable.Range(0, ns).Select(i => l[nc * i + k]));
        foreach (int k in new[] { 3, 0 })
        {
            int i = Enumerable.Range(0, ns).MaxBy(j => Math.Abs(f.ShellForces[nc * j + k]));
            output.WriteLine($"    КЭ {f.ShellIds[i]} (max |{names[k]}|): CSfea " +
                string.Join(" ", Enumerable.Range(0, nc).Select(c => $"{f.ShellForces[nc * i + c] / 1e3:0.00}")) + " | ЛИРА " +
                string.Join(" ", Enumerable.Range(0, nc).Select(c => $"{l[nc * i + c]:0.00}")));
        }
    }

    // ---------------------------------------------------------------- стержни

    void CompareBeams(RcSecantStepFields f, LiraForces lf, int lc)
    {
        string[] names = ["N", "Qy", "Qz", "Mx", "My", "Mz"];
        var l = lf.Beam[lc];
        int nb = f.BeamIds.Length;
        if (Environment.GetEnvironmentVariable("OPENCS_LIRA_CMP_DUMP") is { } dump)
            File.WriteAllLines(Path.Combine(dump, $"beams_lc{lc}.csv"), Enumerable.Range(0, nb).Select(i =>
                $"{f.BeamIds[i]};" + string.Join(";", Enumerable.Range(0, 12).Select(k => (f.BeamForces[12 * i + k] / 1e3).ToString("R", System.Globalization.CultureInfo.InvariantCulture))) + ";" +
                string.Join(";", Enumerable.Range(0, 12).Select(k => l[12 * i + k].ToString("R", System.Globalization.CultureInfo.InvariantCulture)))));
        for (int k = 0; k < 12; k++)
            Fit($"стержни {names[k % 6]}{(k < 6 ? "i" : "j")}", Enumerable.Range(0, nb).Select(i => f.BeamForces[12 * i + k] / 1e3),
                Enumerable.Range(0, nb).Select(i => l[12 * i + k]));
        int b = Enumerable.Range(0, nb).MaxBy(j => Math.Abs(f.BeamForces[12 * j]));
        output.WriteLine($"    стержень {f.BeamIds[b]} (max |N|): CSfea " +
            string.Join(" ", Enumerable.Range(0, 12).Select(c => $"{f.BeamForces[12 * b + c] / 1e3:0.00}")) + " | ЛИРА (сеч. 1, посл.) " +
            string.Join(" ", Enumerable.Range(0, 12).Select(c => $"{l[12 * b + c]:0.00}")));
    }

    // ---------------------------------------------------------------- общее

    /// <summary>s = Σc·l / Σl²; невязка ‖c − sign(s)·l‖ / ‖l‖; max |c|, max |l|.</summary>
    void Fit(string what, IEnumerable<double> cs, IEnumerable<double> ls)
    {
        var c = cs.ToArray();
        var l = ls.ToArray();
        double cl = 0, ll = 0, cc = 0;
        for (int i = 0; i < c.Length; i++) { cl += c[i] * l[i]; ll += l[i] * l[i]; cc += c[i] * c[i]; }
        double s = ll > 0 ? cl / ll : double.NaN;
        double sg = s < 0 ? -1 : 1, rr = 0;
        for (int i = 0; i < c.Length; i++) rr += (c[i] - sg * l[i]) * (c[i] - sg * l[i]);
        double maxC = c.Length > 0 ? c.Max(Math.Abs) : 0, maxL = l.Length > 0 ? l.Max(Math.Abs) : 0;
        output.WriteLine($"  {what}: n = {c.Length}, s = {s:0.0000}, невязка {(ll > 0 ? Math.Sqrt(rr / ll) : double.NaN):0.0000}, " +
            $"max|CSfea| = {maxC:0.###}, max|ЛИРА| = {maxL:0.###}");
    }

    static Dictionary<int, int> Index(int[] ids)
    {
        var d = new Dictionary<int, int>(ids.Length);
        for (int i = 0; i < ids.Length; i++) d[ids[i]] = i;
        return d;
    }

    /// <summary>Номера отрезками «a-b, c, d-e»: длинный перечень через запятую ЛИРА молча обрезает (неспрошенные — мусор).</summary>
    static string Range(IEnumerable<int> ids)
    {
        var s = ids.Distinct().Order().ToArray();
        var parts = new List<string>();
        for (int i = 0; i < s.Length;)
        {
            int j = i;
            while (j + 1 < s.Length && s[j + 1] == s[j] + 1) j++;
            parts.Add(j > i ? $"{s[i]}-{s[j]}" : $"{s[i]}");
            i = j + 1;
        }
        return string.Join(", ", parts);
    }

    // ---------------------------------------------------------------- ЛИРА

    /// <summary>Перемещения узлов по загружениям: 6 на узел (м, рад), порядок <paramref name="nodes"/>.</summary>
    static Dictionary<int, double[]> ReadDisplacements(LiraResultsApi api, string docName, int[] nodes, int[] cases)
    {
        dynamic req = api.CreateRequest("kLiraRequest_LoadCaseDisplacement");
        req.DocumentName = docName;
        req.Nodes.AddFromString(Range(nodes));
        dynamic resp = api.Access.LoadCaseDisplacements(req);
        var result = new Dictionary<int, double[]>();
        foreach (int lc in cases)
        {
            var a = new double[6 * nodes.Length];
            for (int i = 0; i < nodes.Length; i++)
            {
                int n = nodes[i];
                a[6 * i] = resp.GetNodeX(n, lc); a[6 * i + 1] = resp.GetNodeY(n, lc); a[6 * i + 2] = resp.GetNodeZ(n, lc);
                a[6 * i + 3] = resp.GetNodeUX(n, lc); a[6 * i + 4] = resp.GetNodeUY(n, lc); a[6 * i + 5] = resp.GetNodeUZ(n, lc);
            }
            result[lc] = a;
        }
        return result;
    }

    public sealed class LiraForces
    {
        /// <summary>КЭ 57: усилия Rx, Ry, Rz, кН.</summary>
        public Dictionary<int, Dictionary<int, double[]>> Spring { get; set; } = new();
        /// <summary>Отпор Rz пластин с C1, кПа.</summary>
        public Dictionary<int, Dictionary<int, double>> PlateRz { get; set; } = new();
        /// <summary>Пластины: Nx·h, Ny·h, Txy·h, Mx, My, Mxy, Qx, Qy (кН/м, кН·м/м), порядок полей шага, сечение 1.</summary>
        public Dictionary<int, double[]> Shell { get; set; } = new();
        /// <summary>Стержни: N Qy Qz Mx My Mz первого и последнего сечения, кН, кН·м.</summary>
        public Dictionary<int, double[]> Beam { get; set; } = new();
        public List<int> ShellSections { get; set; } = new();
    }

    static LiraForces ReadForces(LiraResultsApi api, string docName, LiraApiUnits u, CsfeaData m, int[] shells, int[] beams, int[] springs)
    {
        dynamic req = api.CreateRequest("kLiraRequest_LoadCaseForces");
        req.DocumentName = docName;
        req.Elements.AddFromString(Range(shells.Concat(beams).Concat(springs).Distinct()));
        dynamic resp = api.Access.LoadCaseForces(req);
        var r = new LiraForces();
        foreach (int e in shells) r.ShellSections.Add((int)resp.GetSectionCount(e));
        var beamSections = beams.Select(e => (int)resp.GetSectionCount(e)).ToArray();
        foreach (int lc in m.Cases.Select(c => c.LiraNum))
        {
            var sp = r.Spring[lc] = new Dictionary<int, double[]>();
            foreach (int e in springs)
                sp[e] = [u.Force(resp.GetSpecElementRx(e, 1, lc)), u.Force(resp.GetSpecElementRy(e, 1, lc)), u.Force(resp.GetSpecElementRz(e, 1, lc))];
            var rz = r.PlateRz[lc] = new Dictionary<int, double>();
            var sh = r.Shell[lc] = new double[8 * shells.Length];
            for (int i = 0; i < shells.Length; i++)
            {
                int e = shells[i];
                double h = m.Elements[e].Thickness;
                sh[8 * i] = u.Stress(resp.GetPlateNx(e, 1, lc)) * h;
                sh[8 * i + 1] = u.Stress(resp.GetPlateNy(e, 1, lc)) * h;
                sh[8 * i + 2] = u.Stress(resp.GetPlateTxy(e, 1, lc)) * h;
                sh[8 * i + 3] = u.MomentPerLength(resp.GetPlateMx(e, 1, lc));
                sh[8 * i + 4] = u.MomentPerLength(resp.GetPlateMy(e, 1, lc));
                sh[8 * i + 5] = u.MomentPerLength(resp.GetPlateMxy(e, 1, lc));
                sh[8 * i + 6] = u.PerLength(resp.GetPlateQx(e, 1, lc));
                sh[8 * i + 7] = u.PerLength(resp.GetPlateQy(e, 1, lc));
                if (m.Elements[e].C1 > 0) rz[e] = u.Stress(resp.GetPlateRz(e, 1, lc));
            }
            var bm = r.Beam[lc] = new double[12 * beams.Length];
            for (int i = 0; i < beams.Length; i++)
            {
                int e = beams[i];
                foreach (var (sec, o) in new[] { (1, 0), (beamSections[i], 6) })
                {
                    bm[12 * i + o] = u.Force(resp.GetBarN(e, sec, lc));
                    bm[12 * i + o + 1] = u.Force(resp.GetBarQy(e, sec, lc));
                    bm[12 * i + o + 2] = u.Force(resp.GetBarQz(e, sec, lc));
                    bm[12 * i + o + 3] = u.Moment(resp.GetBarMx(e, sec, lc));
                    bm[12 * i + o + 4] = u.Moment(resp.GetBarMy(e, sec, lc));
                    bm[12 * i + o + 5] = u.Moment(resp.GetBarMz(e, sec, lc));
                }
            }
        }
        return r;
    }

    // ---------------------------------------------------------------- БД

    sealed record CaseRow(int Id, string Tag, int LiraNum);
    sealed record SpringRow(int Node, int LiraElem);

    sealed class ElementRow
    {
        public required int[] Nodes { get; init; }
        public double Thickness { get; init; }
        public double C1 { get; init; }
        public double Area { get; set; }

        /// <summary>Нормаль по узлам 1, 2, 3 порядка хранения.</summary>
        public (double X, double Y, double Z) Normal(Dictionary<int, (double X, double Y, double Z)> p)
        {
            var (a, b, c) = (p[Nodes[0]], p[Nodes[1]], p[Nodes[2]]);
            var (u, v) = ((b.X - a.X, b.Y - a.Y, b.Z - a.Z), (c.X - a.X, c.Y - a.Y, c.Z - a.Z));
            var n = (u.Item2 * v.Item3 - u.Item3 * v.Item2, u.Item3 * v.Item1 - u.Item1 * v.Item3, u.Item1 * v.Item2 - u.Item2 * v.Item1);
            double len = Math.Sqrt(n.Item1 * n.Item1 + n.Item2 * n.Item2 + n.Item3 * n.Item3);
            return (n.Item1 / len, n.Item2 / len, n.Item3 / len);
        }
    }

    sealed class CsfeaData
    {
        public string SchemaTitle = "";
        public Dictionary<int, (double X, double Y, double Z)> Nodes { get; } = new();
        public Dictionary<int, ElementRow> Elements { get; } = new();
        public List<SpringRow> Springs { get; } = new();
        public List<CaseRow> Cases { get; } = new();
        /// <summary>Поля шага по имени загружения (стадии линейного результата).</summary>
        public Dictionary<string, RcSecantStepFields> Steps { get; } = new();

        public static CsfeaData Read(string path)
        {
            var d = new CsfeaData();
            using var c = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
            c.Open();
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "SELECT id, tag FROM fem_schemas ORDER BY id LIMIT 1";
                using var r = cmd.ExecuteReader();
                r.Read();
                d.SchemaTitle = r.GetString(1);
            }
            foreach (var r in Rows(c, "SELECT node_tag, x, y, z FROM fem_mesh_nodes"))
                d.Nodes[int.Parse(r.GetString(0))] = (r.GetDouble(1), r.GetDouble(2), r.GetDouble(3));
            foreach (var r in Rows(c, "SELECT elem_tag, node_ids_json, thickness_m, foundation_c1 FROM fem_elements"))
            {
                var e = new ElementRow
                {
                    Nodes = JsonSerializer.Deserialize<int[]>(r.GetString(1))!,
                    Thickness = r.IsDBNull(2) ? 0 : r.GetDouble(2),
                    C1 = r.IsDBNull(3) ? 0 : r.GetDouble(3),
                };
                if (e.Nodes.Length >= 3)
                {
                    // площадь: у Q4 узлы хранятся «1 2 4 3» — диагонали 1–4 и 2–3 порядка хранения
                    var p = e.Nodes.Select(n => d.Nodes[n]).ToArray();
                    var (a, b) = e.Nodes.Length == 4
                        ? ((p[3].X - p[0].X, p[3].Y - p[0].Y, p[3].Z - p[0].Z), (p[2].X - p[1].X, p[2].Y - p[1].Y, p[2].Z - p[1].Z))
                        : ((p[1].X - p[0].X, p[1].Y - p[0].Y, p[1].Z - p[0].Z), (p[2].X - p[0].X, p[2].Y - p[0].Y, p[2].Z - p[0].Z));
                    double cx = a.Item2 * b.Item3 - a.Item3 * b.Item2, cy = a.Item3 * b.Item1 - a.Item1 * b.Item3,
                        cz = a.Item1 * b.Item2 - a.Item2 * b.Item1;
                    e.Area = 0.5 * Math.Sqrt(cx * cx + cy * cy + cz * cz);
                }
                d.Elements[int.Parse(r.GetString(0))] = e;
            }
            foreach (var r in Rows(c, "SELECT node_tag, source_elem_tag FROM fem_springs WHERE target_kind = 'mesh_node'"))
                d.Springs.Add(new SpringRow(int.Parse(r.GetString(0)), int.Parse(r.GetString(1))));
            foreach (var r in Rows(c, "SELECT id, tag, source_load_num FROM fem_load_cases ORDER BY id"))
                d.Cases.Add(new CaseRow(r.GetInt32(0), r.GetString(1), r.GetInt32(2)));

            int resultId = 0;
            JsonElement summary = default;
            foreach (var r in Rows(c, "SELECT id, data_json FROM calc_results WHERE task_kind = 'fem_csfea_secant' ORDER BY id DESC"))
            {
                var j = JsonDocument.Parse(r.GetString(1)).RootElement;
                if (j.GetProperty("Kind").GetString() != "csfea_linear") continue;
                resultId = r.GetInt32(0);
                summary = j.Clone();
                break;
            }
            if (resultId == 0) throw new InvalidOperationException("В БД нет линейного результата CSfea.");
            var stages = summary.GetProperty("Stages").EnumerateArray().Select(s => s.GetProperty("Tag").GetString()!).ToArray();
            foreach (var s in summary.GetProperty("Steps").EnumerateArray())
            {
                int n = s.GetProperty("N").GetInt32(), stage = s.GetProperty("Stage").GetInt32();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT data FROM fem_result_steps WHERE result_id = @r AND step_n = @n";
                cmd.Parameters.AddWithValue("@r", resultId);
                cmd.Parameters.AddWithValue("@n", n);
                if (cmd.ExecuteScalar() is byte[] data) d.Steps[stages[stage]] = FemCsfeaStepFieldsCodec.Unpack(data);
            }
            return d;
        }

        static IEnumerable<SqliteDataReader> Rows(SqliteConnection c, string sql)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            using var r = cmd.ExecuteReader();
            while (r.Read()) yield return r;
        }
    }
}
