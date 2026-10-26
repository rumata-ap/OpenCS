using System.Threading;

namespace CScore.History;

/// <summary>Вид этапа истории.</summary>
public enum StageKind
{
    /// <summary>Управление по нагрузке: усилия линейно от конца предыдущего этапа до заданных.</summary>
    Load,
    /// <summary>Доведение до разрушения по кривизне (только последним этапом).</summary>
    Failure,
}

/// <summary>Роль материала части в критерии предельного состояния.</summary>
public enum PartLimitRole
{
    /// <summary>Достижение предела ограничивает (по умолчанию, п. 8.1.30 СП 63).</summary>
    Limits,
    /// <summary>Предел не проверяется, диаграмма продолжается.</summary>
    Ignore,
    /// <summary>Предел не проверяется; за пределом σ спадает до нуля.</summary>
    Fails,
}

/// <summary>Этап истории. Для <see cref="StageKind.Failure"/> из <see cref="Target"/> берётся только направление момента.</summary>
public sealed record HistoryStage(string Tag, StageKind Kind, int? ActivatePart, Load Target, int Steps);

/// <summary>Роли бетона и арматуры части.</summary>
public sealed record PartRoles(int PartIndex, PartLimitRole Concrete, PartLimitRole Rebar);

/// <summary>Параметры этапа разрушения.</summary>
public sealed class FailureOptions
{
    /// <summary>Число шагов по кривизне.</summary>
    public int Steps { get; set; } = 50;
    /// <summary>Остановка, когда |M| падает ниже доли пика.</summary>
    public double DropRatio { get; set; } = 0.85;
    /// <summary>Предельная проекция кривизны; null — оценка 1,5·(|εb2| + εs2)/h_u.</summary>
    public double? CMax { get; set; }
    /// <summary>Длина спада σ до нуля для роли «Разрушается».</summary>
    public double SofteningStrain { get; set; } = 0.0005;
}

/// <summary>Параметры решателя истории.</summary>
public sealed class LoadHistoryOptions
{
    public CalcType Calc { get; set; } = CalcType.C;
    public bool Ten { get; set; }
    public double Tol { get; set; } = 0.5;
    public int MaxIter { get; set; } = 60;
    public double H { get; set; } = 1e-7;
    public bool CentralJacobian { get; set; }
    public FailureOptions Failure { get; set; } = new();
    public IReadOnlyList<PartRoles> Roles { get; set; } = [];
    public CancellationToken Token { get; set; }
    /// <summary>Доля выполненных шагов, 0…1.</summary>
    public Action<double>? Progress { get; set; }

    /// <summary>Роли части; отсутствующие — <see cref="PartLimitRole.Limits"/>.</summary>
    public PartRoles RoleOf(int partIndex) =>
        Roles.FirstOrDefault(r => r.PartIndex == partIndex)
        ?? new PartRoles(partIndex, PartLimitRole.Limits, PartLimitRole.Limits);
}

/// <summary>Деформации части относительно её включения и использование по пределам СП 63.</summary>
public sealed class PartStepState
{
    public int PartIndex { get; init; }
    public double ConcreteEpsMin { get; init; }
    public double ConcreteEpsMax { get; init; }
    public double RebarEpsMin { get; init; }
    public double RebarEpsMax { get; init; }
    /// <summary>ε_min / εb2 (0, если бетон не сжат или бетона нет).</summary>
    public double EtaConcrete { get; init; }
    /// <summary>(ε + εp)_max / εs2 (0, если арматура не растянута или её нет).</summary>
    public double EtaRebar { get; init; }
    public (double X, double Y) ConcreteMinPoint { get; init; }
    public (double X, double Y) RebarMaxPoint { get; init; }
}

/// <summary>Шаг истории.</summary>
public sealed class HistoryStep
{
    public int StageIndex { get; init; }
    public StageKind Kind { get; init; }
    public int Step { get; init; }
    public int ActivePartCount { get; init; }
    public Load Target { get; init; }
    public Load Result { get; init; }
    public Kurvature K { get; init; }
    /// <summary>Проекция кривизны на направление изгиба u.</summary>
    public double C { get; init; }
    /// <summary>Проекция момента на u.</summary>
    public double Mu { get; init; }
    public bool Converged { get; init; }
    public int Iterations { get; init; }
    public double Residual { get; init; }
    /// <summary>Точка уточнённого предела по деформациям.</summary>
    public bool IsLimitPoint { get; init; }
    public List<string> Warnings { get; init; } = [];
    public List<PartStepState> Parts { get; init; } = [];
}

/// <summary>Один путь (с историей или без).</summary>
public sealed class LoadHistoryRun
{
    public List<HistoryStep> Steps { get; } = [];
    public Kurvature[] ActivationPlanes { get; set; } = [];
    /// <summary>"ok" | "partial".</summary>
    public string Status { get; set; } = "ok";
    /// <summary>"completed" | "load_not_converged" | "drop" | "c_max" | "failure_not_converged".</summary>
    public string StopReason { get; set; } = "completed";
    public double? MLim { get; set; }
    public double? MPeak { get; set; }
    public int? GoverningPart { get; set; }
    /// <summary>"concrete" | "rebar".</summary>
    public string? GoverningMaterial { get; set; }
    public (double X, double Y)? GoverningPoint { get; set; }
}

/// <summary>Результат решателя истории.</summary>
public sealed class LoadHistoryResult
{
    public LoadHistoryRun WithHistory { get; init; } = new();
    public LoadHistoryRun? WithoutHistory { get; init; }
    /// <summary>Угол направления изгиба φ (рад): u = (cos φ, sin φ) в осях (Mx, My).</summary>
    public double Phi { get; init; }
}
