using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CScore;
using CScore.Fem;
using CSfea.CScoreBridge.Structural;
using OpenCS.Tasks;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>
/// Расчётная длина элемента по оси СП 16 в сводке результата: μ и N_cr (Н; null — не назначены), источник
/// (<see cref="RcBucklingLengthSource"/>; пусто — длины не назначены: сочетание неустойчиво или нет форм), номер формы
/// (с 1) и её λ, пояснение и ход вычисления для отчёта.
/// </summary>
public sealed class FemBucklingAxisSummary
{
    public double? Mu { get; set; }
    public double? Ncr { get; set; }
    public string Source { get; set; } = "";
    public int? Mode { get; set; }
    public double? Lambda { get; set; }
    public string Note { get; set; } = "";
    public List<string> Details { get; set; } = [];
}

/// <summary>
/// Расчётный элемент (КонЭ или цепочка КЭ) в сводке: КЭ схемы, длина l, м, определяющее сочетание и наибольшее
/// сжатие N, Н (минус), оси СП 16.
/// </summary>
public sealed class FemBucklingUnitSummary
{
    public string Label { get; set; } = "";
    public List<string> ElemTags { get; set; } = [];
    public double Length { get; set; }
    public double N { get; set; }
    public string Combination { get; set; } = "";
    public FemBucklingAxisSummary X { get; set; } = new();
    public FemBucklingAxisSummary Y { get; set; } = new();

    /// <summary>КЗУ по оси: N_cr/|N|; null — μ не назначен.</summary>
    public double? Kzu(FemBucklingAxisSummary axis) => axis.Ncr is double ncr && N < 0 ? ncr / -N : null;

    /// <summary>КЗУ элемента — наименьший по осям.</summary>
    public double? Kzu() => new[] { Kzu(X), Kzu(Y) }.Where(k => k != null).Min();
}

/// <summary>Задача устойчивости по сочетанию: λ форм по возрастанию, номер первой записи формы (с 1).</summary>
public sealed class FemBucklingCaseSummary
{
    public string Tag { get; set; } = "";
    public double[] Lambda { get; set; } = [];
    public bool Converged { get; set; }
    public bool Unstable { get; set; }
    public int Units { get; set; }
    public int FirstRecord { get; set; }
}

/// <summary>
/// Сводка постановки «Расчётные длины — CSfea» — DataJson <see cref="CalcResult"/> вида
/// <see cref="FemBucklingRunner.TaskKind"/>; формы — записями <c>fem_result_steps</c> (<see cref="FemBucklingModeCodec"/>,
/// номера — <see cref="FemBucklingCaseSummary.FirstRecord"/> + индекс формы). <see cref="Fingerprint"/> — отпечаток
/// входа задачи (<see cref="FemBucklingFingerprint"/>): по нему проверка стали решает, годится ли результат.
/// </summary>
public sealed class FemBucklingSummary
{
    public string Fingerprint { get; set; } = "";
    public int Modes { get; set; }
    public double ElapsedSeconds { get; set; }
    public List<string> Report { get; set; } = [];
    public List<string> Errors { get; set; } = [];
    public List<FemBucklingCaseSummary> Cases { get; set; } = [];
    public List<FemBucklingUnitSummary> Units { get; set; } = [];

    static readonly JsonSerializerOptions Options = new() { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static FemBucklingSummary? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<FemBucklingSummary>(json, Options); }
        catch (JsonException) { return null; }
    }

    /// <summary>Строки итога для журнала: задачи по сочетаниям, затем отчёт.</summary>
    public IEnumerable<string> Describe()
    {
        var ci = CultureInfo.CurrentCulture;
        foreach (var c in Cases)
            yield return string.Format(ci, Loc.S("FemBucklingCaseLine"), c.Tag, c.Units,
                c.Lambda.Length > 0 ? c.Lambda[0].ToString("0.###", ci) : "—", c.Lambda.Length)
                + (c.Converged ? "" : " " + Loc.S("FemBucklingNotConverged"))
                + (c.Unstable ? " " + Loc.S("FemBucklingUnstable") : "");
        foreach (var line in Report) yield return line;
    }
}

/// <summary>
/// Форма потери устойчивости на сетке схемы: перемещения узлов (номер узла сетки → ux, uy, uz) и промежуточных точек
/// дробления стержней (номер КЭ → точки по порядку от I к J, равномерно по длине), нормированные на max |u| = 1.
/// </summary>
public sealed record FemBucklingMode(IReadOnlyDictionary<int, (double X, double Y, double Z)> Nodes,
    IReadOnlyDictionary<int, (double X, double Y, double Z)[]> BarPoints);

/// <summary>Двоичная запись формы (<see cref="FemBucklingMode"/>): float, little-endian.</summary>
public static class FemBucklingModeCodec
{
    public static byte[] Encode(FemBucklingMode mode)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms))
        {
            w.Write(mode.Nodes.Count);
            foreach (var (id, u) in mode.Nodes)
            {
                w.Write(id);
                Write(w, u);
            }
            w.Write(mode.BarPoints.Count);
            foreach (var (id, points) in mode.BarPoints)
            {
                w.Write(id);
                w.Write(points.Length);
                foreach (var u in points) Write(w, u);
            }
        }
        return ms.ToArray();
    }

    public static FemBucklingMode Decode(byte[] data)
    {
        using var r = new BinaryReader(new MemoryStream(data));
        int n = r.ReadInt32();
        var nodes = new Dictionary<int, (double, double, double)>(n);
        for (int i = 0; i < n; i++) nodes[r.ReadInt32()] = Read(r);
        int b = r.ReadInt32();
        var bars = new Dictionary<int, (double, double, double)[]>(b);
        for (int i = 0; i < b; i++)
        {
            int id = r.ReadInt32();
            var points = new (double, double, double)[r.ReadInt32()];
            for (int k = 0; k < points.Length; k++) points[k] = Read(r);
            bars[id] = points;
        }
        return new FemBucklingMode(nodes, bars);
    }

    static void Write(BinaryWriter w, (double X, double Y, double Z) u)
    {
        w.Write((float)u.X);
        w.Write((float)u.Y);
        w.Write((float)u.Z);
    }

    static (double, double, double) Read(BinaryReader r) => (r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
}

/// <summary>
/// Отпечаток входа задачи устойчивости: модель адаптера (узлы, стержни с упругими характеристиками и шарнирами,
/// пластины, жёсткие тела, опоры, пружины, загружения), расчётные элементы, сочетания и параметры расчёта. Совпадение —
/// сохранённый результат постановки годится для проверки стали без пересчёта.
/// </summary>
public static class FemBucklingFingerprint
{
    public static string Compute(FemSteelBucklingInput input, RcBucklingOptions options)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(options);
        var m = input.Adapted?.Model ?? throw new InvalidOperationException("Вход расчёта устойчивости без модели.");
        var sb = new StringBuilder();
        void Num(double v) => sb.Append(v.ToString("R", CultureInfo.InvariantCulture)).Append(';');
        void Nums(IEnumerable<double>? v) { foreach (double x in v ?? []) Num(x); sb.Append('|'); }

        sb.Append("modes=").Append(options.Modes).Append(";sub=").Append(options.MinSubdivisions).Append('\n');
        foreach (var n in m.Nodes) { sb.Append('n').Append(n.Id).Append(':'); Num(n.X); Num(n.Y); Num(n.Z); sb.Append('\n'); }
        foreach (var b in m.Beams)
        {
            sb.Append('b').Append(b.Id).Append(':').Append(b.NodeI).Append(',').Append(b.NodeJ).Append(',')
                .Append(b.ReleaseI).Append(',').Append(b.ReleaseJ).Append(',').Append(b.Section.Key).Append(',')
                .Append(b.Section.CrossYAlongLocalY).Append(':');
            Nums(b.RefVec);
            if (b.Section.Elastic is { } e) Nums([e.E, e.A, e.Iy, e.Iz, e.G, e.J]);
            Num(b.Section.TorsionGJ);
            sb.Append('\n');
        }
        foreach (var s in m.Shells)
        {
            sb.Append('s').Append(s.Id).Append(':').Append(string.Join(',', s.NodeIds)).Append(',').Append(s.Section.Key).Append(':');
            Nums(s.SectionAxisX);
            Num(s.FoundationC1 ?? 0);
            if (s.Section.Elastic is { } lam)
            {
                var abd = lam.ABDAs();
                foreach (var a in new[] { abd.A, abd.B, abd.D, abd.As }) Nums(a.Cast<double>());
            }
            sb.Append('\n');
        }
        foreach (var r in m.RigidBodies)
            sb.Append('r').Append(r.Id).Append(':').Append(r.Master).Append(',').Append(string.Join(',', r.Slaves)).Append(',')
                .Append(r.Mask).Append(',').Append(r.EqualDofs).Append('\n');
        foreach (var s in m.Supports) sb.Append('u').Append(s.NodeId).Append(':').Append(s.Mask).Append('\n');
        foreach (var s in m.Springs) { sb.Append('k').Append(s.NodeId).Append(':').Append(s.Dof).Append(','); Num(s.Stiffness); sb.Append('\n'); }
        foreach (var lc in m.LoadCases)
        {
            sb.Append("lc").Append(lc.Id).Append('\n');
            foreach (var l in lc.Nodal) { sb.Append(l.NodeId).Append(':'); Nums(l.Force); }
            foreach (var l in lc.Shells) { sb.Append(l.ShellId).Append(':'); Num(l.Pressure); Nums(l.Direction); }
            foreach (var l in lc.Beams) { sb.Append(l.BeamId).Append(':'); Nums(l.Force); }
            foreach (var l in lc.BeamEnds) { sb.Append(l.BeamId).Append(':'); Nums(l.Forces); }
            sb.Append('\n');
        }
        foreach (var u in input.UnitBeams) sb.Append("e:").Append(string.Join(',', u)).Append('\n');
        foreach (var c in input.Combinations)
        {
            sb.Append("c:").Append(c.Tag).Append(':');
            foreach (var (lc, f) in c.Terms) { sb.Append(lc).Append('*'); Num(f); }
            sb.Append('\n');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }
}

/// <summary>
/// Постановка «Расчётные длины — CSfea» (спека 2026-10-18, срез 5): стадии — сочетания, расчёт устойчивости
/// <see cref="FemSteelBuckling"/> с сохранением сводки (μ, КЗУ, пояснения) и форм для 3D-вида и проверки стали.
/// </summary>
public static class FemBucklingRunner
{
    public const string AnalysisKind = "csfea_buckling";
    public const string TaskKind = "fem_csfea_buckling";

    /// <summary>Параметры расчёта по постановке.</summary>
    public static RcBucklingOptions Options(FemAnalysis analysis) =>
        new(Modes: Math.Max(1, FemAnalysisParams.Parse(analysis.ParamsJson).Buckling?.Modes ?? 20));

    /// <summary>
    /// Сохранённый результат постановки, годный для входа <paramref name="input"/> (отпечаток совпал); null — результата
    /// нет (<paramref name="saved"/> = null) или он устарел.
    /// </summary>
    public static FemBucklingSummary? Current(DatabaseService db, FemAnalysis analysis, FemSteelBucklingInput input, out CalcResult? saved)
    {
        saved = analysis.ResultId is int rid ? db.GetCalcResultById(rid) : null;
        if (saved is not { TaskKind: TaskKind, Status: "ok" or "not_converged" }) saved = null;
        return saved != null && FemBucklingSummary.Parse(saved.DataJson) is { } summary
            && summary.Fingerprint == FemBucklingFingerprint.Compute(input, Options(analysis)) ? summary : null;
    }

    /// <summary>Сетка схемы (устаревшая — перестраивается), вход и расчёт; результат и формы записываются в БД.</summary>
    public static async Task<CalcResult> RunAsync(FemCsfeaRunContext ctx, FemSchema schema, FemAnalysis analysis, CancellationToken ct)
    {
        string created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var diag = new List<FemValidationDiagnostic>();
        await FemCsfeaRunner.EnsureMeshAsync(ctx, schema, true, diag, ct);
        if (diag.Any(d => d.IsError))
            return Error(analysis, created, diag.Where(d => d.IsError).Select(d => d.Message));
        var input = FemSteelBuckling.Prepare(ctx.Db, schema.Id, analysis);
        input.Diagnostics.InsertRange(0, diag);
        return await SolveAsync(ctx.Db, analysis, input, created, ct);
    }

    /// <summary>
    /// Расчёт по готовому входу (UI-поток: запись в БД), результат и формы — в БД; постановке результат не
    /// присваивается (это делает вызывающий).
    /// </summary>
    public static async Task<CalcResult> SolveAsync(DatabaseService db, FemAnalysis analysis, FemSteelBucklingInput input,
        string created, CancellationToken ct)
    {
        if (input.HasErrors)
            return Error(analysis, created, input.Report.Where(l => l.StartsWith("Ошибка: ", StringComparison.Ordinal)), input.Report);
        var options = Options(analysis);
        var clock = Stopwatch.StartNew();
        var (summary, records) = await Task.Run(() =>
        {
            var r = FemSteelBuckling.Run(input, options, ct);
            var s = new FemBucklingSummary
            {
                Fingerprint = FemBucklingFingerprint.Compute(input, options), Modes = options.Modes,
                Report = input.Report.Concat(r.Report).ToList(), Units = FemSteelBuckling.Summarize(input, r),
            };
            var recs = new List<(int, byte[])>();
            foreach (var c in r.Cases)
            {
                ct.ThrowIfCancellationRequested();
                s.Cases.Add(new FemBucklingCaseSummary
                {
                    Tag = c.Tag, Lambda = c.Result.Lambda, Converged = c.Result.Converged, Unstable = c.Unstable,
                    Units = c.Units.Count, FirstRecord = recs.Count + 1,
                });
                foreach (var mode in c.Result.Modes)
                    recs.Add((recs.Count + 1, FemBucklingModeCodec.Encode(Mode(input, c.Result, mode))));
            }
            return (s, recs);
        }, ct);
        clock.Stop();
        summary.ElapsedSeconds = clock.Elapsed.TotalSeconds;
        var result = new CalcResult
        {
            TaskId = 0, TaskKind = TaskKind, TaskTag = analysis.Tag, Created = created,
            Status = summary.Cases.All(c => c.Converged) ? "ok" : "not_converged", DataJson = summary.ToJson(),
        };
        db.SaveCalcResult(result);
        db.SaveFemResultSteps(result.Id, records);
        return result;
    }

    /// <summary>
    /// Форма на сетке схемы: узлы модели адаптера (номера — номера узлов сетки) и промежуточные узлы дробления
    /// стержней; нормирование на наибольшее поступательное перемещение.
    /// </summary>
    public static FemBucklingMode Mode(FemSteelBucklingInput input, RcBucklingResult result, double[] mode)
    {
        var build = result.Build;
        (double, double, double) U(int index) => (mode[6 * index], mode[6 * index + 1], mode[6 * index + 2]);
        var nodes = new Dictionary<int, (double X, double Y, double Z)>();
        foreach (var n in input.Adapted!.Model.Nodes)
            if (build.NodeIndex.TryGetValue(n.Id, out int i)) nodes[n.Id] = U(i);
        var bars = new Dictionary<int, (double X, double Y, double Z)[]>();
        foreach (var (beam, subs) in result.SubBeams)
        {
            if (subs.Length < 2) continue;
            bars[beam] = subs.Take(subs.Length - 1).Select(s => U(build.Mesh.Beams[s].J)).ToArray();
        }
        double max = nodes.Values.Concat(bars.Values.SelectMany(p => p))
            .Select(u => Math.Sqrt(u.X * u.X + u.Y * u.Y + u.Z * u.Z)).DefaultIfEmpty(0).Max();
        if (max <= 0) return new FemBucklingMode(nodes, bars);
        static (double, double, double) Scale((double X, double Y, double Z) u, double k) => (u.X * k, u.Y * k, u.Z * k);
        return new FemBucklingMode(nodes.ToDictionary(p => p.Key, p => Scale(p.Value, 1 / max)),
            bars.ToDictionary(p => p.Key, p => p.Value.Select(u => Scale(u, 1 / max)).ToArray()));
    }

    static CalcResult Error(FemAnalysis analysis, string created, IEnumerable<string> errors, IEnumerable<string>? report = null) => new()
    {
        TaskId = 0, TaskKind = TaskKind, TaskTag = analysis.Tag, Created = created, Status = "error",
        DataJson = new FemBucklingSummary { Errors = errors.ToList(), Report = report?.ToList() ?? [] }.ToJson(),
    };
}
