using System.Windows.Media.Media3D;

namespace OpenCS.ViewModels;

/// <summary>Вид геометрического обозначения нагрузки или закрепления в 3D-сцене FEM.</summary>
public enum FemDiagramGlyphKind
{
    TranslationSupport,
    RotationSupport,
    Force,
    Moment,
    KinematicDisplacement,
    KinematicRotation
}

/// <summary>Независимое от Helix описание одного условного знака в 3D-сцене.</summary>
public sealed record FemDiagramGlyph(
    FemDiagramGlyphKind Kind,
    int NodeId,
    Vector3D Axis,
    double Sign,
    string Component,
    double Value,
    bool IsSupport)
{
    /// <summary>Краткая техническая подпись компоненты без локализованной единицы.</summary>
    public string Label => $"{Component} = {Value:G6}";
}
