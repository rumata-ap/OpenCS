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

    /// <summary>
    /// Оси сечения CScore в местных осях стержня: false — X сечения по местной y, Y — по z (сечения импортных схем:
    /// x ‖ Y1, y ‖ Z1); true — Y сечения по местной y, X — по z (свои схемы: как фибры OpenSees и 3D-вид сечения).
    /// </summary>
    public bool CrossYAlongLocalY { get; init; }

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

/// <summary>
/// Жёсткое тело: ведущий узел, ведомые, маска подчинённых DOF (бит i — DOF i). <paramref name="EqualDofs"/> — объединение
/// перемещений: DOF маски ведомых равны DOF ведущего, без поворотного члена (см. <see cref="RigidLink"/>).
/// </summary>
public sealed record RcRigidBody(int Id, int Master, IReadOnlyList<int> Slaves, int Mask = RigidLink.All, bool EqualDofs = false);

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

/// <summary>
/// Заданное перемещение узла в загружении (м или рад) по глобальному DOF 0…5 (ux, uy, uz, θx, θy, θz). DOF с заданным
/// значением хотя бы в одном загружении закрепляется на весь расчёт; у загружений без значения на нём — 0.
/// </summary>
public sealed record RcPrescribed(int NodeId, int Dof, double Value);

/// <summary>Загружение.</summary>
public sealed record RcLoadCase(int Id, string Name)
{
    public List<RcNodalLoad> Nodal { get; } = new();
    public List<RcShellLoad> Shells { get; } = new();
    public List<RcBeamLoad> Beams { get; } = new();
    public List<RcBeamEndLoad> BeamEnds { get; } = new();
    public List<RcPrescribed> Prescribed { get; } = new();
}

/// <summary>
/// Стадия истории нагружения: <see cref="Loads"/> — приращение нагрузки (сумма загружений с коэффициентами), которое
/// стадия добавляет к нагрузке конца предыдущей стадии; приращение дробится на <see cref="Steps"/> шагов.
/// </summary>
public sealed record RcStage(string Name, IReadOnlyList<(int LoadCase, double Factor)> Loads, int Steps = 1);

/// <summary>
/// Начальное несовершенство схемы для расчёта по теории второго порядка (EN 1993-1-1 п. 5.3): поле начальных
/// перемещений u₀ без напряжений, учитываемое правой частью −K_σ(N)·u₀ (<see cref="ImperfectionField"/>).
/// </summary>
public abstract record RcImperfection;

/// <summary>
/// Крен: u₀ = φ·(z − z₀)·d в узлах КЭ охвата; d — единичный горизонтальный вектор <paramref name="Direction"/> (глобальные
/// оси), повороты стержней — φ·(t·e_z)·(t × d). <paramref name="Beams"/>, <paramref name="Shells"/> — номера КЭ модели
/// (null — все). z₀ на результат не влияет (поступательное смещение не даёт сил) — только для показа u₀.
/// </summary>
public sealed record RcSway(double Phi, double[] Direction, double Z0 = 0.0, IReadOnlySet<int>? Beams = null,
    IReadOnlySet<int>? Shells = null) : RcImperfection;

/// <summary>
/// Погибь-парабола цепочки стержней (номера модели, соосная цепочка; концы — узлы, входящие в один КЭ цепочки):
/// w₀(s) = 4e₀·s(L − s)/L², s — проекция узла на хорду, L — длина хорды; стрелка <paramref name="E0"/> (м, со знаком) вдоль
/// <paramref name="Direction"/> (глобальный вектор, ортогонализуется к хорде — например, местная y или z первого стержня).
/// Узлу КЭ — перемещение w₀·d и поворот w₀′·(t × d); кубика Эрмита воспроизводит параболу точно.
/// </summary>
public sealed record RcBow(IReadOnlyList<int> Beams, double E0, double[] Direction) : RcImperfection;

/// <summary>
/// Несовершенство по формам потери устойчивости (EN 1993-1-1 п. 5.3.2(11)): u₀ = Σ aᵢ·φᵢ, уже просуммированное.
/// <paramref name="Nodes"/> — номер узла модели → 6 чисел (ux, uy, uz, м; θx, θy, θz, рад; глобальные оси);
/// <paramref name="BarPoints"/> — номер стержня модели → поступательные u₀ промежуточных равноотстоящих точек дробления
/// формы по порядку от I к J (по 3 числа); нет записи — форма на КЭ кубика по концам. Узлы без записи — u₀ = 0.
/// Стержню — u₀ концов из узлов и внутренний член по сплайну через точки (<see cref="ImperfectionField.Interior"/>),
/// оболочке — поступательные u₀ узлов.
/// </summary>
public sealed record RcModesImperfection(IReadOnlyDictionary<int, double[]> Nodes, IReadOnlyDictionary<int, double[][]> BarPoints)
    : RcImperfection;

/// <summary>
/// Погибь яруса стены (EN 1992-1-1 п. 5.2(7)a, (9); п. 8.1.7 СП 63): оболочкам <paramref name="Shells"/> (номера модели) —
/// поступательные u₀ узлов w₀(z)·n, w₀ = 4e₀·ζ(1 − ζ), ζ = (z − <paramref name="ZBottom"/>)/(<paramref name="ZTop"/> −
/// <paramref name="ZBottom"/>), обрезка в [0, 1]; <paramref name="E0"/> — м со знаком, <paramref name="Normal"/> — единичная
/// нормаль стенки (глобальные оси).
/// </summary>
public sealed record RcWallTierBow(IReadOnlySet<int> Shells, double ZBottom, double ZTop, double[] Normal, double E0) : RcImperfection;

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

    /// <summary>
    /// Начальные несовершенства (складываются): действуют в секущем расчёте по теории второго порядка
    /// (<see cref="RcStructuralMeshBuild.Imperfection"/>), без геометрической нелинейности не учитываются.
    /// </summary>
    public List<RcImperfection> Imperfections { get; } = new();
}
