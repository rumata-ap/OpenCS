using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Media;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

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

    public MomentCurvatureBiaxialResultVM(CalcResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        TaskTag = result.TaskTag;
        CreatedText = result.Created;

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
                        if (HasMx) mxRows.Add(row);
                        if (HasMy) myRows.Add(row);

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
                SplitByNonPhysical(mxRows, r => Math.Abs(r.Ky), r => Math.Abs(r.Mx));
            (CurvatureZSeries, MomentYSeries, CurvatureZSeriesFaded, MomentYSeriesFaded) =
                SplitByNonPhysical(myRows, r => Math.Abs(r.Kz), r => Math.Abs(r.My));
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
    }

    static (double[] x, double[] y, double[] xFaded, double[] yFaded) SplitByNonPhysical(
        List<MomentCurvatureBiaxialPointRow> rows,
        Func<MomentCurvatureBiaxialPointRow, double> xSel, Func<MomentCurvatureBiaxialPointRow, double> ySel)
    {
        int firstFlagged = rows.FindIndex(r => r.NonPhysical);
        if (firstFlagged < 0)
            return (rows.ConvertAll(r => xSel(r)).ToArray(), rows.ConvertAll(r => ySel(r)).ToArray(), [], []);

        var physical = rows.Take(firstFlagged + 1).ToList(); // включая граничную (первую флаг.) точку
        var faded = rows.Skip(firstFlagged).ToList(); // граничная точка дублируется для непрерывности

        return (
            physical.ConvertAll(r => xSel(r)).ToArray(), physical.ConvertAll(r => ySel(r)).ToArray(),
            faded.ConvertAll(r => xSel(r)).ToArray(), faded.ConvertAll(r => ySel(r)).ToArray());
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
