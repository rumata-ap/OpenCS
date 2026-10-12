using System.Globalization;

namespace CScore.Import;

/// <summary>
/// Текстовый файл схемы ЛИРЫ (+ <c>.ald</c>, если есть) → <see cref="LiraSchemaData"/> — те же данные, что читает импорт из
/// API, для ЛИРЫ без COM API (ранние версии) или без запущенной ЛИРЫ. Узлы — документ 4, КЭ — документ 1 (упругие связи
/// 5x/25x в сетку не идут, их переносит <see cref="LiraBoundaryTransfer"/>), оси пластин — документ 9, жёсткости —
/// документ 3 и описания сечений <c>{13/}</c> строкой параметров API (<see cref="LiraStiffnessParams"/>):
/// <list type="bullet">
/// <item><c>GEI E ν H [RO ρ]</c> (H — в единицах длины) → пластина <c>E: V: H: Ro: … PLATE_END</c>, H — в единицах
/// размеров сечения, как у API;</item>
/// <item><c>S0 E B H</c> (брус, B и H в единицах размеров сечения) → <c>Ro: E: B: H: BAR_END</c> + <c>Mu:</c> и численные
/// <c>EF EIy EIz GIk</c>, если есть; RO бруса в файле — погонный вес, Ro API — удельный (делится на B·H);</item>
/// <item>описание <c>{13/}</c> со ссылкой на сортамент → строка стальной жёсткости, вид
/// <see cref="LiraSteelProfiles.SteelKindCode"/>;</item>
/// <item>численная строка <c>EF EIy EIz GIk …</c> без сечения → <c>Ro: EF: EIy: EIz: GIk: DD10_END</c> (RO — погонный);</item>
/// <item><c>GEO E1 E2 ν12 ν21 G H</c> при E1 = E2, ν12 = ν21 — как <c>GEI</c>; ортотропная — без свойств, в отчёт;</item>
/// <item>прочие сечения (<c>S1</c>, <c>S2</c>, физнелинейные <c>S100</c> …) без численных жёсткостей — без свойств, в отчёт.</item>
/// </list>
/// Ro и имена жёсткостей — из <c>.ald</c> (там Ro = Ro API), иначе по файлу; блоки КЭ — только из <c>.ald</c>; C1 пластин —
/// документ 19 (<see cref="LiraBoundaryTransfer.FoundationC1"/>).
/// </summary>
public static class LiraTextSchemaConverter
{
    /// <summary>Упругие связи и прочие специальные КЭ: в сетку не идут.</summary>
    public static bool IsLinkType(int type) => type is >= 51 and <= 59 or >= 251 and <= 259;

    /// <param name="text">Разобранный текстовый файл.</param>
    /// <param name="ald">Файл проекта; null — нет (без имён жёсткостей и блоков).</param>
    /// <param name="tonToKn">Сколько кН в тонне-силе (настройка импорта).</param>
    /// <param name="report">Замечания конвертации (в журнал).</param>
    public static LiraSchemaData ToSchemaData(LiraTextSchema text, LiraAldFile? ald, double tonToKn, List<string> report)
    {
        var u = text.Units;
        var data = new LiraSchemaData { Units = new LiraUnits(u.ForceUnitN(tonToKn), u.LengthUnitM) };
        for (int i = 0; i < text.Nodes.Count; i++)
        {
            var p = text.Nodes[i];
            data.Nodes.Add(new LiraNodeRecord(i + 1, p.X * u.LengthUnitM, p.Y * u.LengthUnitM, p.Z * u.LengthUnitM, 0));
        }

        int links = 0, other = 0;
        foreach (var e in text.Elements)
        {
            if (IsLinkType(e.Type)) { links++; continue; }
            if (e.Nodes.Length is not (2 or 3 or 4)) { other++; continue; }
            data.Elements.Add(new LiraElementRecord(e.Number, e.Type, 0, e.Stiffness, e.Nodes));
        }
        if (other > 0) report.Add($"КЭ с числом узлов не 2–4 (объёмные и др.) не импортированы: {other}.");

        // Только жёсткости КЭ сетки: у упругих связей (КЭ 57 «Мирной» — своя на каждый КЭ) их нет и в таблице API.
        var used = data.Elements.Select(e => e.StiffnessId).ToHashSet();
        var unsupported = new List<string>();
        foreach (var s in text.Stiffnesses.Where(s => used.Contains(s.Number)))
        {
            var rigid = ald?.Rigids.GetValueOrDefault(s.Number);
            var rec = Stiffness(s, u, text.SectionDescriptions.GetValueOrDefault(s.Number), rigid, out string? problem);
            data.Stiffnesses.Add(rec);
            if (problem != null) unsupported.Add($"{s.Number} ({problem})");
        }
        if (unsupported.Count > 0)
            report.Add($"Жёсткости без свойств для расчёта (сечение не разобрано): {string.Join(", ", unsupported.Take(20))}" +
                (unsupported.Count > 20 ? $" … всего {unsupported.Count}." : "."));
        data.ApplyStiffnesses(new Dictionary<int, int>());

        foreach (var (elem, angle) in text.PlateAxisAngles) data.PlateAxisAngles[elem] = angle;

        if (ald != null)
        {
            var imported = data.Elements.Select(e => e.Id).ToHashSet();
            foreach (var g in ald.ElementBlocks.Where(kv => imported.Contains(kv.Key)).GroupBy(kv => kv.Value).OrderBy(g => g.Key))
                data.ConstructiveBlocks.Add(new LiraConstructiveBlockRecord(g.Key, "", "", "", "",
                    g.Select(kv => kv.Key).Order().ToArray()));
        }

        var shells = data.Elements.Where(e => e.NodeIds.Length is 3 or 4).Select(e => e.Id).ToHashSet();
        var (c1, beyond, _) = LiraBoundaryTransfer.FoundationC1(text, shells, tonToKn);
        foreach (var (elem, value) in c1) data.PlateFoundationC1[elem] = value;
        data.PlateFoundationBeyondC1 = beyond;
        return data;
    }

    /// <summary>Строка жёсткости документа 3 → запись в формате API; <paramref name="problem"/> — почему без свойств.</summary>
    public static LiraStiffnessRecord Stiffness(LiraTextStiffness s, LiraTextUnits u, string? section, LiraAldRigid? rigid,
        out string? problem)
    {
        problem = null;
        static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);
        var tokens = s.Lines.Select(LiraTextTokens.Split).ToList();
        double[]? numeric = s.NumericValues;
        string? Key(string key) => tokens.SelectMany(t => t.Select((x, i) => (x, i, t)))
            .Where(z => z.x.Equals(key, StringComparison.OrdinalIgnoreCase) && z.i + 1 < z.t.Length)
            .Select(z => z.t[z.i + 1]).FirstOrDefault();
        double? Num(string key) => Key(key) is { } v && LiraTextTokens.TryDouble(v, out double d) ? d : null;
        string[]? After(string key) => tokens.Where(t => t.Length > 0 && t[0].Equals(key, StringComparison.OrdinalIgnoreCase))
            .Select(t => t[1..]).FirstOrDefault();
        string Name(string fallback) => rigid?.Comment is { Length: > 0 } c ? c : fallback;
        double? roFile = Num("RO");
        string numericParams = numeric is { Length: >= 3 }
            ? $"EF:{F(numeric[0])} EIy:{F(numeric[1])} EIz:{F(numeric[2])} GIk:{F(numeric.Length > 3 ? numeric[3] : 0)}" : "";

        // Пластина: GEI E ν H, H — в единицах длины, у API — в единицах размеров сечения.
        if (After("GEI") is { Length: >= 3 } gei && Nums(gei, 3) is { } g)
        {
            double h = g[2] / u.LengthPerMeter * u.SectionPerMeter;
            double ro = rigid?.Ro ?? roFile ?? 0;
            var extra = Pairs(tokens, ["GEI", "RO"]);
            return new LiraStiffnessRecord(s.Number, 36, Name($"Пластина H {F(Math.Round(h, 6))}"),
                $"Ro:{F(ro)} E:{F(g[0])} V:{F(g[1])} H:{F(h)} {extra}PLATE_END".Replace("  ", " "), u.SectionUnitM);
        }

        // Ортотропная пластина: GEO E1 E2 ν12 ν21 G H — при E1 = E2 и ν12 = ν21 это изотропная (dorfman), иначе без свойств.
        if (After("GEO") is { Length: >= 6 } geo && Nums(geo, 6) is { } o)
        {
            if (o[0] > 0 && Math.Abs(o[0] - o[1]) <= 1e-9 * o[0] && o[2] == o[3])
            {
                double h = o[5] / u.LengthPerMeter * u.SectionPerMeter;
                double ro = rigid?.Ro ?? roFile ?? 0;
                return new LiraStiffnessRecord(s.Number, 36, Name($"Пластина H {F(Math.Round(h, 6))}"),
                    $"Ro:{F(ro)} E:{F(o[0])} V:{F(o[2])} H:{F(h)} {Pairs(tokens, ["GEO", "RO"])}PLATE_END".Replace("  ", " "),
                    u.SectionUnitM);
            }
            problem = "ортотропная пластина GEO";
            return new LiraStiffnessRecord(s.Number, -2, Name($"GEO {s.Number}"), string.Join(" ", s.Lines), u.SectionUnitM);
        }

        // Брус: S0 E B H; RO файла — погонный вес.
        if (After("S0") is { Length: >= 3 } s0 && Nums(s0, 3) is { } b)
        {
            double area = b[1] * b[2] * Math.Pow(u.SectionUnitM / u.LengthUnitM, 2);
            double? ro = rigid?.Ro ?? (roFile is { } q && area > 0 ? q / area : null);
            string mu = Num("Mu") is { } m ? $" Mu:{F(m)}" : "";
            return new LiraStiffnessRecord(s.Number, LiraStiffnessParams.BarRectKind, Name($"Брус {F(b[1])}×{F(b[2])}"),
                $"Ro:{F(ro ?? 0)} E:{F(b[0])} B:{F(b[1])} H:{F(b[2])} BAR_END{mu}" +
                (numericParams.Length > 0 ? $" {numericParams} STD_END" : ""), u.SectionUnitM);
        }

        // Сталь из сортамента: описание {13/}.
        if (section != null && LiraSteelProfiles.SteelRef(section) != null)
            return new LiraStiffnessRecord(s.Number, LiraSteelProfiles.SteelKindCode,
                Name(LiraSteelProfiles.SteelRef(section)!.Shape), section, u.SectionUnitM);

        // Численная жёсткость стержня.
        if (numericParams.Length > 0)
        {
            double ro = rigid?.Ro ?? roFile ?? 0;
            return new LiraStiffnessRecord(s.Number, 14, Name($"Численная {s.Number}"),
                $"Ro:{F(ro)} {numericParams} DD10_END", u.SectionUnitM);
        }

        string kind = tokens.FirstOrDefault()?.FirstOrDefault() ?? "?";
        problem = section != null ? $"сечение {kind}, нет ссылки на сортамент" : $"сечение {kind}";
        return new LiraStiffnessRecord(s.Number, -2, Name($"{kind} {s.Number}"), string.Join(" ", s.Lines), u.SectionUnitM);
    }

    static double[]? Nums(string[] tokens, int count)
    {
        var v = new double[count];
        for (int i = 0; i < count; i++)
            if (!LiraTextTokens.TryDouble(tokens[i], out v[i])) return null;
        return v;
    }

    /// <summary>Пары «КЛЮЧ число» строк продолжения (<c>WLKE 1 WLKG 1 PLKE 0.6</c>) → «КЛЮЧ:число »; ключи
    /// <paramref name="skip"/> — нет.</summary>
    static string Pairs(List<string[]> tokens, string[] skip)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var t in tokens)
            for (int i = 0; i + 1 < t.Length; i++)
                if (char.IsLetter(t[i][0]) && !skip.Contains(t[i], StringComparer.OrdinalIgnoreCase)
                    && LiraTextTokens.TryDouble(t[i + 1], out double v))
                {
                    sb.Append(t[i]).Append(':').Append(v.ToString("R", CultureInfo.InvariantCulture)).Append(' ');
                    i++;
                }
        return sb.ToString();
    }
}
