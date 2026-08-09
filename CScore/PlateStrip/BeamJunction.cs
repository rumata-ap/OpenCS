using CScore.Planar;

namespace CScore.PlateStrip;

/// <summary>Конец полосы, к которому относится BeamJunction.</summary>
public enum BeamJunctionEnd { Start, End }

/// <summary>Терминальное значение Среза 3a — реальный статус привязки к сетке появится
/// вместе с mesh-адаптером Среза 3b.</summary>
public enum BeamJunctionMeshMode { NotMeshed }

/// <summary>Декларативная provenance-проекция связи конца полосы с опорой. Не хранится в
/// SQLite — строится заново из EquivalentSection.Strip при каждом вызове BeamJunctionBuilder.
/// SupportLocus — та же ссылка (не клон) на объект Strip.StartSupportLocus/EndSupportLocus:
/// уже построенный BeamJunction не замораживает состояние опоры, а разделяет его с Strip
/// (согласуется с тем, что сам EquivalentSection.Strip хранится и используется как живая
/// ссылка, а не защитная копия, во всём CScore.PlateStrip). DofTransfer из родительской схемы
/// не вводится отдельным типом: до появления реального mesh-адаптера (Срез 3b) он тождественен
/// StructuralMode.</summary>
public sealed class BeamJunction
{
    public string StripBeamId { get; init; } = "";
    public BeamJunctionEnd End { get; init; }
    public SupportLocus SupportLocus { get; init; } = new();
    public Frame3D Geometry3D => SupportLocus.Frame;
    public BeamJunctionMode StructuralMode => SupportLocus.StructuralMode;
    public BeamJunctionMeshMode MeshMode { get; init; } = BeamJunctionMeshMode.NotMeshed;
}
