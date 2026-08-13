namespace CScore.PlateStrip;

public enum StripLoadKind { DistributedUniform, Point }

/// <summary>Нагрузка, уже спроецированная в StripFrame (кН, кН/м, доли пролёта [0,1]).
/// Distributed — постоянна на [StationStartFraction, StationEndFraction] (в этом срезе всегда
/// [0,1] — см. docs/superpowers/specs/2026-08-13-plate-strip-loads-design.md, «Упрощение
/// объёма»); Point — сосредоточенное действие на StationFraction; эксцентриситет по ширине уже
/// приведён к оси (Mz вычислен из эксцентриситета; MxKnM хранится для defense-in-depth проверки
/// в StripLoadConsistentNodalProjection.Project — единственный производитель StripLoad,
/// StripLoadMapper.Map, уже гарантирует |MxKnM| в допуске, но поле не убирается, чтобы
/// ручное/тестовое конструирование в обход Map тоже было проверяемо). My по построению всегда 0
/// (плечо эксцентриситета лежит в плоскости полосы, r=(0,v,0) не даёт компоненты My в M=r×F) —
/// поле не заводится.</summary>
public sealed class StripLoad
{
    public string SourceTag { get; init; } = "";
    public StripLoadKind Kind { get; init; } = StripLoadKind.DistributedUniform;

    // Distributed: доли пролёта, на которых действует нагрузка.
    public double StationStartFraction { get; init; }
    public double StationEndFraction { get; init; } = 1.0;
    public double QxKnM { get; init; }
    public double QyKnM { get; init; }
    public double QzKnM { get; init; }

    // Point: положение на оси и уже приведённые к оси генерализованные компоненты.
    public double StationFraction { get; init; }
    public double PxKn { get; init; }
    public double PyKn { get; init; }
    public double PzKn { get; init; }
    public double MxKnM { get; init; }
    public double MzKnM { get; init; }

    public void Validate()
    {
        double[] fields = Kind == StripLoadKind.DistributedUniform
            ? [StationStartFraction, StationEndFraction, QxKnM, QyKnM, QzKnM]
            : [StationFraction, PxKn, PyKn, PzKn, MxKnM, MzKnM];

        foreach (double field in fields)
            if (!double.IsFinite(field))
                throw new ArgumentException($"StripLoad «{SourceTag}» содержит нечисловое поле.");

        if (Kind == StripLoadKind.DistributedUniform)
        {
            // В этом срезе Distributed всегда занимает весь пролёт (StripLoadMapper.Map/
            // MapSelfWeight — единственные производители, всегда пишут литералы 0.0/1.0;
            // StripLoadConsistentNodalProjection не умеет частичное перекрытие элемента).
            // Запрещаем диапазон здесь, а не молча теряем его в Project.
            if (StationStartFraction != 0.0 || StationEndFraction != 1.0)
                throw new ArgumentException(
                    $"StripLoad «{SourceTag}»: в этом срезе Distributed должен занимать весь " +
                    "пролёт (StationStartFraction=0.0, StationEndFraction=1.0).");
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
