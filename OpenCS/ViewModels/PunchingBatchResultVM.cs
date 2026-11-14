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

/// <summary>Столбец Кисп варианта контура в таблице строк пакета.</summary>
/// <param name="Index">Индекс в <see cref="PunchingBatchRowVM.Ratios"/>.</param>
/// <param name="Id">Идентификатор варианта.</param>
/// <param name="Header">Заголовок (краткое название).</param>
/// <param name="Title">Полное название — подсказка.</param>
public sealed record PunchingBatchVariantColumn(int Index, string Id, string Header, string Title);

/// <summary>Строка набора в окне пакета продавливания.</summary>
public sealed class PunchingBatchRowVM : IBatchVerdictRow
{
    /// <summary>Строка результата.</summary>
    public required PunchingBatchReportRow Source { get; init; }
    public int Num => Source.Num;
    public string Label => Source.Label;
    public string NText { get; init; } = "";
    public string MxText { get; init; } = "";
    public string MyText { get; init; } = "";
    public string FText { get; init; } = "";
    /// <summary>Кисп по столбцам вариантов (текст).</summary>
    public IReadOnlyList<string> Ratios { get; init; } = [];
    /// <summary>Кисп по столбцам вариантов (сортировка).</summary>
    public IReadOnlyList<double> RatioValues { get; init; } = [];
    /// <summary>Определяющий контур строки.</summary>
    public string GoverningTitle { get; init; } = "";
    /// <summary>N другого знака, чем преобладающий в наборе.</summary>
    public bool Reversed => Source.Reversed;
    /// <summary>Кисп строки — с тем же округлением вверх, что в столбцах контуров, окне и отчёте.</summary>
    public string KText => Source.Verdict.Util is double k ? Variants.K(k) : "—";
    /// <inheritdoc/>
    public BatchVerdictView Reason { get; init; } = BatchVerdictView.Empty;
}

/// <summary>Строка огибающей по контурам.</summary>
public sealed class PunchingEnvelopeRowVM : ViewModelBase
{
    bool _isCurrent;

    /// <summary>
    /// Контур выделен на рисунке (щелчком по нему, строкой набора или строкой огибающей). Своя рамка строки,
    /// а не выделение таблицы: выделение без фокуса не видно под фоном вердикта, а щелчок по уже выделенной
    /// строке таблица не передаёт.
    /// </summary>
    public bool IsCurrent { get => _isCurrent; set { if (_isCurrent == value) return; _isCurrent = value; OnPropertyChanged(); } }

    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string K { get; init; } = "";
    public double KValue { get; init; }
    /// <summary>Номер строки набора с наибольшим Кисп.</summary>
    public int? Num { get; init; }
    public string RowText { get; init; } = "";
    /// <summary>Контур определяющей строки, определяющий в ней.</summary>
    public bool Governs { get; init; }
    /// <summary>Причина — для общей подсветки строк (<c>BatchVerdictRowStyle</c>).</summary>
    public BatchVerdictView Reason { get; init; } = BatchVerdictView.Empty;
}

/// <summary>
/// Окно результата пакета «Продавливание плиты по набору усилий»: вердикт пакета, рисунок выбранной строки
/// или огибающей, огибающая по контурам и таблица строк. Работает по сохранённому DataJson, без пересчёта.
/// </summary>
public sealed class PunchingBatchResultVM : ViewModelBase
{
    PunchingBatchRowVM? _selectedRow;
    string? _selectedVariantId;
    bool _showEnvelope;

    /// <summary>Создаёт VM по результату пакета.</summary>
    public PunchingBatchResultVM(CalcResult result, CalcTask? task)
    {
        Result = result;
        Task = task;
        Data = PunchingBatchReportData.Parse(result.DataJson);
        Title = string.IsNullOrWhiteSpace(task?.Tag) ? Loc.S("CalcTaskKind_punching_batch") : task!.Tag;
        ErrorText = Data.Error ?? "";
        int edges = Data.EdgeCount;
        var c = CultureInfo.CurrentCulture;

        var shown = Data.Variants.Where(v => v.Kind != PunchingVariantKind.PylonReduced).ToList();
        VariantColumns = shown.Select((v, i) => new PunchingBatchVariantColumn(i, v.Id,
            Variants.Short(v.Kind, v.Zone, v.Id, edges), Variants.Title(v.Kind, v.Zone, v.Id, edges))).ToList();

        foreach (var r in Data.Rows)
        {
            var gv = Data.Variants.FirstOrDefault(v => v.Id == r.Governing);
            Rows.Add(new PunchingBatchRowVM
            {
                Source = r,
                NText = r.N.ToString("0.##", c), MxText = r.Mx.ToString("0.##", c), MyText = r.My.ToString("0.##", c),
                FText = r.F.ToString("0.##", c),
                Ratios = shown.Select(v => r.Ratios.TryGetValue(v.Id, out var k) && k is double kk ? Variants.K(kk) : "—").ToList(),
                RatioValues = shown.Select(v => r.Ratios.TryGetValue(v.Id, out var k) && k is double kk
                    ? (double.IsFinite(kk) ? kk : double.MaxValue) : -1).ToList(),
                GoverningTitle = gv is null ? "—" : Variants.Title(gv.Kind, gv.Zone, gv.Id, edges),
                Reason = new BatchVerdictView(r.Verdict)
            });
        }

        foreach (var v in Data.Variants)
        {
            double k = v.Max ?? 0;
            var row = Data.Rows.FirstOrDefault(r => r.Num == v.Num);
            Envelope.Add(new PunchingEnvelopeRowVM
            {
                Id = v.Id, Title = Variants.Title(v.Kind, v.Zone, v.Id, edges),
                K = v.Max is null ? "—" : Variants.K(k), KValue = double.IsFinite(k) ? k : double.MaxValue,
                Num = v.Num, RowText = row is null ? "—" : $"№ {row.Num} {row.Label}".TrimEnd(),
                Governs = v.Id == Data.GoverningVariant,
                Reason = new BatchVerdictView(new BatchRowVerdict(k > 1 ? BatchRowStatus.NotPassed : BatchRowStatus.Ok, k, v.Id, "", ""))
            });
        }

        var reportVariants = Data.Variants
            .Select(v => new PunchingReportVariant(v.Id, v.Kind, v.Zone, v.Max, false, false, new Dictionary<string, double?>(), []))
            .ToList();
        foreach (var w in Data.Warnings.Where(w => !string.IsNullOrWhiteSpace(w.Text)))
            Warnings.Add(Variants.Warning(w, reportVariants, edges));

        InputsSummary = BuildInputsSummary(Data, c);
        _selectedRow = Rows.FirstOrDefault(r => r.Num == Data.GoverningNum) ?? Rows.FirstOrDefault();
        _selectedVariantId = _selectedRow?.Source.Governing;
        MarkCurrentEnvelope();
    }

    /// <summary>Результат.</summary>
    public CalcResult Result { get; }
    /// <summary>Задача пакета.</summary>
    public CalcTask? Task { get; }
    /// <summary>Разобранные данные.</summary>
    public PunchingBatchReportData Data { get; }
    public string Title { get; }
    public string ErrorText { get; }
    public bool HasError => ErrorText.Length > 0;
    /// <summary>Краткие исходные данные пакета.</summary>
    public string InputsSummary { get; }

    /// <summary>Столбцы Кисп вариантов (строятся окном по результату).</summary>
    public IReadOnlyList<PunchingBatchVariantColumn> VariantColumns { get; }
    /// <summary>Строки набора.</summary>
    public ObservableCollection<PunchingBatchRowVM> Rows { get; } = [];
    /// <summary>Огибающая по контурам.</summary>
    public ObservableCollection<PunchingEnvelopeRowVM> Envelope { get; } = [];
    /// <summary>Предупреждения.</summary>
    public ObservableCollection<string> Warnings { get; } = [];
    public bool HasWarnings => Warnings.Count > 0;

    /// <summary>Рисунок — огибающая (true) или выбранная строка (false).</summary>
    public bool ShowEnvelope
    {
        get => _showEnvelope;
        set
        {
            if (_showEnvelope == value) return;
            _showEnvelope = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowRow));
            OnPropertyChanged(nameof(Scene));
            OnPropertyChanged(nameof(SceneCaption));
        }
    }

    /// <summary>Рисунок — выбранная строка.</summary>
    public bool ShowRow { get => !ShowEnvelope; set => ShowEnvelope = !value; }

    /// <summary>Выбранная строка набора.</summary>
    public PunchingBatchRowVM? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (ReferenceEquals(_selectedRow, value) || value is null) return;
            _selectedRow = value;
            OnPropertyChanged();
            SelectedVariantId = value.Source.Governing;
            OnPropertyChanged(nameof(Scene));
            OnPropertyChanged(nameof(SceneCaption));
        }
    }

    /// <summary>
    /// Щелчок по строке огибающей: показать строку набора с наибольшим Кисп этого контура и выделить контур.
    /// Вызывается на каждый щелчок, а не по смене выделения таблицы: повторный щелчок по той же строке
    /// после смены режима или строки набора тоже должен срабатывать, а выделение DataGrid его не передаёт.
    /// </summary>
    public void ActivateEnvelope(PunchingEnvelopeRowVM envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (Rows.FirstOrDefault(r => r.Num == envelope.Num) is { } row) SelectedRow = row;
        ShowEnvelope = false;
        SelectedVariantId = envelope.Id;
    }

    /// <summary>
    /// Выделенный на рисунке контур. Строка огибающей этого контура отмечается (<see cref="PunchingEnvelopeRowVM.IsCurrent"/>)
    /// без перехода к её строке набора: щелчок по рисунку не меняет показанную строку.
    /// </summary>
    public string? SelectedVariantId
    {
        get => _selectedVariantId;
        set
        {
            if (_selectedVariantId == value) return;
            _selectedVariantId = value;
            OnPropertyChanged();
            MarkCurrentEnvelope();
        }
    }

    void MarkCurrentEnvelope()
    {
        foreach (var e in Envelope) e.IsCurrent = e.Id == _selectedVariantId;
    }

    /// <summary>Сцена рисунка: Кисп контуров выбранной строки или огибающей.</summary>
    public PunchingScene? Scene
    {
        get
        {
            if (Data.Scene is not { } scene) return null;
            if (ShowEnvelope || _selectedRow is not { } row) return scene;
            return scene with
            {
                Variants = scene.Variants.Select(v => v with
                {
                    Utilization = row.Source.Ratios.TryGetValue(v.Id, out var k) && k is double kk ? kk : 0,
                    Governs = v.Id == row.Source.Governing
                }).ToList()
            };
        }
    }

    /// <summary>Подпись рисунка.</summary>
    public string SceneCaption => ShowEnvelope || _selectedRow is null
        ? Loc.S("PunchingBatchSceneCaptionEnvelope", "Рисунок — огибающая: цвет контура — его наибольший Кисп по всем строкам")
        : string.Format(CultureInfo.CurrentCulture,
            Loc.S("PunchingBatchSceneCaptionRow", "Рисунок — строка № {0} «{1}»: цвет контура — его Кисп в этой строке"),
            _selectedRow.Num, _selectedRow.Label);

    /// <summary>Наибольший Кисп пакета.</summary>
    public double Utilization => Data.Utilization ?? double.PositiveInfinity;

    /// <summary>Все строки проходят.</summary>
    public bool Passed => !HasError && Data.Rows.Count > 0 && Data.Rows.All(r => r.Verdict.IsPassed);

    /// <summary>Тон вердикта.</summary>
    public BatchVerdictTone Tone => HasError ? BatchVerdictTone.Error
        : !Passed || Utilization > 1 ? BatchVerdictTone.Fail
        : Utilization >= BatchVerdictView.NearLimitUtilization ? BatchVerdictTone.NearLimit : BatchVerdictTone.Ok;

    /// <summary>Кисть вердикта.</summary>
    public Brush VerdictBrush => Frozen(Tone switch
    {
        BatchVerdictTone.Error => Color.FromRgb(0x8B, 0, 0),
        BatchVerdictTone.Fail => PunchingPlanCanvas.FailColor,
        BatchVerdictTone.NearLimit => PunchingPlanCanvas.NearColor,
        _ => PunchingPlanCanvas.OkColor
    });

    /// <summary>Фон плашки вердикта.</summary>
    public Brush VerdictBackground => Frozen(Tone switch
    {
        BatchVerdictTone.Ok => Color.FromArgb(0x1A, 0x2D, 0x7A, 0x3E),
        BatchVerdictTone.NearLimit => Color.FromArgb(0x40, 0xF1, 0xC4, 0x0F),
        BatchVerdictTone.Fail => Color.FromArgb(0x26, 0xC0, 0x39, 0x2B),
        _ => Color.FromArgb(0x40, 0x8B, 0, 0)
    });

    /// <summary>Главная строка: Кисп,max, строка, контур и вывод.</summary>
    public string VerdictText
    {
        get
        {
            if (HasError) return Loc.S("PunchingVerdictError", "Расчёт не выполнен");
            var gv = Data.Variants.FirstOrDefault(v => v.Id == Data.GoverningVariant);
            string contour = gv is null ? "—" : Variants.Title(gv.Kind, gv.Zone, gv.Id, Data.EdgeCount);
            return string.Format(CultureInfo.CurrentCulture,
                Loc.S(Passed ? "PunchingBatchVerdictPassed" : "PunchingBatchVerdictFailed",
                    Passed ? "Кисп,max = {0} — строка № {1} «{2}», {3}: прочность на продавливание обеспечена во всех строках"
                           : "Кисп,max = {0} — строка № {1} «{2}», {3}: прочность на продавливание НЕ обеспечена"),
                Variants.K(Utilization), Data.GoverningNum?.ToString(CultureInfo.CurrentCulture) ?? "—", Data.GoverningLabel, contour);
        }
    }

    /// <summary>Оценка узла по набору.</summary>
    public string StatusText => HasError ? "" : Loc.S("PunchingStatus_" + Data.Status, Data.Status);

    /// <summary>Наибольшие Кисп без арматуры и с предельной арматурой.</summary>
    public string RatiosText => HasError ? "" : string.Format(CultureInfo.CurrentCulture,
        Loc.S("PunchingRatiosLine", "Кисп без поперечной арматуры: {0}; с предельной арматурой (2Fb, 2Mb): {1}"),
        Data.RatioConcrete is double a ? Variants.K(a) : "—", Data.RatioMaxSw is double b ? Variants.K(b) : "—");

    /// <summary>Счётчики строк.</summary>
    public string CountsText => HasError ? "" : string.Format(CultureInfo.CurrentCulture,
        Loc.S("PunchingBatchCounts", "Строк: {0}; проходят: {1}; не проходят: {2}"),
        Data.Rows.Count, Data.Count(BatchRowStatus.Ok), Data.Count(BatchRowStatus.NotPassed));

    static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    static string BuildInputsSummary(PunchingBatchReportData data, CultureInfo c)
    {
        if (data.Inputs is not { } i) return "";
        string S(string n) => i.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        double D(string n) => i.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
        string area = S("shape") switch
        {
            "circle" => string.Format(c, "Ø{0:0}", D("d") * 1000),
            "ring" => string.Format(c, "Ø{0:0}×{1:0}", D("d") * 1000, D("t") * 1000),
            "polygon" => Loc.S("PunchingShape_polygon", "многоугольник"),
            _ => string.Format(c, "{0:0}×{1:0}", D("bx") * 1000, D("by") * 1000)
        };
        string concrete = S("concrete") is { Length: > 0 } ct ? ct : Loc.S("PunchingManualValue", "задано вручную");
        string sw = i.TryGetProperty("sw", out var swv) && swv.ValueKind == JsonValueKind.True
            ? string.Format(c, Loc.S("PunchingSummarySw", "поперечная арматура {0}×Ø{1:0} шаг {2:0}, Rsw = {3:0.#} МПа"),
                D("swCount"), D("swDiameter") * 1000, D("swStep") * 1000, D("rsw") / 1000)
            : Loc.S("PunchingSummaryNoSw", "без поперечной арматуры");
        return string.Format(c, Loc.S("PunchingBatchSummary",
                "Набор «{0}», строк: {1}. Площадь {2} мм; h = {3:0} мм, h0 = {4:0.#} мм; бетон {5}, Rbt = {6:0.###} МПа; {7}"),
            data.ForceSetTag, data.Rows.Count, area, D("h") * 1000, D("h0") * 1000, concrete, D("rbt") / 1000, sw);
    }
}
