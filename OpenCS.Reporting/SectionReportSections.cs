using System.Globalization;
using CScore;

namespace OpenCS.Reporting;

/// <summary>Общие разделы расчётного отчёта по железобетонному сечению.</summary>
public static class SectionReportSections
{
    /// <summary>Добавляет идентификацию результата и пояснение единиц измерения.</summary>
    public static ReportDocument Identification(ReportDocument document, ReportContext context,
        string unitsNote)
    {
        string sectionId = context.Section?.Id.ToString(CultureInfo.InvariantCulture)
            ?? "не загружено";
        string sectionTag = context.Section?.Tag ?? "модель сечения не передана";
        return document
            .Add(new ReportHeading(1, "Идентификация и единицы"))
            .Add(new ReportKeyValueTable(
            [
                ("Результат", $"{context.Result.Id}#{context.Result.TaskKind}; создан: {ValueOrDash(context.Result.Created)}"),
                ("Задача", $"{context.Task.Num}#{context.Task.Id}; {ValueOrDash(context.Task.Tag)}"),
                ("Сечение", $"{context.Task.SectionId}#{sectionId}; {sectionTag}"),
                ("Вид расчёта", context.Task.CalcType.ToString()),
                ("Статус результата", context.Result.Status),
                ("Единицы", unitsNote)
            ], "Параметр", "Значение"))
            .Add(new ReportParagraph($"Все числовые результаты ниже приведены с единицами: {unitsNote}."));
    }

    /// <summary>Добавляет геометрию сечения, SVG и таблицы его частей.</summary>
    public static ReportDocument Geometry(ReportDocument document, CrossSection section, Kurvature k)
    {
        var areas = section.EnumerateAreas(k).Select(pair => pair.area).ToList();
        var geometryRows = areas.Select((area, index) =>
        {
            var props = AreaProps(area);
            return (IReadOnlyList<string>)[
                (index + 1).ToString(CultureInfo.InvariantCulture),
                area.Tag,
                area.Category.ToString(),
                area.Material?.Tag ?? "не задан",
                area.HostArea?.Tag ?? (area.HostAreaId?.ToString(CultureInfo.InvariantCulture) ?? "—"),
                F(props.A),
                F(props.Centroid?.X ?? 0),
                F(props.Centroid?.Y ?? 0),
                area.Contours.Count.ToString(CultureInfo.InvariantCulture),
                area.Fibers.Count.ToString(CultureInfo.InvariantCulture)
            ];
        }).ToList();

        return document
            .Add(new ReportHeading(1, "Геометрия сечения"))
            .Add(new ReportParagraph("Контуры и координаты приведены в метрах; диаметры и координаты точечной арматуры в таблице — в миллиметрах."))
            .Add(new ReportImage("Геометрия сечения и его частей",
                new CrossSectionReportSvgRenderer().Render(section)))
            .Add(new ReportTable(
                ["№", "Часть", "Категория", "Материал", "Бетон-носитель"],
                geometryRows.Select(row => (IReadOnlyList<string>)[row[0], row[1], row[2], row[3], row[4]]).ToList()))
            .Add(new ReportTable(
                ["№", "A, м²", "xc, м", "yc, м", "Контуры", "Фибры"],
                geometryRows.Select(row => (IReadOnlyList<string>)[row[0], row[5], row[6], row[7], row[8], row[9]]).ToList()));
    }

    /// <summary>Добавляет материалы, характеристики и фактические диаграммы.</summary>
    public static ReportDocument MaterialsAndDiagrams(ReportDocument document, CrossSection section,
        CalcType calc, Kurvature? k = null)
    {
        var areas = k is null
            ? section.Areas.ToList()
            : section.EnumerateAreas(k.Value).Select(pair => pair.area).ToList();

        var materialRows = new List<IReadOnlyList<string>>();
        var diagrams = new List<(string Title, Diagramm Diagram)>();
        var seenDiagrams = new HashSet<Diagramm>();
        foreach (var area in areas)
        {
            area.Diagramms.TryGetValue(calc, out var diagram);
            materialRows.Add(
            [
                area.Tag,
                area.Material?.Tag ?? "не задан",
                area.Material?.Type.ToString() ?? "—",
                area.DiagrammType.ToString(),
                diagram?.Tag ?? "диаграмма не разрешена",
                diagram == null ? "—" : calc.ToString()
            ]);
            if (diagram != null && seenDiagrams.Add(diagram))
                diagrams.Add(($"{area.Material?.Tag ?? area.Tag} — {diagram.Tag} ({calc})", diagram));
        }

        document
            .Add(new ReportHeading(1, "Материалы и диаграммы"))
            .Add(new ReportParagraph("Напряжения на графиках приведены в МПа, деформации ε — безразмерные. Для арматуры, расположенной в бетоне, отображается фактическая разностная диаграмма σст − σб."))
            .Add(new ReportTable(
                ["Часть", "Материал", "Тип", "Вид расчёта"],
                materialRows.Select(row => (IReadOnlyList<string>)[row[0], row[1], row[2], row[5]]).ToList()))
            .Add(new ReportTable(
                ["Часть", "Диаграмма", "Фактическая диаграмма"],
                materialRows.Select(row => (IReadOnlyList<string>)[row[0], row[3], row[4]]).ToList()));

        var materials = areas.Select(area => area.Material)
            .Where(material => material != null)
            .Distinct()
            .Cast<Material>()
            .ToList();
        if (materials.Count > 0)
        {
            var characteristicRows = materials.Select(material =>
            {
                var chars = material.GetChars(calc);
                return (IReadOnlyList<string>)[
                    material.Tag,
                    material.Type.ToString(),
                    F((chars?.E ?? material.E) / 1000.0),
                    F((chars?.Fc ?? 0) / 1000.0),
                    F((chars?.Ft ?? 0) / 1000.0),
                    F((chars?.Ry ?? 0) / 1000.0),
                    F((chars?.Ru ?? 0) / 1000.0)
                ];
            }).ToList();
            document
                .Add(new ReportTable(
                    ["Материал", "Тип", "E, МПа", "Fc, МПа", "Ft, МПа"],
                    characteristicRows.Select(row => (IReadOnlyList<string>)[row[0], row[1], row[2], row[3], row[4]]).ToList()))
                .Add(new ReportTable(
                    ["Материал", "Тип", "Ry, МПа", "Ru, МПа"],
                    characteristicRows.Select(row => (IReadOnlyList<string>)[row[0], row[1], row[5], row[6]]).ToList()));
        }

        foreach (var (diagramTitle, diagram) in diagrams)
            document.Add(new ReportImage($"Диаграмма σ(ε): {diagramTitle}",
                new MaterialDiagramSvgRenderer().Render(diagram, diagramTitle)));
        return document;
    }

    /// <summary>Добавляет две таблицы фактических состояний точечных стержней.</summary>
    public static ReportDocument Rebar(ReportDocument document, CrossSection section, Kurvature k,
        CalcType calc)
    {
        var rows = RebarRows(section, k);
        if (rows.Count == 0) return document;

        return document
            .Add(new ReportHeading(1, "Арматура"))
            .Add(new ReportParagraph("Координаты и размеры стержней — мм; площадь — мм²; ε — безразмерная; σ и Eсек — МПа."))
            .Add(new ReportTable(
                ["№", "Группа", "Материал", "x, мм", "y, мм", "d, мм"],
                rows.Select(row => (IReadOnlyList<string>)[
                    row.Num.ToString(CultureInfo.InvariantCulture), row.Group, row.Material,
                    F(row.Xmm), F(row.Ymm), F(row.DiameterMm)]).ToList()))
            .Add(new ReportTable(
                ["№", "Группа", "A, мм²", "ε", "σ, МПа", "Eсек, МПа"],
                rows.Select(row => (IReadOnlyList<string>)[
                    row.Num.ToString(CultureInfo.InvariantCulture), row.Group, F(row.AreaMm2),
                    F(row.Eps), F(row.SigmaMpa), F(row.SecantModulusMpa)]).ToList()));
    }

    /// <summary>Добавляет расчётные секущие и упругие жёсткости.</summary>
    public static ReportDocument Stiffness(ReportDocument document, CrossSection? section,
        Kurvature k, CalcType calc)
    {
        if (section == null) return document;

        SectionStiffnessResult? result;
        try
        {
            result = SectionStiffnessCalculator.Compute(section, k, calc);
        }
        catch (Exception ex)
        {
            return document.Add(new ReportWarning($"Расширенные характеристики жёсткости не рассчитаны: {ex.Message}"));
        }

        if (result is not SectionStiffnessResult value) return document;
        return document
            .Add(new ReportHeading(1, "Секущая и упругая жёсткости"))
            .Add(new ReportTable(
                ["Характеристика", "Секущая", "Упругая", "Единица"],
                [
                    (IReadOnlyList<string>)["Xc", F(value.Xc_mm), "—", "мм"],
                    (IReadOnlyList<string>)["Yc", F(value.Yc_mm), "—", "мм"],
                    (IReadOnlyList<string>)["EA", F(value.EA_kN), F(value.EAel_kN), "кН"],
                    (IReadOnlyList<string>)["EIy₀", F(value.EIy0_kNm2), "—", "кН·м²"],
                    (IReadOnlyList<string>)["EIz₀", F(value.EIz0_kNm2), "—", "кН·м²"],
                    (IReadOnlyList<string>)["EIy (ц.т.)", F(value.EIyc_kNm2), F(value.EIyel_kNm2), "кН·м²"],
                    (IReadOnlyList<string>)["EIz (ц.т.)", F(value.EIzc_kNm2), F(value.EIzel_kNm2), "кН·м²"],
                    (IReadOnlyList<string>)["φEA", F(value.PhiEA), "—", "безразм."],
                    (IReadOnlyList<string>)["φEIy", F(value.PhiEIy), "—", "безразм."],
                    (IReadOnlyList<string>)["φEIz", F(value.PhiEIz), "—", "безразм."]
                ]));
    }

    /// <summary>Добавляет раздел действий преднапряжения, если они сохранены.</summary>
    public static ReportDocument Prestress(ReportDocument document, PrestressActionsJsonModel? prestress)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (prestress == null || prestress.Groups.Count == 0) return document;

        var groupRows = prestress.Groups.Select(group => (IReadOnlyList<string>)[
            group.Tag, F(group.AreaM2), Dimension(group.X), Dimension(group.Y), F(group.SigSp),
            F(group.GammaSp), F(group.SigActual), F(group.SigLimit)]).ToList();
        document
            .Add(new ReportHeading(1, "Преднапряжение"))
            .Add(new ReportParagraph($"Точка отсчёта моментов: x = {Dimension(prestress.Reference.X)}, y = {Dimension(prestress.Reference.Y)}. Все действия: N — кН, Mx/My — кН·м."))
            .Add(new ReportTable(
                ["Состояние", "N, кН", "Mx, кН·м", "My, кН·м"],
                [
                    (IReadOnlyList<string>)["Номинальное", Force(prestress.Nominal.N), Moment(prestress.Nominal.Mx), Moment(prestress.Nominal.My)],
                    (IReadOnlyList<string>)["Эффективное", Force(prestress.Effective.N), Moment(prestress.Effective.Mx), Moment(prestress.Effective.My)],
                    (IReadOnlyList<string>)["Фактическое", Force(prestress.Actual.N), Moment(prestress.Actual.Mx), Moment(prestress.Actual.My)]
                ]))
            .Add(new ReportTable(
                ["Группа", "A, м²", "x, м", "y, м"],
                groupRows.Select(row => (IReadOnlyList<string>)[row[0], row[1], row[2], row[3]]).ToList()))
            .Add(new ReportTable(
                ["Группа", "σsp, МПа", "γsp", "σфакт., МПа", "σпредел, МПа"],
                groupRows.Select(row => (IReadOnlyList<string>)[row[0], row[4], row[5], row[6], row[7]]).ToList()));
        if (prestress.HasGroupsAboveStrength)
            document.Add(new ReportWarning("Для одной или нескольких групп преднапряжения заданное напряжение превышает расчётное сопротивление."));
        return document;
    }

    /// <summary>Добавляет раздел влияния прогиба η при наличии данных.</summary>
    public static ReportDocument Eta(ReportDocument document, EtaReportData? eta)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (eta == null) return document;
        return document
            .Add(new ReportHeading(1, "Влияние прогиба"))
            .Add(new ReportParagraph($"Режим: {ValueOrDash(eta.Mode)}; исходные моменты: Mx = {Moment(eta.MxOriginal)}, My = {Moment(eta.MyOriginal)}."))
            .Add(new ReportTable(
                ["Направление", "l0, м", "h, м", "l0/h"],
                [
                    (IReadOnlyList<string>)["X", DimensionValue(eta.L0x), DimensionValue(eta.Hx), ValueOrDash(eta.SlendernessX)],
                    (IReadOnlyList<string>)["Y", DimensionValue(eta.L0y), DimensionValue(eta.Hy), ValueOrDash(eta.SlendernessY)]
                ]))
            .Add(new ReportTable(
                ["Направление", "D, кН·м²", "Ncr, кН", "η", "Статус"],
                [
                    (IReadOnlyList<string>)["X", ValueOrDash(eta.DX), ValueOrDash(eta.NcrX), ValueOrDash(eta.EtaX), eta.StableX ? "устойчиво" : "неустойчиво"],
                    (IReadOnlyList<string>)["Y", ValueOrDash(eta.DY), ValueOrDash(eta.NcrY), ValueOrDash(eta.EtaY), eta.StableY ? "устойчиво" : "неустойчиво"]
                ]));
    }

    /// <summary>Добавляет предупреждение, когда живая модель сечения недоступна.</summary>
    public static ReportDocument SectionMissingWarning(ReportDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Add(new ReportWarning("Модель сечения не передана в контекст результата: геометрия, состав частей и диаграммы материалов недоступны."));
    }

    internal static string F(double? value) => value.HasValue ? F(value.Value) : "—";
    internal static string F(double value)
        => double.IsFinite(value) ? value.ToString("G8", CultureInfo.InvariantCulture) : "—";
    internal static string Force(double? value) => $"{F(value)} кН";
    internal static string Moment(double? value) => $"{F(value)} кН·м";
    internal static string Curvature(double? value) => $"{F(value)} 1/м";
    internal static string Dimension(double? value) => $"{F(value)} м";
    internal static string DimensionValue(double? value) => F(value);
    internal static string ValueOrDash(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
    internal static string ValueOrDash(double? value) => F(value);
    internal static string Sub(string symbol, string sub) => $"{symbol}<sub>{sub}</sub>";
    internal static string Sup(string symbol, string sup) => $"{symbol}<sup>{sup}</sup>";

    static List<ReportRebarRow> RebarRows(CrossSection section, Kurvature k)
    {
        var rows = new List<ReportRebarRow>();
        int number = 1;
        foreach (var (area, _) in section.EnumerateAreas(k))
        {
            foreach (var fiber in area.Fibers.Where(f => f.TypeFiber == FiberType.point))
            {
                double eps = fiber.Eps;
                double sigmaMpa = fiber.Sig / 1000.0;
                double eMpa = fiber.E > 0 ? fiber.E / 1000.0 : 0;
                rows.Add(new ReportRebarRow(number++, area.Tag, area.Material?.Tag ?? "не задан",
                    fiber.X * 1000.0, fiber.Y * 1000.0, fiber.Diameter * 1000.0,
                    fiber.Area * 1e6, eps, sigmaMpa, eMpa));
            }
        }
        return rows;
    }

    static GeoProps AreaProps(MaterialArea area)
    {
        if (area.Hull != null)
        {
            var props = new GeoProps(area.Hull);
            foreach (var hole in area.Holes)
                props -= new GeoProps(hole);
            return props;
        }
        return new GeoProps(area);
    }

    sealed record ReportRebarRow(int Num, string Group, string Material, double Xmm, double Ymm,
        double DiameterMm, double AreaMm2, double Eps, double SigmaMpa, double SecantModulusMpa);
}
