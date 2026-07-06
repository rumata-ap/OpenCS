using System.Text.Json;
using System.Windows;

namespace OpenCS.ViewModels;

/// <summary>Разобранные данные результата задачи кручения из JSON.</summary>
public sealed class TorsionResultData
{
    public string Method { get; }
    public bool IsFem => Method == "fem";
    public bool IsBem => Method == "bem";
    public string FemOrder { get; }
    public string Status { get; }
    public double ItMm4 { get; }
    public double ShearCenterXmm { get; }
    public double ShearCenterYmm { get; }
    public bool HasShearCenter { get; }
    public double TauUnitMaxMm2 { get; }
    public double TauMaxMpa { get; }
    public bool HasPhysicalTau { get; }
    public double TwistRate { get; }
    public double GMpa { get; }
    public double EMpa { get; }
    public double MkKNm { get; }
    public int NElements { get; }
    public double ElementSizeM { get; }
    public bool Singular { get; }
    public string? Error { get; }

    public bool AutoConverge { get; }
    public double[]? ConvergenceHMm { get; }
    public double[]? ConvergenceItMm4 { get; }
    public double? ItOrder { get; }
    public bool ItExtrapolated { get; }
    public double? ShearCenterOrderX { get; }
    public double? ShearCenterOrderY { get; }
    public bool ShearCenterExtrapolated { get; }

    public IReadOnlyList<Point> OuterHullMm { get; }
    public IReadOnlyList<IReadOnlyList<Point>> HolesMm { get; }

    public double[]? NodeXM { get; }
    public double[]? NodeYM { get; }
    public double[]? TauUnit { get; }
    public double[]? Potential { get; }
    public int[][]? Triangles { get; }
    public double[]? BoundaryXM { get; }
    public double[]? BoundaryYM { get; }
    public int[]? BoundaryJ1 { get; }

    public bool HasFieldMesh => Triangles is { Length: > 0 };
    public bool HasBoundaryField => BoundaryJ1 is { Length: > 0 };

    TorsionResultData(
        string method, string status,
        double itMm4, double scXmm, double scYmm, bool hasSc,
        double tauUnitMaxMm2, double tauMaxMpa, bool hasPhysicalTau,
        double twistRate, double gMpa, double eMpa, double mkKNm,
        int nElements, double elementSizeM, bool singular, string? error,
        IReadOnlyList<Point> outer, IReadOnlyList<IReadOnlyList<Point>> holes,
        double[]? nodeX, double[]? nodeY, double[]? tauUnit, double[]? potential,
        int[][]? triangles, double[]? bX, double[]? bY, int[]? bJ1,
        bool autoConverge, double[]? convergenceHMm, double[]? convergenceItMm4,
        double? itOrder, bool itExtrapolated,
        double? scOrderX, double? scOrderY, bool scExtrapolated,
        string femOrder)
    {
        Method = method;
        FemOrder = femOrder;
        Status = status;
        ItMm4 = itMm4;
        ShearCenterXmm = scXmm;
        ShearCenterYmm = scYmm;
        HasShearCenter = hasSc;
        TauUnitMaxMm2 = tauUnitMaxMm2;
        TauMaxMpa = tauMaxMpa;
        HasPhysicalTau = hasPhysicalTau;
        TwistRate = twistRate;
        GMpa = gMpa;
        EMpa = eMpa;
        MkKNm = mkKNm;
        NElements = nElements;
        ElementSizeM = elementSizeM;
        Singular = singular;
        Error = error;
        OuterHullMm = outer;
        HolesMm = holes;
        NodeXM = nodeX;
        NodeYM = nodeY;
        TauUnit = tauUnit;
        Potential = potential;
        Triangles = triangles;
        BoundaryXM = bX;
        BoundaryYM = bY;
        BoundaryJ1 = bJ1;
        AutoConverge = autoConverge;
        ConvergenceHMm = convergenceHMm;
        ConvergenceItMm4 = convergenceItMm4;
        ItOrder = itOrder;
        ItExtrapolated = itExtrapolated;
        ShearCenterOrderX = scOrderX;
        ShearCenterOrderY = scOrderY;
        ShearCenterExtrapolated = scExtrapolated;
    }

    public static TorsionResultData FromCalcResult(CScore.CalcResult r)
    {
        if (string.IsNullOrWhiteSpace(r.DataJson))
            return Empty(r.Status);

        try
        {
            using var doc = JsonDocument.Parse(r.DataJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var err))
                return Empty(r.Status, err.GetString());

            string method = root.TryGetProperty("method", out var m) ? m.GetString() ?? "" : "";
            double itMm4 = root.TryGetProperty("It_mm4", out var it) ? it.GetDouble() : double.NaN;
            double scx = root.TryGetProperty("shear_center_x_m", out var sx) ? sx.GetDouble() * 1000.0 : double.NaN;
            double scy = root.TryGetProperty("shear_center_y_m", out var sy) ? sy.GetDouble() * 1000.0 : double.NaN;
            bool hasSc = double.IsFinite(scx) && double.IsFinite(scy);
            double tauUnitMax = root.TryGetProperty("tau_unit_max_mm2", out var tum)
                ? tum.GetDouble()
                : (root.TryGetProperty("tau_unit_max", out var tu) ? tu.GetDouble() * 1e6 : double.NaN);
            double tauMaxMpa = root.TryGetProperty("tau_max_Pa", out var tp) && double.IsFinite(tp.GetDouble())
                ? tp.GetDouble() / 1e6 : double.NaN;
            double gMpa = root.TryGetProperty("g_mpa", out var g) ? g.GetDouble() : 0;
            double eMpa = root.TryGetProperty("e_mpa", out var e) ? e.GetDouble() : 0;
            double mkKNm = root.TryGetProperty("mk_knm", out var mk) ? mk.GetDouble() : 0;
            double twist = root.TryGetProperty("twist_rate", out var tr) ? tr.GetDouble() : double.NaN;
            bool hasPhys = double.IsFinite(tauMaxMpa) && gMpa > 0 && mkKNm > 0;
            int nEl = root.TryGetProperty("n_elements", out var nel) ? nel.GetInt32() : 0;
            double es = root.TryGetProperty("element_size_m", out var esm) ? esm.GetDouble() : double.NaN;
            bool singular = root.TryGetProperty("singular", out var s) && s.GetBoolean();
            string femOrder = root.TryGetProperty("fem_order", out var fo) ? fo.GetString() ?? "linear" : "linear";

            var outer = ParseContourMm(root, "outer_x_mm", "outer_y_mm");
            var holes = ParseHolesMm(root);

            bool autoConverge = root.TryGetProperty("auto_converge", out var ac) && ac.GetBoolean();
            double? itOrder = ParseNullableDouble(root, "it_order");
            bool itExtrapolated = root.TryGetProperty("it_extrapolated", out var ie) && ie.ValueKind == JsonValueKind.True;
            double? scOrderX = ParseNullableDouble(root, "shear_center_order_x");
            double? scOrderY = ParseNullableDouble(root, "shear_center_order_y");
            bool scExtrapolated = root.TryGetProperty("shear_center_extrapolated", out var se) && se.ValueKind == JsonValueKind.True;

            return new TorsionResultData(
                method, r.Status ?? "",
                itMm4, scx, scy, hasSc,
                tauUnitMax, tauMaxMpa, hasPhys,
                twist, gMpa, eMpa, mkKNm,
                nEl, es, singular, null,
                outer, holes,
                ParseDoubleArray(root, "node_x"),
                ParseDoubleArray(root, "node_y"),
                ParseDoubleArray(root, "tau_unit"),
                ParseDoubleArray(root, "potential"),
                ParseTriangles(root),
                ParseDoubleArray(root, "boundary_x"),
                ParseDoubleArray(root, "boundary_y"),
                ParseIntArray(root, "boundary_j1"),
                autoConverge,
                ParseDoubleArray(root, "convergence_h_mm"),
                ParseDoubleArray(root, "convergence_it_mm4"),
                itOrder, itExtrapolated,
                scOrderX, scOrderY, scExtrapolated, femOrder);
        }
        catch
        {
            return Empty(r.Status);
        }
    }

    static TorsionResultData Empty(string status, string? error = null) =>
        new("", status, double.NaN, double.NaN, double.NaN, false,
            double.NaN, double.NaN, false, double.NaN, 0, 0, 0, 0, double.NaN, false, error,
            [], [],
            null, null, null, null, null, null, null, null,
            false, null, null, null, false, null, null, false, "");

    static double? ParseNullableDouble(JsonElement root, string key)
        => root.TryGetProperty(key, out var v) && v.ValueKind is JsonValueKind.Number ? v.GetDouble() : null;

    static IReadOnlyList<Point> ParseContourMm(JsonElement root, string xKey, string yKey)
    {
        var xs = ParseDoubleArray(root, xKey);
        var ys = ParseDoubleArray(root, yKey);
        if (xs == null || ys == null || xs.Length != ys.Length) return [];
        var pts = new Point[xs.Length];
        for (int i = 0; i < xs.Length; i++)
            pts[i] = new Point(xs[i], ys[i]);
        return ScaleLegacyContourMm(pts);
    }

    static IReadOnlyList<IReadOnlyList<Point>> ParseHolesMm(JsonElement root)
    {
        if (!root.TryGetProperty("holes_x_mm", out var hxArr) ||
            !root.TryGetProperty("holes_y_mm", out var hyArr) ||
            hxArr.ValueKind != JsonValueKind.Array ||
            hyArr.ValueKind != JsonValueKind.Array)
            return [];

        int n = hxArr.GetArrayLength();
        if (n != hyArr.GetArrayLength()) return [];
        var holes = new List<IReadOnlyList<Point>>(n);
        for (int h = 0; h < n; h++)
        {
            var xs = hxArr[h].EnumerateArray().Select(e => e.GetDouble()).ToArray();
            var ys = hyArr[h].EnumerateArray().Select(e => e.GetDouble()).ToArray();
            if (xs.Length != ys.Length) continue;
            var pts = new Point[xs.Length];
            for (int i = 0; i < xs.Length; i++)
                pts[i] = new Point(xs[i], ys[i]);
            holes.Add(ScaleLegacyContourMm(pts));
        }
        return holes;
    }

    /// <summary>Старые результаты хранили координаты в метрах в полях *_mm.</summary>
    static IReadOnlyList<Point> ScaleLegacyContourMm(IReadOnlyList<Point> pts)
    {
        if (pts.Count == 0) return pts;
        double maxAbs = 0;
        foreach (var p in pts)
            maxAbs = Math.Max(maxAbs, Math.Max(Math.Abs(p.X), Math.Abs(p.Y)));
        if (maxAbs >= 10.0) return pts;
        return pts.Select(p => new Point(p.X * 1000.0, p.Y * 1000.0)).ToArray();
    }

    static double[]? ParseDoubleArray(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return null;
        return arr.EnumerateArray().Select(e => e.GetDouble()).ToArray();
    }

    static int[]? ParseIntArray(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return null;
        return arr.EnumerateArray().Select(e => e.GetInt32()).ToArray();
    }

    static int[][]? ParseTriangles(JsonElement root)
    {
        if (!root.TryGetProperty("triangles", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return null;
        var tris = new List<int[]>();
        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Array) continue;
            tris.Add(el.EnumerateArray().Select(e => e.GetInt32()).ToArray());
        }
        return tris.Count > 0 ? tris.ToArray() : null;
    }

    public double FieldValue(int i, TorsionFieldMode mode)
    {
        if (mode == TorsionFieldMode.Potential)
            return Potential?[i] ?? double.NaN;
        double tu = TauUnit?[i] ?? double.NaN;
        return mode switch
        {
            TorsionFieldMode.TauUnit => tu * 1e6,
            TorsionFieldMode.TauMpa when HasPhysicalTau => GMpa * TwistRate * tu,
            _ => double.NaN
        };
    }

    public (double min, double max) FieldRange(TorsionFieldMode mode)
    {
        double min = double.MaxValue, max = double.MinValue;
        void Acc(double v)
        {
            if (!double.IsFinite(v)) return;
            if (v < min) min = v;
            if (v > max) max = v;
        }

        if (mode == TorsionFieldMode.Potential && Potential != null)
        {
            foreach (var v in Potential) Acc(v);
        }
        else if (TauUnit != null)
        {
            for (int i = 0; i < TauUnit.Length; i++)
                Acc(FieldValue(i, mode));
        }

        if (min > max) return (0, 1);
        if (Math.Abs(max - min) < 1e-14) return (min, min + 1);
        return (min, max);
    }
}

public enum TorsionFieldMode
{
    TauUnit,
    TauMpa,
    Potential
}
