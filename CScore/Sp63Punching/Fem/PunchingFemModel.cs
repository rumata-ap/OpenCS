using CScore.Fem;
using CScore.Planar;

namespace CScore.Sp63Punching.Fem;

/// <summary>Привязки центров арматуры к грани плиты, м: вдоль X и вдоль Y осей узла.</summary>
public sealed record PunchingFemCover(double Ax, double Ay);

/// <summary>
/// Свойства пластины для продавливания.
/// </summary>
/// <param name="SectionKey">Ключ сечения: у КЭ с одинаковым ключом одна толщина, бетон и армирование.</param>
/// <param name="H">Толщина, м.</param>
/// <param name="ConcreteMaterialId">Бетон проекта; 0 — неизвестен (берётся бетон проверки).</param>
/// <param name="Top">Привязки верхней арматуры к верхней грани; null — неизвестны.</param>
/// <param name="Bottom">Привязки нижней арматуры к нижней грани; null — неизвестны.</param>
/// <param name="Label">Подпись сечения для сообщений.</param>
public sealed record PunchingFemShellProps(
    string SectionKey, double H, int ConcreteMaterialId, PunchingFemCover? Top, PunchingFemCover? Bottom, string Label = "");

/// <summary>Контур бетонного сечения стержня в его осях (Y, Z), м, от оси стержня; <see cref="Label"/> — подпись сечения.</summary>
public sealed record PunchingFemBarSection(IReadOnlyList<(double Y, double Z)> Outline, string Label = "");

/// <summary>
/// Сетка расчётной схемы для поиска узлов продавливания: узлы и КЭ сетки, тела АЖТ, свойства пластин и сечения
/// стержней. Единицы — м. Не зависит от источника (CSfea, ЛИРА-САПР, SCAD).
/// </summary>
public sealed class PunchingFemModel
{
    /// <summary>Узлы сетки по тегу.</summary>
    public required IReadOnlyDictionary<string, FemMeshNode> Nodes { get; init; }
    /// <summary>КЭ сетки (стержни и пластины).</summary>
    public required IReadOnlyList<FemElement> Elements { get; init; }
    /// <summary>Жёсткие тела и объединения перемещений на узлах сетки.</summary>
    public IReadOnlyList<FemRigidBody> RigidBodies { get; init; } = [];
    /// <summary>Свойства пластинчатого КЭ; null — неизвестны.</summary>
    public Func<FemElement, PunchingFemShellProps?> ShellProps { get; init; } = _ => null;
    /// <summary>Сечение стержневого КЭ; null — неизвестно.</summary>
    public Func<FemElement, PunchingFemBarSection?> BarSection { get; init; } = _ => null;
    /// <summary>Поворот сечения стержня, град (<see cref="BeamLocalAxisConvention"/>).</summary>
    public Func<FemElement, double> BarRotationDeg { get; init; } = e => e.BeamRotationDeg ?? 0;
    /// <summary>Узлы с закреплениями или пружинами (для сообщений об опорах в основании пирамиды).</summary>
    public IReadOnlySet<string> SupportedNodes { get; init; } = new HashSet<string>(StringComparer.Ordinal);
}

/// <summary>Настройки поиска узлов продавливания.</summary>
/// <param name="AngleDeg">Наименьший угол оси стержня к плоскости плиты, град: меньше — стержень лежит в плите (балка).</param>
/// <param name="HorizontalTolDeg">Допуск отклонения нормали плиты от вертикали, град; больше — плита наклонная.</param>
/// <param name="PlaneTolM">Допуск попадания узла в плоскость плиты, м.</param>
/// <param name="EdgeInfluence">Края плиты ближе этого числа h0 к грузовой площади учитываются.</param>
/// <param name="HoleInfluence">Отверстия ближе этого числа h (полная толщина плиты) учитываются (п. 8.1.46: 6h).</param>
/// <param name="WallRatio">Площадь с Lmax &gt; WallRatio·Lmin — стена, а не пилон (срез 5).</param>
/// <param name="DefaultCover">Привязки, если у сечения пластины их нет.</param>
/// <param name="WallFigure">Редакция рис. 6.2 СП 430 для углов стен.</param>
public sealed record PunchingFemOptions(
    double AngleDeg = 10, double HorizontalTolDeg = 3, double PlaneTolM = 1e-3,
    double EdgeInfluence = 6, double HoleInfluence = 6, double WallRatio = 4,
    PunchingFemCover? DefaultCover = null, PunchingWallFigure WallFigure = PunchingWallFigure.Sp430Amd1)
{
    /// <summary>Привязки по умолчанию, м (как у одиночной задачи).</summary>
    public static readonly PunchingFemCover StandardCover = new(0.035, 0.045);
}

/// <summary>Линия стены у плиты (ось примыкания) для плана перекрытия.</summary>
/// <param name="SlabIndex">Плита, к которой примыкает стена.</param>
/// <param name="A">Начало линии в общих осях, м.</param>
/// <param name="B">Конец линии, м.</param>
/// <param name="T">Толщина стены, м.</param>
public sealed record PunchingFemWallLine(int SlabIndex, PlanarVector3 A, PlanarVector3 B, double T);

/// <summary>Вид узла продавливания.</summary>
public enum PunchingFemJointKind
{
    /// <summary>Колонна или пилон — стержень.</summary>
    Bar,
    /// <summary>Пилон из пластин: вертикальные пластины примыкают к плите по короткой линии.</summary>
    PylonShells,
    /// <summary>Торец стены (СП 430 п. 6.3.8, рис. 6.1): свободный конец стены из пластин или торец стержня с b/a &gt; 4.</summary>
    WallEnd,
    /// <summary>Угол двух стен под 90° (рис. 6.2).</summary>
    WallCorner,
}

/// <summary>Стержень, примыкающий к плите в узле: КЭ, конец у плиты и сторона.</summary>
/// <param name="ElemTag">КЭ стержня.</param>
/// <param name="NodeTag">Узел конца стержня у плиты.</param>
/// <param name="Below">Стержень под плитой (по нормали плиты).</param>
public sealed record PunchingFemBar(string ElemTag, string NodeTag, bool Below);

/// <summary>Коды «нет проверки» узла.</summary>
public static class PunchingFemNoCheck
{
    public const string AreaUnknown = "area_unknown";
    public const string SlabInclined = "slab_inclined";
    public const string SlabSectionUnknown = "slab_section_unknown";
    public const string SlabSectionMixed = "slab_section_mixed";
    public const string H0NonPositive = "h0_nonpositive";
    public const string JunctionWallAndColumn = "junction_wall_and_column";
    public const string SupportInsideBase = "support_inside_base";
    public const string Wall = "wall_not_pylon";
    public const string BaseOutside = "base_outside_slab";
    public const string BeamAtJoint = "beam_at_joint";
    public const string NoConsistentForces = "no_consistent_forces";
    public const string NodalSelfCheck = "nodal_selfcheck_failed";
    public const string NoForces = "no_forces";
    public const string PylonShellsNoNodal = "pylon_shells_no_nodal";
    /// <summary>Т- или крестовое примыкание стен — по п. 6.3.8 СП 430 не рассматривается.</summary>
    public const string WallTee = "wall_tee";
    /// <summary>Стены сходятся не под прямым углом.</summary>
    public const string WallAngle = "wall_angle";
    /// <summary>В узле стены разной толщины снизу и сверху (или по длине у узла).</summary>
    public const string WallThicknessMixed = "wall_thickness_mixed";
    /// <summary>Силу у торца или угла стены даёт только разрез плиты, а он недоступен.</summary>
    public const string WallNoCut = "wall_no_cut";
}

/// <summary>
/// Узел «колонна — плита», найденный в сетке: оси узла, стержни, геометрия в осях узла (грузовая площадь, края,
/// отверстия, основание пирамиды), толщина и привязки. <see cref="NoCheck"/> ≠ null — узел не проверяется (причина).
/// Оси узла: начало — центр тяжести грузовой площади в плоскости плиты, x — проекция местной оси Y стержня на плиту,
/// z — нормаль плиты вверх, y = z × x.
/// </summary>
public sealed record PunchingFemJoint
{
    /// <summary>Узел сетки, где стержни входят в плиту (ведущий узел АЖТ или общий узел стержня и плиты).</summary>
    public required string NodeTag { get; init; }
    /// <summary>Номер плиты (компоненты пластин одной плоскости) в схеме.</summary>
    public int SlabIndex { get; init; }
    public required PlanarVector3 Origin { get; init; }
    public required PlanarVector3 AxisX { get; init; }
    public required PlanarVector3 AxisY { get; init; }
    public required PlanarVector3 Normal { get; init; }
    public IReadOnlyList<PunchingFemBar> Bars { get; init; } = [];
    public PunchingFemJointKind Kind { get; init; } = PunchingFemJointKind.Bar;
    /// <summary>Пластины пилона или стены, примыкающие к плите (у <see cref="PunchingFemJointKind.PylonShells"/>, торцов и углов стен).</summary>
    public IReadOnlyList<string> WallShells { get; init; } = [];
    /// <summary>Узел стены (торец или угол) в осях узла: начало в центре торца / наружном углу; null — колонна или пилон.</summary>
    public PunchingWall? Wall { get; init; }
    /// <summary>Узел стены.</summary>
    public bool IsWall => Wall is not null;
    /// <summary>Подпись сечения стержня, по которому взята грузовая площадь.</summary>
    public string SectionLabel { get; init; } = "";
    public PunchingLoadArea? Area { get; init; }
    public double H { get; init; }
    public int ConcreteMaterialId { get; init; }
    public double Ax { get; init; }
    public double Ay { get; init; }
    /// <summary>Привязки взяты по умолчанию (у сечения пластины их нет).</summary>
    public bool CoverDefault { get; init; }
    /// <summary>Подпись сечения плиты.</summary>
    public string SlabLabel { get; init; } = "";
    public IReadOnlyList<PunchingSlabEdge> Edges { get; init; } = [];
    public IReadOnlyList<PunchingHole> Holes { get; init; } = [];
    /// <summary>Основание пирамиды продавливания в осях узла (против часовой).</summary>
    public IReadOnlyList<PlanPoint> PyramidBase { get; init; } = [];
    /// <summary>КЭ плиты, у которых хотя бы один узел в основании пирамиды или на его границе (с соседями).</summary>
    public IReadOnlyList<string> BaseShells { get; init; } = [];
    /// <summary>Ведомые узлы АЖТ узла (вместе с ведущим — узлы, через которые стержни передают силу на плиту).</summary>
    public IReadOnlyList<string> JunctionNodes { get; init; } = [];
    /// <summary>В основании пирамиды есть стержни в плоскости плиты (балки): сила по стержню колонны завышена.</summary>
    public bool Ribs { get; init; }
    public string? NoCheck { get; init; }
    public string? NoCheckText { get; init; }
    public IReadOnlyList<PunchingWarning> Warnings { get; init; } = [];

    /// <summary>h0 = h − 0,5(ax + ay), м.</summary>
    public double H0 => H - 0.5 * (Ax + Ay);

    /// <summary>Точка осей узла в общих осях.</summary>
    public PlanarVector3 ToGlobal(PlanPoint p) => Origin + AxisX * p.X + AxisY * p.Y;

    /// <summary>Проекция точки на плоскость осей узла.</summary>
    public PlanPoint ToLocal(PlanarVector3 p)
    {
        var d = p - Origin;
        return new PlanPoint(d.Dot(AxisX), d.Dot(AxisY));
    }

    /// <summary>Вектор общих осей в осях узла (x, y, z).</summary>
    public (double X, double Y, double Z) LocalVector(PlanarVector3 v) => (v.Dot(AxisX), v.Dot(AxisY), v.Dot(Normal));
}
