using System.Globalization;
using CScore;

namespace OpenCS.Reporting;

/// <summary>Поставщик расчётного отчёта одиночной задачи strain_state.</summary>
public sealed class StrainStateReportProvider : IReportProvider
{
    /// <inheritdoc/>
    public string TaskKind => "strain_state";

    /// <inheritdoc/>
    public IReadOnlyCollection<string> SupportedKinds => [TaskKind];

    /// <inheritdoc/>
    public bool CanHandle(CalcTask task) => SupportedKinds.Contains(task.Kind, StringComparer.Ordinal);

    /// <inheritdoc/>
    public IReadOnlyList<ReportImageRequest> DescribeImages(CalcTask task, CalcResult result)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(result);
        var data = StrainStateReportData.Parse(result.DataJson);
        var plane = new Kurvature { e0 = data.E0, ky = data.Ky, kz = data.Kz };
        return
        [
            new("strain", "Карта деформаций ε", plane, task.CalcType, ReportImageMode.Strain),
            new("stress", "Карта напряжений σ", plane, task.CalcType, ReportImageMode.Stress)
        ];
    }

    /// <inheritdoc/>
    public ReportDocument Build(ReportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!SupportedKinds.Contains(context.Task.Kind, StringComparer.Ordinal))
            throw new ArgumentException("Поставщик не поддерживает данный тип задачи.", nameof(context));

        var task = context.Task;
        var data = StrainStateReportData.Parse(context.Result.DataJson);
        string title = string.IsNullOrWhiteSpace(task.Tag)
            ? "Расчётное обоснование НДС"
            : $"Расчётное обоснование НДС — {task.Tag}";

        var document = new ReportDocument(title);
        SectionReportSections.Identification(document, context,
            "координаты и размеры — м; координаты арматуры — мм; площади — м²; ε — безразмерная; κ — 1/м; σ и E — МПа; N — кН; M — кН·м; D — кН·м²/кН");
        document
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
            ], "Параметр", "Значение"))
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
                $"{Sub("ε", "bi")} = {Sub("ε", "0")} + {Sub("κ", "y")}·{Sub("y", "bi")} + {Sub("κ", "z")}·{Sub("x", "bi")}",
                $"{Sub("ε", "0")} = {F(data.E0)}; {Sub("κ", "y")} = {Curvature(data.Ky)}; {Sub("κ", "z")} = {Curvature(data.Kz)}",
                $"бетон: {Sub("ε", "min")} = {F(data.Extrema.ConcreteMin)}, {Sub("ε", "max")} = {F(data.Extrema.ConcreteMax)}"))
            .Add(new ReportFormula(
                "(8.30)",
                $"{Sub("ε", "si")} = {Sub("ε", "0")} + {Sub("κ", "y")}·{Sub("y", "si")} + {Sub("κ", "z")}·{Sub("x", "si")}",
                $"{Sub("ε", "0")} = {F(data.E0)}; {Sub("κ", "y")} = {Curvature(data.Ky)}; {Sub("κ", "z")} = {Curvature(data.Kz)}",
                $"арматура: {Sub("ε", "min")} = {F(data.Extrema.SteelMin)}, {Sub("ε", "max")} = {F(data.Extrema.SteelMax)}"))
            .Add(new ReportFormula(
                "(8.31)", $"{Sub("σ", "bi")} = {Sub("E", "b")}·{Sub("ν", "b")}·{Sub("ε", "bi")}",
                "напряжение бетона определяется диаграммой σ(ε)", "учтено в интеграле равновесия"))
            .Add(new ReportFormula(
                "(8.32)", $"{Sub("σ", "si")} = {Sub("E", "s")}·{Sub("ν", "s")}·{Sub("ε", "si")}",
                "напряжение арматуры определяется диаграммой σ(ε)", "учтено в интеграле равновесия"))
            .Add(new ReportFormula(
                "(8.35)–(8.36)",
                $"{Sub("ν", "b")} = {Sub("σ", "b")}/({Sub("E", "b")}·{Sub("ε", "b")});  {Sub("ν", "s")} = {Sub("σ", "s")}/({Sub("E", "s")}·{Sub("ε", "s")})",
                "коэффициенты определяются текущим участком диаграммы", "использованы текущие секущие модули"))
            .Add(new ReportFormula(
                "(8.37)–(8.38)",
                $"{Sub("ε", "b,min")} ≤ {Sub("ε", "bi")} ≤ {Sub("ε", "b,max")};  {Sub("ε", "s,min")} ≤ {Sub("ε", "si")} ≤ {Sub("ε", "s,max")}",
                "предельные деформации задаются характеристиками материалов", "проверка выполняется по диаграммам материалов"))
            .Add(new ReportHeading(1, "Проверка равновесия"))
            .Add(new ReportFormula(
                "(8.26)",
                $"{Sub("M", "x")} = Σ({Sub("σ", "b")}·{Sub("A", "b")}·{Sub("y", "b")}) + Σ({Sub("σ", "s")}·{Sub("A", "s")}·{Sub("y", "s")})",
                $"{Sub("M", "x")} = {F(data.ResultMx)}", $"{Sub("M", "x")} = {F(data.Equilibrium.Mx)} кН·м"))
            .Add(new ReportFormula(
                "(8.27)",
                $"{Sub("M", "y")} = Σ({Sub("σ", "b")}·{Sub("A", "b")}·{Sub("x", "b")}) + Σ({Sub("σ", "s")}·{Sub("A", "s")}·{Sub("x", "s")})",
                $"{Sub("M", "y")} = {F(data.ResultMy)}", $"{Sub("M", "y")} = {F(data.Equilibrium.My)} кН·м"))
            .Add(new ReportFormula(
                "(8.28)", $"N = Σ({Sub("σ", "b")}·{Sub("A", "b")}) + Σ({Sub("σ", "s")}·{Sub("A", "s")})",
                $"N = {F(data.ResultN)}", $"N = {F(data.Equilibrium.N)} кН"));

        var d = data.Stiffness;
        document
            .Add(new ReportHeading(1, "Матрица жёсткости по СП 63"))
            .Add(new ReportParagraph($"Источник интегрирования: {d.Source}. Порядок матрицы: [Mx, My, N] × [κy, κz, ε₀]."))
            .Add(new ReportParagraph("Размерности D: D11, D12, D22 — кН·м²; D13, D23 — кН·м; D33 — кН. Поэтому произведения D·[κy, κz, ε₀] дают соответственно [кН·м, кН·м, кН]."))
            .Add(new ReportFormula(
                "(8.39)",
                $"{Sub("M", "x")} = {Sub("D", "11")}·{Sub("κ", "y")} + {Sub("D", "12")}·{Sub("κ", "z")} + {Sub("D", "13")}·{Sub("ε", "0")}",
                $"{Sub("M", "x")} = {F(d.D11)}·{Sub("κ", "y")} + {F(d.D12)}·{Sub("κ", "z")} + {F(d.D13)}·{Sub("ε", "0")}",
                $"{Sub("M", "x")} = {F(data.Equilibrium.Mx)} кН·м"))
            .Add(new ReportFormula(
                "(8.40)",
                $"{Sub("M", "y")} = {Sub("D", "12")}·{Sub("κ", "y")} + {Sub("D", "22")}·{Sub("κ", "z")} + {Sub("D", "23")}·{Sub("ε", "0")}",
                $"{Sub("M", "y")} = {F(d.D12)}·{Sub("κ", "y")} + {F(d.D22)}·{Sub("κ", "z")} + {F(d.D23)}·{Sub("ε", "0")}",
                $"{Sub("M", "y")} = {F(data.Equilibrium.My)} кН·м"))
            .Add(new ReportFormula(
                "(8.41)",
                $"N = {Sub("D", "13")}·{Sub("κ", "y")} + {Sub("D", "23")}·{Sub("κ", "z")} + {Sub("D", "33")}·{Sub("ε", "0")}",
                $"N = {F(d.D13)}·{Sub("κ", "y")} + {F(d.D23)}·{Sub("κ", "z")} + {F(d.D33)}·{Sub("ε", "0")}",
                $"N = {F(data.Equilibrium.N)} кН"))
            .Add(new ReportTable(
                ["Элемент", "Формула СП 63", "Результат"],
                [
                    (IReadOnlyList<string>)["D11", "Σ(E_bν_bA_by²) + Σ(E_sν_sA_sy²)", F(d.D11)],
                    (IReadOnlyList<string>)["D12", "Σ(E_bν_bA_bx_by) + Σ(E_sν_sA_sx_sy)", F(d.D12)],
                    (IReadOnlyList<string>)["D13", "Σ(E_bν_bA_by) + Σ(E_sν_sA_sy)", F(d.D13)],
                    (IReadOnlyList<string>)["D22", "Σ(E_bν_bA_bx²) + Σ(E_sν_sA_sx²)", F(d.D22)],
                    (IReadOnlyList<string>)["D23", "Σ(E_bν_bA_bx) + Σ(E_sν_sA_sx)", F(d.D23)],
                    (IReadOnlyList<string>)["D33", "Σ(E_bν_bA_b) + Σ(E_sν_sA_s)", F(d.D33)]
                ]))
            .Add(new ReportFormula(
                "(8.42)",
                $"{Sub("D", "11")} = Σ({Sub("E", "b")}{Sub("ν", "b")}{Sub("A", "b")}{Sup(Sub("y", "b"), "2")}) + Σ({Sub("E", "s")}{Sub("ν", "s")}{Sub("A", "s")}{Sup(Sub("y", "s"), "2")})",
                $"источник: {d.Source}", $"{Sub("D", "11")} = {F(d.D11)}"))
            .Add(new ReportFormula(
                "(8.43)",
                $"{Sub("D", "22")} = Σ({Sub("E", "b")}{Sub("ν", "b")}{Sub("A", "b")}{Sup(Sub("x", "b"), "2")}) + Σ({Sub("E", "s")}{Sub("ν", "s")}{Sub("A", "s")}{Sup(Sub("x", "s"), "2")})",
                $"источник: {d.Source}", $"{Sub("D", "22")} = {F(d.D22)}"))
            .Add(new ReportFormula(
                "(8.44)",
                $"{Sub("D", "12")} = Σ({Sub("E", "b")}{Sub("ν", "b")}{Sub("A", "b")}{Sub("x", "b")}{Sub("y", "b")}) + Σ({Sub("E", "s")}{Sub("ν", "s")}{Sub("A", "s")}{Sub("x", "s")}{Sub("y", "s")})",
                $"источник: {d.Source}", $"{Sub("D", "12")} = {F(d.D12)}"))
            .Add(new ReportFormula(
                "(8.45)",
                $"{Sub("D", "13")} = Σ({Sub("E", "b")}{Sub("ν", "b")}{Sub("A", "b")}{Sub("y", "b")}) + Σ({Sub("E", "s")}{Sub("ν", "s")}{Sub("A", "s")}{Sub("y", "s")})",
                $"источник: {d.Source}", $"{Sub("D", "13")} = {F(d.D13)}"))
            .Add(new ReportFormula(
                "(8.46)",
                $"{Sub("D", "23")} = Σ({Sub("E", "b")}{Sub("ν", "b")}{Sub("A", "b")}{Sub("x", "b")}) + Σ({Sub("E", "s")}{Sub("ν", "s")}{Sub("A", "s")}{Sub("x", "s")})",
                $"источник: {d.Source}", $"{Sub("D", "23")} = {F(d.D23)}"))
            .Add(new ReportFormula(
                "(8.47)",
                $"{Sub("D", "33")} = Σ({Sub("E", "b")}{Sub("ν", "b")}{Sub("A", "b")}) + Σ({Sub("E", "s")}{Sub("ν", "s")}{Sub("A", "s")})",
                $"источник: {d.Source}", $"{Sub("D", "33")} = {F(d.D33)}"));

        var j = data.Jacobian;
        document
            .Add(new ReportHeading(1, "Якобиан Ньютона"))
            .Add(new ReportParagraph($"Строки: [{string.Join(", ", j.Rows)}]; столбцы: [{string.Join(", ", j.Columns)}]; схема: {j.Scheme}; h = {F(j.Step)}."))
            .Add(new ReportParagraph("Размерности якобиана: строки N — кН, Mx/My — кН·м; столбцы ε₀ — безразмерный, κy/κz — 1/м. Размерность каждой производной определяется парой строки и столбца."))
            .Add(new ReportTable(["", "ε₀", "κy", "κz"], JacobianRows(j)));

        AddExtendedSummary(document, context, data);

        if (!data.Converged)
            document.Add(new ReportWarning("Расчёт не достиг заданного критерия сходимости; результаты требуют инженерной проверки."));
        if (context.Images.TryGetValue("strain", out var strainSvg))
            document.Add(new ReportImage("Карта деформаций ε", strainSvg));
        if (context.Images.TryGetValue("stress", out var stressSvg))
            document.Add(new ReportImage("Карта напряжений σ", stressSvg));
        return document;
    }

    static void AddExtendedSummary(ReportDocument document, ReportContext context,
        StrainStateReportData data)
    {
        document.Add(new ReportHeading(1, "Сводка результата"))
            .Add(new ReportKeyValueTable(
            [
                ("Плоскость деформаций", $"ε₀ = {F(data.E0)}; κy = {Curvature(data.Ky)}; κz = {Curvature(data.Kz)}"),
                ("Бетон, εmin…εmax", $"{F(data.Extrema.ConcreteMin)} … {F(data.Extrema.ConcreteMax)}"),
                ("Арматура, εmin…εmax", $"{F(data.Extrema.SteelMin)} … {F(data.Extrema.SteelMax)}"),
                ("Сходимость", $"{data.Iterations} итераций; невязка {Force(data.Residual)}")
            ], "Параметр", "Значение"));

        var plane = new Kurvature { e0 = data.E0, ky = data.Ky, kz = data.Kz };
        SectionReportSections.Eta(document, data.Eta);
        SectionReportSections.Prestress(document, data.Prestress);
        SectionReportSections.Stiffness(document, context.Section, plane, context.Task.CalcType);
        if (context.Section is { } section)
        {
            SectionReportSections.Rebar(document, section, plane, context.Task.CalcType);
            SectionReportSections.Geometry(document, section, plane);
            SectionReportSections.MaterialsAndDiagrams(document, section, context.Task.CalcType, plane);
            return;
        }

        var rebar = data.Rebar.Select(ToReportRebar).ToList();
        if (rebar.Count > 0)
        {
            document.Add(new ReportHeading(1, "Арматура"))
                .Add(new ReportParagraph("Координаты и размеры стержней — мм; площадь — мм²; ε — безразмерная; σ и Eсек — МПа."))
                .Add(new ReportTable(
                    ["№", "Группа", "Материал", "x, мм", "y, мм", "d, мм"],
                    rebar.Select(row => (IReadOnlyList<string>)[
                        row.Num.ToString(CultureInfo.InvariantCulture), row.Group, row.Material,
                        F(row.Xmm), F(row.Ymm), F(row.DiameterMm)]).ToList()))
                .Add(new ReportTable(
                    ["№", "Группа", "A, мм²", "ε", "σ, МПа", "Eсек, МПа"],
                    rebar.Select(row => (IReadOnlyList<string>)[
                        row.Num.ToString(CultureInfo.InvariantCulture), row.Group, F(row.AreaMm2),
                        F(row.Eps), F(row.SigmaMpa), F(row.SecantModulusMpa)]).ToList()));
        }
        SectionReportSections.SectionMissingWarning(document);
    }

    static ReportRebarRow ToReportRebar(StrainStateRebarData row)
        => new(row.Num, row.Group, row.Material, row.Xmm, row.Ymm, row.DiameterMm, row.AreaMm2,
            row.Eps, row.SigmaMpa,
            Math.Abs(row.Eps) > 1e-20 ? Math.Abs(row.SigmaMpa / row.Eps) : 0);

    sealed record ReportRebarRow(int Num, string Group, string Material, double Xmm, double Ymm,
        double DiameterMm, double AreaMm2, double Eps, double SigmaMpa, double SecantModulusMpa);

    static IReadOnlyList<IReadOnlyList<string>> JacobianRows(StrainStateJacobianData jacobian)
    {
        var rows = new List<IReadOnlyList<string>>();
        for (int i = 0; i < jacobian.Values.Length; i++)
        {
            var values = jacobian.Values[i];
            rows.Add([
                i < jacobian.Rows.Length ? jacobian.Rows[i] : $"row {i + 1}",
                values.Length > 0 ? F(values[0]) : "—",
                values.Length > 1 ? F(values[1]) : "—",
                values.Length > 2 ? F(values[2]) : "—"
            ]);
        }
        return rows;
    }

    static string F(double value) => SectionReportSections.F(value);
    static string Force(double value) => $"{F(value)} кН";
    static string Moment(double value) => $"{F(value)} кН·м";
    static string Curvature(double value) => $"{F(value)} 1/м";
    static string Sub(string symbol, string sub) => SectionReportSections.Sub(symbol, sub);
    static string Sup(string symbol, string sup) => SectionReportSections.Sup(symbol, sup);
}
