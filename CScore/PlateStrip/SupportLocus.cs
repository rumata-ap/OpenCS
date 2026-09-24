using CScore.Planar;

namespace CScore.PlateStrip;

/// <summary>Структурный режим примыкания опоры полосы к остальной модели — терминальное
/// provenance-поле в Срезе 1 (BeamJunction ещё не строится и физически не применяется).</summary>
public enum BeamJunctionMode { Support, Tie, RigidTransfer, Interface }

/// <summary>Опора полосы плиты. Единственный источник координаты опоры — Frame.Origin (без
/// отдельного Point, чтобы не было двух потенциально расходящихся представлений одной точки).
/// Оси Frame используются связями (junctions), в построении геометрии полосы не участвуют.
///
/// <see cref="Kind"/> и <see cref="SourceReferences"/> — provenance автовывода опоры из
/// родительской схемы (<c>StripSupportDerivation</c>, Срез 8a). Они не входят в
/// <see cref="PlateStripFingerprint"/>: это не геометрия, и их изменение не делает полосу
/// устаревшей.</summary>
public sealed class SupportLocus
{
    public Frame3D Frame { get; set; } = Frame3D.Identity;
    public BeamJunctionMode StructuralMode { get; set; }
    /// <summary>Происхождение опоры; Manual — задана вручную (значение старых записей).</summary>
    public StripSupportKind Kind { get; set; } = StripSupportKind.Manual;
    /// <summary>Источники родительской схемы, совпавшие с опорой при автовыводе.</summary>
    public List<PlanarBoundarySourceReference> SourceReferences { get; set; } = [];
}
