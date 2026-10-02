using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.Views;

public partial class FemCheckResultView : UserControl
{
    /// <param name="db">БД с результатом — для строк, хранимых отдельно от <c>DataJson</c>.</param>
    public FemCheckResultView(CScore.CalcResult result, DatabaseService? db = null)
    {
        InitializeComponent();
        FemCheckRowsLoader? loader = db != null && result.Id > 0
            ? (elemNum, onlyFailed, limit) => (db.GetFemCheckRows(result.Id, elemNum, onlyFailed, limit),
                                               db.CountFemCheckRows(result.Id, elemNum, onlyFailed))
            : null;
        var vm = new FemCheckResultVM(result.DataJson, loader);
        DataContext = vm;

        // Результаты без агрегата по КЭ (одно сечение на цель) показываются как раньше — одной таблицей.
        Tabs.SelectedIndex = vm.IsPerElement ? 0 : 1;
        var perElement = vm.IsPerElement ? Visibility.Visible : Visibility.Collapsed;
        RowElemColumn.Visibility = RowSectionNumColumn.Visibility = RowSectionColumn.Visibility = perElement;
        AsSelectedColumn.Visibility = AsProvidedColumn.Visibility =
            vm.HasSelectedAs ? Visibility.Visible : Visibility.Collapsed;

        // Коэффициент использования — по столбцу на каждый источник армирования.
        int index = ElementsGrid.Columns.IndexOf(ElemSectionColumn) + 1;
        foreach (string key in vm.SourceKeys)
        {
            string header = Loc.S("FemCheckResultUtil");
            if (vm.SourceKeys.Count > 1) header += " · " + FemCheckContext.SourceName(key);
            ElementsGrid.Columns.Insert(index++, new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding($"Utils[{key}]"),
                SortMemberPath = $"UtilValues[{key}]",
                Width = vm.SourceKeys.Count > 1 ? 120 : 80,
            });
        }
    }
}

/// <summary>Чтение строк результата из БД: строки КЭ (или все), без прошедших, не больше <paramref name="limit"/>;
/// вместе с их полным числом.</summary>
public delegate (IReadOnlyList<CScore.Fem.FemCheckRow> Rows, int Total) FemCheckRowsLoader(int? elemNum, bool onlyFailed, int limit);

public class FemCheckResultVM : ViewModelBase
{
    /// <summary>Сколько строк результата показывать без выбора КЭ (худшие).</summary>
    public const int RowsLimit = 10_000;

    /// <summary>Ключ единственного «источника» у стержневых проверок (в результате он пустой).</summary>
    const string BarSourceKey = "bar";

    public string  SummaryText  { get; } = "";
    public Brush   SummaryBrush { get; } = Brushes.LightGray;
    /// <summary>Пропущенные строки, КЭ без усилий, предупреждения.</summary>
    public string  DetailsText  { get; } = "";
    public Visibility DetailsVisibility => DetailsText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Результат проверки по КЭ (есть агрегат по КЭ).</summary>
    public bool IsPerElement { get; }
    public Visibility ElementsTabVisibility => IsPerElement ? Visibility.Visible : Visibility.Collapsed;
    /// <summary>Источники армирования в порядке расчёта.</summary>
    public IReadOnlyList<string> SourceKeys { get; } = [];
    /// <summary>Есть справочная подобранная арматура стержней.</summary>
    public bool HasSelectedAs { get; }

    readonly List<FemCheckRowVM> _rows = [];
    readonly List<FemCheckElementRowVM> _elements = [];
    /// <summary>Строки хранятся в БД и читаются по фильтру; null — строки в самом <c>DataJson</c>.</summary>
    readonly FemCheckRowsLoader? _loadRows;
    ICollectionView _rowsView = null!;
    public ICollectionView Rows { get => _rowsView; private set { _rowsView = value; OnPropertyChanged(); } }
    public ICollectionView Elements { get; }

    string _rowsLimitText = "";
    /// <summary>Пояснение, что показана только часть строк.</summary>
    public string RowsLimitText
    {
        get => _rowsLimitText;
        private set { _rowsLimitText = value; OnPropertyChanged(); OnPropertyChanged(nameof(RowsLimitVisibility)); }
    }
    public Visibility RowsLimitVisibility => _rowsLimitText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    bool _onlyFailed;
    /// <summary>Показывать только не прошедшие и не проверенные.</summary>
    public bool OnlyFailed
    {
        get => _onlyFailed;
        set { _onlyFailed = value; OnPropertyChanged(); RefreshRows(); Elements.Refresh(); }
    }

    FemCheckElementRowVM? _selectedElement;
    /// <summary>КЭ, выбранный в таблице «По КЭ»: таблица строк показывает только его строки.</summary>
    public FemCheckElementRowVM? SelectedElement
    {
        get => _selectedElement;
        set
        {
            _selectedElement = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ElementFilterText));
            OnPropertyChanged(nameof(ElementFilterVisibility));
            RefreshRows();
        }
    }

    public string ElementFilterText =>
        _selectedElement == null ? "" : string.Format(Loc.S("FemCheckResultElemFilter"), _selectedElement.ElemText);
    public Visibility ElementFilterVisibility => _selectedElement == null ? Visibility.Collapsed : Visibility.Visible;
    public ICommand ClearElementFilterCommand { get; }

    public FemCheckResultVM(string dataJson, FemCheckRowsLoader? loadRows = null)
    {
        ClearElementFilterCommand = new RelayCommand(_ => SelectedElement = null);
        try
        {
            using var doc  = JsonDocument.Parse(dataJson);
            var root = doc.RootElement;

            // Проверка не выполнялась (нет наборов усилий / сечения): показываем причину, а не «0 из 0».
            if (root.TryGetProperty("error", out var err))
            {
                SummaryText  = string.Format(Loc.S("FemCheckResultError"), err.GetString());
                SummaryBrush = Failed;
            }
            else
            {
                IsPerElement = root.TryGetProperty("elements", out var elementsArr);
                int total  = Int(root, "totalRows");
                int passed = Int(root, "passedRows");
                int failed = Int(root, "failedRows");
                if (loadRows != null && root.TryGetProperty("rowsStored", out var stored) && stored.ValueKind == JsonValueKind.True)
                    _loadRows = loadRows;

                if (root.TryGetProperty("rows", out var rowsArr))
                    foreach (var r in rowsArr.EnumerateArray())
                        _rows.Add(new FemCheckRowVM
                        {
                            Label        = Str(r, "label"),
                            ForceSetTag  = Str(r, "forceSetTag"),
                            CalcType     = Str(r, "calcType"),
                            Utilization  = r.TryGetProperty("utilization", out var u) && u.ValueKind == JsonValueKind.Number
                                           ? u.GetDouble() : double.NaN,
                            Passed       = r.TryGetProperty("passed", out var ps) && ps.GetBoolean(),
                            NotChecked   = r.TryGetProperty("notChecked", out var nc) && nc.ValueKind == JsonValueKind.True,
                            WorstFormula = Str(r, "worstFormula"),
                            WorstDesc    = Str(r, "worstDescription"),
                            ElemNum      = NullableInt(r, "elemNum"),
                            SectionNum   = NullableInt(r, "sectionNum"),
                            SectionLabel = Str(r, "sectionLabel"),
                        });

                if (!IsPerElement)
                {
                    SummaryText  = string.Format(Loc.S("FemCheckResultSummary"), passed, failed, total);
                    SummaryBrush = failed == 0 ? Ok : Failed;
                }
                else
                {
                    SourceKeys = root.TryGetProperty("rebarSources", out var src) && src.GetArrayLength() > 0
                        ? [.. src.EnumerateArray().Select(s => s.GetString() ?? "")]
                        : [BarSourceKey];
                    ReadElements(elementsArr);
                    HasSelectedAs = _elements.Any(e => e.AsSelectedText.Length > 0);
                    (SummaryText, SummaryBrush, DetailsText) = Summarize(root, total, passed, failed);
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            SummaryText  = Loc.S("FemCheckResultReadError");
            SummaryBrush = Brushes.LightGray;
        }

        if (_loadRows != null)
            LoadRows();
        else
            // Сначала не прошедшие без коэффициента, затем по убыванию Кисп; непроверенные — в конце.
            Rows = new ListCollectionView(_rows
                .OrderBy(r => r.State == "failed" && double.IsNaN(r.Utilization) ? 0 : r.NotChecked ? 2 : 1)
                .ThenByDescending(r => r.Utilization).ToList())
            {
                Filter = o => o is FemCheckRowVM r
                              && (!_onlyFailed || r.State != "ok")
                              && (_selectedElement == null || r.ElemNum == _selectedElement.ElemNum),
            };
        Elements = new ListCollectionView(_elements
            .OrderBy(e => e.State == "failed" && double.IsNaN(e.SortUtil) ? 0 : e.State is "failed" or "ok" ? 1 : 2)
            .ThenByDescending(e => e.SortUtil).ToList())
        {
            Filter = o => o is FemCheckElementRowVM e && (!_onlyFailed || e.State != "ok"),
        };
    }

    void RefreshRows()
    {
        if (_loadRows != null) LoadRows();
        else Rows.Refresh();
    }

    /// <summary>Строки из БД по текущему фильтру: у выбранного КЭ — все, иначе — худшие <see cref="RowsLimit"/>.</summary>
    void LoadRows()
    {
        int limit = _selectedElement == null ? RowsLimit : int.MaxValue;
        var (rows, total) = _loadRows!(_selectedElement?.ElemNum, _onlyFailed, limit);
        Rows = new ListCollectionView(rows.Select(r => new FemCheckRowVM
        {
            Label        = r.Label,
            ForceSetTag  = r.ForceSetTag,
            CalcType     = r.CalcType,
            Utilization  = r.Utilization,
            Passed       = r.Passed,
            NotChecked   = r.NotChecked,
            WorstFormula = r.WorstFormula,
            WorstDesc    = r.WorstDescription,
            ElemNum      = r.ElemNum,
            SectionNum   = r.SectionNum,
            SectionLabel = r.SectionLabel,
        }).ToList());
        RowsLimitText = rows.Count < total ? string.Format(Loc.S("FemCheckResultRowsLimited"), rows.Count, total) : "";
    }

    static readonly Brush Ok      = new SolidColorBrush(Color.FromArgb(70, 80, 180, 80));
    static readonly Brush Failed  = new SolidColorBrush(Color.FromArgb(60, 192, 57, 43));
    static readonly Brush Partial = new SolidColorBrush(Color.FromArgb(70, 230, 170, 40));

    /// <summary>Агрегаты по КЭ: одна строка таблицы на КЭ, коэффициент — по каждому источнику армирования.</summary>
    void ReadElements(JsonElement elementsArr)
    {
        var byTag = new Dictionary<string, FemCheckElementRowVM>();
        foreach (var e in elementsArr.EnumerateArray())
        {
            string tag = Str(e, "elemTag");
            string source = Str(e, "rebarSource");
            if (source.Length == 0) source = BarSourceKey;
            if (!byTag.TryGetValue(tag, out var row))
            {
                byTag[tag] = row = new FemCheckElementRowVM { ElemNum = NullableInt(e, "elemNum"), ElemText = tag };
                _elements.Add(row);
            }

            string status = Str(e, "status");
            double util = e.TryGetProperty("utilMax", out var u) && u.ValueKind == JsonValueKind.Number ? u.GetDouble() : double.NaN;
            row.Utils[source] = status switch
            {
                "ok" or "failed" when !double.IsNaN(util) => util.ToString("F3"),
                "failed"      => "✗",
                "no_forces"   => Loc.S("FemCheckElemNoForces"),
                "no_section"  => Loc.S("FemCheckElemNoSection"),
                "no_rebar"    => Loc.S("FemCheckElemNoRebar"),
                _             => Loc.S("FemCheckElemNotChecked"),
            };
            row.UtilValues[source] = double.IsNaN(util) ? -1 : util;

            string label = Str(e, "sectionLabel");
            if (label.Length > 0 && !row.SectionLabels.Contains(label)) row.SectionLabels.Add(label);
            row.Rows = Math.Max(row.Rows, Int(e, "rows"));
            row.NotChecked = Math.Max(row.NotChecked, Int(e, "notChecked"));
            if (e.TryGetProperty("asSelected", out var a1) && a1.ValueKind == JsonValueKind.Number) row.AsSelectedText = a1.GetDouble().ToString("F2");
            if (e.TryGetProperty("asProvided", out var a2) && a2.ValueKind == JsonValueKind.Number) row.AsProvidedText = a2.GetDouble().ToString("F2");

            // Определяющий источник: не прошедший важнее непроверенного, тот — прошедшего; внутри — больший Кисп.
            string state = status is "ok" or "failed" ? status : "not_checked";
            int rank = state == "failed" ? 3 : state == "not_checked" ? 2 : 1;
            if (rank > row.Rank || (rank == row.Rank && (double.IsNaN(row.SortUtil) ? !double.IsNaN(util) : util > row.SortUtil)))
            {
                row.Rank = rank;
                row.State = state;
                row.SortUtil = util;
                row.ForceSetTag = Str(e, "forceSetTag");
                row.Label = Str(e, "label");
                string formula = Str(e, "worstFormula"), desc = Str(e, "worstDescription"), reason = Str(e, "reason");
                row.WorstCheckText = reason.Length > 0 ? reason : formula.Length == 0 ? desc : $"{formula} {desc}";
                row.StatusText = status switch
                {
                    "ok"     => "✓",
                    "failed" => "✗",
                    _        => row.Utils[source],
                };
            }
        }
    }

    (string Summary, Brush Brush, string Details) Summarize(JsonElement root, int total, int passed, int failed)
    {
        var summary = root.GetProperty("summary");
        int elementsTotal = Int(summary, "elementsTotal");
        var lines = new List<string>();
        int anyFailed = 0, anyUnchecked = 0;
        var sources = summary.GetProperty("sources").EnumerateArray().ToList();
        foreach (var s in sources)
        {
            int ok = Int(s, "passed"), bad = Int(s, "failed"), unchecked_ = Int(s, "notChecked");
            anyFailed += bad;
            anyUnchecked += unchecked_;
            string prefix = sources.Count > 1 ? FemCheckContext.SourceName(Str(s, "source")) + " — " : "";
            lines.Add(prefix + string.Format(Loc.S("FemCheckResultElemSummary"), ok, bad, unchecked_, elementsTotal));
        }

        int notCheckedRows = Int(root, "notCheckedRows");
        var details = new List<string>
        {
            string.Format(Loc.S("FemCheckResultRowSummary"), total, passed, failed, notCheckedRows),
        };
        int skipped = Int(root, "skippedRows");
        if (skipped > 0) details.Add(string.Format(Loc.S("FemCheckResultSkipped"), skipped));
        string withoutForces = Str(summary, "elementsWithoutForces");
        if (withoutForces.Length > 0) details.Add(string.Format(Loc.S("FemCheckResultNoForces"), withoutForces));
        if (summary.TryGetProperty("warnings", out var warnings))
            details.AddRange(warnings.EnumerateArray().Select(w => "⚠ " + w.GetString()));

        var brush = anyFailed > 0 || failed > 0 ? Failed : anyUnchecked > 0 || notCheckedRows > 0 ? Partial : Ok;
        return (string.Join("\n", lines), brush, string.Join("\n", details));
    }

    static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    static int Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

    static int? NullableInt(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
}

public class FemCheckRowVM
{
    public string Label        { get; init; } = "";
    public string ForceSetTag  { get; init; } = "";
    public string CalcType     { get; init; } = "";
    public double Utilization  { get; init; }
    public bool   Passed       { get; init; }
    /// <summary>Строка не проверена (нет сечения, ошибка расчёта) — не то же, что «не прошла».</summary>
    public bool   NotChecked   { get; init; }
    public string WorstFormula { get; init; } = "";
    public string WorstDesc    { get; init; } = "";
    public int?   ElemNum      { get; init; }
    public int?   SectionNum   { get; init; }
    public string SectionLabel { get; init; } = "";

    /// <summary>"ok" | "failed" | "not_checked" — для раскраски строки.</summary>
    public string State          => Passed ? "ok" : NotChecked ? "not_checked" : "failed";
    public string UtilText       => double.IsNaN(Utilization) ? "—" : Utilization.ToString("F3");
    public string WorstCheckText => string.IsNullOrEmpty(WorstFormula) ? WorstDesc : $"{WorstFormula} {WorstDesc}";
    public string StatusText     => Passed ? "✓" : NotChecked ? "—" : "✗";
}

/// <summary>Строка таблицы «По КЭ»: сводка по одному КЭ цели.</summary>
public class FemCheckElementRowVM
{
    public int?   ElemNum  { get; init; }
    public string ElemText { get; init; } = "";

    /// <summary>Коэффициент использования (или причина его отсутствия) по ключу источника армирования.</summary>
    public Dictionary<string, string> Utils { get; } = [];
    /// <summary>Числовой коэффициент по ключу источника — для сортировки столбца.</summary>
    public Dictionary<string, double> UtilValues { get; } = [];
    internal List<string> SectionLabels { get; } = [];
    public string SectionLabel => string.Join(" / ", SectionLabels);

    internal int Rank { get; set; }
    /// <summary>Наибольший коэффициент определяющего источника.</summary>
    public double SortUtil { get; set; } = double.NaN;
    /// <summary>"ok" | "failed" | "not_checked" — по худшему источнику.</summary>
    public string State          { get; set; } = "not_checked";
    public string StatusText     { get; set; } = "";
    public string ForceSetTag    { get; set; } = "";
    public string Label          { get; set; } = "";
    public string WorstCheckText { get; set; } = "";
    public int    Rows           { get; set; }
    public int    NotChecked     { get; set; }
    public string AsSelectedText { get; set; } = "";
    public string AsProvidedText { get; set; } = "";
}
