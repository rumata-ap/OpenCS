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

/// <summary>Итог расчёта устойчивости: определяющие сочетания элементов и задачи по различным из них.</summary>
public sealed record FemSteelBucklingResult(RcBucklingModel Model, IReadOnlyList<RcBucklingGoverning> Governing,
    IReadOnlyList<FemSteelBucklingCase> Cases, IReadOnlyList<string> Report);

/// <summary>
/// Модель устойчивости схемы для автоматических расчётных длин стали (спека 2026-10-18, срез 1): вход CSfea схемы с
/// упругими сечениями (бетон брутто, как линейный расчёт), расчётные элементы — КонЭ или цепочки КЭ
/// (<see cref="FemBucklingUnits"/>), сочетания — стадии постановки (пакета — его варианты без длительных пар).
/// Каждый элемент дробится не менее чем на 4 КЭ; определяющее сочетание элемента — с наибольшим сжатием; задача
/// устойчивости решается по каждому различному определяющему сочетанию.
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
        CancellationToken ct = default)
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
        }
        return new FemSteelBucklingResult(model, governing, cases, report);
    }
}
