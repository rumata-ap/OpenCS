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
/// Расчёт устойчивости по сочетанию, определяющему для части элементов: <paramref name="Units"/> — индексы элементов
/// входа, для которых сочетание определяющее (п. 10.3.2 СП 16).
/// </summary>
public sealed record FemSteelBucklingCase(int Combination, string Tag, IReadOnlyList<int> Units, RcBucklingResult Result)
{
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
    /// <summary>Задача по определяющему сочетанию элемента <paramref name="unit"/>; null — сжатия нет.</summary>
    public FemSteelBucklingCase? CaseOf(int unit) =>
        Governing[unit].Combination is int c and >= 0 ? Cases.FirstOrDefault(x => x.Combination == c) : null;
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
/// Каждый элемент дробится не менее чем на 4 КЭ; определяющее сочетание элемента — с наибольшим сжатием; задача
/// устойчивости решается по каждому различному определяющему сочетанию, μ элементов — энергетическим разбором её форм
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

    /// <summary>Расчёт — в фоне.</summary>
    public static FemSteelBucklingResult Run(FemSteelBucklingInput input, RcBucklingOptions? options = null,
        CancellationToken ct = default, RcBucklingLengthOptions? lengthOptions = null)
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
        foreach (var group in governing.Select((g, i) => (g.Combination, Unit: i)).Where(x => x.Combination >= 0)
                     .GroupBy(x => x.Combination).OrderBy(g => g.Key))
        {
            ct.ThrowIfCancellationRequested();
            var stage = input.Combinations[group.Key];
            var r = RcBucklingAnalysis.Solve(model, stage.Terms, options, ct);
            var c = new FemSteelBucklingCase(group.Key, stage.Tag, group.Select(x => x.Unit).ToList(), r);
            cases.Add(c);
            string lambda = r.Lambda.Length > 0 ? r.Lambda[0].ToString("0.###", CultureInfo.CurrentCulture) : "—";
            report.Add($"Сочетание «{stage.Tag}»: определяющее для {c.Units.Count} эл., λ₁ = {lambda}"
                + (r.Converged ? "" : " (итерации не сошлись)") + (c.Unstable ? " — схема неустойчива, μ не назначаются." : "."));
            if (c.Unstable) continue;
            // Участие — относительно всех элементов схемы, назначение и локальные задачи — элементам, для которых
            // сочетание определяющее.
            var local = RcBucklingAnalysis.SolveLocal(r, input.UnitBeams, c.Units);
            ct.ThrowIfCancellationRequested();
            var all = RcBucklingLengths.Assign(r, input.UnitBeams, lengthOptions, local);
            foreach (int u in c.Units) lengths[u] = all[u];
        }
        int Count(RcBucklingLengthSource source) => lengths.Count(x => x != null && (x.AboutZ.Source == source || x.AboutY.Source == source));
        int byLocal = Count(RcBucklingLengthSource.Local), notInvolved = Count(RcBucklingLengthSource.NotInvolved);
        if (byLocal > 0)
            report.Add($"Элементов с N_cr по локальной задаче хотя бы в одной плоскости: {byLocal} (не участвуют в {options.Modes} формах или форма без смещения ниже).");
        if (notInvolved > 0)
            report.Add($"Элементов без формы хотя бы в одной плоскости (ни общей, ни локальной): {notInvolved} — μ в этой плоскости не назначен.");
        return new FemSteelBucklingResult(model, governing, cases, lengths, report);
    }

    /// <summary>
    /// Расчётные длины по тегам КЭ в осях СП 16: ось x сечения — местная z у КЭ своей схемы (сечение CScore осью Y по
    /// местной y, <see cref="RcBeamSection.CrossYAlongLocalY"/>), местная y — у импортного (ось x ‖ Y1); lef,x — изгиб
    /// вокруг оси x. Элементы без сжатия не попадают.
    /// </summary>
    public static Dictionary<string, FemSteelAutoLef> ElementLengths(FemSteelBucklingInput input, FemSteelBucklingResult result)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(result);
        var beams = input.Adapted!.Model.Beams.ToDictionary(b => b.Id);
        var map = new Dictionary<string, FemSteelAutoLef>(StringComparer.Ordinal);
        for (int i = 0; i < input.Units.Count; i++)
        {
            if (result.CaseOf(i) is not { } c) continue;
            var unit = input.Units[i];
            FemSteelAutoLef auto;
            if (result.Lengths[i] is not { } x)
            {
                string why = c.Unstable
                    ? string.Format(CultureInfo.CurrentCulture, Loc.S("FemSteelAutoLefUnstable"), c.Tag, c.Result.Lambda[0])
                    : Loc.S("FemSteelAutoLefNoLength");
                auto = new FemSteelAutoLef(unit.Label, 0, null, null, why, why);
            }
            else
            {
                bool xAboutZ = beams[input.UnitBeams[i][0]].Section.CrossYAlongLocalY;
                var (px, py) = xAboutZ ? (x.AboutZ, x.AboutY) : (x.AboutY, x.AboutZ);
                auto = new FemSteelAutoLef(unit.Label, x.Length, Mu(px), Mu(py),
                    Note("x", px, x, c.Tag, unit.Label), Note("y", py, x, c.Tag, unit.Label),
                    Details("x", px, x), Details("y", py, x));
            }
            foreach (string tag in unit.ElemTags) map[tag] = auto;
        }
        return map;
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
