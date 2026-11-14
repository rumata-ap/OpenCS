using System.Text.Json;

namespace CScore.Sp63Punching;

/// <summary>
/// JSON результата продавливания. Ключи верхнего уровня <c>passed</c> и <c>ratio</c> — общие для проверок
/// (их читает агрегатор проверок МКЭ); в <c>details</c> — варианты контура в том же формате.
/// </summary>
public static class PunchingResultJson
{
    /// <summary>Сериализует результат в строку JSON.</summary>
    public static string Write(PunchingResult result)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
        {
            w.WriteStartObject();
            w.WriteBoolean("passed", result.Passed);
            Num(w, "ratio", result.Utilization);
            w.WriteString("status", result.Status switch
            {
                PunchingStatus.ConcreteSufficient => "concrete_sufficient",
                PunchingStatus.ReinforcementRequired => "reinforcement_required",
                _ => "not_ensured"
            });
            Num(w, "ratio_concrete", result.UtilizationConcreteOnly);
            Num(w, "ratio_max_sw", result.UtilizationMaxReinforcement);
            Num(w, "fred", result.Fred);
            Num(w, "base_area", result.BaseArea);
            w.WriteString("governing", result.Governing?.Variant.Id ?? "");
            w.WriteStartArray("details");
            foreach (var v in result.Variants)
            {
                w.WriteStartObject();
                w.WriteString("id", v.Variant.Id);
                w.WriteString("kind", v.Variant.Kind.ToString());
                w.WriteString("zone", v.Variant.Zone.ToString());
                w.WriteBoolean("passed", v.Passed);
                Num(w, "ratio", v.Utilization);
                w.WriteBoolean("governs", v.Governs);
                Num(w, "u", v.Geometry.U);
                Num(w, "xc", v.Geometry.Xc);
                Num(w, "yc", v.Geometry.Yc);
                Num(w, "Wbx", v.Wbx);
                Num(w, "Wby", v.Wby);
                Num(w, "Fult", v.Fult);
                Num(w, "Mxult", v.Mxult);
                Num(w, "Myult", v.Myult);
                Num(w, "MxStar", v.MxStar);
                Num(w, "MyStar", v.MyStar);
                Num(w, "kF", v.KF);
                Num(w, "kM", v.KM);
                w.WriteBoolean("sw_counted", v.SwCounted);
                w.WritePropertyName("pieces");
                WritePieces(w, v.Variant.Contour.Pieces.Select(p => p.Elements));
                w.WritePropertyName("excluded");
                WritePieces(w, v.Variant.Excluded.Select(e => (IReadOnlyList<ContourElement>)new[] { e }));
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("warnings");
            foreach (var x in result.Warnings)
            {
                w.WriteStartObject();
                w.WriteString("code", x.Code);
                w.WriteString("text", x.Text);
                if (x.Args is { Count: > 0 } args)
                {
                    w.WriteStartArray("args");
                    foreach (var a in args) w.WriteStringValue(a);
                    w.WriteEndArray();
                }
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    static void WritePieces(Utf8JsonWriter w, IEnumerable<IReadOnlyList<ContourElement>> pieces)
    {
        w.WriteStartArray();
        foreach (var piece in pieces)
        {
            w.WriteStartArray();
            foreach (var e in piece)
            {
                w.WriteStartObject();
                switch (e)
                {
                    case LineElement l:
                        w.WriteString("type", "line");
                        Num(w, "x1", l.A.X); Num(w, "y1", l.A.Y); Num(w, "x2", l.B.X); Num(w, "y2", l.B.Y);
                        break;
                    case ArcElement a:
                        w.WriteString("type", "arc");
                        Num(w, "cx", a.Center.X); Num(w, "cy", a.Center.Y); Num(w, "r", a.R);
                        Num(w, "phi1", a.Phi1); Num(w, "phi2", a.Phi2);
                        break;
                }
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }
        w.WriteEndArray();
    }

    // JSON не допускает бесконечностей: пишется null (как в verdict пакетов).
    static void Num(Utf8JsonWriter w, string name, double value)
    {
        if (double.IsFinite(value)) w.WriteNumber(name, value);
        else w.WriteNull(name);
    }
}
