using CScore;
using CSfea.Core;

namespace CSfea.CScoreBridge.Structural;

/// <summary>Узел расчётной схемы (координаты — м).</summary>
public sealed record RcNode(int Id, double X, double Y, double Z);

/// <summary>
/// Сечение пластины/оболочки. Задаётся одним из способов: упругий ламинат <see cref="Elastic"/> (СИ) или слоистое
/// ЖБ-сечение CScore <see cref="Plate"/> с диаграммами <see cref="PlateMaterials"/>. <see cref="Key"/> — ключ
/// одинаковых сечений (для дедупликации в фабриках).
/// </summary>
public sealed record RcShellSection(string Key)
{
    /// <summary>Упругий ламинат в осях сечения (Па, м).</summary>
    public Laminate? Elastic { get; init; }

    /// <summary>Слоистое ЖБ-сечение CScore (единицы CScore).</summary>
    public PlateSection? Plate { get; init; }

    /// <summary>Диаграммы и упругие параметры для <see cref="Plate"/>.</summary>
    public PlateSectionMaterials? PlateMaterials { get; init; }
}

/// <summary>
/// Оболочечный КЭ: узлы контура по обходу (3 или 4), сечение и ось x сечения <see cref="SectionAxisX"/> — вектор в
/// глобальных осях (проецируется на плоскость КЭ; null — ось x КЭ CSfea, ребро узел 0 → узел 1).
/// <see cref="FoundationC1"/> — коэффициент постели упругого основания (Винклер), Н/м³, вдоль нормали КЭ; null или 0 —
/// основания нет. Связь двусторонняя (отрыв не моделируется), одинаковая во всех вариантах расчёта.
/// </summary>
public sealed record RcShell(int Id, int[] NodeIds, RcShellSection Section, double[]? SectionAxisX = null,
    double? FoundationC1 = null);

/// <summary>
/// Сечение стержня: упругое <see cref="Elastic"/> (Па, м; Iy — относительно локальной оси y) или сечение CScore
/// <see cref="Cross"/> (подготовленное, с диаграммами) с видом расчёта <see cref="Calc"/>.
/// </summary>
public sealed record RcBeamSection(string Key)
{
    public BeamSection? Elastic { get; init; }
    public CrossSection? Cross { get; init; }
    public CalcType Calc { get; init; } = CalcType.N;

    /// <summary>Крутильная жёсткость GJ (Н·м²) для сечения CScore.</summary>
    public double TorsionGJ { get; init; }
}

/// <summary>
/// Стержневой КЭ: узлы концов, сечение и <see cref="RefVec"/> — направление локальной оси y (конвенция
/// <see cref="BeamElements.Beam3dFrame"/>; null — по умолчанию CSfea). <see cref="ReleaseI"/>, <see cref="ReleaseJ"/> —
/// шарниры концов: маски освобождённых усилий в местных осях (биты 0–5: N, Qy, Qz, T, My, Mz — порядок SCAD X…UZ).
/// </summary>
public sealed record RcBeam(int Id, int NodeI, int NodeJ, RcBeamSection Section, double[]? RefVec = null,
    int ReleaseI = 0, int ReleaseJ = 0);

/// <summary>Жёсткое тело: ведущий узел, ведомые, маска подчинённых DOF (бит i — DOF i).</summary>
public sealed record RcRigidBody(int Id, int Master, IReadOnlyList<int> Slaves, int Mask = RigidLink.All);

/// <summary>Закрепление узла: маска DOF (бит i — DOF i: ux, uy, uz, θx, θy, θz).</summary>
public sealed record RcSupport(int NodeId, int Mask);

/// <summary>Линейная упругая опора узла по глобальному DOF (Н/м или Н·м/рад).</summary>
public sealed record RcSpring(int NodeId, int Dof, double Stiffness);

/// <summary>Узловая нагрузка (Н, Н·м): Fx, Fy, Fz, Mx, My, Mz в глобальных осях.</summary>
public sealed record RcNodalLoad(int NodeId, double[] Force);

/// <summary>
/// Равномерная нагрузка на оболочку, Па: вдоль единичного глобального направления <see cref="Direction"/>
/// или, если null, вдоль нормали КЭ (по обходу контура).
/// </summary>
public sealed record RcShellLoad(int ShellId, double Pressure, double[]? Direction = null);

/// <summary>Равномерная погонная нагрузка на стержень (Н/м) в глобальных осях.</summary>
public sealed record RcBeamLoad(int BeamId, double[] Force);

/// <summary>
/// Пролётная нагрузка стержня, заданная согласованными узловыми силами КЭ без шарниров (Н, Н·м; глобальные оси):
/// 12 чисел — Fx, Fy, Fz, Mx, My, Mz конца I, затем конца J. У стержня с шарнирами силы конденсируются к сохранённым
/// DOF (<see cref="BeamReleases.CondenseLoad"/>), поэтому любую нагрузку на такой КЭ задают здесь, а не узловыми силами.
/// </summary>
public sealed record RcBeamEndLoad(int BeamId, double[] Forces);

/// <summary>Загружение.</summary>
public sealed record RcLoadCase(int Id, string Name)
{
    public List<RcNodalLoad> Nodal { get; } = new();
    public List<RcShellLoad> Shells { get; } = new();
    public List<RcBeamLoad> Beams { get; } = new();
    public List<RcBeamEndLoad> BeamEnds { get; } = new();
}

/// <summary>
/// Стадия истории нагружения: <see cref="Loads"/> — приращение нагрузки (сумма загружений с коэффициентами), которое
/// стадия добавляет к нагрузке конца предыдущей стадии; приращение дробится на <see cref="Steps"/> шагов.
/// </summary>
public sealed record RcStage(string Name, IReadOnlyList<(int LoadCase, double Factor)> Loads, int Steps = 1);

/// <summary>
/// Нейтральная модель ЖБ оболочечно-стержневой схемы для расчётов CSfea (СИ: Н, м, Па). Никакой логики
/// источника (SCAD, ЛИРА, схема OpenCS) — источники переводятся в неё адаптерами.
/// </summary>
public sealed class RcStructuralModel
{
    public List<RcNode> Nodes { get; } = new();
    public List<RcShell> Shells { get; } = new();
    public List<RcBeam> Beams { get; } = new();
    public List<RcRigidBody> RigidBodies { get; } = new();
    public List<RcSupport> Supports { get; } = new();
    public List<RcSpring> Springs { get; } = new();
    public List<RcLoadCase> LoadCases { get; } = new();
    public List<RcStage> Stages { get; } = new();
}
