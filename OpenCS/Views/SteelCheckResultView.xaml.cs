using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CsvHelper;
using CsvHelper.Configuration;
using OpenCS.Utilites;

namespace OpenCS.Views;

public partial class SteelCheckResultView : UserControl
{
    public SteelCheckResultView(string dataJson)
    {
        InitializeComponent();
        DataContext = new SteelCheckResultVM(dataJson);
    }

    /// <summary>Таблицы групп не перехватывают прокрутку колесом — прокручивается вся страница.</summary>
    void Grid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not DependencyObject d) return;
        e.Handled = true;
        var parent = VisualTreeHelper.GetParent(d) as UIElement;
        parent?.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            { RoutedEvent = MouseWheelEvent, Source = sender });
    }

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SteelCheckResultVM vm || vm.Details.Count == 0) return;

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = Loc.S("ExportCsv") + "|*.csv",
            DefaultExt = ".csv",
            FileName = $"SP16_{vm.SectionTag}.csv"
        };
        if (dlg.ShowDialog() != true) return;

        using var writer = new StreamWriter(dlg.FileName, false, System.Text.Encoding.UTF8);
        using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = ";" });

        foreach (var key in new[] { "Sp16ColClause", "Sp16ColFormula", "Sp16ColDescription", "Sp16ColApplied",
                     "Sp16ColAllowable", "Sp16ColRatio", "Sp16ColStatus", "Sp16ColVariables", "Sp16ColNotes" })
            csv.WriteField(Loc.S(key));
        csv.NextRecord();

        foreach (var group in vm.Groups)
        {
            csv.WriteField(group.Name);
            csv.NextRecord();
            foreach (var d in group.Items)
            {
                csv.WriteField(d.Clause);
                csv.WriteField(d.Formula);
                csv.WriteField(d.Description);
                csv.WriteField(d.AppliedDisplay);
                csv.WriteField(d.AllowableDisplay);
                csv.WriteField(d.RatioText);
                csv.WriteField(d.PassedText);
                csv.WriteField(d.Trace);
                csv.WriteField(d.NotesText);
                csv.NextRecord();
            }
        }

        csv.NextRecord();
        csv.WriteField(vm.VerdictText);
        csv.NextRecord();
    }
}

/// <summary>
/// ViewModel отчёта проверки стального сечения по СП 16. Читает результат схемы v2 (пункт, формула,
/// статус, переменные, примечания; неопределённые числа — null) и прежнюю схему v1 (только просмотр).
/// </summary>
public class SteelCheckResultVM
{
    public string SectionTag { get; } = "";
    public string SteelTag { get; } = "";
    /// <summary>Наибольший коэффициент использования; NaN — не определён (ошибка или бесконечный).</summary>
    public double Utilization { get; }
    public bool Passed { get; }
    public string StatusValue => Passed ? Loc.S("SteelCheckStatusPassed") : Loc.S("SteelCheckStatusFailed");
    public Brush StatusBrush => Passed
        ? new SolidColorBrush(Color.FromArgb(40, 0, 128, 0))
        : new SolidColorBrush(Color.FromArgb(40, 255, 0, 0));

    /// <summary>Параметры расчёта и общие примечания.</summary>
    public string ContextSummary { get; } = "";
    public string ForcesSummary { get; } = "";
    public string VerdictText { get; } = "";
    public Brush VerdictBrush => Passed ? Brushes.Green : Brushes.Red;

    public List<SteelCheckDetailVM> Details { get; } = [];
    public List<SteelCheckGroupVM> Groups { get; } = [];
    /// <summary>Определяющая проверка (наибольший коэффициент среди выполненных); null — нет выполненных.</summary>
    public SteelCheckDetailVM? Worst { get; }

    public SteelCheckResultVM(string dataJson)
    {
        using var doc = JsonDocument.Parse(dataJson);
        var root = doc.RootElement;
        if (root.TryGetProperty("error", out var error))
        {
            Utilization = double.NaN;
            VerdictText = error.GetString() ?? "";
            ContextSummary = VerdictText;
            return;
        }
        bool v2 = root.TryGetProperty("schemaVersion", out var version) && version.GetInt32() >= 2;

        SectionTag = Str(root, "sectionTag");
        SteelTag = Str(root, "steelTag");
        Utilization = Number(root, "utilization");
        Passed = root.TryGetProperty("passed", out var passed) ? passed.GetBoolean() : Utilization <= 1;

        if (root.TryGetProperty("details", out var arr))
            foreach (var d in arr.EnumerateArray())
            {
                var formula = Str(d, "formula");
                var detail = new SteelCheckDetailVM
                {
                    Clause = Str(d, "clause"),
                    Formula = formula,
                    Description = Str(d, "description"),
                    Category = d.TryGetProperty("category", out var cat) ? (cat.GetString() ?? "")
                        : formula.StartsWith("10.") ? "constructive"
                        : formula.StartsWith("9.") ? "stability" : "strength",
                    Applied = Number(d, "applied"),
                    Allowable = Number(d, "allowable"),
                    Ratio = Number(d, "ratio"),
                    NotApplicable = Str(d, "status") == "NotApplicable",
                    Passed = d.TryGetProperty("passed", out var p) && p.ValueKind == JsonValueKind.True,
                };
                if (detail.Clause.Length == 0 && d.TryGetProperty("normRef", out var nr)) detail.Clause = nr.GetString() ?? "";
                if (d.TryGetProperty("variables", out var vars) && vars.ValueKind == JsonValueKind.Object)
                    detail.Trace = string.Join(";  ", vars.EnumerateObject().Select(v => $"{v.Name} = {Display(Num(v.Value))}"));
                if (d.TryGetProperty("notes", out var notes) && notes.ValueKind == JsonValueKind.Array)
                    detail.NotesText = string.Join("\n", notes.EnumerateArray().Select(n => n.GetString()));
                detail.AppliedDisplay = Display(detail.Applied);
                detail.AllowableDisplay = Display(detail.Allowable);
                Details.Add(detail);
            }

        AddGroup("strength", "Sp16Strength", Color.FromRgb(41, 128, 185));
        AddGroup("stability", "Sp16Stability", Color.FromRgb(142, 68, 173));
        AddGroup("constructive", "Sp16Constructive", Color.FromRgb(39, 174, 96));

        var notesTop = root.TryGetProperty("notes", out var topNotes) && topNotes.ValueKind == JsonValueKind.Array
            ? string.Join("\n", topNotes.EnumerateArray().Select(n => n.GetString())) : "";
        if (v2)
        {
            var c = root.GetProperty("context");
            ContextSummary = string.Format(Loc.S("Sp16Context"), Number(c, "lefX"), Number(c, "lefY"),
                Number(c, "lefB"), Number(c, "gammaC"));
            var f = root.GetProperty("forces");
            ForcesSummary = string.Format(Loc.S("Sp16Forces"), Number(f, "n"), Number(f, "mx"), Number(f, "my"),
                Number(f, "qx"), Number(f, "qy"), Number(f, "t"));
            var name = Str(f, "name");
            if (name.Length > 0) ForcesSummary = name + ":  " + ForcesSummary;
        }
        else
        {
            // Схема v1 (старый модуль) — только просмотр сохранённых результатов.
            ContextSummary = Loc.S("Sp16LegacyResult");
            if (root.TryGetProperty("forces", out var f))
                ForcesSummary = string.Format(Loc.S("Sp16Forces"), Number(f, "n"), Number(f, "mx"), Number(f, "my"),
                    Number(f, "qy"), Number(f, "qz"), Number(f, "mz"));
        }
        if (notesTop.Length > 0) ContextSummary += "\n" + notesTop;

        // Определяющая проверка: не пройденная с неопределённым коэффициентом — наихудшая.
        var worst = Worst = Details.Where(d => !d.NotApplicable).OrderByDescending(d => d.SortRatio).FirstOrDefault();
        string util = double.IsFinite(Utilization) ? Utilization.ToString("P1") : "—";
        VerdictText = worst == null ? StatusValue
            : Passed ? string.Format(Loc.S("Sp16VerdictPassed"), util)
            : string.Format(Loc.S("Sp16VerdictFailed"), util,
                $"{worst.Clause} {worst.Formula} — {worst.Description}".Trim());
    }

    void AddGroup(string category, string nameKey, Color color)
    {
        var items = Details.Where(d => d.Category == category).ToList();
        if (items.Count == 0) return;
        var applicable = items.Where(d => !d.NotApplicable).ToList();
        Groups.Add(new SteelCheckGroupVM
        {
            Name = Loc.S(nameKey),
            HeaderBrush = new SolidColorBrush(color),
            Items = items,
            MaxRatio = applicable.Count == 0 ? double.NaN : applicable.Max(d => d.SortRatio),
            AnyFailed = applicable.Any(d => !d.Passed),
        });
    }

    static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    static double Number(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? Num(v) : double.NaN;
    static double Num(JsonElement v) => v.ValueKind == JsonValueKind.Number ? v.GetDouble() : double.NaN;
    internal static string Display(double value) => double.IsFinite(value) ? value.ToString("G4") : "—";
}

/// <summary>Группа проверок (прочность / устойчивость / предельная гибкость).</summary>
public class SteelCheckGroupVM
{
    public string Name { get; set; } = "";
    public Brush HeaderBrush { get; set; } = Brushes.Gray;
    public List<SteelCheckDetailVM> Items { get; set; } = [];
    /// <summary>Наибольший коэффициент выполненных проверок (+∞ — не пройдена с неопределённым; NaN — нет выполненных).</summary>
    public double MaxRatio { get; set; }
    public bool AnyFailed { get; set; }
    public string MaxRatioText => double.IsNaN(MaxRatio) ? ""
        : double.IsPositiveInfinity(MaxRatio) ? "max ∞" : $"max {MaxRatio:F3}";
    public Brush MaxRatioBrush => AnyFailed || MaxRatio >= 1.0 ? Brushes.Red
        : MaxRatio >= 0.8 ? Brushes.DarkOrange : Brushes.Green;
}

/// <summary>Одна проверка: пункт, формула, значения, статус, переменные и примечания.</summary>
public class SteelCheckDetailVM
{
    public bool NotApplicable { get; set; }
    public string Clause { get; set; } = "";
    public string Formula { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public double Applied { get; set; }
    public double Allowable { get; set; }
    public double Ratio { get; set; }
    public bool Passed { get; set; }
    /// <summary>Переменные расчёта «имя = значение».</summary>
    public string Trace { get; set; } = "";
    /// <summary>Примечания и причина неприменимости.</summary>
    public string NotesText { get; set; } = "";
    public string AppliedDisplay { get; set; } = "";
    public string AllowableDisplay { get; set; } = "";
    /// <summary>Коэффициент для сравнения: не пройденная проверка без коэффициента — +∞.</summary>
    public double SortRatio => double.IsFinite(Ratio) ? Ratio : Passed ? 0 : double.PositiveInfinity;
    public string RatioText => NotApplicable ? "—" : double.IsFinite(Ratio) ? Ratio.ToString("F3") : Passed ? "—" : "∞";
    public string PassedText => NotApplicable ? Loc.S("Sp16NotApplicable")
        : Passed ? Loc.S("SteelCheckOK") : Loc.S("SteelCheckFail");
    public Brush RatioBrush => NotApplicable ? Brushes.Gray : !Passed || SortRatio >= 1.0 ? Brushes.Red
        : SortRatio >= 0.8 ? Brushes.DarkOrange : Brushes.Green;
    public Brush PassedBrush => NotApplicable ? Brushes.Gray : Passed ? Brushes.Green : Brushes.Red;
    public Brush RowBackground => NotApplicable ? Brushes.Transparent : Passed
        ? new SolidColorBrush(Color.FromArgb(0x1A, 0x2D, 0x7A, 0x3E))
        : new SolidColorBrush(Color.FromArgb(0x1A, 0xC0, 0x39, 0x2B));
}
