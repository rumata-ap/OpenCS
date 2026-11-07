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

    /// <summary>Номограммный метод: плоскость со смещением (true) или без; null — μ по формам.</summary>
    public bool? Sway { get; set; }
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

/// <summary>
/// Задача устойчивости по сочетанию или по огибающей сжатия (<see cref="Envelope"/>): λ форм по возрастанию, номер первой
/// записи формы (с 1).
/// </summary>
public sealed class FemBucklingCaseSummary
{
    public string Tag { get; set; } = "";
    public bool Envelope { get; set; }
    public double[] Lambda { get; set; } = [];
    public bool Converged { get; set; }
    public bool Unstable { get; set; }
    public int Units { get; set; }
    public int FirstRecord { get; set; }

    /// <summary>
    /// Продольные силы стержневых КЭ сетки, по которым собрана K_σ задачи (номер КЭ → N, Н, растяжение — плюс) — N_Ed
    /// для α_ult,k амплитуды формы EN 1993-1-1 (5.9)–(5.11); null — результат до подсреза 5б.
    /// </summary>
    public Dictionary<int, double>? Axial { get; set; }
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

    /// <summary>
    /// Отпечаток геометрии схемы при расчёте (<see cref="FemGeometryFingerprint"/>: сетка, шарниры, сечения, ГУ — без
    /// нагрузок и сочетаний): формы годятся для несовершенства, пока он совпадает с текущим; пусто — результат до
    /// среза 5 несовершенств (формы для несовершенства не годятся).
    /// </summary>
    public string Geometry { get; set; } = "";
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
            yield return string.Format(ci, Loc.S(c.Envelope ? "FemBucklingEnvelopeCaseLine" : "FemBucklingCaseLine"), c.Tag, c.Units,
                c.Lambda.Length > 0 ? c.Lambda[0].ToString("0.###", ci) : "—", c.Lambda.Length)
                + (c.Converged ? "" : " " + Loc.S("FemBucklingNotConverged"))
                + (c.Unstable ? " " + Loc.S("FemBucklingUnstable") : "");
        foreach (var line in Report) yield return line;
    }
}

/// <summary>
/// Форма потери устойчивости на сетке схемы: перемещения узлов (номер узла сетки → ux, uy, uz) и промежуточных точек
/// дробления стержней (номер КЭ → точки по порядку от I к J, равномерно по длине), нормированные на max |u| = 1;
/// <see cref="Rotations"/> — повороты узлов (θx, θy, θz) тем же множителем, null — запись v1 (только для 3D-вида).
/// </summary>
public sealed record FemBucklingMode(IReadOnlyDictionary<int, (double X, double Y, double Z)> Nodes,
    IReadOnlyDictionary<int, (double X, double Y, double Z)[]> BarPoints)
{
    /// <summary>Повороты узлов (номер узла сетки → θx, θy, θz); null — записи v1 (без поворотов, float).</summary>
    public IReadOnlyDictionary<int, (double X, double Y, double Z)>? Rotations { get; init; }

    /// <summary>
    /// Изгибающие моменты формы по стержням (номер КЭ → (My, Mz) местных осей в узле I, точках дробления и узле J, Н·м
    /// тем же множителем); null — запись v1/v2 (без моментов, амплитуда по (5.9) недоступна).
    /// </summary>
    public IReadOnlyDictionary<int, (double My, double Mz)[]>? Moments { get; init; }

    /// <summary>Форма годится для несовершенства (записана v2 и позже: повороты узлов, double).</summary>
    public bool IsFull => Rotations != null;

    /// <summary>Нейтральная форма для несовершенства (узел → 6 DOF, стержень → точки); null — запись v1.</summary>
    public CScore.Fem.Imperfections.FemModeShape? ToShape()
    {
        if (Rotations == null) return null;
        var nodes = new Dictionary<int, double[]>(Nodes.Count);
        foreach (var (id, u) in Nodes)
        {
            var r = Rotations.TryGetValue(id, out var rot) ? rot : (0, 0, 0);
            nodes[id] = [u.X, u.Y, u.Z, r.X, r.Y, r.Z];
        }
        return new CScore.Fem.Imperfections.FemModeShape(nodes,
            BarPoints.ToDictionary(p => p.Key, p => p.Value.Select(u => new[] { u.X, u.Y, u.Z }).ToArray()))
        {
            Moments = Moments?.ToDictionary(p => p.Key, p => p.Value.Select(m => new[] { m.My, m.Mz }).ToArray()),
        };
    }
}

/// <summary>
/// Двоичная запись формы (<see cref="FemBucklingMode"/>), little-endian. v3 (пишется): маркер <see cref="V3Marker"/>,
/// далее как v2 и блок моментов — число стержней, у каждого номер, число значений и по 2 double (My, Mz). v2: маркер
/// <see cref="V2Marker"/>, узлы — номер и 6 double (перемещения, повороты), стержни — номер, число точек и по 3 double.
/// v1 (старые результаты, читается для 3D-вида): без маркера, узлы — 3 float, точки — 3 float.
/// </summary>
public static class FemBucklingModeCodec
{
    /// <summary>Маркер записи v2 вместо числа узлов (у v1 число узлов неотрицательно).</summary>
    public const int V2Marker = -2;
    /// <summary>Маркер записи v3 (v2 + моменты формы, подсрез 5б).</summary>
    public const int V3Marker = -3;

    /// <summary>Запись v3; без <see cref="FemBucklingMode.Moments"/> — v2.</summary>
    public static byte[] Encode(FemBucklingMode mode)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms))
        {
            w.Write(mode.Moments != null ? V3Marker : V2Marker);
            w.Write(mode.Nodes.Count);
            foreach (var (id, u) in mode.Nodes)
            {
                w.Write(id);
                Write(w, u);
                Write(w, mode.Rotations != null && mode.Rotations.TryGetValue(id, out var r) ? r : (0, 0, 0));
            }
            w.Write(mode.BarPoints.Count);
            foreach (var (id, points) in mode.BarPoints)
            {
                w.Write(id);
                w.Write(points.Length);
                foreach (var u in points) Write(w, u);
            }
            if (mode.Moments != null)
            {
                w.Write(mode.Moments.Count);
                foreach (var (id, values) in mode.Moments)
                {
                    w.Write(id);
                    w.Write(values.Length);
                    foreach (var (my, mz) in values)
                    {
                        w.Write(my);
                        w.Write(mz);
                    }
                }
            }
        }
        return ms.ToArray();
    }

    /// <summary>Запись v1 (float, без поворотов) — для проверки чтения старых результатов.</summary>
    public static byte[] EncodeV1(FemBucklingMode mode)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms))
        {
            w.Write(mode.Nodes.Count);
            foreach (var (id, u) in mode.Nodes)
            {
                w.Write(id);
                WriteSingle(w, u);
            }
            w.Write(mode.BarPoints.Count);
            foreach (var (id, points) in mode.BarPoints)
            {
                w.Write(id);
                w.Write(points.Length);
                foreach (var u in points) WriteSingle(w, u);
            }
        }
        return ms.ToArray();
    }

    public static FemBucklingMode Decode(byte[] data)
    {
        using var r = new BinaryReader(new MemoryStream(data));
        int head = r.ReadInt32();
        bool v3 = head == V3Marker, v2 = v3 || head == V2Marker;
        Func<BinaryReader, (double, double, double)> read = v2 ? Read : ReadSingle;
        int n = v2 ? r.ReadInt32() : head;
        var nodes = new Dictionary<int, (double, double, double)>(n);
        var rotations = v2 ? new Dictionary<int, (double, double, double)>(n) : null;
        for (int i = 0; i < n; i++)
        {
            int id = r.ReadInt32();
            nodes[id] = read(r);
            if (rotations != null) rotations[id] = read(r);
        }
        int b = r.ReadInt32();
        var bars = new Dictionary<int, (double, double, double)[]>(b);
        for (int i = 0; i < b; i++)
        {
            int id = r.ReadInt32();
            var points = new (double, double, double)[r.ReadInt32()];
            for (int k = 0; k < points.Length; k++) points[k] = read(r);
            bars[id] = points;
        }
        Dictionary<int, (double, double)[]>? moments = null;
        if (v3)
        {
            int c = r.ReadInt32();
            moments = new Dictionary<int, (double, double)[]>(c);
            for (int i = 0; i < c; i++)
            {
                int id = r.ReadInt32();
                var values = new (double, double)[r.ReadInt32()];
                for (int k = 0; k < values.Length; k++) values[k] = (r.ReadDouble(), r.ReadDouble());
                moments[id] = values;
            }
        }
        return new FemBucklingMode(nodes, bars) { Rotations = rotations, Moments = moments };
    }

    static void Write(BinaryWriter w, (double X, double Y, double Z) u)
    {
        w.Write(u.X);
        w.Write(u.Y);
        w.Write(u.Z);
    }

    static void WriteSingle(BinaryWriter w, (double X, double Y, double Z) u)
    {
        w.Write((float)u.X);
        w.Write((float)u.Y);
        w.Write((float)u.Z);
    }

    static (double, double, double) Read(BinaryReader r) => (r.ReadDouble(), r.ReadDouble(), r.ReadDouble());

    static (double, double, double) ReadSingle(BinaryReader r) => (r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
}

/// <summary>
/// Отпечаток входа задачи устойчивости: модель адаптера (узлы, стержни с упругими характеристиками и шарнирами,
/// пластины, жёсткие тела, опоры, пружины, загружения), расчётные элементы, сочетания и параметры расчёта. Совпадение —
/// сохранённый результат постановки годится для проверки стали без пересчёта.
/// </summary>
public static class FemBucklingFingerprint
{
    /// <param name="method">Метод назначения μ; номограммный — со своими параметрами в отпечатке, по формам — без них
    /// (отпечатки результатов до номограмм остаются прежними). Огибающая сжатия — строкой в отпечатке: результаты по
    /// определяющим сочетаниям (до огибающей) при ней устаревают.</param>
    public static string Compute(FemSteelBucklingInput input, RcBucklingOptions options, FemBucklingParams? method = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(options);
        var m = input.Adapted?.Model ?? throw new InvalidOperationException("Вход расчёта устойчивости без модели.");
        var sb = new StringBuilder();
        void Num(double v) => sb.Append(v.ToString("R", CultureInfo.InvariantCulture)).Append(';');
        void Nums(IEnumerable<double>? v) { foreach (double x in v ?? []) Num(x); sb.Append('|'); }

        sb.Append("modes=").Append(options.Modes).Append(";sub=").Append(options.MinSubdivisions).Append('\n');
        if (method?.Envelope ?? true) sb.Append("forces=envelope\n");
        if (method?.Nomogram() is { } nomo)
        {
            sb.Append("nomogram=").Append(nomo.Method).Append(',').Append(nomo.Sway).Append(',').Append(method.SwayX).Append(',')
                .Append(method.SwayY).Append(':');
            Num(nomo.GFixed); Num(nomo.GPinned); Num(nomo.SwayStiffness); Num(nomo.SwayChord);
            sb.Append('\n');
        }
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
        new(Modes: Math.Max(1, Params(analysis).Modes));

    /// <summary>Параметры постановки (метод назначения μ, смещение, G опор).</summary>
    public static FemBucklingParams Params(FemAnalysis analysis) =>
        FemAnalysisParams.Parse(analysis.ParamsJson).Buckling ?? new FemBucklingParams();

    /// <summary>
    /// Сохранённый результат постановки, годный для входа <paramref name="input"/> (отпечаток совпал); null — результата
    /// нет (<paramref name="saved"/> = null) или он устарел.
    /// </summary>
    public static FemBucklingSummary? Current(DatabaseService db, FemAnalysis analysis, FemSteelBucklingInput input, out CalcResult? saved)
    {
        saved = analysis.ResultId is int rid ? db.GetCalcResultById(rid) : null;
        if (saved is not { TaskKind: TaskKind, Status: "ok" or "not_converged" }) saved = null;
        return saved != null && FemBucklingSummary.Parse(saved.DataJson) is { } summary
            && summary.Fingerprint == FemBucklingFingerprint.Compute(input, Options(analysis), Params(analysis)) ? summary : null;
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
        var method = Params(analysis);
        var clock = Stopwatch.StartNew();
        var (summary, records) = await Task.Run(() =>
        {
            var r = FemSteelBuckling.Run(input, options, ct, method: method);
            var s = new FemBucklingSummary
            {
                Fingerprint = FemBucklingFingerprint.Compute(input, options, method), Geometry = input.Geometry, Modes = options.Modes,
                Report = input.Report.Concat(r.Report).ToList(), Units = FemSteelBuckling.Summarize(input, r),
            };
            var recs = new List<(int, byte[])>();
            foreach (var c in r.Cases)
            {
                ct.ThrowIfCancellationRequested();
                s.Cases.Add(new FemBucklingCaseSummary
                {
                    Tag = c.Tag, Envelope = c.Envelope, Lambda = c.Result.Lambda, Converged = c.Result.Converged, Unstable = c.Unstable,
                    Units = c.Units.Count, FirstRecord = recs.Count + 1,
                    Axial = c.Result.BarAxial().ToDictionary(p => p.Key, p => p.Value),
                });
                for (int k = 0; k < c.Result.Modes.Length; k++)
                    recs.Add((recs.Count + 1, FemBucklingModeCodec.Encode(Mode(input, c.Result, c.Result.Modes[k], c.Result.Lambda[k]))));
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
    /// <param name="lambda">λ формы — для моментов продольно-поперечного изгиба (<see cref="RcBucklingResult.ModeMoments"/>).</param>
    public static FemBucklingMode Mode(FemSteelBucklingInput input, RcBucklingResult result, double[] mode, double lambda)
    {
        var ids = input.Adapted!.Model.Nodes.Select(n => n.Id).ToList();
        var shape = result.ModeShape(mode, ids);
        return new FemBucklingMode(shape.Nodes.ToDictionary(p => p.Key, p => (p.Value[0], p.Value[1], p.Value[2])),
            shape.BarPoints.ToDictionary(p => p.Key, p => p.Value.Select(u => (u[0], u[1], u[2])).ToArray()))
        {
            Rotations = shape.Nodes.ToDictionary(p => p.Key, p => (p.Value[3], p.Value[4], p.Value[5])),
            Moments = result.ModeMoments(mode, lambda, ids).ToDictionary(p => p.Key, p => p.Value.Select(m => (m[0], m[1])).ToArray()),
        };
    }

    static CalcResult Error(FemAnalysis analysis, string created, IEnumerable<string> errors, IEnumerable<string>? report = null) => new()
    {
        TaskId = 0, TaskKind = TaskKind, TaskTag = analysis.Tag, Created = created, Status = "error",
        DataJson = new FemBucklingSummary { Errors = errors.ToList(), Report = report?.ToList() ?? [] }.ToJson(),
    };
}
