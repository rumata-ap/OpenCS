namespace CScore.Planar;

/// <summary>Поставщик parent или template boundary actions.</summary>
public interface IPlanarBoundaryActionProvider
{
    /// <summary>Возвращает actions, нормализованные по request target frame.</summary>
    PlanarBoundaryActionProviderResult Resolve(PlanarBoundaryActionRequest request);
}
