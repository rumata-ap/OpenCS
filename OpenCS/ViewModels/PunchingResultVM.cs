using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows.Media;
using CScore;
using CScore.Batch;
using CScore.Sp63Punching;
using OpenCS.Reporting;
using OpenCS.Utilites;
using OpenCS.Views.Helpers;

namespace OpenCS.ViewModels;

/// <summary>Строка таблицы вариантов контура продавливания.</summary>
public sealed class PunchingVariantRowVM
{
    /// <summary>Идентификатор варианта ядра.</summary>
    public string Id { get; init; } = "";
    /// <summary>Название (вид, номер края, зона).</summary>
    public string Title { get; init; } = "";
    /// <summary>Определяющий вариант.</summary>
    public bool Governs { get; init; }
    public string U { get; init; } = "";
    /// <summary>Продавливающая сила за вычетом отпора, кН.</summary>
    public string F { get; init; } = "";
    public string Fult { get; init; } = "";
    public string Mxult { get; init; } = "";
    public string Myult { get; init; } = "";
    public string MxStar { get; init; } = "";
    public string MyStar { get; init; } = "";
    public string KF { get; init; } = "";
    public string KM { get; init; } = "";
    /// <summary>Кисп текстом.</summary>
    public string K { get; init; } = "";
    /// <summary>Кисп числом (сортировка).</summary>
    public double KValue { get; init; }
    /// <summary>Учтена ли поперечная арматура.</summary>
    public string Sw { get; init; } = "";
    /// <summary>Причина для общей подсветки строк пакетов (<c>BatchVerdictRowStyle</c>).</summary>
    public BatchVerdictView Reason { get; init; } = BatchVerdictView.Empty;
}

/// <summary>Окно результата задачи «Продавливание плиты (СП 63)» — по сохранённому DataJson, без пересчёта.</summary>
public sealed class PunchingResultVM : ViewModelBase
{
    string? _selectedVariantId;
    PunchingVariantRowVM? _selectedRow;

    /// <summary>Создаёт VM по результату задачи.</summary>
    public PunchingResultVM(CalcResult result, CalcTask? task)
    {
        Result = result;
        Task = task;
        var data = PunchingReportData.Parse(result.DataJson);
        Data = data;
        Title = string.IsNullOrWhiteSpace(task?.Tag) ? Loc.S("CalcTaskKind_punching") : task!.Tag;
        ErrorText = data.Error ?? "";
        Scene = data.Scene;
        int edgeCount = data.Scene?.Edges.Count ?? 2;

        foreach (var v in data.Variants)
        {
            double k = v.Ratio ?? double.PositiveInfinity;
            string title = Variants.Title(v.Kind, v.Zone, v.Id, edgeCount);
            Rows.Add(new PunchingVariantRowVM
            {
                Id = v.Id, Title = title, Governs = v.Governs,
                U = N(v, "u", "0.000"), F = N(v, "Fred", "0.0"), Fult = N(v, "Fult", "0.0"), Mxult = N(v, "Mxult", "0.0"), Myult = N(v, "Myult", "0.0"),
                MxStar = N(v, "MxStar", "0.0"), MyStar = N(v, "MyStar", "0.0"), KF = N(v, "kF", "0.000"), KM = N(v, "kM", "0.000"),
                K = Variants.K(k), KValue = double.IsFinite(k) ? k : double.MaxValue,
                Sw = v.SwCounted ? Loc.S("PunchingSwCounted", "учтена") : "—",
                Reason = new BatchVerdictView(new BatchRowVerdict(k > 1 ? BatchRowStatus.NotPassed : BatchRowStatus.Ok, k,
                    v.Kind.ToString(), v.Trace.LastOrDefault()?.Reference ?? "", ""))
            });
        }
        _selectedRow = Rows.FirstOrDefault(r => r.Governs);
        _selectedVariantId = _selectedRow?.Id;

        foreach (var w in data.Warnings.Where(w => !string.IsNullOrWhiteSpace(w.Text)))
            Warnings.Add(Variants.Warning(w, data.Variants, edgeCount));
        InputsSummary = BuildInputsSummary(data.Inputs);
        DesignText = data.Design is { } d ? DesignLine(d) : "";
        foreach (var c in data.Detailing ?? []) Detailing.Add(DetailingLine(c));
        int bad = (data.Detailing ?? []).Count(x => x.State is "violated" or "allowed");
        DetailingHeader = bad == 0
            ? Loc.S("PunchingDetailingOk", "Конструктивные требования п. 10.3.17 — выполнены")
            : string.Format(CultureInfo.CurrentCulture, Loc.S("PunchingDetailingBad", "Конструктивные требования п. 10.3.17 — не выполнено: {0}"), bad);
    }

    /// <summary>Заголовок блока п. 10.3.17 с итогом.</summary>
    public string DetailingHeader { get; } = "";

    /// <summary>Подбор поперечной арматуры одной строкой; пусто — подбор не выполнялся (обеспечено бетоном).</summary>
    public string DesignText { get; }
    /// <summary>Есть подбор.</summary>
    public bool HasDesign => DesignText.Length > 0;
    /// <summary>Конструктивные требования п. 10.3.17 построчно.</summary>
    public ObservableCollection<string> Detailing { get; } = [];
    /// <summary>Есть проверки п. 10.3.17.</summary>
    public bool HasDetailing => Detailing.Count > 0;

    /// <summary>Строка подбора по сохранённому результату.</summary>
    public static string DesignLine(PunchingReportDesign d)
    {
        var c = CultureInfo.CurrentCulture;
        string req = d.Cross
            ? string.Format(c, Loc.S("PunchingDesignReqCross", "qsw,x ≥ {0:0.#} кН/м (лучи вдоль Y, есть {1:0.#}), qsw,y ≥ {2:0.#} кН/м (лучи вдоль X, есть {3:0.#})"),
                d.QswXReq ?? 0, d.QswX ?? 0, d.QswYReq ?? 0, d.QswY ?? 0)
            : d.Qsw is double q && q > 0
                ? string.Format(c, Loc.S("PunchingDesignReqHave", "qsw ≥ {0:0.#} кН/м (есть {1:0.#})"), d.QswReq ?? 0, q)
                : string.Format(c, Loc.S("PunchingDesignReq", "qsw ≥ {0:0.#} кН/м"), d.QswReq ?? 0);
        string extra = "";
        if (d.BarDiameter is double dia) extra += string.Format(c, Loc.S("PunchingDesignBar", "; стержни Ø{0:0}"), dia * 1000);
        else if (d.BarAreaReq is double ab) extra += string.Format(c, Loc.S("PunchingDesignBarTooBig", "; площадь стержня ≥ {0:0.#} мм² — больше Ø32"), ab * 1e6);
        if (d.RowsReq is int n) extra += string.Format(c, Loc.S("PunchingDesignRows", "; рядов ≥ {0}"), n);
        else if (d.ZoneReq is double z) extra += string.Format(c, Loc.S("PunchingDesignZone", "; зона ≥ {0:0} мм от грани"), z * 1000);
        string text = d.State switch
        {
            "not_required" => Loc.S("PunchingDesign_not_required", "Подбор: поперечная арматура не требуется."),
            "not_ensured" => Loc.S("PunchingDesign_not_ensured", "Подбор: арматурой не обеспечить (Кисп > 1 и при 2Fb, 2Mb) — увеличьте толщину, класс бетона или площадку."),
            "layout_ineffective" => Loc.S("PunchingDesign_layout_ineffective", "Подбор: раскладка не пересекает определяющий контур или пирамиду — измените лучи или ряды."),
            "by_threshold" => string.Format(c, Loc.S("PunchingDesign_by_threshold", "Подбор: {0} — по порогу Fsw ≥ 0,25·Fb (п. 8.1.48){1}."), req, extra),
            _ => string.Format(c, Loc.S("PunchingDesign_by_calculation", "Подбор: {0} — из условия Кисп = 1{1}."), req, extra)
        };
        if (d.State is "by_threshold" or "by_calculation")
        {
            text += " " + (d.ProvidedSufficient ? Loc.S("PunchingDesignEnough", "Заданной арматуры достаточно.") : Loc.S("PunchingDesignNotEnough", "Заданной арматуры недостаточно."));
            if (d.BeyondNotEnsured) text += " " + Loc.S("PunchingDesignBeyondBad", "Контур за армированием не проходит при любой ширине зоны.");
        }
        return text;
    }

    /// <summary>Строка проверки п. 10.3.17: знак итога, требование, значение и предел.</summary>
    public static string DetailingLine(PunchingReportDetailing d)
    {
        var c = CultureInfo.CurrentCulture;
        string mark = d.State switch { "ok" => "✔", "allowed" => "≈", "violated" => "✘", _ => "·" };
        string text = Loc.S("PunchingDetailing_" + d.Code, d.Text);
        if (d.Code == "anchorage" || d.Value is not double v || d.Limit is not double lim) return $"{mark} {text}";
        string state = d.State == "allowed" ? Loc.S("PunchingDetailingAllowed", " — допускается, учтены стержни пирамиды") : "";
        string mm = Loc.S("UnitMm", "мм");
        return string.Format(c, "{0} {1}: {2:0.#} {6} {3} {4:0.#} {6}{5}", mark, text, v * 1000, d.Relation, lim * 1000, state, mm);
    }

    /// <summary>Результат.</summary>
    public CalcResult Result { get; }
    /// <summary>Задача.</summary>
    public CalcTask? Task { get; }
    /// <summary>Разобранные данные.</summary>
    public PunchingReportData Data { get; }
    /// <summary>Имя задачи.</summary>
    public string Title { get; }
    /// <summary>Текст ошибки расчёта; пусто — нет.</summary>
    public string ErrorText { get; }
    /// <summary>Есть ошибка.</summary>
    public bool HasError => ErrorText.Length > 0;
    /// <summary>Сцена для рисунка.</summary>
    public PunchingScene? Scene { get; }
    /// <summary>Варианты контура.</summary>
    public ObservableCollection<PunchingVariantRowVM> Rows { get; } = [];
    /// <summary>Предупреждения (id контуров заменены названиями).</summary>
    public ObservableCollection<string> Warnings { get; } = [];
    /// <summary>Есть предупреждения.</summary>
    public bool HasWarnings => Warnings.Count > 0;
    /// <summary>Краткие исходные данные.</summary>
    public string InputsSummary { get; }

    /// <summary>Выбранный на рисунке вариант.</summary>
    public string? SelectedVariantId
    {
        get => _selectedVariantId;
        set
        {
            if (_selectedVariantId == value) return;
            _selectedVariantId = value;
            OnPropertyChanged();
            var row = Rows.FirstOrDefault(r => r.Id == value);
            if (!ReferenceEquals(row, _selectedRow)) { _selectedRow = row; OnPropertyChanged(nameof(SelectedRow)); }
        }
    }

    /// <summary>Выбранная строка таблицы.</summary>
    public PunchingVariantRowVM? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (ReferenceEquals(_selectedRow, value)) return;
            _selectedRow = value;
            OnPropertyChanged();
            if (value is not null && _selectedVariantId != value.Id) { _selectedVariantId = value.Id; OnPropertyChanged(nameof(SelectedVariantId)); }
        }
    }

    /// <summary>Кисп определяющего варианта.</summary>
    public double Utilization => Data.Ratio ?? double.PositiveInfinity;

    /// <summary>Тон вердикта — как у строк пакетов.</summary>
    public BatchVerdictTone Tone => HasError ? BatchVerdictTone.Error
        : Utilization > 1 ? BatchVerdictTone.Fail
        : Utilization >= BatchVerdictView.NearLimitUtilization ? BatchVerdictTone.NearLimit : BatchVerdictTone.Ok;

    /// <summary>Кисть вердикта.</summary>
    public Brush VerdictBrush => Frozen(Tone == BatchVerdictTone.Error ? Color.FromRgb(0x8B, 0, 0) : PunchingPlanCanvas.VerdictColor(Utilization));

    /// <summary>Фон плашки вердикта (тот же тон, что у строк пакетов).</summary>
    public Brush VerdictBackground => Frozen(Tone switch
    {
        BatchVerdictTone.Ok => Color.FromArgb(0x1A, 0x2D, 0x7A, 0x3E),
        BatchVerdictTone.NearLimit => Color.FromArgb(0x40, 0xF1, 0xC4, 0x0F),
        BatchVerdictTone.Fail => Color.FromArgb(0x26, 0xC0, 0x39, 0x2B),
        _ => Color.FromArgb(0x40, 0x8B, 0, 0)
    });

    /// <summary>Главная строка: Кисп и вывод.</summary>
    public string VerdictText => HasError
        ? Loc.S("PunchingVerdictError", "Расчёт не выполнен")
        : string.Format(CultureInfo.CurrentCulture, Loc.S(Data.Passed ? "PunchingVerdictPassed" : "PunchingVerdictFailed",
            Data.Passed ? "Кисп = {0} — прочность на продавливание обеспечена" : "Кисп = {0} — прочность на продавливание НЕ обеспечена"),
            Variants.K(Utilization));

    /// <summary>Оценка узла: по бетону / нужна арматура / не обеспечено.</summary>
    public string StatusText => HasError ? "" : Loc.S("PunchingStatus_" + Data.Status, Data.Status);

    /// <summary>Кисп без арматуры и с предельной арматурой.</summary>
    public string RatiosText => HasError ? "" : string.Format(CultureInfo.CurrentCulture,
        Loc.S("PunchingRatiosLine", "Кисп без поперечной арматуры: {0}; с предельной арматурой (2Fb, 2Mb): {1}"),
        Data.RatioConcrete is double a ? Variants.K(a) : "—", Data.RatioMaxSw is double b ? Variants.K(b) : "—");

    static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    static string N(PunchingReportVariant v, string key, string format)
        => v.Numbers.TryGetValue(key, out var x) && x is double d ? d.ToString(format, CultureInfo.CurrentCulture) : "—";

    static string BuildInputsSummary(JsonElement? inputs)
    {
        if (inputs is not { } i) return "";
        string S(string n) => i.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        double D(string n) => i.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
        var c = CultureInfo.CurrentCulture;
        string area = S("shape") switch
        {
            "circle" => string.Format(c, "Ø{0:0}", D("d") * 1000),
            "ring" => string.Format(c, "Ø{0:0}×{1:0}", D("d") * 1000, D("t") * 1000),
            "polygon" => Loc.S("PunchingShape_polygon", "многоугольник"),
            _ => string.Format(c, "{0:0}×{1:0}", D("bx") * 1000, D("by") * 1000)
        };
        string concrete = S("concrete") is { Length: > 0 } ct ? ct : Loc.S("PunchingManualValue");
        bool hasSw = i.TryGetProperty("sw", out var swv) && swv.ValueKind == JsonValueKind.True;
        bool rows = i.TryGetProperty("swFirstRow", out var fr) && fr.ValueKind == JsonValueKind.Number;
        string sw = !hasSw ? Loc.S("PunchingSummaryNoSw", "без поперечной арматуры")
            : S("swLayout") == "cross"
                ? string.Format(c, Loc.S("PunchingSummarySwCross", "крестообразная арматура Ø{0:0}, {1:0} рядов с шагом {2:0} мм, Rsw = {3:0.#} МПа"),
                    D("swDiameter") * 1000, D("swRowCount"), D("swRowStep") * 1000, D("rsw") / 1000)
            : rows
                ? string.Format(c, Loc.S("PunchingSummarySwRows", "поперечная арматура Ø{0:0} шаг {1:0} мм вдоль контура, {2:0} рядов с шагом {3:0} мм, Rsw = {4:0.#} МПа"),
                    D("swDiameter") * 1000, D("swStep") * 1000, D("swRowCount"), D("swRowStep") * 1000, D("rsw") / 1000)
                : string.Format(c, Loc.S("PunchingSummarySw", "поперечная арматура {0}×Ø{1:0} шаг {2:0}, Rsw = {3:0.#} МПа"),
                    D("swCount"), D("swDiameter") * 1000, D("swStep") * 1000, D("rsw") / 1000);
        if (WallText(S("wall"), D("a1"), D("a2"), c) is { } wall)
            return string.Format(c, Loc.S("PunchingSummaryWall",
                    "Узел: {0} мм; h = {1:0} мм, h0 = {2:0.#} мм; бетон {3}, Rbt = {4:0.###} МПа; {5}; F = {6:0.#} кН{7}"),
                wall, D("h") * 1000, D("h0") * 1000, concrete, D("rbt") / 1000, sw, D("F"),
                S("wall") == "end" ? string.Format(c, ", ΔM = {0:0.#} кН·м", D("Mx")) : "");
        return string.Format(c, Loc.S("PunchingSummary",
                "Площадь {0} мм; h = {1:0} мм, h0 = {2:0.#} мм; бетон {3}, Rbt = {4:0.###} МПа; {5}; F = {6:0.#} кН, Mx = {7:0.#} кН·м, My = {8:0.#} кН·м"),
            area, D("h") * 1000, D("h0") * 1000, concrete, D("rbt") / 1000, sw, D("F"), D("Mx"), D("My"));
    }

    /// <summary>Узел стены для сводки исходных данных («торец стены, a1 = 200»); null — не стена.</summary>
    internal static string? WallText(string kind, double a1, double a2, CultureInfo c) => kind switch
    {
        "end" => string.Format(c, Loc.S("PunchingSummaryWallEnd", "торец стены, a1 = {0:0}"), a1 * 1000),
        "corner" => string.Format(c, Loc.S("PunchingSummaryWallCorner", "угол стен, a1 = {0:0}, a2 = {1:0}"), a1 * 1000, a2 * 1000),
        _ => null
    };
}
