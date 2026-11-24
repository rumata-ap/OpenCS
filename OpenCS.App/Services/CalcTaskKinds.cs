using CScore;
using CScore.Fem;
using CScore.History;
using CScore.PrestressLoss;
using CScore.Sp16;
using CScore.Sp63.CrackWidth;
using CScore.Sp63.Deflection;
using CScore.Sp63.Normal;
using OpenCS.LimitSurface;
using OpenCS.Tasks;

namespace OpenCS.Services;

/// <summary>Какое сечение нужно виду задачи.</summary>
public enum CalcTaskSectionKind
{
    /// <summary>Стержневое сечение проекта (простое, параметрическое, стальное, стадийное).</summary>
    Cross,
    /// <summary>Только стадийное (двухстадийное) сечение.</summary>
    Staged,
    /// <summary>Сечение пластины (<c>plate_sections</c>).</summary>
    Plate,
    /// <summary>Огневое сечение: Id — в параметрах, <c>section_id</c> — его стержневое сечение.</summary>
    Fire,
    /// <summary>Сечения нет (продавливание).</summary>
    None,
}

/// <summary>Нужна ли задаче ссылка (набор, строка).</summary>
public enum CalcTaskRefNeed
{
    /// <summary>Ссылки быть не должно.</summary>
    None,
    /// <summary>Ссылка необязательна.</summary>
    Optional,
    /// <summary>Ссылка обязательна.</summary>
    Required,
}

/// <summary>Класс параметров вида: тип и нечувствительность имён полей к регистру при чтении.</summary>
public sealed record CalcTaskParamsType(Type Type, bool CaseInsensitive);

/// <summary>
/// Требования вида задачи: сечение, набор (и его вид), строка набора, допустимые типы расчёта, классы параметров.
/// Строка «необязательна» у видов, где её обязательность зависит от параметров (ручные усилия, конструктивная
/// гибкость стали, профиль МКЭ наклонных сечений) — это проверяет <see cref="CalcTaskAuthoring"/>.
/// </summary>
public sealed record CalcTaskKindSpec(
    string Kind,
    CalcTaskSectionKind Section,
    CalcTaskRefNeed Set,
    CalcTaskRefNeed Row,
    bool ShellSet,
    IReadOnlyList<CalcType> CalcTypes,
    IReadOnlyList<CalcTaskParamsType> ParamTypes,
    bool ParamsInOwnWindow = false)
{
    /// <summary>Пакетный вид (одна задача на весь набор).</summary>
    public bool IsBatch => Kind.EndsWith("_batch", StringComparison.Ordinal);
}

/// <summary>
/// Требования всех видов расчётных задач — одна таблица для диалога задачи, дерева, <c>opencs add tasks</c> и вида
/// <c>tasks</c> документа <c>opencs-model</c>. Новый вид добавляется сюда, в <see cref="TaskRunner"/> и в
/// группы дерева (<c>CalcTaskGroups</c>).
/// </summary>
public static class CalcTaskKinds
{
    static readonly CalcType[] All = [CalcType.C, CalcType.CL, CalcType.N, CalcType.NL];
    static readonly CalcType[] Short = [CalcType.C, CalcType.CL];
    static readonly CalcType[] Long = [CalcType.N, CalcType.NL];

    static CalcTaskParamsType P<T>(bool insensitive = false) => new(typeof(T), insensitive);

    static readonly CalcTaskParamsType LimitForce = P<LimitForceParams>(true);
    static readonly CalcTaskParamsType CrackWidth = P<CrackWidthTaskParams>();
    static readonly CalcTaskParamsType TotalCurvature = P<TotalCurvatureTaskParams>(true);
    static readonly CalcTaskParamsType ShellSimpl = P<ShellSimplParams>();
    static readonly CalcTaskParamsType ShellStrain = P<ShellStrainParams>();
    static readonly CalcTaskParamsType ShellLayeredSls = P<ShellLayeredSlsParams>();
    static readonly CalcTaskParamsType Steel = P<SteelDesignParams>();
    static readonly CalcTaskParamsType Torsion = P<TorsionParams>();
    static readonly CalcTaskParamsType Shear = P<ShearInclinedParams>();
    static readonly CalcTaskParamsType Punching = P<PunchingTaskParams>();
    static readonly CalcTaskParamsType Surface = P<LimitSurfaceTaskParams>();
    static readonly CalcTaskParamsType FireCheck = P<FireRCheckParams>();

    static readonly Dictionary<string, CalcTaskKindSpec> Specs = Build();

    static Dictionary<string, CalcTaskKindSpec> Build()
    {
        var d = new Dictionary<string, CalcTaskKindSpec>(StringComparer.Ordinal);
        void Add(string kind, CalcTaskSectionKind section, CalcTaskRefNeed set, CalcTaskRefNeed row,
            CalcType[] types, params CalcTaskParamsType[] p) =>
            d[kind] = new CalcTaskKindSpec(kind, section, set, row, kind.StartsWith("shell_", StringComparison.Ordinal), types, p);
        void Single(string kind, CalcType[] types, params CalcTaskParamsType[] p) =>
            Add(kind, CalcTaskSectionKind.Cross, CalcTaskRefNeed.Optional, CalcTaskRefNeed.Optional, types, p);
        void Batch(string kind, CalcType[] types, params CalcTaskParamsType[] p) =>
            Add(kind, CalcTaskSectionKind.Cross, CalcTaskRefNeed.Required, CalcTaskRefNeed.None, types, p);
        void ByRow(string kind, CalcType[] types, params CalcTaskParamsType[] p) =>
            Add(kind, CalcTaskSectionKind.Cross, CalcTaskRefNeed.Required, CalcTaskRefNeed.Required, types, p);

        // НДС, несущая способность (усилия — в параметрах или строкой набора)
        Single("strain_state", All, LimitForce);
        Batch("strain_state_batch", All, LimitForce);
        foreach (var k in new[] { "limit_force", "limit_moment", "limit_axial" })
        {
            Single(k, All, LimitForce);
            Batch(k + "_batch", All, LimitForce);
        }
        Batch("strength_ndm_batch", All, LimitForce);
        ByRow("rc_check", All, P<BarCheckParams>());
        Add("load_history", CalcTaskSectionKind.Staged, CalcTaskRefNeed.None, CalcTaskRefNeed.None, All, P<LoadHistoryParams>());

        // Трещины, кривизна
        Single("cracking", Long, LimitForce);
        Batch("cracking_batch", Long, LimitForce);
        Single("crack_width", Long, CrackWidth, LimitForce);
        Batch("crack_width_batch", Long, CrackWidth, LimitForce);
        Single("total_curvature", Long, TotalCurvature);
        Batch("total_curvature_batch", Long, TotalCurvature);
        Single("moment_curvature_biaxial", All, P<MomentCurvatureBiaxialTaskParams>(true));
        Add("prestress_loss", CalcTaskSectionKind.Cross, CalcTaskRefNeed.None, CalcTaskRefNeed.None, All, P<PrestressLossParams>());
        d["prestress_loss"] = d["prestress_loss"] with { ParamsInOwnWindow = true };

        // Поверхности (набор — облако точек, необязателен)
        Add("limit_surface", CalcTaskSectionKind.Cross, CalcTaskRefNeed.Optional, CalcTaskRefNeed.None, All, Surface);
        Add("crack_surface", CalcTaskSectionKind.Cross, CalcTaskRefNeed.Optional, CalcTaskRefNeed.None, Long, Surface);
        Add("acrc_surface", CalcTaskSectionKind.Cross, CalcTaskRefNeed.Optional, CalcTaskRefNeed.None, All, Surface, CrackWidth);

        // OpenSees
        ByRow("opensees_section_moment_curvature", All, P<OpenSeesSectionParams>(true));
        ByRow("opensees_section_interaction_nm", All, P<OpenSeesSectionInteractionParams>(true));
        Add("opensees_section_interaction_n_mx_my", CalcTaskSectionKind.Cross, CalcTaskRefNeed.Required, CalcTaskRefNeed.None,
            All, P<OpenSeesSpatialInteractionParams>(true));

        // СП 63, упрощённые проверки (строка обязательна без ручных усилий)
        Single("sp63_normal", All, P<Sp63NormalTaskParams>(true));
        Single("sp63_crack_width", All, P<Sp63CrackWidthTaskParams>(true));
        Single("sp63_deflection", Long, P<Sp63DeflectionTaskParams>(true));

        // Наклонные сечения, кручение
        Single("shear_inclined", All, Shear);
        Batch("shear_inclined_batch", All, Shear);
        Single("torsion_bem", All, Torsion);
        Single("torsion_fem", All, Torsion);

        // Сталь (строка обязательна без ручных усилий, кроме конструктивной гибкости)
        foreach (var k in new[] { "steel_check", "steel_central_compression", "steel_central_tension", "steel_bending",
                     "steel_compression_bending", "steel_tension_bending", "steel_shear", "steel_constructive" })
            Single(k, All, Steel);
        Batch("steel_check_batch", All, Steel);

        // Продавливание: без сечения, параметры — в своём окне
        Add("punching", CalcTaskSectionKind.None, CalcTaskRefNeed.Optional, CalcTaskRefNeed.Optional, All, Punching);
        Add("punching_batch", CalcTaskSectionKind.None, CalcTaskRefNeed.Required, CalcTaskRefNeed.None, All, Punching);
        d["punching"] = d["punching"] with { ParamsInOwnWindow = true };
        d["punching_batch"] = d["punching_batch"] with { ParamsInOwnWindow = true };

        // Огнестойкость
        Add("fire_r_check", CalcTaskSectionKind.Fire, CalcTaskRefNeed.Required, CalcTaskRefNeed.Required, All, FireCheck);
        Add("fire_r_check_batch", CalcTaskSectionKind.Fire, CalcTaskRefNeed.Required, CalcTaskRefNeed.None, All, FireCheck);
        Add("fire_r_time", CalcTaskSectionKind.Fire, CalcTaskRefNeed.Required, CalcTaskRefNeed.Required, All, FireCheck);
        Add("fire_thermal_curvature", CalcTaskSectionKind.Fire, CalcTaskRefNeed.None, CalcTaskRefNeed.None, All,
            P<FireThermalCurvatureParams>());

        // Пластины: одиночные — усилия в параметрах (набор и строка — источник предзаполнения), пакеты — набор shell
        void Plate(string kind, CalcType[] types, CalcTaskParamsType p)
        {
            Add(kind, CalcTaskSectionKind.Plate, CalcTaskRefNeed.Optional, CalcTaskRefNeed.Optional, types, p);
            Add(kind + "_batch", CalcTaskSectionKind.Plate, CalcTaskRefNeed.Required, CalcTaskRefNeed.None, types, p);
        }
        Plate("shell_strain_state", All, ShellStrain);
        Plate("shell_simpl_wa_uls", Short, ShellSimpl);
        Plate("shell_simpl_wa_sls", Long, ShellSimpl);
        Plate("shell_simpl_capri_uls", Short, ShellSimpl);
        Plate("shell_simpl_capri_sls", Long, ShellSimpl);
        Plate("shell_simpl_capri_design_uls", Short, ShellSimpl);
        Plate("shell_simpl_capri_design_sls", Long, ShellSimpl);
        Plate("shell_layered_uls", Short, ShellStrain);
        Plate("shell_layered_sls", Long, ShellLayeredSls);
        return d;
    }

    /// <summary>Требования вида или null, если вид неизвестен.</summary>
    public static CalcTaskKindSpec? Get(string? kind) => kind != null && Specs.TryGetValue(kind, out var s) ? s : null;

    /// <summary>Все известные виды (по алфавиту).</summary>
    public static IReadOnlyList<string> Names { get; } = Specs.Keys.Order(StringComparer.Ordinal).ToList();
}
