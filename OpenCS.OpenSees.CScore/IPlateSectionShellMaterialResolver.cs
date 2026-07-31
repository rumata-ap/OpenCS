using OpenCS.OpenSees.Model;

namespace OpenCS.OpenSees.CScore;

/// <summary>Разрешает исходные material ids в native shell-compatible materials.</summary>
public interface IPlateSectionShellMaterialResolver
{
    /// <summary>Разрешает material id бетона в упорядоченную цепочку зависимостей
    /// (база → обёртки, например PlasticDamageConcretePlaneStress → PlateFromPlaneStress).
    /// Для материалов без зависимостей — список из одного элемента.</summary>
    IReadOnlyList<NativeShellMaterialDefinition> ResolveConcrete(int sourceMaterialId);

    /// <summary>Разрешает material id арматуры в упорядоченную цепочку зависимостей
    /// (например uniaxial сталь → PlateRebar). Последний элемент цепочки — материал,
    /// на который ссылается слой сечения; если это PlateRebarShellMaterialSpec, его
    /// AngleDegrees будет переопределён вызывающим кодом при ориентации на X/Y.</summary>
    IReadOnlyList<NativeShellMaterialDefinition> ResolveRebar(int sourceMaterialId);
}
