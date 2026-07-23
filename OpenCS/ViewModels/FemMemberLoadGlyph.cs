using System.Windows.Media.Media3D;

namespace OpenCS.ViewModels;

/// <summary>Описание условного знака распределённой нагрузки на конструктивном стержне.</summary>
public sealed record FemMemberLoadGlyph(
    string MemberTag,
    Point3D Start,
    Point3D End,
    Vector3D LoadAtStart,
    Vector3D LoadAtEnd,
    string Label);
