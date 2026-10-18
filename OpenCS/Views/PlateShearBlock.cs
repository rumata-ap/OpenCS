using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using OpenCS.Utilites;

namespace OpenCS.Views;

/// <summary>
/// Блок «Поперечная сила» результатов упрощённых проверок и подбора пластин (спека, раздел 8):
/// строки хода расчёта из JSON <see cref="CScore.ShellSimpl.PlateShearResult"/>. В проверке
/// поперечной арматуры у сечения нет — не выполняется ни (8.55), ни (8.60); в подборе хомуты
/// подбираются, нарушение — только (8.55).
/// </summary>
public sealed class PlateShearBlock
{
    public List<string> Lines { get; } = [];
    public List<string> Notes { get; } = [];
    public bool Ok { get; private set; }
    /// <summary>Кисп,Q проверки (наибольший из (8.55), (8.60)); в подборе — по (8.55).</summary>
    public double Eta { get; private set; }
    public string StatusText { get; private set; } = "";

    /// <summary>Блок из свойства JSON; null — свойства нет или оно null (Qx = Qy = 0, задача ПС2).</summary>
    public static PlateShearBlock? From(JsonElement doc, string property, bool design)
    {
        if (!doc.TryGetProperty(property, out var q) || q.ValueKind != JsonValueKind.Object) return null;
        var ci = CultureInfo.CurrentCulture;
        double N(string name) => q.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0.0;
        static string K(double v, CultureInfo ci) => v >= 1e9 ? "∞" : v.ToString("F3", ci);

        double q0 = N("Q0"), qStrip = N("QStrip"), qb1 = N("Qb1");
        double etaStrip = N("EtaStrip"), etaConcrete = N("EtaConcrete");
        string face = q.TryGetProperty("TensionFace", out var f) ? f.GetString() ?? "" : "";
        bool toStrip = q.TryGetProperty("PhiNToStrip", out var ts) && ts.ValueKind == JsonValueKind.True;

        var b = new PlateShearBlock();
        b.Lines.Add(string.Format(ci, Loc.S("PlateShearQ0"), q0, N("ThetaDeg"), N("Qx"), N("Qy")));
        b.Lines.Add(string.Format(ci, Loc.S("PlateShearFacet"), N("N"), N("M"),
            Loc.S(face switch { "top" => "PlateShearFaceTop", "bot" => "PlateShearFaceBot", _ => "PlateShearFaceNone" }),
            N("H0"), N("Rb") / 1000.0, N("Rbt") / 1000.0));
        b.Lines.Add(string.Format(ci, Loc.S(toStrip ? "PlateShearPhiNStrip" : "PlateShearPhiN"), N("PhiN")));
        b.Lines.Add(string.Format(ci, Loc.S("PlateShear855"), q0, qStrip, K(etaStrip, ci),
            Loc.S(etaStrip <= 1.0 ? "PlateShearMet" : "PlateShearNotMet")));
        b.Lines.Add(string.Format(ci, Loc.S("PlateShear861"), q0, qb1, K(etaConcrete, ci),
            Loc.S(etaConcrete <= 1.0 ? "PlateShearMet" : "PlateShearNotMet")));
        if (etaConcrete > 1.0)
            b.Lines.Add(string.Format(ci, Loc.S("PlateShear862"), N("QswMin"), N("QswRequired"),
                N("Rsw") / 1000.0, N("AswPerSw") * 1e4));
        else
            b.Lines.Add(Loc.S("PlateShearNoStirrups"));

        if (q.TryGetProperty("Notes", out var notes) && notes.ValueKind == JsonValueKind.Array)
            foreach (var n in notes.EnumerateArray()) b.Notes.Add(n.GetString() ?? "");

        b.Eta = design ? etaStrip : System.Math.Max(etaStrip, etaConcrete);
        b.Ok = b.Eta <= 1.0;
        // В подборе ссылка на (8.62) — только когда поперечная арматура действительно требуется.
        string status = !design ? "PlateShearStatus"
            : etaConcrete > 1.0 ? "PlateShearStatusDesign" : "PlateShearStatusDesignNoStirrups";
        b.StatusText = string.Format(ci, Loc.S(status), K(b.Eta, ci),
            Loc.S(b.Ok ? "PlateShearMet" : "PlateShearNotMet"));
        return b;
    }
}
