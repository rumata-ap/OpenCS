using CScore.Planar;

namespace CScore.PlateStrip;

/// <summary>Precondition-проверки, ограничивающие объём Среза 3b (см. docs/superpowers/specs/
/// 2026-08-11-plate-strip-shell-mesh-adapter-design.md, раздел «Сужения объёма»): RVE-адаптер
/// строится только когда StripFrame ориентированно совпадает с PlanarRegion.Frame и все
/// резолвленные слои армирования имеют Angle=0.</summary>
public static class RvePatchPreconditions
{
    /// <summary>Ориентированное (не коллинеарное по модулю) совпадение осей — разворот на 180°
    /// или поворот вокруг любой оси должны быть отклонены. tol — допуск строгого выравнивания,
    /// не общий угловой допуск: значение выше 1e-2 фактически превращает precondition в
    /// разрешающий режим (закрывает С6 четвёртого ревью плана), поэтому валидируется явно.</summary>
    public static bool FrameAligned(Frame3D strip, Frame3D region, double tol = 1e-6)
    {
        ArgumentNullException.ThrowIfNull(strip);
        ArgumentNullException.ThrowIfNull(region);
        if (!double.IsFinite(tol) || !(tol > 0.0) || tol > 1e-2)
            throw new ArgumentOutOfRangeException(nameof(tol),
                "Допуск frame-precondition должен быть конечным, положительным и не больше 1e-2 (строгое выравнивание, не общий угловой допуск).");
        return strip.LocalX.Dot(region.LocalX) >= 1.0 - tol
            && strip.LocalY.Dot(region.LocalY) >= 1.0 - tol
            && strip.LocalZ.Dot(region.LocalZ) >= 1.0 - tol;
    }

    /// <summary>CScore.PlateSection.IntegrateRebar не учитывает PlateRebarLayer.Angle ни для
    /// одного движка (предсуществующий пробел, вне объёма Среза 3b) — RVE-адаптер поэтому
    /// принимает только раскладки с Angle=0 во всех слоях.</summary>
    public static bool AllRebarAnglesZero(IReadOnlyList<PlateRebarLayer> layers, double tol = 1e-9)
    {
        ArgumentNullException.ThrowIfNull(layers);
        foreach (var layer in layers)
            if (Math.Abs(layer.Angle) > tol)
                return false;
        return true;
    }
}
