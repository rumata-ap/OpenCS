using System.Globalization;
using CScore.Sp16;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Вариант выпадающего списка: значение и локализованная подпись.</summary>
public sealed record EnumOption<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Редактор параметров проверки стального элемента по СП 16 (<see cref="SteelDesignParams"/>) — общий для
/// диалога расчётной задачи и редактора конструктивного элемента FEM. Числа вводятся строками (допускаются
/// запятая и точка), проверяются в <see cref="TryBuild"/>. Поля, которых нет в редакторе (профиль, ручные
/// усилия), сохраняются из загруженных параметров без изменений.
/// </summary>
public sealed class SteelDesignParamsEditorVM : ViewModelBase
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    SteelDesignParams _base = new();

    public SteelDesignParamsEditorVM() => Load(new SteelDesignParams());

    // ── Списки ──

    public IReadOnlyList<EnumOption<LtbLoadKind>> LtbLoadOptions { get; } = Options<LtbLoadKind>();
    public IReadOnlyList<EnumOption<LtbRestraints>> LtbRestraintsOptions { get; } = Options<LtbRestraints>();
    public IReadOnlyList<EnumOption<MomentShape>> MomentShapeOptions { get; } = Options<MomentShape>();
    public IReadOnlyList<EnumOption<CompressionMemberCategory>> CompressionCategoryOptions { get; } = Options<CompressionMemberCategory>();
    public IReadOnlyList<EnumOption<TensionMemberCategory>> TensionCategoryOptions { get; } = Options<TensionMemberCategory>();
    public IReadOnlyList<EnumOption<TensionLoadKind>> TensionLoadOptions { get; } = Options<TensionLoadKind>();

    /// <summary>Тип сечения по табл. 7: «по профилю» (null) или a/b/c.</summary>
    public IReadOnlyList<EnumOption<SectionCurve?>> CurveOptions { get; } =
    [
        new(null, Loc.S("Sp16CurveAuto")),
        new(SectionCurve.a, "a"), new(SectionCurve.b, "b"), new(SectionCurve.c, "c"),
    ];

    static IReadOnlyList<EnumOption<T>> Options<T>() where T : struct, Enum =>
        Enum.GetValues<T>().Select(v => new EnumOption<T>(v, Loc.S($"Sp16Enum_{typeof(T).Name}_{v}"))).ToList();

    static EnumOption<T> Pick<T>(IReadOnlyList<EnumOption<T>> list, T value) =>
        list.FirstOrDefault(o => EqualityComparer<T>.Default.Equals(o.Value, value)) ?? list[0];

    // ── Общие ──

    string _gammaC = "", _lefX = "", _lefY = "", _netAreaRatio = "";
    bool _tensionYieldAllowed, _useGammaRes, _dynamicLoad;

    public string GammaC { get => _gammaC; set { _gammaC = value; OnPropertyChanged(); } }
    public string LefX { get => _lefX; set { _lefX = value; OnPropertyChanged(); } }
    public string LefY { get => _lefY; set { _lefY = value; OnPropertyChanged(); } }
    public string NetAreaRatio { get => _netAreaRatio; set { _netAreaRatio = value; OnPropertyChanged(); } }
    public bool TensionYieldAllowed { get => _tensionYieldAllowed; set { _tensionYieldAllowed = value; OnPropertyChanged(); } }
    public bool UseGammaRes { get => _useGammaRes; set { _useGammaRes = value; OnPropertyChanged(); } }
    public bool DynamicLoad { get => _dynamicLoad; set { _dynamicLoad = value; OnPropertyChanged(); } }

    // ── Устойчивость плоской формы изгиба (8.4, прил. Ж) ──

    string _lefB = "";
    EnumOption<LtbLoadKind> _ltbLoad = null!;
    EnumOption<LtbRestraints> _ltbRestraints = null!;
    bool _ltbLoadOnTensionFlange, _ltbFixedEnds, _cantilever, _continuousRigidDeck;

    public string LefB { get => _lefB; set { _lefB = value; OnPropertyChanged(); } }
    public EnumOption<LtbLoadKind> LtbLoad { get => _ltbLoad; set { _ltbLoad = value; OnPropertyChanged(); } }
    public EnumOption<LtbRestraints> LtbRestraints { get => _ltbRestraints; set { _ltbRestraints = value; OnPropertyChanged(); } }
    public bool LtbLoadOnTensionFlange { get => _ltbLoadOnTensionFlange; set { _ltbLoadOnTensionFlange = value; OnPropertyChanged(); } }
    public bool LtbFixedEnds { get => _ltbFixedEnds; set { _ltbFixedEnds = value; OnPropertyChanged(); } }
    public bool Cantilever { get => _cantilever; set { _cantilever = value; OnPropertyChanged(); } }
    public bool ContinuousRigidDeck { get => _continuousRigidDeck; set { _continuousRigidDeck = value; OnPropertyChanged(); } }

    // ── Пластические деформации (8.2.3, 9.1.1) ──

    bool _allowPlastic, _pureBendingZone;
    string _gammaFEq = "";

    public bool AllowPlastic { get => _allowPlastic; set { _allowPlastic = value; OnPropertyChanged(); } }
    public string GammaFEq { get => _gammaFEq; set { _gammaFEq = value; OnPropertyChanged(); } }
    public bool PureBendingZone { get => _pureBendingZone; set { _pureBendingZone = value; OnPropertyChanged(); } }

    // ── Местная нагрузка и стенка (8.2.2, 8.5) ──

    string _localForce = "", _bearingLength = "", _flangeWeldLeg = "", _ribSpacing = "";
    bool _oneSidedFlangeWelds, _frictionFlangeJoints;

    public string LocalForce { get => _localForce; set { _localForce = value; OnPropertyChanged(); } }
    public string BearingLength { get => _bearingLength; set { _bearingLength = value; OnPropertyChanged(); } }
    public string FlangeWeldLeg { get => _flangeWeldLeg; set { _flangeWeldLeg = value; OnPropertyChanged(); } }
    public string RibSpacing { get => _ribSpacing; set { _ribSpacing = value; OnPropertyChanged(); } }
    public bool OneSidedFlangeWelds { get => _oneSidedFlangeWelds; set { _oneSidedFlangeWelds = value; OnPropertyChanged(); } }
    public bool FrictionFlangeJoints { get => _frictionFlangeJoints; set { _frictionFlangeJoints = value; OnPropertyChanged(); } }

    // ── Сжатие с изгибом (9.2) ──

    EnumOption<MomentShape> _momentShape = null!;
    string _endMomentRatio = "", _middleThirdMomentRatio = "";
    bool _cantileverColumn, _useFormula121a;

    public EnumOption<MomentShape> MomentShape
    {
        get => _momentShape;
        set { _momentShape = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowEndMomentRatio)); OnPropertyChanged(nameof(ShowMiddleThirdRatio)); }
    }
    public bool ShowEndMomentRatio => _momentShape?.Value == CScore.Sp16.MomentShape.LinearEndMoments;
    public bool ShowMiddleThirdRatio => _momentShape?.Value == CScore.Sp16.MomentShape.PinnedTransverse;
    public string EndMomentRatio { get => _endMomentRatio; set { _endMomentRatio = value; OnPropertyChanged(); } }
    public string MiddleThirdMomentRatio { get => _middleThirdMomentRatio; set { _middleThirdMomentRatio = value; OnPropertyChanged(); } }
    public bool CantileverColumn { get => _cantileverColumn; set { _cantileverColumn = value; OnPropertyChanged(); } }
    public bool UseFormula121a { get => _useFormula121a; set { _useFormula121a = value; OnPropertyChanged(); } }

    // ── Предельная гибкость (10.4) ──

    EnumOption<CompressionMemberCategory> _compressionCategory = null!;
    EnumOption<TensionMemberCategory> _tensionCategory = null!;
    EnumOption<TensionLoadKind> _tensionLoad = null!;
    bool _group4, _slendernessGovernsSection;

    public EnumOption<CompressionMemberCategory> CompressionCategory { get => _compressionCategory; set { _compressionCategory = value; OnPropertyChanged(); } }
    public EnumOption<TensionMemberCategory> TensionCategory { get => _tensionCategory; set { _tensionCategory = value; OnPropertyChanged(); } }
    public EnumOption<TensionLoadKind> TensionLoad { get => _tensionLoad; set { _tensionLoad = value; OnPropertyChanged(); } }
    public bool Group4 { get => _group4; set { _group4 = value; OnPropertyChanged(); } }
    public bool SlendernessGovernsSection { get => _slendernessGovernsSection; set { _slendernessGovernsSection = value; OnPropertyChanged(); } }

    // ── Переопределения ──

    EnumOption<SectionCurve?> _curveX = null!, _curveY = null!;
    string _etaOverride = "";

    public EnumOption<SectionCurve?> CurveX { get => _curveX; set { _curveX = value; OnPropertyChanged(); } }
    public EnumOption<SectionCurve?> CurveY { get => _curveY; set { _curveY = value; OnPropertyChanged(); } }
    /// <summary>η по табл. Д.2; пусто — по профилю.</summary>
    public string EtaOverride { get => _etaOverride; set { _etaOverride = value; OnPropertyChanged(); } }

    // ── Загрузка / сборка ──

    static string F(double v) => v.ToString("G6", Inv);

    /// <summary>Заполняет редактор из параметров (устаревший формат уже приведён <see cref="SteelDesignParams.Parse"/>).</summary>
    public void Load(SteelDesignParams p)
    {
        _base = p;
        GammaC = F(p.GammaC); LefX = F(p.LefX); LefY = F(p.LefY); NetAreaRatio = F(p.NetAreaRatio);
        TensionYieldAllowed = p.TensionYieldAllowed; UseGammaRes = p.UseGammaRes; DynamicLoad = p.DynamicLoad;
        LefB = F(p.LefB); LtbLoad = Pick(LtbLoadOptions, p.LtbLoad); LtbRestraints = Pick(LtbRestraintsOptions, p.LtbRestraints);
        LtbLoadOnTensionFlange = p.LtbLoadOnTensionFlange; LtbFixedEnds = p.LtbFixedEnds;
        Cantilever = p.Cantilever; ContinuousRigidDeck = p.ContinuousRigidDeck;
        AllowPlastic = p.AllowPlastic; GammaFEq = F(p.GammaFEq); PureBendingZone = p.PureBendingZone;
        LocalForce = F(p.LocalForce); BearingLength = F(p.BearingLength); FlangeWeldLeg = F(p.FlangeWeldLeg);
        RibSpacing = F(p.RibSpacing); OneSidedFlangeWelds = p.OneSidedFlangeWelds; FrictionFlangeJoints = p.FrictionFlangeJoints;
        MomentShape = Pick(MomentShapeOptions, p.MomentShape); EndMomentRatio = F(p.EndMomentRatio);
        MiddleThirdMomentRatio = F(p.MiddleThirdMomentRatio); CantileverColumn = p.CantileverColumn; UseFormula121a = p.UseFormula121a;
        CompressionCategory = Pick(CompressionCategoryOptions, p.CompressionCategory);
        TensionCategory = Pick(TensionCategoryOptions, p.TensionCategory); TensionLoad = Pick(TensionLoadOptions, p.TensionLoad);
        Group4 = p.Group4; SlendernessGovernsSection = p.SlendernessGovernsSection;
        CurveX = Pick(CurveOptions, p.CurveX); CurveY = Pick(CurveOptions, p.CurveY);
        EtaOverride = p.EtaOverride is { } eta ? F(eta) : "";
    }

    /// <summary>Число из строки (запятая или точка); null — не распознано.</summary>
    public static double? ParseNumber(string? s) =>
        double.TryParse((s ?? "").Trim().Replace(',', '.'), NumberStyles.Float, Inv, out var v) && double.IsFinite(v) ? v : null;

    /// <summary>
    /// Собирает параметры; при ошибке — false и локализованное сообщение с названием поля.
    /// </summary>
    public bool TryBuild(out SteelDesignParams result, out string error)
    {
        result = _base;
        var errors = new List<string>();
        double Num(string text, string labelKey, Func<double, bool> valid)
        {
            if (ParseNumber(text) is { } v && valid(v)) return v;
            errors.Add(Loc.S(labelKey));
            return 0;
        }
        static bool Pos(double v) => v > 0;
        static bool NonNeg(double v) => v >= 0;

        double gammaC = Num(GammaC, "Sp16GammaC", Pos);
        double lefX = Num(LefX, "Sp16LefX", Pos);
        double lefY = Num(LefY, "Sp16LefY", Pos);
        double net = Num(NetAreaRatio, "Sp16NetAreaRatio", v => v > 0 && v <= 1);
        double lefB = Num(LefB, "Sp16LefB", NonNeg);
        double gammaF = Num(GammaFEq, "Sp16GammaFEq", Pos);
        double force = Num(LocalForce, "Sp16LocalForce", NonNeg);
        double bearing = Num(BearingLength, "Sp16BearingLength", NonNeg);
        double weld = Num(FlangeWeldLeg, "Sp16FlangeWeldLeg", NonNeg);
        double ribs = Num(RibSpacing, "Sp16RibSpacing", NonNeg);
        double delta = Num(EndMomentRatio, "Sp16EndMomentRatio", v => v >= -1 && v <= 1);
        double m1 = Num(MiddleThirdMomentRatio, "Sp16MiddleThirdMomentRatio", v => v > 0 && v <= 1);
        double? eta = null;
        if (!string.IsNullOrWhiteSpace(EtaOverride)) eta = Num(EtaOverride, "Sp16EtaOverride", Pos);

        if (errors.Count > 0)
        {
            error = string.Format(Loc.S("Sp16EditorInvalidFields"), string.Join("; ", errors));
            return false;
        }
        error = "";
        result = _base with
        {
            GammaC = gammaC, LefX = lefX, LefY = lefY, NetAreaRatio = net,
            TensionYieldAllowed = TensionYieldAllowed, UseGammaRes = UseGammaRes, DynamicLoad = DynamicLoad,
            LefB = lefB, LtbLoad = LtbLoad.Value, LtbRestraints = LtbRestraints.Value,
            LtbLoadOnTensionFlange = LtbLoadOnTensionFlange, LtbFixedEnds = LtbFixedEnds,
            Cantilever = Cantilever, ContinuousRigidDeck = ContinuousRigidDeck,
            AllowPlastic = AllowPlastic, GammaFEq = gammaF, PureBendingZone = PureBendingZone,
            LocalForce = force, BearingLength = bearing, FlangeWeldLeg = weld, RibSpacing = ribs,
            OneSidedFlangeWelds = OneSidedFlangeWelds, FrictionFlangeJoints = FrictionFlangeJoints,
            MomentShape = MomentShape.Value, EndMomentRatio = delta, MiddleThirdMomentRatio = m1,
            CantileverColumn = CantileverColumn, UseFormula121a = UseFormula121a,
            CompressionCategory = CompressionCategory.Value, TensionCategory = TensionCategory.Value,
            TensionLoad = TensionLoad.Value, Group4 = Group4, SlendernessGovernsSection = SlendernessGovernsSection,
            CurveX = CurveX.Value, CurveY = CurveY.Value, EtaOverride = eta,
        };
        return true;
    }
}
