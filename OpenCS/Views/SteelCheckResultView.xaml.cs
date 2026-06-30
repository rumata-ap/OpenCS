using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
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
        var cfg = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            Delimiter = ";"
        };
        using var csv = new CsvWriter(writer, cfg);

        // Заголовок
        csv.WriteField("Формула");
        csv.WriteField("Проверка");
        csv.WriteField("Норма");
        csv.WriteField("Трассировка");
        csv.WriteField("Приложенное");
        csv.WriteField("Допустимое");
        csv.WriteField("Коэфф.");
        csv.WriteField("Результат");
        csv.NextRecord();

        // Группы
        foreach (var group in vm.Groups)
        {
            csv.NextRecord();
            csv.WriteField(group.Name);
            csv.NextRecord();

            foreach (var d in group.Items)
            {
                csv.WriteField(d.Formula);
                csv.WriteField(d.Description);
                csv.WriteField(d.NormRef);
                csv.WriteField(d.Trace);
                csv.WriteField(d.AppliedDisplay);
                csv.WriteField(d.AllowableDisplay);
                csv.WriteField(d.RatioText);
                csv.WriteField(d.PassedText);
                csv.NextRecord();
            }
        }

        // Итог
        csv.NextRecord();
        csv.WriteField("");
        csv.WriteField(vm.VerdictText);
        csv.NextRecord();
    }
}

/// <summary>
/// ViewModel для развёрнутого отчёта проверки стального сечения.
/// </summary>
public class SteelCheckResultVM
{
    public string SectionTag { get; }
    public string SteelTag { get; }
    public double Utilization { get; }
    public string UtilizationValue => $"{Loc.S("SteelCheckUtilizationLabel")}{Utilization:P1}";
    public Brush UtilizationBrush => Utilization switch
    {
        < 0.8 => Brushes.Green,
        < 1.0 => Brushes.DarkOrange,
        _ => Brushes.Red
    };
    public string StatusValue => Utilization <= 1.0
        ? Loc.S("SteelCheckStatusPassed")
        : Loc.S("SteelCheckStatusFailed");
    public Brush StatusBrush => Utilization <= 1.0
        ? new SolidColorBrush(Color.FromArgb(40, 0, 128, 0))
        : new SolidColorBrush(Color.FromArgb(40, 255, 0, 0));

    // Контекст расчёта
    public string ContextSummary { get; private set; } = "";
    // Усилия
    public string ForcesSummary { get; private set; } = "";
    // Итоговый вердикт
    public string VerdictText { get; private set; } = "";
    public Brush VerdictBrush => Utilization <= 1.0 ? Brushes.Green : Brushes.Red;

    // Все детали
    public List<SteelCheckDetailVM> Details { get; } = [];
    // Группы
    public List<SteelCheckGroupVM> Groups { get; } = [];

    public SteelCheckResultVM(string dataJson)
    {
        var doc = JsonDocument.Parse(dataJson);
        var root = doc.RootElement;

        SectionTag = root.GetProperty("sectionTag").GetString() ?? "";
        SteelTag = root.GetProperty("steelTag").GetString() ?? "";
        Utilization = root.GetProperty("utilization").GetDouble();

        // Контекст
        if (root.TryGetProperty("context", out var ctx))
        {
            var sb = new StringBuilder();
            sb.Append($"l₀x={ctx.GetProperty("l0x").GetDouble():F2} м");
            sb.Append($"  l₀y={ctx.GetProperty("l0y").GetDouble():F2} м");
            var muX = ctx.GetProperty("muX").GetDouble();
            var muY = ctx.GetProperty("muY").GetDouble();
            if (Math.Abs(muX - 1.0) > 0.001 || Math.Abs(muY - 1.0) > 0.001)
                sb.Append($"  μx={muX:F2}  μy={muY:F2}");
            var betaM = ctx.GetProperty("betaM").GetDouble();
            if (Math.Abs(betaM - 1.0) > 0.001)
                sb.Append($"  βm={betaM:F2}");
            sb.Append($"  γM={ctx.GetProperty("gammaM").GetDouble():F3}");
            var lbit = ctx.GetProperty("lbit").GetDouble();
            if (lbit > 0.001)
                sb.Append($"  lbit={lbit:F2} м");
            ContextSummary = sb.ToString();
        }

        // Усилия (в кН, как в InternalForces)
        if (root.TryGetProperty("forces", out var f))
        {
            var sb = new StringBuilder();
            var name = f.GetProperty("name").GetString();
            if (!string.IsNullOrEmpty(name))
                sb.Append($"{name}:  ");
            sb.Append($"N = {f.GetProperty("n").GetDouble():F1} кН");
            sb.Append($"  Mx = {f.GetProperty("mx").GetDouble():F1} кН·м");
            sb.Append($"  My = {f.GetProperty("my").GetDouble():F1} кН·м");
            var mz = f.GetProperty("mz").GetDouble();
            var qy = f.GetProperty("qy").GetDouble();
            var qz = f.GetProperty("qz").GetDouble();
            if (Math.Abs(mz) > 0.01)
                sb.Append($"  Mz = {mz:F1} кН·м");
            if (Math.Abs(qy) > 0.01)
                sb.Append($"  Qy = {qy:F1} кН");
            if (Math.Abs(qz) > 0.01)
                sb.Append($"  Qz = {qz:F1} кН");
            ForcesSummary = sb.ToString();
        }

        // Детали
        if (root.TryGetProperty("details", out var arr))
        {
            foreach (var d in arr.EnumerateArray())
            {
                var formula = d.GetProperty("formula").GetString() ?? "";
                var detail = new SteelCheckDetailVM
                {
                    Formula = formula,
                    Description = d.GetProperty("description").GetString() ?? "",
                    NormRef = d.TryGetProperty("normRef", out var nr) ? (nr.GetString() ?? "") : "",
                    Category = d.TryGetProperty("category", out var cat) ? (cat.GetString() ?? "")
                             : formula.StartsWith("8.") ? "strength"
                             : formula.StartsWith("9.") ? "stability"
                             : formula.StartsWith("10.") ? "constructive" : "",
                    Applied = d.GetProperty("applied").GetDouble(),
                    Allowable = d.GetProperty("allowable").GetDouble(),
                    Ratio = d.GetProperty("ratio").GetDouble(),
                    Passed = d.GetProperty("passed").GetBoolean()
                };

                // Переменные трассировки — показываем все с единицами
                if (d.TryGetProperty("variables", out var vars))
                {
                    detail.Variables = [];
                    foreach (var kv in vars.EnumerateObject())
                        detail.Variables[kv.Name] = kv.Value.GetDouble();
                    detail.Trace = FormatTrace(detail.Variables, formula);
                }

                Details.Add(detail);
            }
        }

        // Группировка
        var strengthItems = Details.Where(d => d.Category == "strength").ToList();
        var stabilityItems = Details.Where(d => d.Category == "stability").ToList();
        var constructiveItems = Details.Where(d => d.Category == "constructive").ToList();

        if (strengthItems.Count > 0)
            Groups.Add(new SteelCheckGroupVM
            {
                Name = "Прочность (раздел 8 СП 16)",
                HeaderBrush = new SolidColorBrush(Color.FromRgb(41, 128, 185)),
                Items = strengthItems,
                MaxRatio = strengthItems.Max(d => d.Ratio)
            });
        if (stabilityItems.Count > 0)
            Groups.Add(new SteelCheckGroupVM
            {
                Name = "Устойчивость (раздел 9 СП 16)",
                HeaderBrush = new SolidColorBrush(Color.FromRgb(142, 68, 173)),
                Items = stabilityItems,
                MaxRatio = stabilityItems.Max(d => d.Ratio)
            });
        if (constructiveItems.Count > 0)
            Groups.Add(new SteelCheckGroupVM
            {
                Name = "Конструктивные (раздел 10 СП 16)",
                HeaderBrush = new SolidColorBrush(Color.FromRgb(39, 174, 96)),
                Items = constructiveItems,
                MaxRatio = constructiveItems.Max(d => d.Ratio)
            });

        // Applied/Allowable с единицами
        foreach (var det in Details)
        {
            det.AppliedDisplay = FormatWithUnit(det.Applied, det.Formula);
            det.AllowableDisplay = FormatWithUnit(det.Allowable, det.Formula);
        }

        // Вердикт
        var worst = Details.OrderByDescending(d => d.Ratio).FirstOrDefault();
        if (worst != null)
        {
            VerdictText = Utilization <= 1.0
                ? $"ПРОЙДЕНО  —  коэфф. использования {Utilization:P1}"
                : $"НЕ ПРОЙДЕНО  —  коэфф. использования {Utilization:P1}  (наихудшая: {worst.Formula} {worst.Description})";
        }
    }

    /// <summary>Applied/Allowable с единицами по типу проверки.</summary>
    static string FormatWithUnit(double v, string formula)
    {
        // 10.x — гибкость (безразмерная)
        if (formula.StartsWith("10."))
            return v.ToString("F1");
        // 8.4, 9.5 — гибкость (безразмерная)
        if (formula is "8.4" or "9.5")
            return v.ToString("F3");
        // Силовые: всё в кН (кПа × м² = кН)
        return $"{v:F1} кН";
    }

    /// <summary>Формат трассировки: кПа+м+кН, единицы для отображения.</summary>
    static string FormatTrace(Dictionary<string, double> vars, string formula)
    {
        var parts = new List<string>();
        foreach (var (key, val) in vars)
        {
            string unit = key switch
            {
                // Силы и моменты — уже в кН / кН·м
                "N" or "M" or "Mx" or "My" or "Mz" or "Q" or "Qy" or "Qz"
                    => $"{val:F1} кН",
                "Ncr" => $"{val:F1} кН",
                // Площади — м² → см²
                "Aeff" or "An" or "Aw" => $"{val * 1e4:F1} см²",
                // Моменты сопротивления — м³ → см³
                "Weff" or "Wx" or "Wy" or "Wt" => $"{val * 1e6:F1} см³",
                // Моменты инерции — м⁴ → см⁴
                "Ix" or "Iy" => $"{val * 1e8:F0} см⁴",
                // Напряжения — кПа → МПа
                "Ry" => $"{val / 1e3:F1} МПа",
                "E" => $"{val / 1e6:F0} ГПа",
                "τ" or "τcr" or "σ" => $"{val / 1e3:F1} МПа",
                // Безразмерные коэффициенты
                "φ" or "φb" or "φt" or "ρ" => val.ToString("F3"),
                "λ̄" or "λ̄local" => val.ToString("F3"),
                "η" or "βm" or "γM" or "ν" => val.ToString("F3"),
                "k" => val.ToString("F1"),
                // Размеры — м → мм
                "a+5h0" or "a" or "h0" or "h" or "tw" => $"{val * 1e3:F1} мм",
                _ => val.ToString("G4")
            };
            parts.Add($"{key} = {unit}");
        }
        return string.Join(";  ", parts);
    }
}

/// <summary>
/// Группа проверок (Прочность / Устойчивость / Конструктивные).
/// </summary>
public class SteelCheckGroupVM
{
    public string Name { get; set; } = "";
    public Brush HeaderBrush { get; set; } = Brushes.Gray;
    public List<SteelCheckDetailVM> Items { get; set; } = [];
    public double MaxRatio { get; set; }
    public string MaxRatioText => $"max {MaxRatio:F3}";
    public Brush MaxRatioBrush => MaxRatio switch
    {
        < 0.8 => Brushes.Green,
        < 1.0 => Brushes.DarkOrange,
        _ => Brushes.Red
    };
}

/// <summary>
/// Детали одной проверки с трассировкой.
/// </summary>
public class SteelCheckDetailVM
{
    public string Formula { get; set; } = "";
    public string Description { get; set; } = "";
    public string NormRef { get; set; } = "";
    public string Category { get; set; } = "";
    public double Applied { get; set; }
    public double Allowable { get; set; }
    public double Ratio { get; set; }
    public bool Passed { get; set; }
    public string Trace { get; set; } = "";
    public Dictionary<string, double> Variables { get; set; } = [];
    public string AppliedDisplay { get; set; } = "";
    public string AllowableDisplay { get; set; } = "";
    public string RatioText => Ratio.ToString("F3");
    public string PassedText => Passed
        ? Loc.S("SteelCheckOK")
        : Loc.S("SteelCheckFail");
    public Brush RatioBrush => Ratio switch
    {
        < 0.8 => Brushes.Green,
        < 1.0 => Brushes.DarkOrange,
        _ => Brushes.Red
    };
    public Brush PassedBrush => Passed ? Brushes.Green : Brushes.Red;
    public Brush RowBackground => Passed
        ? new SolidColorBrush(Color.FromArgb(0x1A, 0x2D, 0x7A, 0x3E))
        : new SolidColorBrush(Color.FromArgb(0x1A, 0xC0, 0x39, 0x2B));
}
