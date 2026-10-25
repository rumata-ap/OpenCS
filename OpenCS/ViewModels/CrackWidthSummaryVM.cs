using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>ViewModel сводки результата задачи «Ширина раскрытия трещин» (crack_width).</summary>
public sealed class CrackWidthSummaryVM : ViewModelBase
{
    public string TaskTag { get; }
    public string CreatedText { get; }
    public string StatusText { get; }
    public Brush StatusBrush { get; }
    public bool HasError { get; }
    public string ErrorText { get; } = "";

    public string NText { get; } = "—";
    public string MxLongText { get; } = "—";
    public string MxTotalText { get; } = "—";
    public string MyLongText { get; } = "—";
    public string MyTotalText { get; } = "—";

    public CrackingMomentPartVM CrackingPart { get; }
    public PrestressSummaryVM Prestress { get; private set; } = new();

    public bool Cracked { get; }
    public string AcrcLongText { get; } = "—";
    public string AcrcUltLongText { get; } = "—";
    public string UtilLongText { get; } = "—";
    public bool PassedLong { get; }
    public string PassedLongText { get; } = "—";
    public Brush PassedLongBrush { get; } = Brushes.Gray;
    public string AcrcShortText { get; } = "—";
    public string AcrcUltShortText { get; } = "—";
    public string UtilShortText { get; } = "—";
    public bool PassedShort { get; }
    public string PassedShortText { get; } = "—";
    public Brush PassedShortBrush { get; } = Brushes.Gray;

    public string SigmaSText { get; } = "—";
    public string SigmaSCrcText { get; } = "—";
    public string SigmaSCrcShortText { get; } = "—";
    public string PsiSText { get; } = "—";
    public string PsiSShortText { get; } = "—";
    public string Acrc1Text { get; } = "—";
    public string Acrc2Text { get; } = "—";
    public string Acrc3Text { get; } = "—";
    public string LsText { get; } = "—";
    public string DsEqText { get; } = "—";
    public string AsTensText { get; } = "—";
    public string AbtText { get; } = "—";
    public string H0Text { get; } = "—";
    public string HText { get; } = "—";
    public string XtText { get; } = "—";

    public string SigmaSTotalText { get; } = "—";
    public string RsSerText { get; } = "—";
    public string UtilSteelText { get; } = "—";
    /// <summary>σs ≤ Rs,ser (п. 8.2.16); у старых результатов без проверки — выполнено.</summary>
    public bool PassedSteel { get; } = true;
    public string PassedSteelText { get; } = "—";
    public Brush PassedSteelBrush { get; } = Brushes.Gray;
    public bool HasSteelCheck { get; }
    public string UtilConcreteText { get; } = "—";
    public bool OnsetByAxialForce { get; }
    /// <summary>Трещины образует только полная нагрузка (длительная ниже Mcrc).</summary>
    public bool LongUncracked { get; }
    /// <summary>Полная нагрузка не сошлась (total_converged = false): непродолжительное раскрытие и σs ≤ Rs,ser не определены.</summary>
    public bool TotalNotConverged { get; }
    public string TotalNotConvergedText { get; } = "";
    /// <summary>Вместо значений, которые не определены при несошедшейся полной нагрузке.</summary>
    public string UndeterminedText { get; } = "—";

    public bool EtaEnabled { get; }
    public string EtaModeText { get; } = "";
    public string EtaXText { get; } = "—";
    public string EtaYText { get; } = "—";
    public string EtaPsiText { get; } = "";
    public string EtaMxText { get; } = "";
    public string EtaMyText { get; } = "";

    public ObservableCollection<RebarRow> RebarRows { get; } = [];
    public bool HasRebar => RebarRows.Count > 0;

    public bool PlaneConverged { get; }
    public bool ShowPlaneWarning => Cracked && !PlaneConverged;

    public record RebarRow(int Num, string X, string Y, string Eps, string Sigma,
        string PsiS, string AcrcLongMm, string PsiS2, string AcrcShortMm);

    /// <summary>Одна запись из "acrc_by_rebar" (см. CScore.RebarAcrcEntry) — координаты в мм.</summary>
    public readonly record struct RebarAcrcParsed(
        double XMm, double YMm, double PsiS, double AcrcLongMm, double PsiS2, double AcrcShortMm);

    /// <summary>
    /// Разбирает "acrc_by_rebar" из DataJson задачи crack_width — используется и таблицей
    /// стержней сводки (<see cref="RebarRows"/>), и тултипом стержня на канвасе сечения
    /// (см. CrackWidthResultView).
    /// </summary>
    public static IReadOnlyList<RebarAcrcParsed> ParseAcrcByRebar(string dataJson)
    {
        var result = new List<RebarAcrcParsed>();
        try
        {
            var doc = JsonDocument.Parse(dataJson);
            if (!doc.RootElement.TryGetProperty("acrc_by_rebar", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var el in arr.EnumerateArray())
            {
                if (!el.TryGetProperty("x", out var xEl) || !el.TryGetProperty("y", out var yEl)
                    || !el.TryGetProperty("psi_s", out var psEl) || !el.TryGetProperty("acrc_long_mm", out var aclEl)
                    || !el.TryGetProperty("psi_s2", out var ps2El) || !el.TryGetProperty("acrc_short_mm", out var acsEl))
                    continue;
                result.Add(new RebarAcrcParsed(xEl.GetDouble(), yEl.GetDouble(),
                    psEl.GetDouble(), aclEl.GetDouble(), ps2El.GetDouble(), acsEl.GetDouble()));
            }
        }
        catch { /* пустой список — таблица/тултип просто не покажут доп. колонку */ }
        return result;
    }

    /// <summary>Ближайшая (по X/Y в мм, допуск 1 мм) запись <see cref="ParseAcrcByRebar"/> для стержня — null, если не нашлась.</summary>
    public static RebarAcrcParsed? FindNearest(IReadOnlyList<RebarAcrcParsed> entries, double xMm, double yMm)
    {
        RebarAcrcParsed? best = null;
        double bestDist = 1.0; // мм
        foreach (var e in entries)
        {
            double dist = Math.Sqrt((e.XMm - xMm) * (e.XMm - xMm) + (e.YMm - yMm) * (e.YMm - yMm));
            if (dist < bestDist) { bestDist = dist; best = e; }
        }
        return best;
    }

    public CrackWidthSummaryVM(CalcResult result, CrossSection? section)
    {
        TaskTag = result.TaskTag;
        CreatedText = result.Created;

        if (result.Status == "error")
        {
            HasError = true;
            try
            {
                var errDoc = JsonDocument.Parse(result.DataJson);
                ErrorText = errDoc.RootElement.TryGetProperty("error", out var e) ? e.GetString() ?? "" : result.DataJson;
            }
            catch { ErrorText = result.DataJson; }
            StatusText = Loc.S("CalcResultErrorLabel");
            StatusBrush = Brushes.DarkRed;
            CrackingPart = new CrackingMomentPartVM(0, 0, 0, 0, false, null, null);
            return;
        }

        var doc = JsonDocument.Parse(result.DataJson);
        var root = doc.RootElement;
        Prestress = PrestressSummaryVM.Parse(root);

        Cracked = root.TryGetProperty("cracked", out var cr) && cr.GetBoolean();
        PassedLong = root.TryGetProperty("passed_long", out var pl) && pl.GetBoolean();
        PassedShort = root.TryGetProperty("passed_short", out var ps) && ps.GetBoolean();
        PlaneConverged = root.TryGetProperty("plane_converged", out var pc) && pc.GetBoolean();
        // Старые результаты без ключа total_converged считаются сошедшимися.
        TotalNotConverged = Cracked && root.TryGetProperty("total_converged", out var tcv) && tcv.ValueKind == JsonValueKind.False;
        UndeterminedText = Loc.S("CrackWidth_Undetermined");
        TotalNotConvergedText = Loc.S("CrackWidth_TotalNotConvergedNote");

        NText = NumUnit(root, "N", "кН");
        MxLongText = NumUnit(root, "Mx_long", "кН·м");
        MxTotalText = NumUnit(root, "Mx_total", "кН·м");
        MyLongText = NumUnit(root, "My_long", "кН·м");
        MyTotalText = NumUnit(root, "My_total", "кН·м");

        double mxLong = GetD(root, "Mx_long");
        double myLong = GetD(root, "My_long");
        // Трещины образуются, если Mcrc превышает полный момент (длительный может быть и ниже).
        double mxTotal = GetD(root, "Mx_total");
        double myTotal = GetD(root, "My_total");
        double mLong = Math.Max(Math.Sqrt(mxLong * mxLong + myLong * myLong), Math.Sqrt(mxTotal * mxTotal + myTotal * myTotal));
        double mxCrc = GetD(root, "Mx_crc");
        double myCrc = GetD(root, "My_crc");
        double mcrc = GetD(root, "Mcrc");
        bool crcConverged = root.TryGetProperty("crc_converged", out var ccv) && ccv.GetBoolean();
        double? epsMaxTension = root.TryGetProperty("eps_max_tension", out var emt) ? emt.GetDouble() : null;
        double? epsTensionLimit = root.TryGetProperty("eps_tension_limit", out var etl) ? etl.GetDouble() : null;
        double? utilCrc = mcrc > 1e-9 ? mLong / mcrc : null;
        CrackingPart = new CrackingMomentPartVM(GetD(root, "N"), mxCrc, myCrc, mcrc, crcConverged, epsMaxTension, epsTensionLimit, utilCrc);

        double acrcLong = GetD(root, "acrc_long");
        double acrcUltLong = GetD(root, "acrc_ult_long");
        AcrcLongText = $"{acrcLong:0.000}  мм";
        AcrcUltLongText = $"{acrcUltLong:0.000}  мм";
        UtilLongText = acrcUltLong > 1e-9 ? $"{acrcLong / acrcUltLong * 100:0.0}%" : "—";
        PassedLongText = PassedLong ? Loc.S("StrengthNDM_Passed") : Loc.S("StrengthNDM_NotPassed");
        PassedLongBrush = PassedLong ? Brushes.Green : Brushes.Red;

        double acrcShort = GetD(root, "acrc_short");
        double acrcUltShort = GetD(root, "acrc_ult_short");
        AcrcShortText = $"{acrcShort:0.000}  мм";
        AcrcUltShortText = $"{acrcUltShort:0.000}  мм";
        UtilShortText = acrcUltShort > 1e-9 ? $"{acrcShort / acrcUltShort * 100:0.0}%" : "—";
        PassedShortText = PassedShort ? Loc.S("StrengthNDM_Passed") : Loc.S("StrengthNDM_NotPassed");
        PassedShortBrush = PassedShort ? Brushes.Green : Brushes.Red;

        SigmaSText = NumUnit(root, "sigma_s", "МПа");
        SigmaSCrcText = NumUnit(root, "sigma_s_crc", "МПа");
        SigmaSCrcShortText = NumUnit(root, "sigma_s_crc2", "МПа");
        PsiSText = NumRaw(root, "psi_s");
        PsiSShortText = NumRaw(root, "psi_s2");
        Acrc1Text = NumUnit(root, "acrc1", "мм");
        Acrc2Text = NumUnit(root, "acrc2", "мм");
        Acrc3Text = NumUnit(root, "acrc3", "мм");
        LsText = NumUnit(root, "ls", "мм");
        DsEqText = NumUnit(root, "ds_eq", "мм");
        AsTensText = NumUnit(root, "As_tens", "см²");
        AbtText = NumUnit(root, "Abt", "см²");
        H0Text = NumUnit(root, "h0", "мм");
        HText = NumUnit(root, "h", "мм");
        XtText = NumUnit(root, "xt", "мм");

        HasSteelCheck = root.TryGetProperty("passed_steel", out var pst) && pst.ValueKind is JsonValueKind.True or JsonValueKind.False;
        if (HasSteelCheck)
        {
            PassedSteel = pst.GetBoolean();
            SigmaSTotalText = NumUnit(root, "sigma_s_total", "МПа");
            RsSerText = NumUnit(root, "rs_ser", "МПа");
            UtilSteelText = NumRaw(root, "util_steel");
            PassedSteelText = PassedSteel ? Loc.S("StrengthNDM_Passed") : Loc.S("StrengthNDM_NotPassed");
            PassedSteelBrush = PassedSteel ? Brushes.Green : Brushes.Red;
            UtilConcreteText = NumRaw(root, "util_concrete");
        }
        OnsetByAxialForce = root.TryGetProperty("onset_by_axial_force", out var oaf) && oaf.ValueKind == JsonValueKind.True;
        LongUncracked = Cracked && root.TryGetProperty("plane_is_total", out var pit) && pit.ValueKind == JsonValueKind.True;

        EtaEnabled = root.TryGetProperty("eta", out var etaEl) && etaEl.ValueKind == JsonValueKind.Object;
        if (EtaEnabled)
        {
            string mode = etaEl.TryGetProperty("mode", out var modeEl) ? modeEl.GetString() ?? "formula" : "formula";
            EtaModeText = mode == "iterative" ? Loc.S("ResultEtaModeIterative") : Loc.S("ResultEtaModeFormula");
            EtaXText = NumRaw(etaEl, "etaX");
            EtaYText = NumRaw(etaEl, "etaY");
            EtaPsiText = $"ψx={NumRaw(etaEl, "psiX")}, ψy={NumRaw(etaEl, "psiY")}";
            EtaMxText = $"Mx: {NumRaw(etaEl, "mxOriginal")} → {MxTotalText}";
            EtaMyText = $"My: {NumRaw(etaEl, "myOriginal")} → {MyTotalText}";
        }

        if (section != null && PlaneConverged
            && root.TryGetProperty("e0", out var e0El) && e0El.ValueKind == JsonValueKind.Number
            && root.TryGetProperty("ky", out var kyEl) && kyEl.ValueKind == JsonValueKind.Number
            && root.TryGetProperty("kz", out var kzEl) && kzEl.ValueKind == JsonValueKind.Number)
        {
            var k = new Kurvature { e0 = e0El.GetDouble(), ky = kyEl.GetDouble(), kz = kzEl.GetDouble() };
            var acrcByRebar = ParseAcrcByRebar(result.DataJson);
            int num = 1;
            foreach (var (area, _) in section.EnumerateAreas(k))
                foreach (var f in area.Fibers.Where(f => f.TypeFiber == FiberType.point && f.Eps > 0))
                {
                    double xMm = f.X * 1000, yMm = f.Y * 1000;
                    var nearest = FindNearest(acrcByRebar, xMm, yMm);
                    RebarRows.Add(new RebarRow(
                        num++,
                        $"{xMm:+0.0;-0.0}",
                        $"{yMm:+0.0;-0.0}",
                        $"{f.Eps:+0.00000;-0.00000}",
                        $"{f.SigOwn / 1000.0:+0.0;-0.0}",
                        nearest.HasValue ? $"{nearest.Value.PsiS:0.000}" : "—",
                        nearest.HasValue ? $"{nearest.Value.AcrcLongMm:0.000}" : "—",
                        nearest.HasValue ? ShortOrUndetermined($"{nearest.Value.PsiS2:0.000}") : "—",
                        nearest.HasValue ? ShortOrUndetermined($"{nearest.Value.AcrcShortMm:0.000}") : "—"));
                }
        }

        if (TotalNotConverged)
        {
            // Непродолжительное раскрытие и σs (полная нагрузка) не определены: фолбэк решателя
            // подставил длительную плоскость, поэтому значения не выводим как результат.
            AcrcShortText = UndeterminedText;
            UtilShortText = UndeterminedText;
            PassedShortText = UndeterminedText;
            PassedShortBrush = Brushes.DarkOrange;
            Acrc2Text = UndeterminedText;
            SigmaSCrcShortText = UndeterminedText;
            PsiSShortText = UndeterminedText;
            SigmaSTotalText = UndeterminedText;
            UtilSteelText = UndeterminedText;
            PassedSteelText = UndeterminedText;
            PassedSteelBrush = Brushes.DarkOrange;
        }

        StatusText = !PlaneConverged && Cracked
            ? Loc.S("ResultConvergedNo")
            : !Cracked
                ? Loc.S("CrackWidth_NoCracks")
                : TotalNotConverged
                    ? Loc.S("CrackWidth_TotalNotConverged")
                    : (PassedLong && PassedShort && PassedSteel ? Loc.S("StrengthNDM_Passed") : Loc.S("StrengthNDM_NotPassed"));
        StatusBrush = !PlaneConverged && Cracked
            ? Brushes.Red
            : TotalNotConverged ? Brushes.DarkOrange
            : (!Cracked || (PassedLong && PassedShort && PassedSteel)) ? Brushes.Green : Brushes.OrangeRed;
    }

    string ShortOrUndetermined(string value) => TotalNotConverged ? UndeterminedText : value;

    static double GetD(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0.0;

    static string NumRaw(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble().ToString("0.####", CultureInfo.InvariantCulture) : "—";

    static string NumUnit(JsonElement el, string key, string unit) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number
            ? $"{v.GetDouble().ToString("0.####", CultureInfo.InvariantCulture)}  {unit}" : "—";
}
