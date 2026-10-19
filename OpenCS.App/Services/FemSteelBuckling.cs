using System.Globalization;
using CScore.Fem;
using CScore.Fem.Loads;
using CSfea.CScoreBridge.Structural;
using OpenCS.Tasks;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>
/// Вход расчёта устойчивости для расчётных длин: модель адаптера (упругие сечения), расчётные элементы и номера их
/// стержней в модели, сочетания (стадии постановки).
/// </summary>
public sealed record FemSteelBucklingInput(FemRcModelResult? Adapted, IReadOnlyList<FemBucklingUnit> Units,
    IReadOnlyList<IReadOnlyList<int>> UnitBeams, IReadOnlyList<FemRcStage> Combinations, List<FemValidationDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(d => d.IsError) || Adapted is not { HasErrors: false };

    /// <summary>Строки отчёта: ошибки, затем предупреждения и сведения.</summary>
    public IEnumerable<string> Report => Diagnostics.Concat(Adapted?.Diagnostics ?? []).OrderByDescending(d => d.IsError)
        .Select(d => (d.IsError ? "Ошибка: " : "") + d.Message);
}

/// <summary>
/// Расчёт устойчивости по сочетанию, определяющему для части элементов (п. 10.3.2 СП 16), или по огибающей сжатия
/// (<see cref="Combination"/> = −1, <see cref="RcBucklingAnalysis.SolveEnvelope"/>): <paramref name="Units"/> — индексы
/// элементов входа, которым задача назначает μ.
/// </summary>
public sealed record FemSteelBucklingCase(int Combination, string Tag, IReadOnlyList<int> Units, RcBucklingResult Result)
{
    /// <summary>Задача по огибающей сжатия всех сочетаний.</summary>
    public bool Envelope => Combination < 0;

    /// <summary>Схема неустойчива при сочетании: λ₁ ≤ 1 (μ по нему не назначаются).</summary>
    public bool Unstable => Result.Lambda.Length > 0 && Result.Lambda[0] <= 1.0;
}

/// <summary>
/// Итог расчёта устойчивости: определяющие сочетания элементов, задачи по различным из них и расчётные длины элементов
/// по формам своего сочетания (<see cref="RcBucklingLengths"/>; null — сжатия нет или схема при сочетании неустойчива).
/// </summary>
public sealed record FemSteelBucklingResult(RcBucklingModel Model, IReadOnlyList<RcBucklingGoverning> Governing,
    IReadOnlyList<FemSteelBucklingCase> Cases, IReadOnlyList<RcBucklingUnitLength?> Lengths, IReadOnlyList<string> Report)
{
    /// <summary>Задача, назначившая μ элементу <paramref name="unit"/> (огибающая или его определяющее сочетание); null — сжатия нет.</summary>
    public FemSteelBucklingCase? CaseOf(int unit) =>
        Governing[unit].Combination is int c and >= 0 ? Cases.FirstOrDefault(x => x.Envelope || x.Combination == c) : null;
}

/// <summary>
/// Расчётные длины КЭ в осях СП 16 по расчёту устойчивости (<see cref="FemSteelBuckling.ElementLengths"/>): l — длина
/// расчётного элемента, м; μx, μy — null, если не назначены; пояснения — источник по каждой оси для отчёта, подробности
/// — эквивалентная стойка по форме и справочная по жёсткостям окружения.
/// </summary>
public sealed record FemSteelAutoLef(string Unit, double Length, double? MuX, double? MuY, string NoteX, string NoteY,
    IReadOnlyList<string>? DetailsX = null, IReadOnlyList<string>? DetailsY = null)
{
    /// <summary>Пояснения для примечаний результата: ось x с подробностями, затем ось y.</summary>
    public IReadOnlyList<string> Notes => [NoteX, .. DetailsX ?? [], NoteY, .. DetailsY ?? []];
}

/// <summary>
/// Модель устойчивости схемы для автоматических расчётных длин стали (спека 2026-10-18, срез 1): вход CSfea схемы с
/// упругими сечениями (бетон брутто, как линейный расчёт), расчётные элементы — КонЭ или цепочки КЭ
/// (<see cref="FemBucklingUnits"/>), сочетания — стадии постановки (пакета — его варианты без длительных пар).
/// Каждый элемент дробится не менее чем на 4 КЭ; определяющее сочетание элемента — с наибольшим сжатием. По умолчанию
/// задача одна — по огибающей сжатия стержней (<see cref="RcBucklingAnalysis.SolveEnvelope"/>); при
/// <see cref="FemBucklingParams.Forces"/> = «governing» — по каждому различному определяющему сочетанию (п. 10.3.2
/// СП 16; незагруженные в нём колонны раскрепляют загруженные, для других сочетаний μ бывает не в запас). μ элементов —
/// энергетическим разбором форм задачи
/// (<see cref="RcBucklingLengths"/>, срез 2) и локальными задачами (срез 3), в оси СП 16 — <see cref="ElementLengths"/>.
/// </summary>
public static class FemSteelBuckling
{
    /// <summary>Сборка входа — в UI-потоке (соединение SQLite одно); адаптер — внутри, он чисто вычислительный.</summary>
    public static FemSteelBucklingInput Prepare(DatabaseService db, int schemaId, FemAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(analysis);
        var diag = new List<FemValidationDiagnostic>();
        var setup = FemCsfeaSetup.FromAnalysis(analysis) with { Calc = CScore.CalcType.N, GrossBars = true };
        var input = FemCsfeaInputBuilder.Build(db, schemaId, setup);
        var adapted = FemRcModelAdapter.Adapt(input);
        var units = FemBucklingUnits.Build(input.MeshElements, input.MeshNodes);

        var beams = adapted.Model.Beams.Select(b => b.Id).ToHashSet();
        var kept = new List<FemBucklingUnit>();
        var unitBeams = new List<IReadOnlyList<int>>();
        var lost = new List<string>();
        foreach (var u in units)
        {
            var ids = u.ElemTags.Select(t => int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) ? id : 0)
                .Where(beams.Contains).ToList();
            if (ids.Count == 0) { lost.Add(u.Label); continue; }
            kept.Add(u);
            unitBeams.Add(ids);
        }
        if (lost.Count > 0)
            diag.Add(new("buckling_unit_not_in_model",
                $"Элементы без стержней в модели CSfea — расчётные длины не определяются: {FemLoadTargets.Sample(lost)}.", false, lost));
        if (kept.Count == 0) diag.Add(new("buckling_no_units", "В схеме нет стержней для расчётных длин.", true));
        if (input.Stages.Count == 0) diag.Add(new("buckling_no_combinations", "В постановке нет сочетаний (стадий).", true));
        return new FemSteelBucklingInput(adapted, kept, unitBeams, input.Stages, diag);
    }

    /// <summary>
    /// Расчёт — в фоне. <paramref name="method"/> — метод назначения μ постановки (номограммы ASCE / табл. 31 СП 16
    /// поверх назначения по формам, <see cref="RcNomogramLengths"/>); null или «mode» — по формам.
    /// </summary>
    public static FemSteelBucklingResult Run(FemSteelBucklingInput input, RcBucklingOptions? options = null,
        CancellationToken ct = default, RcBucklingLengthOptions? lengthOptions = null, FemBucklingParams? method = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.HasErrors) throw new InvalidOperationException("Вход расчёта устойчивости содержит ошибки.");
        options ??= new RcBucklingOptions();
        var model = RcBucklingAnalysis.Prepare(input.Adapted!.Model, input.UnitBeams, options);
        var combinations = input.Combinations.Select(c => c.Terms).ToList();
        var governing = RcBucklingAnalysis.Governing(model, input.UnitBeams, combinations, ct);

        var report = new List<string>();
        int idle = governing.Count(g => g.Combination < 0);
        if (idle > 0) report.Add($"Элементов без сжатия ни в одном сочетании: {idle} — расчётные длины не нужны.");
        var cases = new List<FemSteelBucklingCase>();
        var lengths = new RcBucklingUnitLength?[input.Units.Count];
        var groups = governing.Select((g, i) => (g.Combination, Unit: i)).Where(x => x.Combination >= 0)
            .GroupBy(x => x.Combination).OrderBy(g => g.Key).ToList();
        var tasks = new List<(int Combination, string Tag, List<int> Units)>();
        if (method?.Envelope ?? true)
        {
            if (groups.Count > 0)
                tasks.Add((-1, Loc.S("FemBucklingEnvelopeTag"), groups.SelectMany(g => g.Select(x => x.Unit)).Order().ToList()));
        }
        else
            tasks.AddRange(groups.Select(g => (g.Key, input.Combinations[g.Key].Tag, g.Select(x => x.Unit).ToList())));
        foreach (var task in tasks)
        {
            ct.ThrowIfCancellationRequested();
            // Оболочки огибающей — по сочетанию, определяющему для наибольшего числа элементов.
            int shells = task.Combination < 0 ? groups.MaxBy(g => g.Count())!.Key : task.Combination;
            var r = task.Combination < 0
                ? RcBucklingAnalysis.SolveEnvelope(model, combinations, shells, options, ct)
                : RcBucklingAnalysis.Solve(model, combinations[task.Combination], options, ct);
            var c = new FemSteelBucklingCase(task.Combination, task.Tag, task.Units, r);
            cases.Add(c);
            string lambda = r.Lambda.Length > 0 ? r.Lambda[0].ToString("0.###", CultureInfo.CurrentCulture) : "—";
            report.Add((c.Envelope
                    ? string.Format(CultureInfo.CurrentCulture, Loc.S("FemBucklingEnvelopeReport"), combinations.Count, c.Units.Count, lambda)
                    : $"Сочетание «{task.Tag}»: определяющее для {c.Units.Count} эл., λ₁ = {lambda}")
                + (r.Converged ? "" : " (итерации не сошлись)") + (c.Unstable ? " — схема неустойчива, μ не назначаются." : "."));
            if (c.Envelope && model.Build.Mesh.Shells.Count > 0)
                report.Add(string.Format(CultureInfo.CurrentCulture, Loc.S("FemBucklingEnvelopeShells"), input.Combinations[shells].Tag));
            if (c.Unstable) continue;
            // Участие — относительно всех элементов схемы, назначение и локальные задачи — элементам, для которых
            // сочетание определяющее.
            var local = RcBucklingAnalysis.SolveLocal(r, input.UnitBeams, c.Units);
            ct.ThrowIfCancellationRequested();
            var all = RcBucklingLengths.Assign(r, input.UnitBeams, lengthOptions, local);
            if (method?.Nomogram() is { } nomogram)
                all = RcNomogramLengths.Assign(r, input.UnitBeams, c.Units, all, nomogram, local,
                    (unit, xy) => xy == XAboutZ(input, unit) ? method.SwayX : method.SwayY);
            foreach (int u in c.Units) lengths[u] = all[u];
        }
        int Count(RcBucklingLengthSource source) => lengths.Count(x => x != null && (x.AboutZ.Source == source || x.AboutY.Source == source));
        int byLocal = Count(RcBucklingLengthSource.Local), notInvolved = Count(RcBucklingLengthSource.NotInvolved);
        if (byLocal > 0)
            report.Add($"Элементов с N_cr по локальной задаче хотя бы в одной плоскости: {byLocal} (не участвуют в {options.Modes} формах или форма без смещения ниже).");
        if (notInvolved > 0)
            report.Add($"Элементов без формы хотя бы в одной плоскости (ни общей, ни локальной): {notInvolved} — μ в этой плоскости не назначен.");
        if (method?.Nomogram() is { } used)
        {
            var planes = lengths.Where(x => x != null).SelectMany(x => new[] { x!.AboutZ, x.AboutY })
                .Where(p => p.Nomogram != null).ToList();
            int sway = planes.Count(p => p.Nomogram!.Sway), none = planes.Count(p => !double.IsFinite(p.Mu));
            int fallback = planes.Count(p => p.Nomogram!.Sp16NotApplicable);
            report.Add(string.Format(CultureInfo.CurrentCulture, Loc.S(used.Method == RcLengthMethod.Sp16 ? "FemBucklingReportSp16" : "FemBucklingReportAsce"),
                planes.Count, sway, planes.Count - sway)
                + (fallback > 0 ? " " + string.Format(CultureInfo.CurrentCulture, Loc.S("FemBucklingReportSp16Fallback"), fallback) : "")
                + (none > 0 ? " " + string.Format(CultureInfo.CurrentCulture, Loc.S("FemBucklingReportNomogramNone"), none) : ""));
        }
        return new FemSteelBucklingResult(model, governing, cases, lengths, report);
    }

    /// <summary>
    /// Расчётные длины по тегам КЭ в осях СП 16: ось x сечения — местная z у КЭ своей схемы (сечение CScore осью Y по
    /// местной y, <see cref="RcBeamSection.CrossYAlongLocalY"/>), местная y — у импортного (ось x ‖ Y1); lef,x — изгиб
    /// вокруг оси x. Элементы без сжатия не попадают.
    /// </summary>
    public static Dictionary<string, FemSteelAutoLef> ElementLengths(FemSteelBucklingInput input, FemSteelBucklingResult result) =>
        ElementLengths(Summarize(input, result));

    /// <summary>Расчётные длины по тегам КЭ из сводки элементов (<see cref="Summarize"/>, результат постановки).</summary>
    public static Dictionary<string, FemSteelAutoLef> ElementLengths(IEnumerable<FemBucklingUnitSummary> units)
    {
        ArgumentNullException.ThrowIfNull(units);
        var map = new Dictionary<string, FemSteelAutoLef>(StringComparer.Ordinal);
        foreach (var u in units)
        {
            var auto = new FemSteelAutoLef(u.Label, u.Length, u.X.Mu, u.Y.Mu, u.X.Note, u.Y.Note, u.X.Details, u.Y.Details);
            foreach (string tag in u.ElemTags) map[tag] = auto;
        }
        return map;
    }

    /// <summary>
    /// Расчётные длины элементов в осях СП 16 (см. <see cref="ElementLengths(FemSteelBucklingInput, FemSteelBucklingResult)"/>)
    /// с N_cr, КЗУ, источником и пояснениями — для сохранения в результате постановки. Элементы без сжатия не попадают.
    /// </summary>
    public static List<FemBucklingUnitSummary> Summarize(FemSteelBucklingInput input, FemSteelBucklingResult result)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(result);
        var names = new NomogramNames(input, result.Model.Build.NodeIds);
        var list = new List<FemBucklingUnitSummary>();
        for (int i = 0; i < input.Units.Count; i++)
        {
            if (result.CaseOf(i) is not { } c) continue;
            var unit = input.Units[i];
            var s = new FemBucklingUnitSummary
            {
                Label = unit.Label, ElemTags = unit.ElemTags.ToList(), N = result.Governing[i].N,
                Combination = input.Combinations[result.Governing[i].Combination].Tag,
            };
            if (result.Lengths[i] is not { } x)
            {
                string why = c.Unstable
                    ? string.Format(CultureInfo.CurrentCulture, Loc.S("FemSteelAutoLefUnstable"), c.Tag, c.Result.Lambda[0])
                    : Loc.S("FemSteelAutoLefNoLength");
                s.X = new FemBucklingAxisSummary { Note = why };
                s.Y = new FemBucklingAxisSummary { Note = why };
            }
            else
            {
                bool xAboutZ = XAboutZ(input, i);
                var (px, py) = xAboutZ ? (x.AboutZ, x.AboutY) : (x.AboutY, x.AboutZ);
                s.Length = x.Length;
                s.N = x.N;
                s.X = Axis("x", px, x, c.Tag, unit.Label, names);
                s.Y = Axis("y", py, x, c.Tag, unit.Label, names);
            }
            list.Add(s);
        }
        return list;
    }

    // Ось x СП 16 элемента — местная z (своя схема) или местная y (импорт), см. ElementLengths.
    static bool XAboutZ(FemSteelBucklingInput input, int unit)
    {
        int beam = input.UnitBeams[unit][0];
        return input.Adapted!.Model.Beams.First(b => b.Id == beam).Section.CrossYAlongLocalY;
    }

    static FemBucklingAxisSummary Axis(string axis, RcBucklingPlaneLength p, RcBucklingUnitLength x, string combination,
        string unit, NomogramNames names)
    {
        bool assigned = Mu(p) != null;
        var form = p.Form ?? p;
        return new FemBucklingAxisSummary
        {
            Mu = Mu(p), Ncr = assigned && double.IsFinite(p.Ncr) ? p.Ncr : null, Source = p.Source.ToString(),
            Mode = form.Mode >= 0 ? form.Mode + 1 : null, Lambda = double.IsFinite(form.Lambda) ? form.Lambda : null,
            Sway = p.Nomogram?.Sway,
            Note = p.Nomogram is { } nomo ? NomogramNote(axis, p, nomo, x, combination, unit) : Note(axis, p, x, combination, unit),
            Details = p.Nomogram is { } n
                ? [.. NomogramDetails(n, names), Loc.S("FemSteelAutoLefNomogramFormPrefix") + " " + Note(axis, p.Form!, x, combination, unit),
                    .. Details(axis, p.Form!, x)]
                : Details(axis, p, x),
        };
    }

    /// <summary>Подписи для хода номограммного расчёта: расчётные элементы и узлы сетки по номерам схемы.</summary>
    sealed class NomogramNames(FemSteelBucklingInput input, int[] nodeIds)
    {
        public string Unit(int unit) => unit >= 0 && unit < input.Units.Count ? input.Units[unit].Label : Loc.S("FemSteelAutoLefNomogramBar");

        public string Node(int index) => index >= 0 && index < nodeIds.Length ? nodeIds[index].ToString(CultureInfo.InvariantCulture) : "—";
    }

    static string G(double g) => double.IsPositiveInfinity(g) ? "∞" : g.ToString("0.###", CultureInfo.CurrentCulture);

    // Пояснение к μ по номограмме: метод, смещение и его признак, G концов (или p, n таблицы СП 16), N и N_cr.
    static string NomogramNote(string axis, RcBucklingPlaneLength p, RcNomogramPlane n, RcBucklingUnitLength x, string combination, string unit)
    {
        var ci = CultureInfo.CurrentCulture;
        string sway = Loc.S(n.Sway ? "FemSteelAutoLefNomogramSway" : "FemSteelAutoLefNomogramBraced");
        string reason = n.SwayReason switch
        {
            RcSwayReason.ModeChord => string.Format(ci, Loc.S("FemSteelAutoLefNomogramByMode"), (p.Form?.Mode ?? -1) + 1, n.SwayValue),
            RcSwayReason.NotInModes => Loc.S("FemSteelAutoLefNomogramNotInModes"),
            RcSwayReason.Stiffness => string.Format(ci, Loc.S("FemSteelAutoLefNomogramByStiffness"), n.SwayValue),
            RcSwayReason.Manual => Loc.S("FemSteelAutoLefNomogramManual"),
            _ => Loc.S("FemSteelAutoLefNomogramNoSurround"),
        };
        if (!double.IsFinite(p.Mu))
            return string.Format(ci, Loc.S("FemSteelAutoLefNomogramNone"), axis, unit, sway, reason, G(n.A.G), G(n.B.G));
        string tail = string.Format(ci, Loc.S("FemSteelAutoLefNomogramTail"), -x.N / 1e3, p.Ncr / 1e3);
        if (n.Sp16 is { } t)
            return string.Format(ci, Loc.S("FemSteelAutoLefNomogramSp16"), axis, p.Mu, unit, x.Length, combination, sway,
                reason, Loc.S("FemSp16Storey" + n.Storey), n.Spans, P(t.P), t.N, t.Formula) + " " + tail;
        string text = string.Format(ci, Loc.S("FemSteelAutoLefNomogramAsce"), axis, p.Mu, unit, x.Length, combination, sway,
            reason, G(n.A.G), G(n.B.G), n.Sway ? "(2.9)" : "(2.13)") + " " + tail;
        return n.Sp16NotApplicable ? Loc.S("FemSteelAutoLefNomogramSp16NotColumn") + " " + text : text;
    }

    static string P(double p) => double.IsPositiveInfinity(p) ? "∞" : p.ToString("0.###", CultureInfo.CurrentCulture);

    // Ход номограммного расчёта: по концам — колонны и ригели с погонными жёсткостями и коэффициентами, опоры, G;
    // затем уравнение (ASCE) или формула таблицы (СП 16) с подстановкой.
    static List<string> NomogramDetails(RcNomogramPlane n, NomogramNames names)
    {
        var ci = CultureInfo.CurrentCulture;
        bool sp16 = n.Sp16 != null;
        var list = new List<string>();
        foreach (var (end, where) in new[] { (n.A, sp16 ? "FemSteelAutoLefNomogramBottom" : "FemSteelAutoLefNomogramEndA"),
                     (n.B, sp16 ? "FemSteelAutoLefNomogramTop" : "FemSteelAutoLefNomogramEndB") })
        {
            string Kilo(double v) => (v / 1e3).ToString("0.###", ci);
            var cols = end.Arms.Where(a => a.Column).Select(a => $"{names.Unit(a.Unit)} {Kilo(a.Stiffness)}"
                + (a.Released ? " " + Loc.S("FemSteelAutoLefNomogramReleased") : ""));
            var girders = end.Arms.Where(a => !a.Column).Select(a =>
            {
                string g = $"{names.Unit(a.Unit)} {Kilo(a.Stiffness)}";
                if (a.Released) return g + " " + Loc.S("FemSteelAutoLefNomogramReleased");
                if (!sp16 && Math.Abs(a.Factor - 1) > 1e-6) g += " × " + a.Factor.ToString("0.###", ci);
                if (!sp16 && a.Far != RcFarEnd.Joint) g += " (" + Loc.S("FemSteelAutoLefNomogramFar" + a.Far) + ")";
                return g;
            });
            var extra = new List<string>();
            if (end.OwnReleased) extra.Add(Loc.S("FemSteelAutoLefNomogramOwnReleased"));
            if (end.SupportFixed) extra.Add(Loc.S("FemSteelAutoLefNomogramSupportFixed"));
            else if (end.SupportPinned) extra.Add(Loc.S("FemSteelAutoLefNomogramSupportPinned"));
            if (end.Spring > 0) extra.Add(string.Format(ci, Loc.S("FemSteelAutoLefNomogramSpring"), Kilo(end.Spring)));
            if (end.Shells > 0) extra.Add(string.Format(ci, Loc.S("FemSteelAutoLefNomogramShells"), end.Shells));
            string own = string.Join(", ", cols.DefaultIfEmpty("—"));
            list.Add(string.Format(ci, Loc.S("FemSteelAutoLefNomogramEnd"), names.Node(end.Node), Loc.S(where),
                Kilo(end.Columns), own, Kilo(end.Girders), string.Join(", ", girders.DefaultIfEmpty("—")),
                extra.Count > 0 ? "; " + string.Join("; ", extra) : "", G(end.G)));
        }
        if (n.Sp16 is { } t)
            list.Add(string.Format(ci, Loc.S("FemSteelAutoLefNomogramSp16Formula"), Loc.S(n.Sway ? "FemSteelAutoLefNomogramSway" : "FemSteelAutoLefNomogramBraced"),
                Loc.S("FemSp16Storey" + n.Storey), n.Spans, P(t.P), t.N, t.Formula, t.Mu));
        else
            list.Add(string.Format(ci, Loc.S(n.Sway ? "FemSteelAutoLefNomogramEq29" : "FemSteelAutoLefNomogramEq213"), G(n.A.G), G(n.B.G), n.K));
        return list;
    }

    static double? Mu(RcBucklingPlaneLength p) => double.IsFinite(p.Mu) && p.Mu > 0 ? p.Mu : null;

    // Ход вычисления μ по оси (спека 2026-10-18, п. 3Е): эквивалентная стойка Масленникова с пружинами из формы, по
    // которой назначена N_cr (её корень — тот же μ с точностью дробления), и справочно — с жёсткостями окружения при
    // фактическом сочетании (диагональ сведённой матрицы: связи между DOF отброшены, μ лишь близок).
    static List<string> Details(string axis, RcBucklingPlaneLength p, RcBucklingUnitLength x)
    {
        var ci = CultureInfo.CurrentCulture;
        var list = new List<string>();
        double l = x.Length, n = -x.N;
        if (p.Equivalent is { } eq && double.IsFinite(eq.U))
        {
            string text = string.Format(ci, Loc.S("FemSteelAutoLefEquivalent"), axis, Kilo(eq.Restraint.R1),
                Kilo(eq.Restraint.R2), Kilo(eq.Restraint.R3), p.EI / 1e3, Rho(eq.Rho1), Rho(eq.Rho2), Rho(eq.Rho3),
                eq.U, eq.Mu, p.Ncr / n);
            if (eq.Restraint.Interior > InteriorTolerance)
                text += " " + string.Format(ci, Loc.S("FemSteelAutoLefEquivalentInterior"), eq.Restraint.Interior * 100);
            else if (Math.Abs(eq.Mu / p.Mu - 1) > MuTolerance)
                text += " " + string.Format(ci, Loc.S("FemSteelAutoLefEquivalentDiffers"), p.Mu);
            list.Add(text);
        }
        if (p.Surround is { } s)
            list.Add(double.IsFinite(s.U)
                ? string.Format(ci, Loc.S("FemSteelAutoLefSurround"), axis, Kilo(s.Restraint.R1), Kilo(s.Restraint.R2),
                    Kilo(s.Restraint.R3), Rho(s.Rho1), Rho(s.Rho2), Rho(s.Rho3), s.U, s.Mu, p.EI * s.U * s.U / (l * l) / 1e3)
                : string.Format(ci, Loc.S("FemSteelAutoLefSurroundNoRoot"), axis, Kilo(s.Restraint.R1),
                    Kilo(s.Restraint.R2), Kilo(s.Restraint.R3)));
        return list;
    }

    // Доля усилий промежуточных узлов, выше которой стойка — приближённая; расхождение μ стойки и μ без пояснения
    // (переменные N, EI по длине элемента).
    const double InteriorTolerance = 0.01, MuTolerance = 0.01;

    // Жёсткость пружины в кН·м/рад или кН/м; +∞ — закрепление.
    static string Kilo(double r) => double.IsPositiveInfinity(r) ? "∞" : (r / 1e3).ToString("0.###", CultureInfo.CurrentCulture);

    static string Rho(double rho) => double.IsPositiveInfinity(rho) ? "∞" : rho.ToString("0.###", CultureInfo.CurrentCulture);

    // Пояснение к μ по оси для отчёта проверки: форма, её λ, доля работы, ρ/λ и N_cr = λ·N; локальная задача — α и
    // N_cr = α·N (без смещения — в сравнении с λ·N формы).
    static string Note(string axis, RcBucklingPlaneLength p, RcBucklingUnitLength x, string combination, string unit)
    {
        var ci = CultureInfo.CurrentCulture;
        return p.Source switch
        {
            RcBucklingLengthSource.Mode => string.Format(ci, Loc.S("FemSteelAutoLefMode"), axis, p.Mu, unit, x.Length,
                combination, p.Mode + 1, p.Lambda, p.Share * 100, p.Rho / p.Lambda, -x.N / 1e3, p.Ncr / 1e3),
            RcBucklingLengthSource.Rocking when double.IsFinite(p.LocalNcr) => string.Format(ci,
                Loc.S("FemSteelAutoLefRockingLocal"), axis, p.Mu, unit, x.Length, combination, p.Mode + 1, p.Lambda,
                -x.N / 1e3, p.LocalNcr / 1e3, p.LocalAlpha),
            RcBucklingLengthSource.Rocking => string.Format(ci, Loc.S("FemSteelAutoLefRocking"), axis, unit, x.Length,
                combination, p.Mode + 1, p.Lambda),
            RcBucklingLengthSource.Local when p.LocalBraced => string.Format(ci, Loc.S("FemSteelAutoLefLocalBraced"), axis,
                p.Mu, unit, x.Length, combination, -x.N / 1e3, p.Ncr / 1e3, p.LocalAlpha, -p.Lambda * x.N / 1e3,
                p.Mode + 1, p.Lambda),
            RcBucklingLengthSource.Local => string.Format(ci, Loc.S("FemSteelAutoLefLocal"), axis, p.Mu, unit, x.Length,
                combination, -x.N / 1e3, p.Ncr / 1e3, p.LocalAlpha),
            _ => string.Format(ci, Loc.S("FemSteelAutoLefNotInvolved"), axis, unit, combination),
        };
    }
}
