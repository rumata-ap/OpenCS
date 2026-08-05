using CScore.Planar;

namespace CScore.PlateStrip;

/// <summary>Структурный режим примыкания опоры полосы к остальной модели — терминальное
/// provenance-поле в Срезе 1 (BeamJunction ещё не строится и физически не применяется).</summary>
public enum BeamJunctionMode { Support, Tie, RigidTransfer, Interface }

/// <summary>Опора полосы плиты. В Срезе 1 — только явно заданная точка: единственный
/// источник координаты опоры — Frame.Origin (без отдельного Point, чтобы не было двух
/// потенциально расходящихся представлений одной точки). Оси Frame используются связями
/// будущих срезов (junctions), в построении геометрии Среза 1 не участвуют. Auto-derivation
/// через PlanarConnection/RigidTransferDomain/геометрическое пересечение — задача будущих
/// срезов.</summary>
public sealed class SupportLocus
{
    public Frame3D Frame { get; set; } = Frame3D.Identity;
    public BeamJunctionMode StructuralMode { get; set; }
}
