using CScore.Planar;

namespace CScore.Fem.Loads;

/// <summary>
/// Нагрузка одного КЭ «частью по виду» (спека 2026-11-05 «нагрузки КЭ на гранях фрагмента по виду», разд. 3): общие оси,
/// коэффициент загружения учтён. Нужна, чтобы взять часть нагрузки внутри границ тем же правилом, каким её переносит
/// построитель фрагмента (сосредоточенная — по точке, погонная — обрезкой, по площади — интегралом по части КЭ).
/// </summary>
public abstract record FemLoadPiece
{
    /// <summary>Та же часть с множителем <paramref name="k"/> (все виды линейны по значению).</summary>
    public abstract FemLoadPiece Scale(double k);
}

/// <summary>Сосредоточенная сила <see cref="F"/> (Н) и момент <see cref="M"/> (Н·м) в точке <see cref="At"/>.</summary>
public sealed record FemPointPiece(PlanarVector3 At, PlanarVector3 F, PlanarVector3 M) : FemLoadPiece
{
    public override FemLoadPiece Scale(double k) => new FemPointPiece(At, F * k, M * k);
}

/// <summary>Линейная погонная нагрузка (Н/м) на отрезке A → B: <see cref="QA"/> в A, <see cref="QB"/> в B.</summary>
public sealed record FemLinePiece(PlanarVector3 A, PlanarVector3 B, PlanarVector3 QA, PlanarVector3 QB) : FemLoadPiece
{
    public override FemLoadPiece Scale(double k) => new FemLinePiece(A, B, QA * k, QB * k);
}

/// <summary>
/// Нагрузка на площадь пластины (Па) по функциям формы КЭ <see cref="Geometry"/>: <see cref="Q"/> — значения в узлах
/// (порядок <see cref="FemElementGeometry.Points"/>); равномерная — все значения равны.
/// </summary>
public sealed record FemSurfacePiece(FemElementGeometry Geometry, IReadOnlyList<PlanarVector3> Q) : FemLoadPiece
{
    public override FemLoadPiece Scale(double k) => new FemSurfacePiece(Geometry, Q.Select(q => q * k).ToArray());
}
