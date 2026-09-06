using CScore;

namespace OpenCS.Reporting;

/// <summary>Режим визуализации карты в portable-контракте отчёта.</summary>
public enum ReportImageMode
{
    /// <summary>Карта нормальных напряжений.</summary>
    Stress,
    /// <summary>Карта деформаций.</summary>
    Strain
}

/// <summary>Заказ иллюстрации: какую плоскость деформаций и в каком режиме отрисовать.
/// Картинку строит вызывающая сторона, поэтому поставщик остаётся portable.</summary>
public sealed record ReportImageRequest(
    string Key, string Title, Kurvature Plane, CalcType Calc, ReportImageMode Mode);
