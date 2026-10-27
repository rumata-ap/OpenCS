using System.Globalization;
using CScore;
using CScore.History;

namespace OpenCS.Reporting;

/// <summary>
/// Отчёт по истории нагружения стадийного сечения: исходные данные (части, этапы, роли), сводка,
/// график «момент–кривизна» и сокращённая таблица (концы этапов, M_lim, M_peak).
/// </summary>
public sealed class LoadHistoryReportProvider : IReportProvider
{
    /// <inheritdoc/>
    public string TaskKind => "load_history";

    /// <inheritdoc/>
    public IReadOnlyCollection<string> SupportedKinds => [TaskKind];

    /// <inheritdoc/>
    public bool CanHandle(CalcTask task) => SupportedKinds.Contains(task.Kind, StringComparer.Ordinal);

    /// <inheritdoc/>
    public ReportDocument Build(ReportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!CanHandle(context.Task))
            throw new ArgumentException("Поставщик не поддерживает данный тип задачи.", nameof(context));

        var data = LoadHistoryResultData.Parse(context.Result.DataJson);
        var p = LoadHistoryParams.Parse(context.Task.ParamsJson);
        string tag = string.IsNullOrWhiteSpace(context.Task.Tag) ? "без метки" : context.Task.Tag;
        var document = new ReportDocument($"История нагружения — {tag}");
        SectionReportSections.Identification(document, context, "координаты — м; c — 1/м; N — кН; M — кН·м");

        if (data.Error != null)
        {
            document.Add(new ReportWarning($"Результат не прочитан: {data.Error}"));
            return document;
        }

        AddInput(document, data, p);
        AddRoles(document, data);
        AddSummary(document, data);
        AddChart(document, data);
        AddShortTable(document, data);
        if (data.Hysteresis is null)
            document.Add(new ReportParagraph(
                "Материал принят нелинейно-упругим: разгрузка идёт по кривой нагружения, остаточные деформации не учитываются."));
        return document;
    }

    static void AddInput(ReportDocument document, LoadHistoryResultData data, LoadHistoryParams p)
    {
        document.Add(new ReportHeading(1, "Исходные данные"));
        document.Add(new ReportTable(["№", "Часть"],
            data.PartTags.Select((t, i) => (IReadOnlyList<string>)[(i + 1).ToString(CultureInfo.InvariantCulture), t]).ToList()));

        var stages = p.Stages.Select((st, i) => (IReadOnlyList<string>)
        [
            (i + 1).ToString(CultureInfo.InvariantCulture),
            st.Kind == StageKind.Failure ? "доведение до разрушения" : st.Tag,
            st.Kind == StageKind.Failure ? "по кривизне" : "нагрузка",
            st.ActivatePart is int part ? $"{part + 1} {PartTag(data, part)}".TrimEnd() : "—",
            st.Kind == StageKind.Failure ? "(с предыдущего этапа)" : ForceValue(st.Force, f => f.N),
            ForceValue(st.Force, f => f.Mx),
            ForceValue(st.Force, f => f.My),
            st.Kind == StageKind.Failure ? p.Failure.Steps.ToString(CultureInfo.InvariantCulture) : st.Steps.ToString(CultureInfo.InvariantCulture),
        ]).ToList();
        document.Add(new ReportTable(["№", "Этап", "Тип", "Включается часть", "N, кН", "Mx, кН·м", "My, кН·м", "Шагов"], stages));

        document.Add(new ReportKeyValueTable(
        [
            ("Остановка по падению момента", $"{F(p.Failure.DropRatio)}·M_peak"),
            ("Предельная кривизна c_max", p.Failure.CMax is double c ? $"{F(c)} 1/м" : "авто"),
            ("Длина спада Δε (роль «Разрушается»)", F(p.Failure.SofteningStrain)),
            ("При несходимости шага по нагрузке", data.Fallback == nameof(LoadStepFallback.WithoutConcreteTension)
                ? "шаг решается без растяжения бетона" : "шаг проходится по кривизне"),
            ("Модель материала", MaterialModel(data)),
            ("Деформации стадий", data.StrainMode == "fibers" ? "по волокнам" : "по плоскости включения"),
            ("Сравнение с расчётом без истории", data.StepsNoHistory != null ? "да" : "нет"),
        ], "Параметр", "Значение"));
    }

    static string MaterialModel(LoadHistoryResultData data)
    {
        if (data.Hysteresis is not { } h) return "нелинейно-упругая";
        string steel = h.Steel == CScore.Hysteresis.SteelHysteresisModel.Bilinear
            ? "билинейная"
            : $"Menegotto–Pinto (R0 = {F(h.R0)}; cR1 = {F(h.CR1)}; cR2 = {F(h.CR2)})";
        string concrete = h.Concrete == CScore.Hysteresis.ConcreteHysteresisModel.NonlinearElastic
            ? "нелинейно-упругий"
            : "Karsan–Jirsa";
        return $"гистерезис: бетон {concrete}, сталь {steel}";
    }

    static void AddRoles(ReportDocument document, LoadHistoryResultData data)
    {
        if (data.NonStandardRoles.Count == 0) return;
        var list = data.NonStandardRoles.Select(r =>
            $"часть {r.PartIndex + 1} {PartTag(data, r.PartIndex)}: бетон — {Role(r.Concrete)}, арматура — {Role(r.Rebar)}");
        document.Add(new ReportWarning(
            "Критерий предельного состояния отличается от п. 8.1.30 СП 63: " + string.Join("; ", list) + "."));
    }

    static void AddSummary(ReportDocument document, LoadHistoryResultData data)
    {
        document.Add(new ReportHeading(1, "Сводка"));
        var rows = new List<(string, string)>
        {
            ("M_lim с историей", Moment(data.With.MLim)),
            ("M_peak с историей", Moment(data.With.MPeak)),
        };
        if (data.Without is { } w)
        {
            rows.Add(("M_lim без истории", Moment(w.MLim)));
            rows.Add(("M_peak без истории", Moment(w.MPeak)));
            rows.Add(("Отношение M_lim с историей / без", Ratio(data.With.MLim, w.MLim)));
            rows.Add(("Отношение M_peak с историей / без", Ratio(data.With.MPeak, w.MPeak)));
        }
        if (data.With.GoverningPart is int part)
            rows.Add(("Определяет предел", $"часть {part + 1} {PartTag(data, part)}, "
                + (data.With.GoverningMaterial == "rebar" ? "арматура" : "бетон")
                + $", точка ({F(data.With.GoverningX ?? 0)}; {F(data.With.GoverningY ?? 0)}) м"));
        rows.Add(("Причина остановки (с историей)", StopReason(data.With.StopReason)));
        if (data.Without is { } wo)
            rows.Add(("Причина остановки (без истории)", StopReason(wo.StopReason)));
        document.Add(new ReportKeyValueTable(rows, "Параметр", "Значение"));
        if (data.Status != "ok")
            document.Add(new ReportWarning("История выполнена не полностью: сохранены сошедшиеся шаги."));
    }

    static void AddChart(ReportDocument document, LoadHistoryResultData data)
    {
        var series = new List<(string, string, IReadOnlyList<(double X, double Y)>)>
        {
            ("с историей", "#2563eb", Curve(data.Steps)),
        };
        if (data.StepsNoHistory != null)
            series.Add(("без истории", "#ea580c", Curve(data.StepsNoHistory)));

        var marks = new List<(string Label, double X, double Y)>();
        for (int i = 1; i < data.Steps.Count; i++)
            if (data.Steps[i].Parts > data.Steps[i - 1].Parts)
                marks.Add(($"включение части {data.Steps[i].Parts}", data.Steps[i].C, data.Steps[i].Mu));
        if (data.Steps.FindIndex(s => s.LimitPoint) is int li and >= 0)
            marks.Add(("M_lim", data.Steps[li].C, data.Steps[li].Mu));
        if (PeakIndex(data.Steps) is int pi and >= 0)
            marks.Add(("M_peak", data.Steps[pi].C, data.Steps[pi].Mu));

        document.Add(new ReportHeading(1, "Момент–кривизна"));
        document.Add(new ReportImage("moment_curvature", MomentCurvatureSvgRenderer.Render(series, marks,
            "Момент–кривизна", "c — кривизна вдоль направления изгиба, 1/м", "M — момент вдоль направления изгиба, кН·м")));
    }

    static void AddShortTable(ReportDocument document, LoadHistoryResultData data)
    {
        var steps = data.Steps;
        var pick = new List<int>();
        for (int i = 0; i < steps.Count; i++)
            if (steps[i].Kind == "load" && (i == steps.Count - 1 || steps[i + 1].Stage != steps[i].Stage))
                pick.Add(i);
        int limit = steps.FindIndex(s => s.LimitPoint);
        if (limit >= 0 && !pick.Contains(limit)) pick.Add(limit);
        int peak = PeakIndex(steps);
        if (peak >= 0 && !pick.Contains(peak)) pick.Add(peak);
        pick.Sort();

        var rows = pick.Select(i =>
        {
            var s = steps[i];
            string stage = s.Kind == "failure" ? "разрушение" : s.Stage < data.StageTags.Count ? data.StageTags[s.Stage] : (s.Stage + 1).ToString(CultureInfo.InvariantCulture);
            string note = i == limit ? " (M_lim)" : i == peak ? " (M_peak)" : "";
            return (IReadOnlyList<string>)
            [
                stage + note, s.Step.ToString(CultureInfo.InvariantCulture), s.Parts.ToString(CultureInfo.InvariantCulture),
                F(s.N), F(s.Mx), F(s.My), F(s.C), F(s.Mu),
                F(s.PartStates.Count == 0 ? 0 : s.PartStates.Max(x => x.EtaB)),
                F(s.PartStates.Count == 0 ? 0 : s.PartStates.Max(x => x.EtaS)),
            ];
        }).ToList();
        document.Add(new ReportHeading(1, "Ключевые шаги (с историей)"));
        document.Add(new ReportTable(["Этап", "Шаг", "Частей", "N, кН", "Mx, кН·м", "My, кН·м", "c, 1/м", "M_u, кН·м", "Кисп бетона", "Кисп арматуры"], rows));
    }

    static IReadOnlyList<(double X, double Y)> Curve(IReadOnlyList<HistoryStepDto> steps) =>
        [(0, 0), .. steps.Select(s => (s.C, s.Mu))];

    static int PeakIndex(IReadOnlyList<HistoryStepDto> steps)
    {
        int best = -1;
        for (int i = 0; i < steps.Count; i++)
            if (steps[i].Converged && (best < 0 || Math.Abs(steps[i].Mu) > Math.Abs(steps[best].Mu))) best = i;
        return best;
    }

    static string PartTag(LoadHistoryResultData data, int part) =>
        part < data.PartTags.Count ? $"«{data.PartTags[part]}»" : "";

    static string Role(PartLimitRole r) => r switch
    {
        PartLimitRole.Ignore => "не ограничивает",
        PartLimitRole.Fails => "разрушается",
        _ => "ограничивает",
    };

    static string StopReason(string code) => code switch
    {
        "completed" => "все этапы выполнены",
        "load_not_converged" => "шаг по нагрузке не сошёлся",
        "drop" => "падение момента ниже заданной доли пика",
        "c_max" => "достигнута предельная кривизна c_max",
        "failure_not_converged" => "потеря сходимости при доведении до разрушения",
        _ => code,
    };

    static string ForceValue(StageForce f, Func<StageForce, double> pick) =>
        f.Mode == "manual" ? F(pick(f)) : $"строка набора #{f.ForceItemId}";

    static string Ratio(double? a, double? b) =>
        a is double x && b is double y && Math.Abs(y) > 1e-12 ? F(x / y) : "—";

    static string Moment(double? v) => v is double d ? $"{F(d)} кН·м" : "—";

    static string F(double v) => SectionReportSections.F(v);
}
