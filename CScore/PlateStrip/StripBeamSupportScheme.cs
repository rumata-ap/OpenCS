namespace CScore.PlateStrip;

/// <summary>Условие закрепления конца полосы по поперечным степеням свободы.</summary>
public enum StripBeamEndCondition
{
    /// <summary>Шарнир: закреплены поперечные перемещения v и w, ротации свободны.</summary>
    Pinned,
    /// <summary>Заделка: дополнительно закреплены обе изгибные ротации θy и θz.</summary>
    Fixed
}

/// <summary>Продольное закрепление полосы.</summary>
public enum StripAxialRestraint
{
    /// <summary>u закреплено только в начале полосы — осевая задача статически определима.</summary>
    StartOnly,
    /// <summary>u закреплено на обоих концах — осевая задача один раз неопределима.</summary>
    BothEnds
}

/// <summary>Опорная схема производной балки полосы. Явный вход Среза 6: автовывод из
/// SupportLocus.StructuralMode/BeamJunction — задача будущего среза (та же логика, что
/// WidthPolicy = ExplicitWidth в Срезе 1).
///
/// Поперечные степени свободы и ротации задаются StartCondition/EndCondition, продольная —
/// исключительно AxialRestraint. Разделение обязательно: если бы Pinned закреплял ещё и u,
/// режим StartOnly был бы недостижим. При этом раздельные маски НЕ означают разделения задачи:
/// при недиагональной матрице сечения осевая и изгибные компоненты связаны материально.</summary>
public sealed record StripBeamSupportScheme(
    StripBeamEndCondition StartCondition,
    StripBeamEndCondition EndCondition,
    StripAxialRestraint AxialRestraint)
{
    /// <summary>Шарнирно опёртая балка с продольным закреплением только в начале.</summary>
    public static StripBeamSupportScheme SimplySupported =>
        new(StripBeamEndCondition.Pinned, StripBeamEndCondition.Pinned, StripAxialRestraint.StartOnly);
}
