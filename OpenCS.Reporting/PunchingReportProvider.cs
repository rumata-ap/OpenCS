using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CScore.Sp63Punching;

namespace OpenCS.Reporting;

/// <summary>Шаг трассировки продавливания из сохранённого результата.</summary>
public sealed record PunchingReportStep(
    string StepId, string Reference, string Formula, string Substitution, string Result, string Status,
    string FormulaCode, IReadOnlyDictionary<string, (double? Value, string Unit)> Values);

/// <summary>Вариант контура из сохранённого результата.</summary>
public sealed record PunchingReportVariant(
    string Id, PunchingVariantKind Kind, PunchingZone Zone, double? Ratio, bool Governs, bool SwCounted,
    IReadOnlyDictionary<string, double?> Numbers, IReadOnlyList<PunchingReportStep> Trace);

/// <summary>Предупреждение расчёта: код, русский текст и аргументы локализованного шаблона.</summary>
public sealed record PunchingReportWarning(string Code, string Text, IReadOnlyList<string> Args);

/// <summary>Разбор <c>DataJson</c> задачи продавливания для отчёта и окна результата.</summary>
public sealed record PunchingReportData(
    bool Passed, double? Ratio, string Status, double? RatioConcrete, double? RatioMaxSw, string Governing,
    IReadOnlyList<PunchingReportVariant> Variants, IReadOnlyList<PunchingReportWarning> Warnings,
    IReadOnlyList<PunchingReportStep> CommonTrace, PunchingScene? Scene, JsonElement? Inputs, string? Error)
{
    /// <summary>Разбирает JSON; при ошибке задачи заполняет только <see cref="Error"/>.</summary>
    public static PunchingReportData Parse(string json)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        var r = doc.RootElement;
        if (r.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String)
            return new(false, null, "error", null, null, "", [], [], [], null, null, err.GetString());

        var variants = new List<PunchingReportVariant>();
        if (r.TryGetProperty("details", out var det) && det.ValueKind == JsonValueKind.Array)
            foreach (var d in det.EnumerateArray())
            {
                var numbers = new Dictionary<string, double?>(StringComparer.Ordinal);
                foreach (var p in d.EnumerateObject())
                    if (p.Value.ValueKind is JsonValueKind.Number or JsonValueKind.Null)
                        numbers[p.Name] = p.Value.ValueKind == JsonValueKind.Number ? p.Value.GetDouble() : null;
                variants.Add(new PunchingReportVariant(
                    Str(d, "id"),
                    Enum.TryParse<PunchingVariantKind>(Str(d, "kind"), out var k) ? k : PunchingVariantKind.Custom,
                    Enum.TryParse<PunchingZone>(Str(d, "zone"), out var z) ? z : PunchingZone.Main,
                    Num(d, "ratio"), Bool(d, "governs"), Bool(d, "sw_counted"), numbers,
                    d.TryGetProperty("trace", out var t) ? Steps(t) : []));
            }
        var warnings = new List<PunchingReportWarning>();
        if (r.TryGetProperty("warnings", out var w) && w.ValueKind == JsonValueKind.Array)
            foreach (var x in w.EnumerateArray())
                warnings.Add(x.ValueKind == JsonValueKind.String
                    ? new("", x.GetString() ?? "", [])
                    : new(Str(x, "code"), Str(x, "text"),
                        x.TryGetProperty("args", out var a) && a.ValueKind == JsonValueKind.Array
                            ? a.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : e.ToString()).ToList()
                            : []));
        PunchingScene? scene = r.TryGetProperty("scene", out var s) && s.ValueKind == JsonValueKind.Object ? PunchingSceneJson.Read(s) : null;
        JsonElement? inputs = r.TryGetProperty("inputs", out var inp) && inp.ValueKind == JsonValueKind.Object ? inp.Clone() : null;
        return new(Bool(r, "passed"), Num(r, "ratio"), Str(r, "status"), Num(r, "ratio_concrete"), Num(r, "ratio_max_sw"),
            Str(r, "governing"), variants, warnings, r.TryGetProperty("traceCommon", out var tc) ? Steps(tc) : [],
            scene, inputs, null);
    }

    static List<PunchingReportStep> Steps(JsonElement a)
    {
        var list = new List<PunchingReportStep>();
        if (a.ValueKind != JsonValueKind.Array) return list;
        foreach (var s in a.EnumerateArray())
        {
            var values = new Dictionary<string, (double?, string)>(StringComparer.Ordinal);
            if (s.TryGetProperty("values", out var v) && v.ValueKind == JsonValueKind.Object)
                foreach (var p in v.EnumerateObject())
                    values[p.Name] = (Num(p.Value, "value"), Str(p.Value, "unit"));
            list.Add(new PunchingReportStep(Str(s, "stepId"), Str(s, "codeReference"), Str(s, "formulaLatex"),
                Str(s, "substitutionLatexTemplate"), Str(s, "resultLatexTemplate"), Str(s, "status"), Str(s, "formulaCode"), values));
        }
        return list;
    }

    static string Str(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    static double? Num(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
    static bool Bool(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
}

/// <summary>
/// Отчёт по расчёту плиты на продавливание. Структура — по образцу strength-web
/// (<c>strength/punching/report.py</c>): исходные данные, общие величины, расчётная схема, по каждому
/// контуру — шаги «обозначение = формула = подстановка = значение» и вердикт с запасом, вывод, примечания.
/// </summary>
public sealed class PunchingReportProvider : IReportProvider
{
    static readonly Regex Token = new(@"\{(?<name>[A-Za-z0-9_]+)\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <inheritdoc/>
    public string TaskKind => "punching";

    /// <inheritdoc/>
    public IReadOnlyCollection<string> SupportedKinds => [TaskKind];

    /// <inheritdoc/>
    public ReportDocument Build(ReportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var data = PunchingReportData.Parse(context.Result.DataJson);
        string tag = string.IsNullOrWhiteSpace(context.Task.Tag) ? $"№ {context.Task.Num}" : context.Task.Tag;
        var doc = new ReportDocument($"Расчёт плиты на продавливание — {tag}");
        bool pylon = data.Variants.Any(v => v.Kind is PunchingVariantKind.PylonBroken or PunchingVariantKind.PylonReduced);

        doc.Add(new ReportHeading(1, "Идентификация и единицы"))
            .Add(new ReportKeyValueTable(
            [
                ("Задача", $"№ {context.Task.Num} — {tag}"),
                ("Вид расчёта", ReportNames.Calc(context.Task.CalcType)),
                ("Результат получен", string.IsNullOrWhiteSpace(context.Result.Created) ? "—" : context.Result.Created),
                ("Статус результата", ReportNames.Status(context.Result.Status)),
                ("Нормативная база", "СП 63.13330.2018, пп. 8.1.46–8.1.52" + (pylon ? "; контур пилона — СП 430.1325800, п. 6.3.8" : "")),
                ("Единицы", "длины — м, силы — кН, моменты — кН·м, напряжения — кПа")
            ], "Параметр", "Значение"));

        if (data.Error is not null)
        {
            doc.Add(new ReportWarning("Расчёт не выполнен: " + data.Error));
            return doc;
        }
        AppendBody(doc, data);
        return doc;
    }

    /// <summary>
    /// Тело отчёта после идентификации: исходные данные, общие величины, схема, разделы контуров, сводка,
    /// вывод и примечания. Общее для одиночной задачи и хода определяющей строки пакета.
    /// </summary>
    public static void AppendBody(ReportDocument doc, PunchingReportData data)
    {
        int edgeCount = data.Scene?.Edges.Count ?? 2;
        AddInputs(doc, data.Inputs);
        if (data.CommonTrace.Count > 0)
        {
            doc.Add(new ReportHeading(1, "Общие величины"));
            foreach (var s in data.CommonTrace) doc.Add(StepBlock(s));
        }
        if (data.Scene is { } scene)
            doc.Add(new ReportHeading(1, "Расчётная схема"))
               .Add(new ReportParagraph("Контуры проведены на расстоянии h0/2 от грузовой площади (п. 8.1.46). "
                   + "Определяющий контур — сплошная линия, прочие — штриховая; цвет — по коэффициенту использования."))
               .Add(new ReportImage("Расчётные контуры продавливания", new PunchingPlanSvgRenderer().Render(scene)));

        foreach (var v in data.Variants)
        {
            string title = PunchingReportText.VariantTitle(v.Kind, v.Zone, v.Id, edgeCount);
            doc.Add(new ReportHeading(1, title + (v.Governs ? " — определяющий" : "")));
            if (v.Kind == PunchingVariantKind.PylonBroken)
                doc.Add(new ReportParagraph("Для вытянутой площади (Lmax > 2Lmin + h0) у каждого торца пилона проверяется "
                    + "П-образный контур по СП 430.1325800, п. 6.3.8, рис. 6.1а (методика книги «Продавливание» Г. Гоши). "
                    + "Обоснование концентрации поперечной силы у торцов — Галяутдинов З. Ш., дис. … канд. техн. наук, СамГТУ, 2022."));
            if (v.Kind == PunchingVariantKind.PylonReduced)
                doc.Add(new ReportParagraph("Справочно, не нормативно: приведённый периметр по Галяутдинову З. Ш. «Влияние "
                    + "конструктивных параметров узла сопряжения плиты и колонны на прочность железобетонных плит при "
                    + "продавливании»: дис. … канд. техн. наук. Самара: СамГТУ, 2022, формулы (5.9)–(5.35). Моменты не учитываются."));
            if (v.Zone == PunchingZone.BeyondReinforcement)
                doc.Add(new ReportParagraph("Контур за зоной поперечной арматуры проверяется только по бетону (п. 8.1.48, рис. 8.13)."));
            if (data.Scene is { } sc && sc.Variants.Any(x => x.Id == v.Id && x.Pieces.Count > 0))
                doc.Add(new ReportImage(title, new PunchingPlanSvgRenderer().Render(sc, v.Id, title)));
            foreach (var s in v.Trace) doc.Add(StepBlock(s));
            doc.Add(new ReportParagraph(Verdict(v.Ratio)));
        }

        AddSummary(doc, data, edgeCount);
        foreach (var w in data.Warnings.Where(w => !string.IsNullOrWhiteSpace(w.Text)))
        {
            string readable = w.Text;
            foreach (var v in data.Variants)
                readable = readable.Replace($"«{v.Id}»", $"«{PunchingReportText.VariantTitle(v.Kind, v.Zone, v.Id, edgeCount)}»", StringComparison.Ordinal);
            doc.Add(new ReportWarning("Примечание: " + readable));
        }
    }

    internal static string Verdict(double? ratio)
    {
        if (ratio is not double k || !double.IsFinite(k))
            return "Несущая способность контура не определена — прочность не обеспечена.";
        string ks = ReportNumberFormatter.FormatUtilization(k).Replace('.', ',');
        string reserve = ((1 - k) * 100).ToString("0.0", CultureInfo.GetCultureInfo("ru-RU"));
        return k <= 1
            ? $"K = {ks} ≤ 1 — прочность обеспечена; запас Δ = (1 − K)·100 % = {reserve} %."
            : $"K = {ks} > 1 — прочность НЕ обеспечена; перегрузка (K − 1)·100 % = {((k - 1) * 100).ToString("0.0", CultureInfo.GetCultureInfo("ru-RU"))} %.";
    }

    internal static void AddInputs(ReportDocument doc, JsonElement? inputs)
    {
        doc.Add(new ReportHeading(1, "Исходные данные"));
        if (inputs is not { } i)
        {
            doc.Add(new ReportWarning("Исходные данные в результате не сохранены."));
            return;
        }
        var rows = new List<IReadOnlyList<string>>();
        void Row(string sym, double? v, string unit, string name) => rows.Add([sym, v is double x ? Sig(x) : "—", unit, name]);
        void Text(string sym, string v, string name) => rows.Add([sym, v, "", name]);

        string shape = S(i, "shape");
        switch (shape)
        {
            case "circle": Row("D", N(i, "d"), "м", "диаметр круглой грузовой площади"); break;
            case "ring": Row("D", N(i, "d"), "м", "наружный диаметр кольца"); Row("t", N(i, "t"), "м", "толщина стенки кольца"); break;
            case "polygon": Text("—", "многоугольник", "грузовая площадь"); break;
            default:
                Row("b_x", N(i, "bx"), "м", "размер грузовой площади вдоль X");
                Row("b_y", N(i, "by"), "м", "размер грузовой площади вдоль Y");
                break;
        }
        Row("h", N(i, "h"), "м", "толщина плиты");
        Row("a_x", N(i, "ax"), "м", "привязка арматуры вдоль X к растянутой грани");
        Row("a_y", N(i, "ay"), "м", "привязка арматуры вдоль Y к растянутой грани");
        Text("—", S(i, "concrete") is { Length: > 0 } ct ? ct : "Rbt задано вручную", "бетон плиты");
        Row("R_bt", N(i, "rbt"), "кПа", "расчётное сопротивление бетона растяжению");
        Text("—", EdgesText(i), "положение относительно краёв плиты");
        if (N(i, "holes") is double holes && holes > 0) Text("—", holes.ToString("0", CultureInfo.InvariantCulture), "отверстий рядом с площадью");
        if (i.TryGetProperty("sw", out var sw) && sw.ValueKind == JsonValueKind.True)
        {
            Text("—", S(i, "rebar") is { Length: > 0 } rt ? rt : "Rsw задано вручную", "поперечная арматура");
            Row("d_sw", N(i, "swDiameter"), "м", "диаметр поперечных стержней");
            Row("n", N(i, "swCount"), "шт.", "стержней в ряду на длине шага");
            Row("s_w", N(i, "swStep"), "м", "шаг вдоль контура");
            Row("R_sw", N(i, "rsw"), "кПа", "расчётное сопротивление поперечной арматуры");
            if (N(i, "swZone") is double zone) Row("—", zone, "м", "от грани площади до внешнего ряда арматуры");
        }
        else Text("—", "нет", "поперечная арматура");
        bool mloc = i.TryGetProperty("momentsAreMloc", out var ml) && ml.ValueKind == JsonValueKind.True;
        if (i.TryGetProperty("F", out _))
        {
            Row("F", N(i, "F"), "кН", "продавливающая сила");
            Row(mloc ? "M_loc,x" : "M_x", N(i, "Mx"), "кН·м", mloc ? "полный момент узла (в расчёт — половина)" : "момент относительно оси X");
            Row(mloc ? "M_loc,y" : "M_y", N(i, "My"), "кН·м", mloc ? "полный момент узла (в расчёт — половина)" : "момент относительно оси Y");
        }
        else
            Text("F, M_x, M_y", "строки набора", "усилия: F = |N|, Mx и My строки" + (mloc ? " (моменты — полные Mloc узла, в расчёт — половина)" : ""));
        Text("—", S(i, "loadSide") == "from_above" ? "сверху (на плиту)" : "снизу (колонна под плитой)", "сторона приложения нагрузки");
        if (N(i, "counterPressure") is double q && q != 0) Row("q", q, "кПа", "нагрузка в основании пирамиды против F");
        Text("—", $"{Mode(S(i, "eccentricityX"))} / {Mode(S(i, "eccentricityY"))}", "учёт эксцентриситета F для Mx / My");
        Text("—", S(i, "wb") == "stress_side" ? "со стороны наибольших напряжений" : "наименьший (п. 8.1.51)", "момент сопротивления контура");
        doc.Add(new ReportTable(["Обозначение", "Значение", "Ед. изм.", "Наименование"], rows));
        if (i.TryGetProperty("source", out var src) && src.ValueKind == JsonValueKind.Object)
            AddSource(doc, src);
    }

    /// <summary>Источник усилий задачи, созданной по строке проверки продавливания по МКЭ.</summary>
    internal static void AddSource(ReportDocument doc, JsonElement src)
    {
        bool contour = S(src, "method") == "contour";
        var rows = new List<(string, string)>
        {
            ("Схема МКЭ", S(src, "schema")),
            ("Узел сетки", S(src, "node")),
            ("Загружение или сочетание", S(src, "key")),
            ("Способ определения F", contour
                ? "по разрезу плиты по границе основания пирамиды продавливания (узловые силы КЭ)"
                : "по стержням колонн за вычетом нагрузки в основании пирамиды продавливания"),
        };
        if (N(src, "columnAction") is double a) rows.Add(("Действие колонн на плиту по нормали, кН", Sig(a)));
        if (N(src, "baseLoad") is double b) rows.Add(("Нагрузка в основании пирамиды по нормали, кН", Sig(b)));
        if (N(src, "contourForce") is double c) rows.Add(("Сила по разрезу основания пирамиды, кН", Sig(c)));
        doc.Add(new ReportHeading(2, "Источник усилий"))
           .Add(new ReportKeyValueTable(rows, "Параметр", "Значение"))
           .Add(new ReportParagraph("Сила F принимается за вычетом сил, действующих в пределах основания пирамиды продавливания "
               + "в противоположном направлении (п. 8.1.46). Положительное направление по нормали — вверх; колонна снизу передаёт "
               + "на плиту силу вверх. Моменты — неуравновешенный момент колонн на плиту относительно центра тяжести грузовой "
               + "площади в осях узла (x — местная ось Y стержня, спроецированная на плиту)."));
    }

    static string EdgesText(JsonElement i)
    {
        if (!i.TryGetProperty("edges", out var e) || e.ValueKind != JsonValueKind.Array || e.GetArrayLength() == 0)
            return "внутри плиты";
        var parts = e.EnumerateArray().Select(x =>
        {
            string side = S(x, "side");
            double c = N(x, "c") ?? 0, a = N(x, "angleDeg") ?? 0;
            return $"край со стороны {side}, c = {Sig(c)} м" + (a != 0 ? $", скос {Sig(a)}°" : "");
        });
        return (e.GetArrayLength() == 2 ? "угол плиты: " : "у края плиты: ") + string.Join("; ", parts);
    }

    static string Mode(string m) => m switch
    {
        "auto" => "по знакам",
        "unloading" => "разгружающий",
        "ignore" => "не учитывается",
        _ => "догружающий"
    };

    static void AddSummary(ReportDocument doc, PunchingReportData data, int edgeCount)
    {
        doc.Add(new ReportHeading(1, "Сводка вариантов"))
            .Add(new ReportTable(
                ["Контур", "u, м", "Fult, кН", "Mx,ult, кН·м", "My,ult, кН·м", "M*x, кН·м", "M*y, кН·м", "kF", "kM", "K", "Арматура"],
                data.Variants.Select(v => (IReadOnlyList<string>)
                [
                    PunchingReportText.VariantTitle(v.Kind, v.Zone, v.Id, edgeCount) + (v.Governs ? " *" : ""),
                    Sig(v.Numbers.GetValueOrDefault("u")), Sig(v.Numbers.GetValueOrDefault("Fult")),
                    Sig(v.Numbers.GetValueOrDefault("Mxult")), Sig(v.Numbers.GetValueOrDefault("Myult")),
                    Sig(v.Numbers.GetValueOrDefault("MxStar")), Sig(v.Numbers.GetValueOrDefault("MyStar")),
                    Sig(v.Numbers.GetValueOrDefault("kF")), Sig(v.Numbers.GetValueOrDefault("kM")),
                    v.Ratio is double k ? Ku(k) : "∞",
                    v.SwCounted ? "учтена" : "—"
                ]).ToList()))
            .Add(new ReportParagraph("* — определяющий контур (наибольший K, п. 8.1.46). "
                + "K округлён в бо́льшую сторону, поэтому может на единицу последнего знака превышать сумму округлённых kF и kM."));

        doc.Add(new ReportHeading(1, "Вывод"));
        if (data.Ratio is double kmax)
        {
            string ks = ReportNumberFormatter.FormatUtilization(kmax).Replace('.', ',');
            string reserve = ((1 - kmax) * 100).ToString("0.0", CultureInfo.GetCultureInfo("ru-RU"));
            doc.Add(new ReportParagraph(data.Passed
                ? $"Максимальный коэффициент использования Kmax = {ks}, запас несущей способности {reserve} % — прочность плиты на продавливание обеспечена."
                : $"Максимальный коэффициент использования Kmax = {ks} > 1 — прочность плиты на продавливание НЕ обеспечена."));
        }
        else doc.Add(new ReportParagraph("Несущая способность не определена — прочность плиты на продавливание не обеспечена."));
        doc.Add(new ReportKeyValueTable(
        [
            ("K без поперечной арматуры", data.RatioConcrete is double kb ? Ku(kb) : "—"),
            ("K с предельной арматурой (Fult = 2Fb, Mult = 2Mb)", data.RatioMaxSw is double km ? Ku(km) : "—"),
            ("Оценка узла", PunchingReportText.Status(data.Status))
        ], "Показатель", "Значение"));
    }

    /// <summary>Шаг «обозначение = формула = подстановка = значение» с единицей результата.</summary>
    public static ReportCalculationStep StepBlock(PunchingReportStep s)
    {
        var parts = new List<string> { s.Formula };
        string subst = Substitute(s.Substitution, s.Values, parenthesizeNegative: true);
        if (subst.Length > 0) parts.Add(subst);
        // Итоговый K условия прочности — тем же консервативным округлением, что в сводке, вердикте и окне.
        bool utilization = s.StepId.EndsWith(".check", StringComparison.Ordinal);
        string result = Substitute(s.Result, s.Values, parenthesizeNegative: false, utilizationKey: utilization ? "K" : null);
        if (result.Length > 0 && result != subst) parts.Add(result);
        string unit = "";
        var m = Token.Match(s.Result);
        if (m.Success && s.Values.TryGetValue(m.Groups["name"].Value, out var rv)) unit = UnitText(rv.Unit);
        return new ReportCalculationStep
        {
            StepId = s.StepId,
            Reference = s.Reference,
            Title = Title(s.StepId),
            Formula = ReportMathExpression.Latex(string.Join(" = ", parts) + (unit.Length > 0 ? $@"\ \text{{{unit}}}" : "")),
            Status = s.Status switch
            {
                "passed" => ReportCalculationStatus.Passed,
                "failed" => ReportCalculationStatus.Failed,
                "notapplicable" => ReportCalculationStatus.NotApplicable,
                _ => ReportCalculationStatus.Informational
            },
            StatusText = s.Status switch
            {
                "passed" when s.StepId.EndsWith(".fsw", StringComparison.Ordinal) => "Fsw,ult ≥ 0,25·Fb,ult — арматура учитывается (п. 8.1.48).",
                "notapplicable" when s.StepId.EndsWith(".fsw", StringComparison.Ordinal) => "Fsw,ult < 0,25·Fb,ult — поперечная арматура в расчёте не учитывается (п. 8.1.48).",
                "passed" => "Условие выполнено.",
                "failed" => "Условие не выполнено.",
                _ => null
            }
        };
    }

    static string Substitute(string template, IReadOnlyDictionary<string, (double? Value, string Unit)> values, bool parenthesizeNegative,
        string? utilizationKey = null)
        => Token.Replace(template ?? "", match =>
        {
            string name = match.Groups["name"].Value;
            if (!values.TryGetValue(name, out var v) || v.Value is not double x) return "—";
            string text = (name == utilizationKey ? Ku(x) : Sig(x)).Replace(",", "{,}");
            return x < 0 && parenthesizeNegative ? $"({text})" : text;
        });

    static string Title(string stepId)
    {
        string q = stepId[(stepId.LastIndexOf('.') + 1)..];
        return q switch
        {
            "h0x" => "Рабочая высота вдоль X",
            "h0y" => "Рабочая высота вдоль Y",
            "h0" => "Приведённая рабочая высота",
            "rbt" => "Расчётное сопротивление бетона растяжению",
            "asw" => "Площадь поперечной арматуры в ряду на шаге",
            "rsw" => "Расчётное сопротивление поперечной арматуры",
            "mloc_x" or "mloc_y" => "Момент, передаваемый на продавливание",
            "fred" => "Продавливающая сила за вычетом нагрузки в основании пирамиды",
            "u" => "Периметр расчётного контура",
            "xc" => "Центр тяжести контура по X",
            "yc" => "Центр тяжести контура по Y",
            "ibx" => "Момент инерции контура относительно оси X",
            "iby" => "Момент инерции контура относительно оси Y",
            "wbx" => "Момент сопротивления контура относительно оси X",
            "wby" => "Момент сопротивления контура относительно оси Y",
            "ex" => "Эксцентриситет силы по X",
            "ey" => "Эксцентриситет силы по Y",
            "mx" => "Момент относительно оси X с учётом эксцентриситета",
            "my" => "Момент относительно оси Y с учётом эксцентриситета",
            "fb" => "Предельное усилие, воспринимаемое бетоном",
            "mbx" => "Предельный момент, воспринимаемый бетоном, относительно оси X",
            "mby" => "Предельный момент, воспринимаемый бетоном, относительно оси Y",
            "qsw" => "Погонное усилие в поперечной арматуре",
            "fsw" => "Предельное усилие, воспринимаемое поперечной арматурой",
            "mswx" => "Предельный момент поперечной арматуры относительно оси X",
            "mswy" => "Предельный момент поперечной арматуры относительно оси Y",
            "fult" => "Предельная продавливающая сила",
            "mxult" => "Предельный момент относительно оси X",
            "myult" => "Предельный момент относительно оси Y",
            "kf" => "Доля продавливающей силы",
            "km" => "Доля моментов",
            "ured" => "Приведённый периметр (справочно)",
            "check" => "Условие прочности",
            _ => "Расчётная величина"
        };
    }

    static string UnitText(string unit) => unit switch
    {
        "Meter" => "м",
        "Kilonewton" => "кН",
        "KilonewtonMeter" => "кН·м",
        "Kilopascal" => "кПа",
        "SquareMeter" => "м²",
        "CubicMeter" => "м³",
        "KilonewtonPerMeter" => "кН/м",
        _ => ""
    };

    /// <summary>Четыре значащие цифры, десятичная запятая; большие числа — с одним знаком после запятой.</summary>
    public static string Sig(double? value)
    {
        if (value is not double v || !double.IsFinite(v)) return "—";
        if (v == 0) return "0";
        int mag = (int)Math.Floor(Math.Log10(Math.Abs(v)));
        int digits = Math.Clamp(3 - mag, 0, 10);
        if (mag >= 3) digits = Math.Min(digits, 1);
        double r = Math.Round(v, digits, MidpointRounding.AwayFromZero);
        string s = r.ToString("F" + digits, CultureInfo.InvariantCulture);
        if (s.Contains('.')) s = s.TrimEnd('0').TrimEnd('.');
        return s.Replace('.', ',');
    }

    internal static string Ku(double k) => ReportNumberFormatter.FormatUtilization(k).Replace('.', ',');

    static double? N(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
    static string S(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
