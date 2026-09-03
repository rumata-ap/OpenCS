using System.Globalization;
using CScore;

namespace OpenCS.Reporting;

/// <summary>Контекст построения отчёта с расчётной задачей, результатом и иллюстрациями.</summary>
public sealed class ReportContext
{
    /// <summary>Расчётная задача.</summary>
    public CalcTask Task { get; }
    /// <summary>Результат расчёта.</summary>
    public CalcResult Result { get; }
    /// <summary>Сечение, использованное расчётной задачей; нужно для геометрии и материалов.</summary>
    public CrossSection? Section { get; }
    /// <summary>Встроенные SVG по именам, например stress и strain.</summary>
    public IReadOnlyDictionary<string, string> Images { get; }

    /// <summary>Создаёт контекст отчёта.</summary>
    public ReportContext(CalcTask task, CalcResult result,
        IReadOnlyDictionary<string, string>? images = null)
        : this(task, result, null, images)
    {
    }

    /// <summary>Создаёт контекст с моделью сечения и встроенными иллюстрациями.</summary>
    public ReportContext(CalcTask task, CalcResult result, CrossSection? section,
        IReadOnlyDictionary<string, string>? images = null)
    {
        Task = task;
        Result = result;
        Section = section;
        Images = images ?? new Dictionary<string, string>();
    }
}

/// <summary>Общий контракт поставщика отчёта для отдельного типа расчётной задачи.</summary>
public interface IReportProvider
{
    /// <summary>Проверяет, поддерживает ли поставщик тип задачи.</summary>
    bool CanHandle(CalcTask task);

    /// <summary>Строит нейтральный документ отчёта.</summary>
    ReportDocument Build(ReportContext context);
}

/// <summary>Поставщик расчётного отчёта одиночной задачи strain_state.</summary>
public sealed class StrainStateReportProvider : IReportProvider
{
    /// <inheritdoc/>
    public bool CanHandle(CalcTask task) => task.Kind == "strain_state";

    /// <inheritdoc/>
    public ReportDocument Build(ReportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!CanHandle(context.Task))
            throw new ArgumentException("Поставщик не поддерживает данный тип задачи.", nameof(context));

        var task = context.Task;
        var data = StrainStateReportData.Parse(context.Result.DataJson);
        string sectionId = context.Section?.Id.ToString(CultureInfo.InvariantCulture)
            ?? data.Section?.Id.ToString(CultureInfo.InvariantCulture)
            ?? "не загружено";
        string sectionTag = context.Section?.Tag
            ?? data.Section?.Tag
            ?? "модель сечения не передана";
        string title = string.IsNullOrWhiteSpace(task.Tag)
            ? "Расчётное обоснование НДС"
            : $"Расчётное обоснование НДС — {task.Tag}";

        var document = new ReportDocument(title)
            .Add(new ReportHeading(1, "Идентификация и единицы"))
            .Add(new ReportKeyValueTable(
            [
                ("Результат", $"{context.Result.Id}#{context.Result.TaskKind}; создан: {ValueOrDash(context.Result.Created)}"),
                ("Задача", $"{task.Num}#{task.Id}; {ValueOrDash(task.Tag)}"),
                ("Сечение", $"{task.SectionId}#{sectionId}; {sectionTag}"),
                ("Вид расчёта", task.CalcType.ToString()),
                ("Статус результата", context.Result.Status),
                ("Единицы", "координаты и размеры — м; координаты арматуры — мм; площади — м²; ε — безразмерная; κ — 1/м; σ и E — МПа; N — кН; M — кН·м; D — кН·м²/кН"),
            ]))
            .Add(new ReportParagraph("Все числовые результаты ниже приведены с единицами: силы — кН, моменты — кН·м, координаты — м, напряжения и модули — МПа, кривизны — 1/м."))
            .Add(new ReportHeading(1, "Исходные данные"))
            .Add(new ReportKeyValueTable(
            [
                ("Тип задачи", task.Kind),
                ("Вид расчёта", task.CalcType.ToString()),
                ("Статус", context.Result.Status),
                ("Сходимость", data.Converged ? "достигнута" : "не достигнута"),
                ("Итерации Ньютона", data.Iterations.ToString(CultureInfo.InvariantCulture)),
                ("Невязка, кН", Force(data.Residual)),
                ("Версия формул", data.FormulaVersion)
            ]))
            .Add(new ReportTable(
                ["Величина", "Целевое значение", "Результат"],
                [
                    (IReadOnlyList<string>)["N, кН", Force(data.TargetN), Force(data.ResultN)],
                    (IReadOnlyList<string>)["Mx, кН·м", Moment(data.TargetMx), Moment(data.ResultMx)],
                    (IReadOnlyList<string>)["My, кН·м", Moment(data.TargetMy), Moment(data.ResultMy)]
                ]))
            .Add(new ReportHeading(1, "Плоскость деформаций"))
            .Add(new ReportFormula(
                "(8.29)",
                "ε_bi = ε₀ + ky·y_bi + kz·x_bi",
                $"ε₀ = {F(data.E0)}; ky = {Curvature(data.Ky)}; kz = {Curvature(data.Kz)}",
                $"бетон: εmin = {F(data.Extrema.ConcreteMin)}, εmax = {F(data.Extrema.ConcreteMax)}"))
            .Add(new ReportFormula(
                "(8.30)",
                "ε_si = ε₀ + ky·y_si + kz·x_si",
                $"ε₀ = {F(data.E0)}; ky = {Curvature(data.Ky)}; kz = {Curvature(data.Kz)}",
                $"арматура: εmin = {F(data.Extrema.SteelMin)}, εmax = {F(data.Extrema.SteelMax)}"))
            .Add(new ReportFormula(
                "(8.31)", "σ_bi = E_b·ν_b·ε_bi",
                "напряжение бетона определяется диаграммой σ(ε)",
                "учтено в интеграле равновесия"))
            .Add(new ReportFormula(
                "(8.32)", "σ_si = E_s·ν_s·ε_si",
                "напряжение арматуры определяется диаграммой σ(ε)",
                "учтено в интеграле равновесия"))
            .Add(new ReportFormula(
                "(8.35)–(8.36)", "ν_b = σ_b/(E_b·ε_b);  ν_s = σ_s/(E_s·ε_s)",
                "коэффициенты определяются текущим участком диаграммы",
                "использованы текущие секущие модули"))
            .Add(new ReportFormula(
                "(8.37)–(8.38)", "ε_b,min ≤ ε_bi ≤ ε_b,max;  ε_s,min ≤ ε_si ≤ ε_s,max",
                "предельные деформации задаются характеристиками материалов",
                "проверка выполняется по диаграммам материалов"));

        document
            .Add(new ReportHeading(1, "Проверка равновесия"))
            .Add(new ReportFormula(
                "(8.26)", "Mx = Σ(σ_b·A_b·y_b) + Σ(σ_s·A_s·y_s)",
                $"Mx = {F(data.ResultMx)}",
                $"Mx = {F(data.Equilibrium.Mx)} кН·м"))
            .Add(new ReportFormula(
                "(8.27)", "My = Σ(σ_b·A_b·x_b) + Σ(σ_s·A_s·x_s)",
                $"My = {F(data.ResultMy)}",
                $"My = {F(data.Equilibrium.My)} кН·м"))
            .Add(new ReportFormula(
                "(8.28)", "N = Σ(σ_b·A_b) + Σ(σ_s·A_s)",
                $"N = {F(data.ResultN)}",
                $"N = {F(data.Equilibrium.N)} кН"));

        var d = data.Stiffness;
        document
            .Add(new ReportHeading(1, "Матрица жёсткости по СП 63"))
            .Add(new ReportParagraph($"Источник интегрирования: {d.Source}. Порядок матрицы: [Mx, My, N] × [ky, kz, ε₀]."))
            .Add(new ReportParagraph("Размерности D: D11, D12, D22 — кН·м²; D13, D23 — кН·м; D33 — кН. Поэтому произведения D·[ky, kz, ε₀] дают соответственно [кН·м, кН·м, кН]."))
            .Add(new ReportFormula(
                "(8.39)", "Mx = D11·ky + D12·kz + D13·ε₀",
                $"Mx = {F(d.D11)}·ky + {F(d.D12)}·kz + {F(d.D13)}·ε₀",
                $"Mx = {F(data.Equilibrium.Mx)} кН·м"))
            .Add(new ReportFormula(
                "(8.40)", "My = D12·ky + D22·kz + D23·ε₀",
                $"My = {F(d.D12)}·ky + {F(d.D22)}·kz + {F(d.D23)}·ε₀",
                $"My = {F(data.Equilibrium.My)} кН·м"))
            .Add(new ReportFormula(
                "(8.41)", "N = D13·ky + D23·kz + D33·ε₀",
                $"N = {F(d.D13)}·ky + {F(d.D23)}·kz + {F(d.D33)}·ε₀",
                $"N = {F(data.Equilibrium.N)} кН"))
            .Add(new ReportTable(
                ["Элемент", "Формула СП 63", "Результат"],
                [
                    (IReadOnlyList<string>)["D11", "Σ(E_bν_bA_by_b²) + Σ(E_sν_sA_sy_s²)", F(d.D11)],
                    (IReadOnlyList<string>)["D12", "Σ(E_bν_bA_bx_by) + Σ(E_sν_sA_sx_sy)", F(d.D12)],
                    (IReadOnlyList<string>)["D13", "Σ(E_bν_bA_by) + Σ(E_sν_sA_sy)", F(d.D13)],
                    (IReadOnlyList<string>)["D22", "Σ(E_bν_bA_bx²) + Σ(E_sν_sA_sx²)", F(d.D22)],
                    (IReadOnlyList<string>)["D23", "Σ(E_bν_bA_bx) + Σ(E_sν_sA_sx)", F(d.D23)],
                    (IReadOnlyList<string>)["D33", "Σ(E_bν_bA_b) + Σ(E_sν_sA_s)", F(d.D33)]
                ]))
            .Add(new ReportFormula(
                "(8.42)", "D11 = Σ(E_bν_bA_by_b²) + Σ(E_sν_sA_sy_s²)",
                $"источник: {d.Source}", $"D11 = {F(d.D11)}"))
            .Add(new ReportFormula(
                "(8.43)", "D12 = Σ(E_bν_bA_bx_by) + Σ(E_sν_sA_sx_sy)",
                $"источник: {d.Source}", $"D12 = {F(d.D12)}"))
            .Add(new ReportFormula(
                "(8.44)", "D13 = Σ(E_bν_bA_by) + Σ(E_sν_sA_sy)",
                $"источник: {d.Source}", $"D13 = {F(d.D13)}"))
            .Add(new ReportFormula(
                "(8.45)", "D22 = Σ(E_bν_bA_bx²) + Σ(E_sν_sA_sx²)",
                $"источник: {d.Source}", $"D22 = {F(d.D22)}"))
            .Add(new ReportFormula(
                "(8.46)", "D23 = Σ(E_bν_bA_bx) + Σ(E_sν_sA_sx)",
                $"источник: {d.Source}", $"D23 = {F(d.D23)}"))
            .Add(new ReportFormula(
                "(8.47)", "D33 = Σ(E_bν_bA_b) + Σ(E_sν_sA_s)",
                $"источник: {d.Source}", $"D33 = {F(d.D33)}"));

        var j = data.Jacobian;
        document
            .Add(new ReportHeading(1, "Якобиан Ньютона"))
            .Add(new ReportParagraph($"Строки: [{string.Join(", ", j.Rows)}]; столбцы: [{string.Join(", ", j.Columns)}]; схема: {j.Scheme}; h = {F(j.Step)}."))
            .Add(new ReportParagraph("Размерности якобиана: строки N — кН, Mx/My — кН·м; столбцы ε₀ — безразмерный, ky/kz — 1/м. Размерность каждой производной определяется парой строки и столбца."))
            .Add(new ReportTable(
                ["", "e0", "ky", "kz"],
                JacobianRows(j)));

        AddExtendedSummary(document, context, data, new Kurvature
        {
            e0 = data.E0,
            ky = data.Ky,
            kz = data.Kz
        });

        if (!data.Converged)
            document.Add(new ReportWarning("Расчёт не достиг заданного критерия сходимости; результаты требуют инженерной проверки."));

        if (context.Images.TryGetValue("strain", out var strainSvg))
            document.Add(new ReportImage("Карта деформаций ε", strainSvg));
        if (context.Images.TryGetValue("stress", out var stressSvg))
            document.Add(new ReportImage("Карта напряжений σ", stressSvg));

        return document;
    }

    static void AddExtendedSummary(ReportDocument document, ReportContext context,
        StrainStateReportData data, Kurvature k)
    {
        document.Add(new ReportHeading(1, "Сводка результата"))
            .Add(new ReportKeyValueTable(
            [
                ("Плоскость деформаций", $"ε₀ = {F(data.E0)}; ky = {Curvature(data.Ky)}; kz = {Curvature(data.Kz)}"),
                ("Бетон, εmin…εmax", $"{F(data.Extrema.ConcreteMin)} … {F(data.Extrema.ConcreteMax)}"),
                ("Арматура, εmin…εmax", $"{F(data.Extrema.SteelMin)} … {F(data.Extrema.SteelMax)}"),
                ("Сходимость", $"{data.Iterations} итераций; невязка {Force(data.Residual)}"),
            ]));

        AddEta(document, data.Eta);
        AddPrestress(document, data.Prestress);
        AddStiffnessSummary(document, context.Section, k, context.Task.CalcType);

        var rebar = context.Section != null
            ? RebarRows(context.Section, k, context.Task.CalcType)
            : data.Rebar.Select(ToReportRebar).ToList();
        if (rebar.Count > 0)
        {
            document.Add(new ReportHeading(1, "Арматура"))
                .Add(new ReportParagraph("Координаты и размеры стержней — мм; площадь — мм²; ε — безразмерная; σ и Eсек — МПа."))
                .Add(new ReportTable(
                    ["№", "Группа", "Материал", "x, мм", "y, мм", "d, мм"],
                    rebar.Select(row => (IReadOnlyList<string>)
                    [
                        row.Num.ToString(CultureInfo.InvariantCulture),
                        row.Group,
                        row.Material,
                        F(row.Xmm),
                        F(row.Ymm),
                        F(row.DiameterMm)
                    ]).ToList()))
                .Add(new ReportTable(
                    ["№", "Группа", "A, мм²", "ε", "σ, МПа", "Eсек, МПа"],
                    rebar.Select(row => (IReadOnlyList<string>)
                    [
                        row.Num.ToString(CultureInfo.InvariantCulture),
                        row.Group,
                        F(row.AreaMm2),
                        F(row.Eps),
                        F(row.SigmaMpa),
                        F(row.SecantModulusMpa)
                    ]).ToList()));
        }

        if (context.Section is not { } section)
        {
            document.Add(new ReportWarning("Модель сечения не передана в контекст результата: геометрия, состав частей и диаграммы материалов недоступны."));
            return;
        }

        var areas = section.EnumerateAreas(k).Select(pair => pair.area).ToList();
        var geometryRows = areas.Select((area, index) =>
        {
            var props = AreaProps(area);
            return (IReadOnlyList<string>)[
                (index + 1).ToString(CultureInfo.InvariantCulture),
                area.Tag,
                area.Category.ToString(),
                area.Material?.Tag ?? "не задан",
                area.HostArea?.Tag ?? (area.HostAreaId?.ToString(CultureInfo.InvariantCulture) ?? "—"),
                F(props.A),
                F(props.Centroid?.X ?? 0),
                F(props.Centroid?.Y ?? 0),
                area.Contours.Count.ToString(CultureInfo.InvariantCulture),
                area.Fibers.Count.ToString(CultureInfo.InvariantCulture)
            ];
        }).ToList();

        document.Add(new ReportHeading(1, "Геометрия сечения"))
            .Add(new ReportParagraph("Контуры и координаты приведены в метрах; диаметры и координаты точечной арматуры в таблице — в миллиметрах."))
            .Add(new ReportImage("Геометрия сечения и его частей",
                new CrossSectionReportSvgRenderer().Render(section)))
            .Add(new ReportTable(
                ["№", "Часть", "Категория", "Материал", "Бетон-носитель"],
                geometryRows.Select(row => (IReadOnlyList<string>)[row[0], row[1], row[2], row[3], row[4]]).ToList()))
            .Add(new ReportTable(
                ["№", "A, м²", "xc, м", "yc, м", "Контуры", "Фибры"],
                geometryRows.Select(row => (IReadOnlyList<string>)[row[0], row[5], row[6], row[7], row[8], row[9]]).ToList()));

        var materialRows = new List<IReadOnlyList<string>>();
        var diagrams = new List<(string Title, Diagramm Diagram)>();
        var seenDiagrams = new HashSet<Diagramm>();
        foreach (var area in areas)
        {
            var material = area.Material;
            area.Diagramms.TryGetValue(context.Task.CalcType, out var diagram);
            materialRows.Add(
            [
                area.Tag,
                material?.Tag ?? "не задан",
                material?.Type.ToString() ?? "—",
                area.DiagrammType.ToString(),
                diagram?.Tag ?? "диаграмма не разрешена",
                diagram == null ? "—" : context.Task.CalcType.ToString()
            ]);
            if (diagram != null && seenDiagrams.Add(diagram))
                diagrams.Add(($"{material?.Tag ?? area.Tag} — {diagram.Tag} ({context.Task.CalcType})", diagram));
        }

        document.Add(new ReportHeading(1, "Материалы и диаграммы"))
            .Add(new ReportParagraph("Напряжения на графиках приведены в МПа, деформации ε — безразмерные. Для арматуры, расположенной в бетоне, отображается фактическая разностная диаграмма σст − σб."))
            .Add(new ReportTable(
                ["Часть", "Материал", "Тип", "Вид расчёта"],
                materialRows.Select(row => (IReadOnlyList<string>)[row[0], row[1], row[2], row[5]]).ToList()))
            .Add(new ReportTable(
                ["Часть", "Диаграмма", "Фактическая диаграмма"],
                materialRows.Select(row => (IReadOnlyList<string>)[row[0], row[3], row[4]]).ToList()));

        var materials = areas.Select(area => area.Material)
            .Where(material => material != null)
            .Distinct()
            .Cast<Material>()
            .ToList();
        if (materials.Count > 0)
        {
            var characteristicRows = materials.Select(material =>
                {
                    var chars = material.GetChars(context.Task.CalcType);
                    return (IReadOnlyList<string>)
                    [
                        material.Tag,
                        material.Type.ToString(),
                        F((chars?.E ?? material.E) / 1000.0),
                        F((chars?.Fc ?? 0) / 1000.0),
                        F((chars?.Ft ?? 0) / 1000.0),
                        F((chars?.Ry ?? 0) / 1000.0),
                        F((chars?.Ru ?? 0) / 1000.0)
                    ];
                }).ToList();
            document.Add(new ReportTable(
                ["Материал", "Тип", "E, МПа", "Fc, МПа", "Ft, МПа"],
                characteristicRows.Select(row => (IReadOnlyList<string>)[row[0], row[1], row[2], row[3], row[4]]).ToList()))
                .Add(new ReportTable(
                    ["Материал", "Ry, МПа", "Ru, МПа"],
                    characteristicRows.Select(row => (IReadOnlyList<string>)[row[0], row[5], row[6]]).ToList()));
        }

        foreach (var (diagramTitle, diagram) in diagrams)
            document.Add(new ReportImage($"Диаграмма σ(ε): {diagramTitle}",
                new MaterialDiagramSvgRenderer().Render(diagram, diagramTitle)));
    }

    static void AddEta(ReportDocument document, StrainStateEtaData? eta)
    {
        if (eta == null) return;
        document.Add(new ReportHeading(1, "Влияние прогиба"))
            .Add(new ReportParagraph($"Режим: {ValueOrDash(eta.Mode)}; исходные моменты: Mx = {Moment(eta.MxOriginal)}, My = {Moment(eta.MyOriginal)}."))
            .Add(new ReportTable(
                ["Направление", "l0, м", "h, м", "l0/h"],
                [
                    (IReadOnlyList<string>)["X", F(eta.L0x), F(eta.Hx), F(eta.SlendernessX ?? 0)],
                    (IReadOnlyList<string>)["Y", F(eta.L0y), F(eta.Hy), F(eta.SlendernessY ?? 0)]
                ]))
            .Add(new ReportTable(
                ["Направление", "D, кН·м²", "Ncr, кН", "η", "Статус"],
                [
                    (IReadOnlyList<string>)["X", F(eta.DX ?? 0), F(eta.NcrX ?? 0), F(eta.EtaX), eta.StableX ? "устойчиво" : "неустойчиво"],
                    (IReadOnlyList<string>)["Y", F(eta.DY ?? 0), F(eta.NcrY ?? 0), F(eta.EtaY), eta.StableY ? "устойчиво" : "неустойчиво"]
                ]));
        if (eta.EtaHistoryX.Length > 0 || eta.EtaHistoryY.Length > 0)
            document.Add(new ReportParagraph($"История η: X — {string.Join(" → ", eta.EtaHistoryX.Select(F))}; Y — {string.Join(" → ", eta.EtaHistoryY.Select(F))}."));
    }

    static void AddPrestress(ReportDocument document, PrestressActionsJsonModel? prestress)
    {
        if (prestress == null || prestress.Groups.Count == 0) return;
        var groupRows = prestress.Groups.Select(group => (IReadOnlyList<string>)
            [
                group.Tag,
                F(group.AreaM2),
                Dimension(group.X),
                Dimension(group.Y),
                F(group.SigSp),
                F(group.GammaSp),
                F(group.SigActual),
                F(group.SigLimit)
            ]).ToList();

        document.Add(new ReportHeading(1, "Преднапряжение"))
            .Add(new ReportParagraph($"Точка отсчёта моментов: x = {Dimension(prestress.Reference.X)}, y = {Dimension(prestress.Reference.Y)}. Все действия: N — кН, Mx/My — кН·м."))
            .Add(new ReportTable(
                ["Состояние", "N, кН", "Mx, кН·м", "My, кН·м"],
                [
                    (IReadOnlyList<string>)["Номинальное", Force(prestress.Nominal.N), Moment(prestress.Nominal.Mx), Moment(prestress.Nominal.My)],
                    (IReadOnlyList<string>)["Эффективное", Force(prestress.Effective.N), Moment(prestress.Effective.Mx), Moment(prestress.Effective.My)],
                    (IReadOnlyList<string>)["Фактическое", Force(prestress.Actual.N), Moment(prestress.Actual.Mx), Moment(prestress.Actual.My)]
                ]))
            .Add(new ReportTable(
                ["Группа", "A, м²", "x, м", "y, м"],
                groupRows.Select(row => (IReadOnlyList<string>)[row[0], row[1], row[2], row[3]]).ToList()))
            .Add(new ReportTable(
                ["Группа", "σsp, МПа", "γsp", "σфакт., МПа", "σпредел, МПа"],
                groupRows.Select(row => (IReadOnlyList<string>)[row[0], row[4], row[5], row[6], row[7]]).ToList()));
        if (prestress.HasGroupsAboveStrength)
            document.Add(new ReportWarning("Для одной или нескольких групп преднапряжения заданное напряжение превышает расчётное сопротивление."));
    }

    static void AddStiffnessSummary(ReportDocument document, CrossSection? section,
        Kurvature k, CalcType calc)
    {
        if (section == null) return;
        SectionStiffnessResult? result;
        try
        {
            result = SectionStiffnessCalculator.Compute(section, k, calc);
        }
        catch (Exception ex)
        {
            document.Add(new ReportWarning($"Расширенные характеристики жёсткости не рассчитаны: {ex.Message}"));
            return;
        }

        if (result is not SectionStiffnessResult value) return;
        document.Add(new ReportHeading(1, "Секущая и упругая жёсткости"))
            .Add(new ReportTable(
                ["Характеристика", "Секущая", "Упругая", "Единица"],
                [
                    (IReadOnlyList<string>)["Xc", F(value.Xc_mm), "—", "мм"],
                    (IReadOnlyList<string>)["Yc", F(value.Yc_mm), "—", "мм"],
                    (IReadOnlyList<string>)["EA", F(value.EA_kN), F(value.EAel_kN), "кН"],
                    (IReadOnlyList<string>)["EIy₀", F(value.EIy0_kNm2), "—", "кН·м²"],
                    (IReadOnlyList<string>)["EIz₀", F(value.EIz0_kNm2), "—", "кН·м²"],
                    (IReadOnlyList<string>)["EIy (ц.т.)", F(value.EIyc_kNm2), F(value.EIyel_kNm2), "кН·м²"],
                    (IReadOnlyList<string>)["EIz (ц.т.)", F(value.EIzc_kNm2), F(value.EIzel_kNm2), "кН·м²"],
                    (IReadOnlyList<string>)["φEA", F(value.PhiEA), "—", "безразм."],
                    (IReadOnlyList<string>)["φEIy", F(value.PhiEIy), "—", "безразм."],
                    (IReadOnlyList<string>)["φEIz", F(value.PhiEIz), "—", "безразм."]
                ]));
    }

    static List<ReportRebarRow> RebarRows(CrossSection section, Kurvature k, CalcType calc)
    {
        var rows = new List<ReportRebarRow>();
        int number = 1;
        foreach (var (area, effectiveK) in section.EnumerateAreas(k))
        {
            foreach (var fiber in area.Fibers.Where(f => f.TypeFiber == FiberType.point))
            {
                double eps = fiber.Eps;
                double sigmaMpa = fiber.Sig / 1000.0;
                double eMpa = fiber.E > 0 ? fiber.E / 1000.0 : 0;
                rows.Add(new ReportRebarRow(number++, area.Tag, area.Material?.Tag ?? "не задан",
                    fiber.X * 1000.0, fiber.Y * 1000.0, fiber.Diameter * 1000.0,
                    fiber.Area * 1e6, eps, sigmaMpa, eMpa));
            }
        }
        return rows;
    }

    static ReportRebarRow ToReportRebar(StrainStateRebarData row)
        => new(row.Num, row.Group, row.Material, row.Xmm, row.Ymm, row.DiameterMm, row.AreaMm2,
            row.Eps, row.SigmaMpa,
            Math.Abs(row.Eps) > 1e-20 ? Math.Abs(row.SigmaMpa / row.Eps) : 0);

    static GeoProps AreaProps(MaterialArea area)
    {
        if (area.Hull != null)
        {
            var props = new GeoProps(area.Hull);
            foreach (var hole in area.Holes)
                props -= new GeoProps(hole);
            return props;
        }
        return new GeoProps(area);
    }

    sealed record ReportRebarRow(int Num, string Group, string Material, double Xmm, double Ymm,
        double DiameterMm, double AreaMm2, double Eps, double SigmaMpa,
        double SecantModulusMpa);

    static IReadOnlyList<IReadOnlyList<string>> JacobianRows(StrainStateJacobianData jacobian)
    {
        var rows = new List<IReadOnlyList<string>>();
        for (int i = 0; i < jacobian.Values.Length; i++)
        {
            var values = jacobian.Values[i];
            rows.Add(
            [
                i < jacobian.Rows.Length ? jacobian.Rows[i] : $"row {i + 1}",
                values.Length > 0 ? F(values[0]) : "—",
                values.Length > 1 ? F(values[1]) : "—",
                values.Length > 2 ? F(values[2]) : "—"
            ]);
        }
        return rows;
    }

    static string F(double value)
        => double.IsFinite(value)
            ? value.ToString("G8", CultureInfo.InvariantCulture)
            : "—";

    static string Force(double value) => $"{F(value)} кН";
    static string Moment(double value) => $"{F(value)} кН·м";
    static string Curvature(double value) => $"{F(value)} 1/м";
    static string Dimension(double value) => $"{F(value)} м";
    static string ValueOrDash(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
}
