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
    /// <summary>Встроенные SVG по именам, например stress и strain.</summary>
    public IReadOnlyDictionary<string, string> Images { get; }

    /// <summary>Создаёт контекст отчёта.</summary>
    public ReportContext(CalcTask task, CalcResult result,
        IReadOnlyDictionary<string, string>? images = null)
    {
        Task = task;
        Result = result;
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
        string title = string.IsNullOrWhiteSpace(task.Tag)
            ? "Расчётное обоснование НДС"
            : $"Расчётное обоснование НДС — {task.Tag}";

        var document = new ReportDocument(title)
            .Add(new ReportHeading(1, "Исходные данные"))
            .Add(new ReportKeyValueTable(
            [
                ("Тип задачи", task.Kind),
                ("Вид расчёта", task.CalcType.ToString()),
                ("Статус", context.Result.Status),
                ("Сходимость", data.Converged ? "достигнута" : "не достигнута"),
                ("Итерации Ньютона", data.Iterations.ToString(CultureInfo.InvariantCulture)),
                ("Невязка", F(data.Residual)),
                ("Версия формул", data.FormulaVersion)
            ]))
            .Add(new ReportTable(
                ["Величина", "Целевое значение", "Результат"],
                [
                    (IReadOnlyList<string>)["N", F(data.TargetN), F(data.ResultN)],
                    (IReadOnlyList<string>)["Mx", F(data.TargetMx), F(data.ResultMx)],
                    (IReadOnlyList<string>)["My", F(data.TargetMy), F(data.ResultMy)]
                ]))
            .Add(new ReportHeading(1, "Плоскость деформаций"))
            .Add(new ReportFormula(
                "(8.29)",
                "ε_bi = ε₀ + ky·y_bi + kz·x_bi",
                $"ε₀ = {F(data.E0)}; ky = {F(data.Ky)}; kz = {F(data.Kz)}",
                $"бетон: εmin = {F(data.Extrema.ConcreteMin)}, εmax = {F(data.Extrema.ConcreteMax)}"))
            .Add(new ReportFormula(
                "(8.30)",
                "ε_si = ε₀ + ky·y_si + kz·x_si",
                $"ε₀ = {F(data.E0)}; ky = {F(data.Ky)}; kz = {F(data.Kz)}",
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
            .Add(new ReportTable(
                ["", "e0", "ky", "kz"],
                JacobianRows(j)));

        if (!data.Converged)
            document.Add(new ReportWarning("Расчёт не достиг заданного критерия сходимости; результаты требуют инженерной проверки."));

        if (context.Images.TryGetValue("strain", out var strainSvg))
            document.Add(new ReportImage("Карта деформаций ε", strainSvg));
        if (context.Images.TryGetValue("stress", out var stressSvg))
            document.Add(new ReportImage("Карта напряжений σ", stressSvg));

        return document;
    }

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
}
