using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Один выбираемый стержень (точечное волокно арматуры) для графиков деформация/напряжение-момент.</summary>
public sealed class RebarOption : ViewModelBase
{
    /// <summary>Порядковый номер стержня (сквозная нумерация по всем областям, как в StrainSummaryVM).</summary>
    public int Index { get; }
    public string Label { get; }
    public Brush ColorBrush { get; }
    internal Fiber Fiber { get; }

    bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public RebarOption(int index, Fiber fiber, string label, string colorHex)
    {
        Index = index;
        Fiber = fiber;
        Label = label;
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(colorHex)!;
        brush.Freeze();
        ColorBrush = brush;
    }
}

/// <summary>Одна точка траектории деформации/напряжения выбранного стержня.</summary>
public sealed class RebarSeriesPoint
{
    public double MomentAbs { get; init; }
    public double Eps { get; init; }
    public double SigmaMPa { get; init; }
    public bool NonPhysical { get; init; }
}

/// <summary>Готовые для отрисовки серии (физическая + блёклая часть) деформации и напряжения стержня.</summary>
public sealed record RebarSeriesResult(
    double[] MomentEps, double[] Eps, double[] MomentEpsFaded, double[] EpsFaded,
    double[] MomentSigma, double[] Sigma, double[] MomentSigmaFaded, double[] SigmaFaded);

/// <summary>Непрерывный фрагмент серии графика с одинаковым физическим статусом точек.</summary>
public sealed record MomentCurvaturePlotSeries(double[] X, double[] Y);

/// <summary>ViewModel результата задачи «Кривизна-момент (двухплоскостной изгиб)».</summary>
public sealed class MomentCurvatureBiaxialResultVM : ViewModelBase
{
    public string TaskTag { get; }
    public string CreatedText { get; }
    public string StatusText { get; } = "";
    public Brush StatusBrush { get; } = Brushes.Gray;
    public bool HasError { get; }
    public string ErrorText { get; } = "";

    public bool HasMx { get; }
    public bool HasMy { get; }
    public bool UsePsi { get; }
    public string UsePsiText => UsePsi ? Loc.S("MomentCurvature_Yes") : Loc.S("MomentCurvature_No");
    public string NModeText { get; } = "";

    public ObservableCollection<MomentCurvatureBiaxialPointRow> Rows { get; } = [];
    public int ConvergedCount { get; }
    public int TotalCount => Rows.Count;
    public string ConvergedCountText => $"{ConvergedCount}/{TotalCount}";

    public double[] CurvatureYSeries { get; private set; } = [];
    public double[] MomentXSeries { get; private set; } = [];
    public double[] CurvatureZSeries { get; private set; } = [];
    public double[] MomentYSeries { get; private set; } = [];
    public double[] CurvatureYSeriesFaded { get; private set; } = [];
    public double[] MomentXSeriesFaded { get; private set; } = [];
    public double[] CurvatureZSeriesFaded { get; private set; } = [];
    public double[] MomentYSeriesFaded { get; private set; } = [];

    public IReadOnlyList<MomentCurvaturePlotSeries> CurvatureYSeriesParts { get; private set; } = [];
    public IReadOnlyList<MomentCurvaturePlotSeries> CurvatureYSeriesFadedParts { get; private set; } = [];
    public IReadOnlyList<MomentCurvaturePlotSeries> CurvatureZSeriesParts { get; private set; } = [];
    public IReadOnlyList<MomentCurvaturePlotSeries> CurvatureZSeriesFadedParts { get; private set; } = [];

    public double[] NStiffnessAxis { get; private set; } = [];
    public double[] NStiffnessRatio { get; private set; } = [];
    public double[] MxStiffnessAxis { get; private set; } = [];
    public double[] MxStiffnessRatio { get; private set; } = [];
    public double[] MyStiffnessAxis { get; private set; } = [];
    public double[] MyStiffnessRatio { get; private set; } = [];

    public MomentCurvatureBiaxialPointRow? Cracking { get; private set; }
    public MomentCurvatureBiaxialPointRow? CrackTransition { get; private set; }
    public MomentCurvatureBiaxialPointRow? Yield { get; private set; }
    public MomentCurvatureBiaxialPointRow? Ultimate { get; private set; }

    readonly CrossSection? _section;
    readonly CalcType _calcType;

    /// <summary>Палитра для наложения кривых нескольких стержней (matplotlib tab10).</summary>
    static readonly string[] RebarPalette =
    [
        "#1F77B4", "#FF7F0E", "#2CA02C", "#D62728", "#9467BD",
        "#8C564B", "#E377C2", "#7F7F7F", "#BCBD22", "#17BECF"
    ];

    public ObservableCollection<RebarOption> RebarOptions { get; } = [];
    public bool HasRebarData => RebarOptions.Count > 0;

    public MomentCurvatureBiaxialResultVM(CalcResult result, CrossSection? section = null,
        CalcType calcType = CalcType.C, CalcSettings? settings = null,
        IReadOnlyList<Diagramm>? diagramPool = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        TaskTag = result.TaskTag;
        CreatedText = result.Created;
        _section = section;
        _calcType = calcType;

        if (result.Status == "error")
        {
            HasError = true;
            ErrorText = ExtractError(result.DataJson);
            StatusText = Loc.S("CalcResultErrorLabel");
            StatusBrush = Brushes.Firebrick;
            return;
        }

        try
        {
            var root = JsonDocument.Parse(result.DataJson).RootElement;
            HasMx = root.TryGetProperty("has_mx", out var hasMx) && hasMx.GetBoolean();
            HasMy = root.TryGetProperty("has_my", out var hasMy) && hasMy.GetBoolean();
            UsePsi = root.TryGetProperty("use_psi", out var usePsi) && usePsi.GetBoolean();
            string nMode = root.TryGetProperty("n_mode", out var nm) ? nm.GetString() ?? "constant" : "constant";
            NModeText = nMode == "proportional"
                ? Loc.S("MomentCurvature_NModeProportional")
                : Loc.S("MomentCurvature_NModeConstant");

            double ea0 = root.TryGetProperty("ea0", out var ea0El) && ea0El.ValueKind == JsonValueKind.Number
                ? ea0El.GetDouble() : 0.0;
            double b0x = root.TryGetProperty("b0x", out var b0xEl) && b0xEl.ValueKind == JsonValueKind.Number
                ? b0xEl.GetDouble() : 0.0;
            double b0y = root.TryGetProperty("b0y", out var b0yEl) && b0yEl.ValueKind == JsonValueKind.Number
                ? b0yEl.GetDouble() : 0.0;

            var mxRows = new List<MomentCurvatureBiaxialPointRow>();
            var myRows = new List<MomentCurvatureBiaxialPointRow>();
            var nAxis = new List<double>(); var nRatio = new List<double>();
            var mxAxis = new List<double>(); var mxRatio = new List<double>();
            var myAxis = new List<double>(); var myRatio = new List<double>();
            int converged = 0;

            if (root.TryGetProperty("points", out var points) && points.ValueKind == JsonValueKind.Array)
            {
                int index = 0;
                foreach (var p in points.EnumerateArray())
                {
                    var row = MomentCurvatureBiaxialPointRow.Parse(p);
                    Rows.Add(row);
                    if (row.Converged)
                    {
                        converged++;
                        // Графики строятся по модулю — направление луча момента/кривизны
                        // задаётся знаком входной нагрузки и само по себе не информативно,
                        // пользователь ожидает вид |κ|-|M| независимо от знака Mx0/My0.
                        // Участок 2 — вспомогательная пересчётная петля. При включённом ψs
                        // она не является частью основной кривой (Example 47: петля показана
                        // отдельно, а основная ψs-ветвь продолжается от Mcrc).
                        if (HasMx && (!UsePsi || row.Segment != 2)) mxRows.Add(row);
                        if (HasMy && (!UsePsi || row.Segment != 2)) myRows.Add(row);

                        // Первая точка траектории (κ≈0/λ≈0) исключается из секанс-серий —
                        // секущая жёсткость там неопределена (0/0). См. спеку, решение 7.
                        if (index > 0)
                        {
                            if (ea0 != 0.0 && Math.Abs(row.E0) > 1e-12)
                            {
                                nAxis.Add(Math.Abs(row.N));
                                nRatio.Add(Math.Abs((row.N / row.E0) / ea0));
                            }
                            if (HasMx && b0x != 0.0 && Math.Abs(row.Ky) > 1e-12)
                            {
                                mxAxis.Add(Math.Abs(row.Mx));
                                mxRatio.Add(Math.Abs((row.Mx / row.Ky) / b0x));
                            }
                            if (HasMy && b0y != 0.0 && Math.Abs(row.Kz) > 1e-12)
                            {
                                myAxis.Add(Math.Abs(row.My));
                                myRatio.Add(Math.Abs((row.My / row.Kz) / b0y));
                            }
                        }
                    }
                    index++;
                }
            }

            Cracking = TryParseControlPoint(root, "cracking");
            CrackTransition = TryParseControlPoint(root, "crack_transition");
            Yield = TryParseControlPoint(root, "yield_point");
            Ultimate = TryParseControlPoint(root, "ultimate");

            (CurvatureYSeries, MomentXSeries, CurvatureYSeriesFaded, MomentXSeriesFaded) =
                SplitByNonPhysical(mxRows, r => r.NonPhysical, r => Math.Abs(r.Ky), r => Math.Abs(r.Mx));
            (CurvatureZSeries, MomentYSeries, CurvatureZSeriesFaded, MomentYSeriesFaded) =
                SplitByNonPhysical(myRows, r => r.NonPhysical, r => Math.Abs(r.Kz), r => Math.Abs(r.My));
            (CurvatureYSeriesParts, CurvatureYSeriesFadedParts) =
                SplitByNonPhysicalRuns(mxRows, r => r.NonPhysical, r => Math.Abs(r.Ky), r => Math.Abs(r.Mx));
            (CurvatureZSeriesParts, CurvatureZSeriesFadedParts) =
                SplitByNonPhysicalRuns(myRows, r => r.NonPhysical, r => Math.Abs(r.Kz), r => Math.Abs(r.My));
            NStiffnessAxis = nAxis.ToArray();
            NStiffnessRatio = nRatio.ToArray();
            MxStiffnessAxis = mxAxis.ToArray();
            MxStiffnessRatio = mxRatio.ToArray();
            MyStiffnessAxis = myAxis.ToArray();
            MyStiffnessRatio = myRatio.ToArray();
            ConvergedCount = converged;

            StatusText = result.Status switch
            {
                "ok" => Loc.S("MomentCurvature_StatusOk"),
                "partial" => Loc.S("MomentCurvature_StatusPartial"),
                _ => Loc.S("CalcResultErrorLabel")
            };
            StatusBrush = result.Status switch
            {
                "ok" => Brushes.SeaGreen,
                "partial" => Brushes.DarkOrange,
                _ => Brushes.Firebrick
            };
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorText = ex.Message;
            StatusText = Loc.S("CalcResultErrorLabel");
            StatusBrush = Brushes.Firebrick;
        }

        if (section != null)
            BuildRebarOptions(section, settings, diagramPool);
    }

    void BuildRebarOptions(CrossSection section, CalcSettings? settings, IReadOnlyList<Diagramm>? diagramPool)
    {
        try
        {
            var actualSettings = settings ?? CalcSettings.Default;
            section.ResolveAndBuildDiagramms(actualSettings.Sp63DescEtaMin,
                pool: diagramPool, rebarDifferentialDiagram: actualSettings.RebarDifferentialDiagram);

            var zero = new Kurvature { e0 = 0, ky = 0, kz = 0 };
            int index = 1;
            foreach (var (area, _) in section.EnumerateAreas(zero))
                foreach (var fiber in area.Fibers.Where(f => f.TypeFiber == FiberType.point))
                {
                    string tag = string.IsNullOrWhiteSpace(area.Tag) ? Loc.S("MomentCurvature_RebarDefaultTag") : area.Tag;
                    string label = $"№{index} ({fiber.X * 1000:0.#}; {fiber.Y * 1000:0.#})  мм — {tag}";
                    RebarOptions.Add(new RebarOption(index, fiber, label, RebarPalette[(index - 1) % RebarPalette.Length]));
                    index++;
                }
        }
        catch
        {
            RebarOptions.Clear();
        }
    }

    /// <summary>Строит траекторию деформации/напряжения выбранного стержня по уже посчитанным
    /// точкам кривой (E0/Ky/Kz) — солвер и JSON-контракт задачи не меняются.</summary>
    public RebarSeriesResult? BuildRebarSeries(RebarOption option, bool useMx)
    {
        if (_section == null) return null;

        var points = new List<RebarSeriesPoint>();
        foreach (var row in Rows)
        {
            if (!row.Converged) continue;
            if (UsePsi && row.Segment == 2) continue;
            var k = new Kurvature { e0 = row.E0, ky = row.Ky, kz = row.Kz };
            bool ten = row.Segment <= 2;
            _section.SetEps(k, _calcType, ten, true);
            points.Add(new RebarSeriesPoint
            {
                MomentAbs = Math.Abs(useMx ? row.Mx : row.My),
                Eps = option.Fiber.Eps,
                SigmaMPa = option.Fiber.Sig / 1000.0,
                NonPhysical = row.NonPhysical
            });
        }
        if (points.Count < 2) return null;

        var (momentEps, eps, momentEpsFaded, epsFaded) =
            SplitByNonPhysical(points, p => p.NonPhysical, p => p.MomentAbs, p => p.Eps);
        var (momentSigma, sigma, momentSigmaFaded, sigmaFaded) =
            SplitByNonPhysical(points, p => p.NonPhysical, p => p.MomentAbs, p => p.SigmaMPa);

        return new RebarSeriesResult(
            momentEps, eps, momentEpsFaded, epsFaded,
            momentSigma, sigma, momentSigmaFaded, sigmaFaded);
    }

    /// <summary>Деформация/напряжение выбранного стержня в одной контрольной точке (трещина/текучесть/предел).</summary>
    public (double momentAbs, double eps, double sigmaMPa)? RebarValueAt(
        RebarOption option, MomentCurvatureBiaxialPointRow? point, bool useMx)
    {
        if (_section == null || point == null) return null;
        var k = new Kurvature { e0 = point.E0, ky = point.Ky, kz = point.Kz };
        bool ten = point.Segment <= 2;
        _section.SetEps(k, _calcType, ten, true);
        return (Math.Abs(useMx ? point.Mx : point.My), option.Fiber.Eps, option.Fiber.Sig / 1000.0);
    }

    static (double[] x, double[] y, double[] xFaded, double[] yFaded) SplitByNonPhysical<T>(
        List<T> rows, Func<T, bool> nonPhysical,
        Func<T, double> xSel, Func<T, double> ySel)
    {
        int firstFlagged = rows.FindIndex(r => nonPhysical(r));
        if (firstFlagged < 0)
            return (rows.ConvertAll(r => xSel(r)).ToArray(), rows.ConvertAll(r => ySel(r)).ToArray(), [], []);

        var physical = rows.Take(firstFlagged + 1).ToList(); // включая граничную (первую флаг.) точку
        var faded = rows.Skip(firstFlagged).ToList(); // граничная точка дублируется для непрерывности

        return (
            physical.ConvertAll(r => xSel(r)).ToArray(), physical.ConvertAll(r => ySel(r)).ToArray(),
            faded.ConvertAll(r => xSel(r)).ToArray(), faded.ConvertAll(r => ySel(r)).ToArray());
    }

    static (IReadOnlyList<MomentCurvaturePlotSeries> physical,
        IReadOnlyList<MomentCurvaturePlotSeries> nonPhysical) SplitByNonPhysicalRuns<T>(
        IReadOnlyList<T> rows, Func<T, bool> nonPhysical,
        Func<T, double> xSel, Func<T, double> ySel)
    {
        var physical = new List<MomentCurvaturePlotSeries>();
        var nonPhysicalParts = new List<MomentCurvaturePlotSeries>();
        if (rows.Count == 0) return (physical, nonPhysicalParts);

        if (rows.Count == 1)
        {
            AddRun(nonPhysical(rows[0]), [rows[0]]);
            return (physical, nonPhysicalParts);
        }

        var run = new List<T> { rows[0], rows[1] };
        bool runNonPhysical = nonPhysical(rows[0]) || nonPhysical(rows[1]);
        for (int i = 2; i < rows.Count; i++)
        {
            bool segmentNonPhysical = nonPhysical(rows[i - 1]) || nonPhysical(rows[i]);
            if (segmentNonPhysical != runNonPhysical)
            {
                AddRun(runNonPhysical, run);

                // Общая граничная точка сохраняет геометрию перехода между цветами.
                run = [rows[i - 1], rows[i]];
                runNonPhysical = segmentNonPhysical;
            }
            else
            {
                run.Add(rows[i]);
            }
        }
        AddRun(runNonPhysical, run);

        return (physical, nonPhysicalParts);

        void AddRun(bool isNonPhysical, List<T> points)
        {
            var series = new MomentCurvaturePlotSeries(
                points.ConvertAll(x => xSel(x)).ToArray(),
                points.ConvertAll(y => ySel(y)).ToArray());
            (isNonPhysical ? nonPhysicalParts : physical).Add(series);
        }
    }

    static MomentCurvatureBiaxialPointRow? TryParseControlPoint(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var el) || el.ValueKind != JsonValueKind.Object) return null;
        var row = MomentCurvatureBiaxialPointRow.Parse(el);
        return row.Converged ? row : null;
    }

    static string ExtractError(string dataJson)
    {
        try
        {
            var doc = JsonDocument.Parse(dataJson);
            return doc.RootElement.TryGetProperty("error", out var error) ? error.GetString() ?? "" : dataJson;
        }
        catch
        {
            return dataJson;
        }
    }
}

/// <summary>Одна строка траектории составной диаграммы кривизна-момент.</summary>
public sealed class MomentCurvatureBiaxialPointRow
{
    public double N { get; init; }
    public double Mx { get; init; }
    public double My { get; init; }
    public double E0 { get; init; }
    public double Ky { get; init; }
    public double Kz { get; init; }
    public int Segment { get; init; }
    public bool Converged { get; init; }
    public bool PsiActive { get; init; }
    public bool NonPhysical { get; init; }

    /// <summary>Локализованный статус превышения эталонного предельного усилия.</summary>
    public string LimitStatusText => NonPhysical
        ? Loc.S("MomentCurvature_NonPhysicalStatus")
        : Loc.S("MomentCurvature_PhysicalStatus");

    public static MomentCurvatureBiaxialPointRow Parse(JsonElement p) => new()
    {
        N = GetDouble(p, "n"),
        Mx = GetDouble(p, "mx"),
        My = GetDouble(p, "my"),
        E0 = GetDouble(p, "e0"),
        Ky = GetDouble(p, "ky"),
        Kz = GetDouble(p, "kz"),
        Segment = p.TryGetProperty("segment", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt32() : 0,
        Converged = p.TryGetProperty("converged", out var c) && c.ValueKind == JsonValueKind.True && c.GetBoolean(),
        PsiActive = p.TryGetProperty("psi_active", out var pa) && pa.ValueKind == JsonValueKind.True && pa.GetBoolean(),
        NonPhysical = p.TryGetProperty("non_physical", out var np) && np.ValueKind == JsonValueKind.True && np.GetBoolean()
    };

    static double GetDouble(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0.0;
}
