using System.Globalization;
using CScore.Planar;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>Настройки проекта, которые правят окно «Настройки» и вид <c>settings</c> документа CLI.</summary>
public sealed class ProjectSettings
{
    public CalcSettings Calc { get; init; } = new();
    public LiraImportSettings Lira { get; init; } = new();
    public AcadImportSettings Acad { get; init; } = new();
    public GmshSettings Gmsh { get; init; } = new();

    public static ProjectSettings Load(DatabaseService db) => new()
    {
        Calc = db.LoadCalcSettings(), Lira = db.LoadLiraImportSettings(), Acad = db.LoadAcadImportSettings(),
        Gmsh = db.LoadGmshSettings(),
    };

    public ProjectSettings Clone() => new() { Calc = Calc.Clone(), Lira = Lira.Clone(), Acad = Acad.Clone(), Gmsh = Gmsh.Clone() };
}

/// <summary>Допустимый диапазон числа; границы включительно или нет.</summary>
public sealed record SettingRange(double? Min, bool MinInclusive, double? Max, bool MaxInclusive)
{
    public static SettingRange Positive { get; } = new(0, false, null, false);
    public static SettingRange NonNegative { get; } = new(0, true, null, false);
    public static SettingRange OpenUnit { get; } = new(0, false, 1, false);
    public static SettingRange AtLeast(double min) => new(min, true, null, false);
    public static SettingRange Between(double min, double max) => new(min, true, max, true);

    public bool Contains(double v) =>
        double.IsFinite(v)
        && (Min is not { } lo || (MinInclusive ? v >= lo : v > lo))
        && (Max is not { } hi || (MaxInclusive ? v <= hi : v < hi));

    /// <summary>Запись диапазона без слов («x > 0», «1 ≤ x ≤ 30») — одинакова на обоих языках.</summary>
    public string Text()
    {
        static string N(double v) => v.ToString("G", CultureInfo.CurrentCulture);
        string lo = Min is { } a ? $"{N(a)} {(MinInclusive ? "≤" : "<")} " : "";
        string hi = Max is { } b ? $" {(MaxInclusive ? "≤" : "<")} {N(b)}" : "";
        if (Min is { } m && Max == null) return $"x {(MinInclusive ? "≥" : ">")} {N(m)}";
        return $"{lo}x{hi}";
    }
}

/// <summary>Вид значения поля настроек.</summary>
public enum SettingKind { Number, Integer, Bool, Choice, IntList }

/// <summary>
/// Поле настроек проекта: путь в документе (<c>sections.newton.tolerance</c>), чтение и запись в единицах окна
/// и документа, проверка. Значения: <see cref="double"/>, <see cref="int"/>, <see cref="bool"/>, <see cref="string"/>
/// (слово выбора), <see cref="IReadOnlyList{T}"/> целых или null.
/// </summary>
public sealed class SettingField
{
    public required string Path { get; init; }
    public required SettingKind Kind { get; init; }
    /// <summary>false — только окно (таймауты, потоки): от машины, в документ не входит.</summary>
    public bool InDocument { get; init; } = true;
    public SettingRange? Range { get; init; }
    /// <summary>Допустимые значения числа (переключатель окна, например 9,80665 или 10).</summary>
    public IReadOnlyList<double>? Allowed { get; init; }
    /// <summary>Слова выбора (Choice).</summary>
    public IReadOnlyList<string>? Choices { get; init; }
    public required Func<ProjectSettings, object?> Get { get; init; }
    public required Action<ProjectSettings, object?> Set { get; init; }

    /// <summary>Ошибка значения или null.</summary>
    public string? Check(object? value)
    {
        switch (Kind)
        {
            case SettingKind.Bool:
                return value is bool ? null : Loc.S("SettingErrBool");
            case SettingKind.Choice:
                return value is string s && Choices!.Contains(s) ? null
                    : string.Format(Loc.S("SettingErrChoice"), string.Join(", ", Choices!));
            case SettingKind.Integer:
                if (value is not int i) return Loc.S("SettingErrInteger");
                if (Allowed != null && !Allowed.Contains(i)) return AllowedError();
                return Range == null || Range.Contains(i) ? null : RangeError();
            case SettingKind.Number:
                if (value is not double d || !double.IsFinite(d)) return Loc.S("NumberInputInvalid");
                if (Allowed != null && !Allowed.Any(a => Math.Abs(a - d) <= 1e-9 * Math.Max(1, Math.Abs(a)))) return AllowedError();
                return Range == null || Range.Contains(d) ? null : RangeError();
            case SettingKind.IntList:
                if (value == null) return null;
                return value is IReadOnlyList<int> list && list.Count > 0 && list.All(x => x >= 1) ? null : Loc.S("SettingErrIntList");
        }
        return null;
    }

    string RangeError() => string.Format(Loc.S("SettingErrRange"), Range!.Text());

    string AllowedError() => string.Format(Loc.S("SettingErrChoice"),
        string.Join(", ", Allowed!.Select(a => a.ToString("G", CultureInfo.CurrentCulture))));

    /// <summary>Разбор текста поля окна (запятая и точка); ошибка — текст, значение — в <paramref name="value"/>.</summary>
    public string? Parse(string text, out object? value)
    {
        text = text.Trim();
        value = null;
        switch (Kind)
        {
            case SettingKind.Integer:
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) return Loc.S("SettingErrInteger");
                value = i;
                break;
            case SettingKind.Number:
                if (!ParseNumber(text, out double d)) return Loc.S("NumberInputInvalid");
                value = d;
                break;
            case SettingKind.IntList:
                if (text.Length == 0) return null;
                var items = new List<int>();
                foreach (var token in text.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int p) || p < 1)
                        return Loc.S("SettingErrIntList");
                    items.Add(p);
                }
                value = items;
                break;
            default:
                value = text;
                break;
        }
        return Check(value);
    }

    /// <summary>Число с запятой или точкой (как <c>Pars.ParseAny</c> окна); только конечное.</summary>
    public static bool ParseNumber(string text, out double value) =>
        double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    /// <summary>Значение для поля окна (текущая культура).</summary>
    public string Format(ProjectSettings s) => Get(s) switch
    {
        double d => d.ToString("G6", CultureInfo.CurrentCulture),
        int i => i.ToString(CultureInfo.InvariantCulture),
        IReadOnlyList<int> list => string.Join(", ", list),
        null => "",
        var v => v.ToString() ?? "",
    };
}

/// <summary>
/// Проверка и запись настроек проекта (ключи <c>calc</c>, <c>lira_import</c>, <c>acad_import</c>, <c>gmsh</c> таблицы
/// <c>settings</c>). Одна таблица полей для окна «Настройки» и для вида <c>settings</c> документа CLI: диапазоны
/// и слова выбора не расходятся. В документ входят настройки, от которых зависят результаты расчёта и импорта;
/// оформление, запомненные выборы окон и среда ПК (пути, таймауты, потоки) — только в окне.
/// </summary>
public static class ProjectSettingsAuthoring
{
    static SettingField Num(string path, SettingRange range, Func<ProjectSettings, double> get, Action<ProjectSettings, double> set,
        bool doc = true, IReadOnlyList<double>? allowed = null) => new()
    {
        Path = path, Kind = SettingKind.Number, Range = range, Allowed = allowed, InDocument = doc,
        Get = s => get(s), Set = (s, v) => set(s, (double)v!),
    };

    static SettingField Int(string path, SettingRange range, Func<ProjectSettings, int> get, Action<ProjectSettings, int> set,
        bool doc = true, IReadOnlyList<double>? allowed = null) => new()
    {
        Path = path, Kind = SettingKind.Integer, Range = range, Allowed = allowed, InDocument = doc,
        Get = s => get(s), Set = (s, v) => set(s, (int)v!),
    };

    static SettingField Flag(string path, Func<ProjectSettings, bool> get, Action<ProjectSettings, bool> set) => new()
    {
        Path = path, Kind = SettingKind.Bool, Get = s => get(s), Set = (s, v) => set(s, (bool)v!),
    };

    /// <summary>Выбор: слово документа ↔ хранимое значение (первое — умолчание для неизвестного хранимого).</summary>
    static SettingField Choice<T>(string path, (string Word, T Stored)[] map, Func<ProjectSettings, T> get, Action<ProjectSettings, T> set) => new()
    {
        Path = path, Kind = SettingKind.Choice, Choices = map.Select(m => m.Word).ToList(),
        Get = s =>
        {
            var stored = get(s);
            foreach (var (word, value) in map) if (EqualityComparer<T>.Default.Equals(value, stored)) return word;
            return map[0].Word;
        },
        Set = (s, v) => set(s, map.First(m => m.Word == (string)v!).Stored),
    };

    static (string, string)[] Same(params string[] words) => words.Select(w => (w, w)).ToArray();

    /// <summary>Слово документа — хранимое имя с первой строчной (Linear → linear, PDelta → pDelta).</summary>
    static (string, string)[] Camel(params string[] stored) =>
        stored.Select(v => (char.ToLowerInvariant(v[0]) + v[1..], v)).ToArray();

    public static IReadOnlyList<SettingField> Fields { get; } =
    [
        Int("sections.gridDensity", SettingRange.AtLeast(1), s => s.Calc.GridDensity, (s, v) => s.Calc.GridDensity = v),
        Num("sections.newton.tolerance", SettingRange.Positive, s => s.Calc.NewtonTolerance, (s, v) => s.Calc.NewtonTolerance = v),
        Int("sections.newton.maxIterations", SettingRange.AtLeast(1), s => s.Calc.NewtonMaxIter, (s, v) => s.Calc.NewtonMaxIter = v),
        Num("sections.newton.deltaH", SettingRange.Positive, s => s.Calc.NewtonDeltaH, (s, v) => s.Calc.NewtonDeltaH = v),
        Choice("sections.newton.jacobian", Same("forward", "central"), s => s.Calc.NewtonJacobian, (s, v) => s.Calc.NewtonJacobian = v),
        Num("sections.limitBisection.absTolerance", SettingRange.Positive, s => s.Calc.LimitBisectAbsTol, (s, v) => s.Calc.LimitBisectAbsTol = v),
        Num("sections.limitBisection.relTolerance", SettingRange.NonNegative, s => s.Calc.LimitBisectRelTol, (s, v) => s.Calc.LimitBisectRelTol = v),
        Num("sections.descendingBranch.sp63EtaMin", SettingRange.OpenUnit, s => s.Calc.Sp63DescEtaMin, (s, v) => s.Calc.Sp63DescEtaMin = v),
        Num("sections.descendingBranch.ekbEtaMin", SettingRange.OpenUnit, s => s.Calc.EkbDescEtaMin, (s, v) => s.Calc.EkbDescEtaMin = v),
        Num("sections.plates.newtonResidual", SettingRange.Positive, s => s.Calc.ShellNewtonTolRes, (s, v) => s.Calc.ShellNewtonTolRes = v),
        Flag("sections.plates.warmStart", s => s.Calc.ShellWarmStart, (s, v) => s.Calc.ShellWarmStart = v),
        Flag("sections.rebarDifferentialDiagram", s => s.Calc.RebarDifferentialDiagram, (s, v) => s.Calc.RebarDifferentialDiagram = v),
        Flag("sections.rebarCompressionByRs", s => s.Calc.RebarCompressionByRs, (s, v) => s.Calc.RebarCompressionByRs = v),
        Flag("sections.concreteTensionUls", s => s.Calc.ConsiderConcreteTensionUls, (s, v) => s.Calc.ConsiderConcreteTensionUls = v),
        Flag("sections.elasticStiffnessWithoutRebar", s => s.Calc.ElasticStiffnessWithoutRebar, (s, v) => s.Calc.ElasticStiffnessWithoutRebar = v),
        Choice("sections.stageStrain", Same("plane", "fibers"), s => s.Calc.StageStrainMode, (s, v) => s.Calc.StageStrainMode = v),
        Choice("sections.crackWidthPsiS", Same("stress8138", "strain8232"), s => s.Calc.CrackWidthPsiSMethod, (s, v) => s.Calc.CrackWidthPsiSMethod = v),
        Choice("sections.fireCompressionZone", Same("auto", "sp468_8_11", "fiber_equilibrium"),
            s => s.Calc.FireCompressionZoneMethod, (s, v) => s.Calc.FireCompressionZoneMethod = v),
        Num("sections.shear.stationStepM", SettingRange.NonNegative, s => s.Calc.ShearStationStep, (s, v) => s.Calc.ShearStationStep = v),
        Num("sections.shear.projectionStepM", SettingRange.NonNegative, s => s.Calc.ShearProjectionStep, (s, v) => s.Calc.ShearProjectionStep = v),
        Num("sections.shear.momentZoneLengthM", SettingRange.NonNegative, s => s.Calc.ShearMomentZoneLength, (s, v) => s.Calc.ShearMomentZoneLength = v),
        Num("sections.shear.anchorageFactor", SettingRange.Positive, s => s.Calc.ShearAnchorageFactor, (s, v) => s.Calc.ShearAnchorageFactor = v),

        Choice("loads.valueKind", Same("normative", "design"), s => s.Calc.Sp20LoadValueKind, (s, v) => s.Calc.Sp20LoadValueKind = v),
        Num("loads.gammaF.permanent", SettingRange.Positive, s => s.Calc.Sp20GammaFPermanent, (s, v) => s.Calc.Sp20GammaFPermanent = v),
        Num("loads.gammaF.permanentFavourable", SettingRange.Positive, s => s.Calc.Sp20GammaFPermanentFav, (s, v) => s.Calc.Sp20GammaFPermanentFav = v),
        Num("loads.gammaF.longTerm", SettingRange.Positive, s => s.Calc.Sp20GammaFLongTerm, (s, v) => s.Calc.Sp20GammaFLongTerm = v),
        Num("loads.gammaF.shortTerm", SettingRange.Positive, s => s.Calc.Sp20GammaFShortTerm, (s, v) => s.Calc.Sp20GammaFShortTerm = v),
        Num("loads.gammaF.accidental", SettingRange.Positive, s => s.Calc.Sp20GammaFAccidental, (s, v) => s.Calc.Sp20GammaFAccidental = v),

        Num("import.lira.tonToKn", SettingRange.Positive, s => Math.Abs(s.Lira.TonToKnFactor - 10) <= 0.01 ? 10 : 9.80665,
            (s, v) => s.Lira.TonToKnFactor = v, allowed: [9.80665, 10]),
        Flag("import.lira.invertBarMoments", s => s.Lira.InvertBarBendingMoments, (s, v) => s.Lira.InvertBarBendingMoments = v),
        Flag("import.lira.invertShellMoments", s => s.Lira.InvertShellBendingMoments, (s, v) => s.Lira.InvertShellBendingMoments = v),
        Choice("import.autocad.units", [("mm", 0.001), ("cm", 0.01), ("m", 1.0)],
            s => s.Acad.ScaleFactor switch { < 0.005 => 0.001, < 0.5 => 0.01, _ => 1.0 }, (s, v) => s.Acad.ScaleFactor = v),
        Choice("import.autocad.arcs.mode", [("chord", ArcDiscretization.ChordLength), ("segments", ArcDiscretization.FixedSegments)],
            s => s.Acad.ArcDiscretizationMode, (s, v) => s.Acad.ArcDiscretizationMode = v),
        Num("import.autocad.arcs.chordMm", SettingRange.Positive, s => s.Acad.ArcChordLength, (s, v) => s.Acad.ArcChordLength = v),
        Int("import.autocad.arcs.segments", SettingRange.AtLeast(3), s => s.Acad.ArcSegments, (s, v) => s.Acad.ArcSegments = v),

        Int("mesh.algorithm", SettingRange.AtLeast(1), s => s.Gmsh.Algorithm, (s, v) => s.Gmsh.Algorithm = v, allowed: [1, 2, 5, 6, 8, 9]),
        Choice("mesh.elements", [("triangles", PlanarMeshElementMode.Triangles), ("quads", PlanarMeshElementMode.Quads), ("mixed", PlanarMeshElementMode.Mixed)],
            s => s.Gmsh.ElementMode, (s, v) => s.Gmsh.ElementMode = v),
        Num("mesh.seamToleranceMm", SettingRange.Between(CScore.Fem.FemPlanarMeshPlan.MinSeamToleranceM * 1000, CScore.Fem.FemPlanarMeshPlan.MaxSeamToleranceM * 1000),
            s => s.Gmsh.SeamToleranceM * 1000, (s, v) => s.Gmsh.SeamToleranceM = v / 1000),

        Choice("csfea.barStiffness", [("min", false), ("mean", true)], s => s.Calc.CsfeaBarStiffnessMean, (s, v) => s.Calc.CsfeaBarStiffnessMean = v),
        Num("csfea.stiffnessFloor", SettingRange.OpenUnit, s => s.Calc.ResolveCsfeaStiffnessFloor(), (s, v) => s.Calc.CsfeaStiffnessFloor = v),

        Num("openSees.defaultGjKnm2", SettingRange.Positive, s => s.Calc.OpenSeesDefaultGjKnm2, (s, v) => s.Calc.OpenSeesDefaultGjKnm2 = v),
        Flag("openSees.autoGjFromSection", s => s.Calc.OpenSeesAutoGjFromSection, (s, v) => s.Calc.OpenSeesAutoGjFromSection = v),
        Int("openSees.refinementDivisions", SettingRange.AtLeast(1), s => s.Calc.OpenSeesRefinementDivisions, (s, v) => s.Calc.OpenSeesRefinementDivisions = v),
        Int("openSees.maxRefinementDepth", SettingRange.AtLeast(1), s => s.Calc.OpenSeesMaxRefinementDepth, (s, v) => s.Calc.OpenSeesMaxRefinementDepth = v),
        Num("openSees.tolerance", SettingRange.Positive, s => s.Calc.OpenSeesTolerance, (s, v) => s.Calc.OpenSeesTolerance = v),
        Int("openSees.maxIterations", SettingRange.AtLeast(1), s => s.Calc.OpenSeesMaxIterations, (s, v) => s.Calc.OpenSeesMaxIterations = v),
        Choice("openSees.geomTransf", Camel("Linear", "PDelta", "Corotational"), s => s.Calc.OpenSeesGeomTransfKind, (s, v) => s.Calc.OpenSeesGeomTransfKind = v),
        Choice("openSees.convergenceTest", Camel("EnergyIncr", "NormUnbalance", "NormDispIncr"),
            s => s.Calc.OpenSeesConvergenceTest, (s, v) => s.Calc.OpenSeesConvergenceTest = v),
        Choice("openSees.algorithm", Camel("Newton", "NewtonLineSearch"), s => s.Calc.OpenSeesAlgorithm, (s, v) => s.Calc.OpenSeesAlgorithm = v),
        Int("openSees.integrationPoints", SettingRange.AtLeast(1), s => s.Calc.OpenSeesIntegrationPoints, (s, v) => s.Calc.OpenSeesIntegrationPoints = v),
        Flag("openSees.recordFiberStates", s => s.Calc.OpenSeesRecordFiberStates, (s, v) => s.Calc.OpenSeesRecordFiberStates = v),
        new SettingField
        {
            Path = "openSees.fiberStatesIntegrationPoints", Kind = SettingKind.IntList,
            Get = s => s.Calc.ResolveOpenSeesFiberStatesIntegrationPoints() is { } set ? set.Order().ToList() : null,
            Set = (s, v) => s.Calc.OpenSeesFiberStatesIntegrationPoints =
                v is IReadOnlyList<int> list && list.Count > 0 ? string.Join(",", list) : null,
        },
        Flag("submodels.linearPrecheck", s => s.Calc.SubmodelLinearPrecheck, (s, v) => s.Calc.SubmodelLinearPrecheck = v),

        // Только окно: среда ПК, на результат не влияет.
        Int("gui.openSeesTimeoutSeconds", SettingRange.AtLeast(1), s => s.Calc.OpenSeesTimeoutSeconds, (s, v) => s.Calc.OpenSeesTimeoutSeconds = v, doc: false),
        Int("gui.gmshTimeoutSeconds", SettingRange.AtLeast(1), s => s.Gmsh.TimeoutSeconds, (s, v) => s.Gmsh.TimeoutSeconds = v, doc: false),
        Int("gui.csfeaThreads", SettingRange.NonNegative, s => Math.Max(0, s.Calc.CsfeaThreads), (s, v) => s.Calc.CsfeaThreads = v, doc: false),
    ];

    static readonly Dictionary<string, SettingField> ByPath = Fields.ToDictionary(f => f.Path, StringComparer.Ordinal);

    public static SettingField? Find(string path) => ByPath.GetValueOrDefault(path);

    public static SettingField Field(string path) => ByPath[path];

    /// <summary>Ошибки всех полей черновика: (путь, текст).</summary>
    public static IReadOnlyList<(string Path, string Error)> Validate(ProjectSettings draft) =>
        Fields.Select(f => (f.Path, Error: f.Check(f.Get(draft)))).Where(x => x.Error != null).Select(x => (x.Path, x.Error!)).ToList();

    /// <summary>Записывает черновик в базу; при ошибках ничего не пишет и возвращает их.</summary>
    public static IReadOnlyList<(string Path, string Error)> Save(DatabaseService db, ProjectSettings draft)
    {
        var errors = Validate(draft);
        if (errors.Count > 0) return errors;
        db.SaveCalcSettings(draft.Calc);
        db.SaveLiraImportSettings(draft.Lira);
        db.SaveAcadImportSettings(draft.Acad);
        db.SaveGmshSettings(draft.Gmsh);
        return [];
    }

    /// <summary>Меняются ли «авто»-коэффициенты комбинаций (вид величин или γf СП 20).</summary>
    public static bool LoadsDiffer(CalcSettings a, CalcSettings b) =>
        a.Sp20LoadValueKind != b.Sp20LoadValueKind
        || a.Sp20GammaFPermanent != b.Sp20GammaFPermanent || a.Sp20GammaFPermanentFav != b.Sp20GammaFPermanentFav
        || a.Sp20GammaFLongTerm != b.Sp20GammaFLongTerm || a.Sp20GammaFShortTerm != b.Sp20GammaFShortTerm
        || a.Sp20GammaFAccidental != b.Sp20GammaFAccidental;
}
