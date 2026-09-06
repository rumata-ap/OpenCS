namespace CScore.PlateStrip;

public enum StripLoadKind { DistributedUniform, DistributedLinear, Point }

/// <summary>Нагрузка, уже спроецированная в StripFrame (кН, кН/м, доли пролёта [0,1]).
/// Distributed действует на участке [StationStartFraction, StationEndFraction]:
/// <see cref="StripLoadKind.DistributedUniform"/> — постоянная интенсивность Q*KnM,
/// <see cref="StripLoadKind.DistributedLinear"/> — линейная от Q*KnM в начале участка до
/// Q*EndKnM в конце (Срез 6, восстановленная нагрузка кусочно-линейна по построению; см.
/// docs/superpowers/specs/2026-09-06-plate-strip-equivalent-beam-load-recovery-design.md).
/// Point — сосредоточенное действие на StationFraction; эксцентриситет по ширине уже приведён к
/// оси (Mz вычислен из эксцентриситета; MxKnM хранится для defense-in-depth проверки в
/// StripLoadConsistentNodalProjection.Project — StripLoadMapper.Map уже гарантирует |MxKnM| в
/// допуске, но поле не убирается, чтобы ручное/тестовое конструирование в обход Map тоже было
/// проверяемо). My по построению всегда 0 (плечо эксцентриситета лежит в плоскости полосы,
/// r=(0,v,0) не даёт компоненты My в M=r×F) — поле не заводится.</summary>
public sealed class StripLoad
{
    public string SourceTag { get; init; } = "";
    public StripLoadKind Kind { get; init; } = StripLoadKind.DistributedUniform;

    // Distributed: доли пролёта, на которых действует нагрузка.
    public double StationStartFraction { get; init; }
    public double StationEndFraction { get; init; } = 1.0;

    /// <summary>Интенсивность в начале участка (для DistributedUniform — на всём участке).</summary>
    public double QxKnM { get; init; }
    public double QyKnM { get; init; }
    public double QzKnM { get; init; }

    /// <summary>Интенсивность в конце участка; используется только при DistributedLinear,
    /// при DistributedUniform обязана быть нулевой.</summary>
    public double QxEndKnM { get; init; }
    public double QyEndKnM { get; init; }
    public double QzEndKnM { get; init; }

    // Point: положение на оси и уже приведённые к оси генерализованные компоненты.
    public double StationFraction { get; init; }
    public double PxKn { get; init; }
    public double PyKn { get; init; }
    public double PzKn { get; init; }
    public double MxKnM { get; init; }
    public double MzKnM { get; init; }

    /// <summary>Распределённая ли это нагрузка (обе Distributed-разновидности).</summary>
    public bool IsDistributed => Kind != StripLoadKind.Point;

    /// <summary>Интенсивность на доле пролёта внутри участка нагрузки. Для DistributedUniform
    /// постоянна, для DistributedLinear — линейная интерполяция между началом и концом участка.
    /// Вне участка возвращает нули.</summary>
    public (double Qx, double Qy, double Qz) IntensityAt(double fraction)
    {
        if (!IsDistributed)
            throw new InvalidOperationException(
                $"StripLoad «{SourceTag}»: IntensityAt применим только к распределённой нагрузке.");

        if (fraction < StationStartFraction || fraction > StationEndFraction)
            return (0.0, 0.0, 0.0);

        if (Kind == StripLoadKind.DistributedUniform)
            return (QxKnM, QyKnM, QzKnM);

        double span = StationEndFraction - StationStartFraction;
        double t = (fraction - StationStartFraction) / span;
        return (QxKnM + (QxEndKnM - QxKnM) * t,
                QyKnM + (QyEndKnM - QyKnM) * t,
                QzKnM + (QzEndKnM - QzKnM) * t);
    }

    public void Validate()
    {
        double[] fields = IsDistributed
            ? [StationStartFraction, StationEndFraction, QxKnM, QyKnM, QzKnM,
               QxEndKnM, QyEndKnM, QzEndKnM]
            : [StationFraction, PxKn, PyKn, PzKn, MxKnM, MzKnM];

        foreach (double field in fields)
            if (!double.IsFinite(field))
                throw new ArgumentException($"StripLoad «{SourceTag}» содержит нечисловое поле.");

        if (IsDistributed)
        {
            if (StationStartFraction < 0.0 || StationEndFraction > 1.0 ||
                StationStartFraction >= StationEndFraction)
                throw new ArgumentException(
                    $"StripLoad «{SourceTag}»: участок распределённой нагрузки должен лежать в " +
                    "[0,1] и иметь положительную длину.");

            if (Kind == StripLoadKind.DistributedUniform &&
                (QxEndKnM != 0.0 || QyEndKnM != 0.0 || QzEndKnM != 0.0))
                throw new ArgumentException(
                    $"StripLoad «{SourceTag}»: при DistributedUniform поля Q*EndKnM не " +
                    "используются и обязаны быть нулевыми (иначе интенсивность конца участка " +
                    "молча теряется).");
        }
        else
        {
            if (StationFraction < 0.0 || StationFraction > 1.0)
                throw new ArgumentOutOfRangeException(nameof(StationFraction),
                    $"StripLoad «{SourceTag}»: StationFraction должен быть в [0,1].");
        }
    }
}

public sealed record StripLoadSet(IReadOnlyList<StripLoad> Loads);
