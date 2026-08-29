using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using CScore.Sp63Shear;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Одна проверка наклонного сечения в отчёте.</summary>
public sealed class ShearInclinedDetailVM
{
    /// <summary>Плоскость сдвига: «vy» или «vx».</summary>
    public string Plane { get; init; } = "";
    /// <summary>Номер формулы нормы.</summary>
    public string Formula { get; init; } = "";
    /// <summary>Название проверки.</summary>
    public string Description { get; init; } = "";
    /// <summary>Ссылка на пункт нормы.</summary>
    public string NormRef { get; init; } = "";
    /// <summary>Действующее усилие.</summary>
    public double Applied { get; init; }
    /// <summary>Предельное усилие.</summary>
    public double Allowable { get; init; }
    /// <summary>Коэффициент использования; +∞ — нулевая несущая способность.</summary>
    public double Ratio { get; init; }
    /// <summary>Признак прохождения проверки.</summary>
    public bool Passed { get; init; }
    /// <summary>Трассировка расчёта.</summary>
    public string Trace { get; init; } = "";

    /// <summary>Заголовок строки: плоскость и формула.</summary>
    public string Title => $"{Plane.ToUpperInvariant()} · {Formula}";
    /// <summary>Текст коэффициента использования.</summary>
    public string RatioText => double.IsFinite(Ratio)
        ? Ratio.ToString("F3", CultureInfo.CurrentCulture)
        : "∞";
    /// <summary>Действующее усилие с округлением.</summary>
    public string AppliedText => Applied.ToString("F1", CultureInfo.CurrentCulture);
    /// <summary>Предельное усилие с округлением.</summary>
    public string AllowableText => Allowable.ToString("F1", CultureInfo.CurrentCulture);
    /// <summary>Текст результата проверки.</summary>
    public string PassedText => Passed ? Loc.S("ShearInclinedPassed") : Loc.S("ShearInclinedFailed");
    /// <summary>Цвет коэффициента использования.</summary>
    public Brush RatioBrush => !double.IsFinite(Ratio) || Ratio >= 1.0
        ? Brushes.Red
        : Ratio < 0.8 ? Brushes.Green : Brushes.DarkOrange;
    /// <summary>Фон строки проверки.</summary>
    public Brush RowBackground => Passed
        ? new SolidColorBrush(Color.FromArgb(0x1A, 0x2D, 0x7A, 0x3E))
        : new SolidColorBrush(Color.FromArgb(0x1A, 0xC0, 0x39, 0x2B));
}

/// <summary>Группа проверок одного пункта нормы.</summary>
public sealed class ShearInclinedGroupVM
{
    /// <summary>Название группы.</summary>
    public string Name { get; init; } = "";
    /// <summary>Проверки группы.</summary>
    public List<ShearInclinedDetailVM> Items { get; init; } = [];
    /// <summary>Наибольший коэффициент использования в группе.</summary>
    public double MaxRatio => Items.Count == 0 ? 0.0 : Items.Max(i => i.Ratio);
    /// <summary>Текст наибольшего коэффициента.</summary>
    public string MaxRatioText => double.IsFinite(MaxRatio) ? $"max {MaxRatio:F3}" : "max ∞";
    /// <summary>Цвет наибольшего коэффициента.</summary>
    public Brush MaxRatioBrush => !double.IsFinite(MaxRatio) || MaxRatio >= 1.0
        ? Brushes.Red
        : MaxRatio < 0.8 ? Brushes.Green : Brushes.DarkOrange;
}

/// <summary>Строка таблицы стоянок вдоль элемента.</summary>
public sealed class ShearInclinedStationVM
{
    /// <summary>Плоскость сдвига.</summary>
    public string Plane { get; init; } = "";
    /// <summary>Координата стоянки, м.</summary>
    public double S { get; init; }
    /// <summary>Продольная сила, кН.</summary>
    public double N { get; init; }
    /// <summary>Коэффициент φn.</summary>
    public double PhiN { get; init; }
    /// <summary>Растянута грань с положительной координатой.</summary>
    public bool TensionOnPositiveSide { get; init; }
    /// <summary>Поперечная сила, кН.</summary>
    public double Q { get; init; }
    /// <summary>Критическая проекция по Q, м; NaN — проверка не выполнялась.</summary>
    public double CriticalC { get; init; }
    /// <summary>Сила, воспринимаемая бетоном, кН.</summary>
    public double Qb { get; init; }
    /// <summary>Сила, воспринимаемая хомутами, кН.</summary>
    public double Qsw { get; init; }
    /// <summary>Коэффициент использования по поперечной силе; NaN — не проверялось.</summary>
    public double Eta { get; init; }
    /// <summary>Момент в точке 0 при критической проекции по моменту, кН·м.</summary>
    public double MomentApplied { get; init; }
    /// <summary>Критическая проекция по моменту, м; NaN — проверка не выполнялась.</summary>
    public double CriticalCMoment { get; init; }
    /// <summary>Момент продольной арматуры, кН·м.</summary>
    public double Ms { get; init; }
    /// <summary>Момент хомутов, кН·м.</summary>
    public double Msw { get; init; }
    /// <summary>Коэффициент использования по моменту; NaN — не проверялось.</summary>
    public double EtaM { get; init; }

    /// <summary>Текст растянутой грани для таблицы.</summary>
    public string TensionSideText => TensionOnPositiveSide ? "+" : "−";
}

/// <summary>Данные одной диаграммы несущей способности по проекции C.</summary>
public sealed record ShearInclinedProjectionChartVM(
    string Plane,
    ShearInclinedStationVM Station,
    IReadOnlyList<ProjectionPoint> Curve)
{
    /// <summary>Критическая проекция выбранной плоскости, м.</summary>
    public double CriticalC => Station.CriticalC;

    /// <summary>Есть ли точки для отрисовки кривой.</summary>
    public bool HasCurve => Curve.Count >= 2;
}

/// <summary>ViewModel отчёта по расчёту наклонных сечений.</summary>
public sealed class ShearInclinedResultVM
{
    /// <summary>Исходные данные одной плоскости, восстановленные из результата.</summary>
    /// <param name="B">Ширина, м.</param>
    /// <param name="H0">Рабочая высота, м.</param>
    /// <param name="Qsw">Погонное усилие хомутов, кН/м.</param>
    /// <param name="Sw">Шаг хомутов, м.</param>
    /// <param name="Ns">Усилие в продольной арматуре, кН.</param>
    /// <param name="Rb">Сопротивление бетона сжатию, кПа.</param>
    /// <param name="Rbt">Сопротивление бетона растяжению, кПа.</param>
    public readonly record struct PlaneInputs(
        double B, double H0, double Qsw, double Sw, double Ns, double Rb, double Rbt);

    readonly List<string> _cautions = [];
    readonly Dictionary<string, PlaneInputs> _inputs = [];
    readonly Dictionary<string, JsonElement> _profiles = [];

    /// <summary>Разбирает DataJson результата задачи.</summary>
    public ShearInclinedResultVM(string dataJson)
    {
        using var doc = JsonDocument.Parse(dataJson);
        var root = doc.RootElement;

        if (root.TryGetProperty("error", out var error))
        {
            VerdictText = error.GetString() ?? "";
            return;
        }

        SectionTag = root.GetProperty("sectionTag").GetString() ?? "";
        ForceLabel = root.TryGetProperty("forceLabel", out var label) ? label.GetString() ?? "" : "";
        // null в utilization — нулевая несущая способность
        Utilization = Number(root, "utilization", double.PositiveInfinity);
        UtilizationExact = Number(root, "utilizationExact", double.PositiveInfinity);
        Direction = root.TryGetProperty("direction", out var dir) ? dir.GetInt32() : -1;

        var details = new List<ShearInclinedDetailVM>();
        foreach (var item in root.GetProperty("details").EnumerateArray())
        {
            var variables = new Dictionary<string, double>();
            if (item.TryGetProperty("variables", out var vars))
                foreach (var kv in vars.EnumerateObject())
                    variables[kv.Name] = kv.Value.GetDouble();

            double ratio = Number(item, "ratio", double.PositiveInfinity);
            details.Add(new ShearInclinedDetailVM
            {
                Plane = item.GetProperty("plane").GetString() ?? "",
                Formula = item.GetProperty("formula").GetString() ?? "",
                Description = item.GetProperty("description").GetString() ?? "",
                NormRef = item.GetProperty("normRef").GetString() ?? "",
                Applied = item.GetProperty("applied").GetDouble(),
                Allowable = item.GetProperty("allowable").GetDouble(),
                Ratio = ratio,
                Passed = item.GetProperty("passed").GetBoolean() && double.IsFinite(ratio),
                Trace = FormatTrace(variables)
            });
        }

        AddGroup(Loc.S("ShearInclinedGroupStrip"), details, f => f == "8.55");
        AddGroup(Loc.S("ShearInclinedGroupShear"), details, f => f is "8.56" or "8.60");
        AddGroup(Loc.S("ShearInclinedGroupMoment"), details, f => f.StartsWith("8.63"));

        foreach (var station in root.GetProperty("stations").EnumerateArray())
            Stations.Add(new ShearInclinedStationVM
            {
                Plane = station.GetProperty("plane").GetString() ?? "",
                S = station.GetProperty("s").GetDouble(),
                N = station.GetProperty("n").GetDouble(),
                PhiN = station.GetProperty("phiN").GetDouble(),
                TensionOnPositiveSide = station.GetProperty("tensionOnPositiveSide").GetBoolean(),
                Q = station.GetProperty("q").GetDouble(),
                CriticalC = Number(station, "cCrit", double.NaN),
                Qb = Number(station, "qb", double.NaN),
                Qsw = Number(station, "qsw", double.NaN),
                Eta = Number(station, "eta", double.NaN),
                MomentApplied = Number(station, "mApplied", double.NaN),
                CriticalCMoment = Number(station, "cCritMoment", double.NaN),
                Ms = Number(station, "ms", double.NaN),
                Msw = Number(station, "msw", double.NaN),
                EtaM = Number(station, "etaM", double.NaN)
            });

        if (root.TryGetProperty("profile", out var profileBlock))
            foreach (var plane in profileBlock.EnumerateObject())
                _profiles[plane.Name] = plane.Value.Clone();

        foreach (var warning in root.GetProperty("warnings").EnumerateArray())
            if (warning.GetString() is { } text) _cautions.Add(text);

        foreach (var plane in root.GetProperty("inputs").EnumerateObject())
            _inputs[plane.Name] = new PlaneInputs(
                plane.Value.GetProperty("b").GetDouble(),
                plane.Value.GetProperty("h0").GetDouble(),
                plane.Value.GetProperty("qsw").GetDouble(),
                plane.Value.GetProperty("sw").GetDouble(),
                plane.Value.GetProperty("ns").GetDouble(),
                plane.Value.GetProperty("rb").GetDouble(),
                plane.Value.GetProperty("rbt").GetDouble());

        InputsSummary = FormatInputs(root.GetProperty("inputs"));
        VerdictText = !double.IsFinite(Utilization)
            ? $"{Loc.S("ShearInclinedFailed")} — ∞"
            : Utilization <= 1.0
                ? $"{Loc.S("ShearInclinedPassed")} — {Utilization:F3}"
                : $"{Loc.S("ShearInclinedFailed")} — {Utilization:F3}";
    }

    /// <summary>Числовое поле; null или отсутствие поля дают значение по умолчанию.</summary>
    static double Number(JsonElement owner, string name, double fallback) =>
        owner.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : fallback;

    /// <summary>Направление наклонного сечения, использованное в расчёте.</summary>
    public int Direction { get; } = -1;
    /// <summary>Метка сечения.</summary>
    public string SectionTag { get; } = "";
    /// <summary>Метка строки усилий.</summary>
    public string ForceLabel { get; } = "";
    /// <summary>Наибольший коэффициент использования, включая упрощённые условия.</summary>
    public double Utilization { get; }
    /// <summary>Коэффициент использования только по точным проверкам (8.55), (8.56), (8.63).</summary>
    public double UtilizationExact { get; }
    /// <summary>Итоговый вердикт.</summary>
    public string VerdictText { get; } = "";
    /// <summary>Оговорки расчёта.</summary>
    public IReadOnlyList<string> Cautions => _cautions;
    /// <summary>Группы проверок.</summary>
    public List<ShearInclinedGroupVM> Groups { get; } = [];
    /// <summary>Стоянки вдоль элемента.</summary>
    public List<ShearInclinedStationVM> Stations { get; } = [];
    /// <summary>Сводка исходных данных.</summary>
    public string InputsSummary { get; } = "";

    /// <summary>Текст коэффициента по точным проверкам для шапки отчёта.</summary>
    public string ExactUtilizationText => double.IsFinite(UtilizationExact)
        ? $"{Loc.S("ShearInclinedExactUtilization")}: {UtilizationExact:F3}"
        : $"{Loc.S("ShearInclinedExactUtilization")}: ∞";

    /// <summary>Цвет итогового вердикта.</summary>
    public Brush VerdictBrush => double.IsFinite(Utilization) && Utilization <= 1.0
        ? Brushes.Green
        : Brushes.Red;

    /// <summary>Видимость блока оговорок.</summary>
    public System.Windows.Visibility CautionsVisibility => _cautions.Count > 0
        ? System.Windows.Visibility.Visible
        : System.Windows.Visibility.Collapsed;

    /// <summary>Рабочая высота плоскости, м.</summary>
    public double WorkingDepth(string plane) =>
        _inputs.TryGetValue(plane, out var data) ? data.H0 : 0.0;

    /// <summary>Строит отдельную диаграмму по проекции C для каждой плоскости.</summary>
    public IReadOnlyList<ShearInclinedProjectionChartVM> BuildProjectionCharts()
    {
        return Stations
            .Where(station => station.Plane is "vy" or "vx")
            .GroupBy(station => station.Plane, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key.Equals("vy", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .Select(group => group
                .Where(station => double.IsFinite(station.Eta))
                .OrderByDescending(station => station.Eta)
                .Select(station => new ShearInclinedProjectionChartVM(
                    group.Key.ToLowerInvariant(), station, BuildProjectionCurve(station)))
                .FirstOrDefault())
            .Where(chart => chart is not null)
            .Cast<ShearInclinedProjectionChartVM>()
            .ToList();
    }

    /// <summary>
    /// Строит кривую несущей способности по проекции для выбранной стоянки.
    /// Профиль восстанавливается из блока «profile» результата — тот же, которым вёлся
    /// расчёт: подставной ConstantProfile для переменной или FEM-эпюры давал бы кривую,
    /// не соответствующую расчётным значениям.
    /// </summary>
    public IReadOnlyList<ProjectionPoint> BuildProjectionCurve(ShearInclinedStationVM station)
    {
        ArgumentNullException.ThrowIfNull(station);
        if (!_inputs.TryGetValue(station.Plane, out var data)) return [];
        if (RestoreProfile(station.Plane) is not IForceProfile profile) return [];

        var input = new ShearInclinedInput(
            B: data.B, H0: data.H0, Rb: data.Rb, Rbt: data.Rbt, Qsw: data.Qsw, Sw: data.Sw,
            Ns: data.Ns, Kind: ElementKind.BendingUnstressed, AnchorageFactor: 1.0,
            StationStep: 0.0, ProjectionStep: 0.0,
            MomentZoneLength: 0.0, BarCutoffs: [], CheckMoment: true,
            PhiNOverride: station.PhiN,
            FixedB: data.B, FixedH0: data.H0, FixedNs: data.Ns);

        // Геометрия зафиксирована значениями стоянки: обе стороны пары одинаковы,
        // а FixedB/FixedH0/FixedNs не дают WithGeometry их перезаписать.
        var side = new InclinedSectionGeometry(
            B: data.B, H0: data.H0, Ns: data.Ns, As: 0.0, Rb: data.Rb, Rbt: data.Rbt,
            Ab: 0.0, AsTotal: 0.0, Eb: 1.0, Eb0: 1.0, Ebt0: 1.0,
            Plane: station.Plane == "vx" ? ShearPlane.Vx : ShearPlane.Vy,
            TensionOnPositiveSide: station.TensionOnPositiveSide, Warnings: []);
        var geometry = new InclinedSectionGeometryPair(side, side);

        return ShearInclinedChecker.ProjectionCurve(input, profile, geometry, station.S, Direction);
    }

    /// <summary>Воссоздаёт профиль усилий плоскости по сохранённому описанию.</summary>
    IForceProfile? RestoreProfile(string plane)
    {
        if (!_profiles.TryGetValue(plane, out var data)) return null;

        string kind = data.GetProperty("kind").GetString() ?? "constant";
        if (kind == "sampled")
        {
            var samples = data.GetProperty("samples").EnumerateArray()
                .Select(s => new ForceSample(
                    s.GetProperty("s").GetDouble(), s.GetProperty("q").GetDouble(),
                    s.GetProperty("m").GetDouble(), s.GetProperty("n").GetDouble()))
                .ToList();
            if (samples.Count < 2) return null;
            return new SampledProfile(
                samples, 0.0, samples[^1].S - samples[0].S,
                data.GetProperty("supportAtStart").GetBoolean(),
                data.GetProperty("supportAtEnd").GetBoolean());
        }

        double q0 = data.GetProperty("q0").GetDouble();
        double m0 = data.GetProperty("m0").GetDouble();
        double n0 = data.GetProperty("n0").GetDouble();
        double supportDistance = data.GetProperty("supportDistance").GetDouble();

        if (kind == "uniform_load")
            return new UniformLoadProfile(
                q0, m0, n0, data.GetProperty("load").GetDouble(), supportDistance,
                data.GetProperty("supportAtStart").GetBoolean(),
                data.GetProperty("supportAtEnd").GetBoolean());

        return new ConstantProfile(q0, m0, n0, supportDistance);
    }

    /// <summary>Добавляет группу проверок, отобранных по номеру формулы.</summary>
    void AddGroup(string name, List<ShearInclinedDetailVM> details, Func<string, bool> filter)
    {
        var items = details.Where(d => filter(d.Formula)).ToList();
        if (items.Count > 0) Groups.Add(new ShearInclinedGroupVM { Name = name, Items = items });
    }

    /// <summary>Форматирует переменные проверки в строку трассировки.</summary>
    static string FormatTrace(Dictionary<string, double> variables)
    {
        var parts = variables.Select(kv => $"{kv.Key} = {FormatVariable(kv.Key, kv.Value)}");
        return string.Join(";  ", parts);
    }

    /// <summary>Форматирует одну переменную с подходящими единицами.</summary>
    static string FormatVariable(string name, double value) => name switch
    {
        "s" or "C" or "d" or "b" or "h0" => $"{value * 1000.0:F0} мм",
        "Qb" or "Qsw" or "Qb,min" or "Qsw,min" => $"{value:F1} кН",
        "Ms" or "Msw" => $"{value:F1} кН·м",
        "Rb" or "Rbt" => $"{value / 1000.0:F1} МПа",
        "qsw" => $"{value:F1} кН/м",
        _ => value.ToString("F3", CultureInfo.CurrentCulture)
    };

    /// <summary>Собирает сводку исходных данных по плоскостям.</summary>
    static string FormatInputs(JsonElement inputs)
    {
        var builder = new StringBuilder();
        foreach (var plane in inputs.EnumerateObject())
        {
            var values = plane.Value;
            double b = values.GetProperty("b").GetDouble();
            double h0 = values.GetProperty("h0").GetDouble();
            string bMark = Math.Abs(b - values.GetProperty("autoB").GetDouble()) < 1e-9
                ? Loc.S("ShearInclinedAuto") : Loc.S("ShearInclinedManual");
            string h0Mark = Math.Abs(h0 - values.GetProperty("autoH0").GetDouble()) < 1e-9
                ? Loc.S("ShearInclinedAuto") : Loc.S("ShearInclinedManual");

            if (builder.Length > 0) builder.AppendLine();
            builder.Append(
                $"{plane.Name.ToUpperInvariant()}:  b = {b * 1000.0:F0} мм ({bMark});  "
                + $"h0 = {h0 * 1000.0:F0} мм ({h0Mark});  "
                + $"qsw = {values.GetProperty("qsw").GetDouble():F1} кН/м;  "
                + $"sw = {values.GetProperty("sw").GetDouble() * 1000.0:F0} мм;  "
                + $"Ns = {values.GetProperty("ns").GetDouble():F1} кН");
        }
        return builder.ToString();
    }
}
