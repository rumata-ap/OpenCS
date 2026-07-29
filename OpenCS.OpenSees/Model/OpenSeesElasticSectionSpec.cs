namespace OpenCS.OpenSees.Model;

/// <summary>Линейно-упругая секция (OpenSees <c>section Elastic</c>) — приведённые (transformed)
/// EA/EIz/EIy исходного контурного/фиброво заданного сечения к единому эталонному модулю упругости
/// <see cref="E"/>, без явных волокон и материалов. Используется вместо fiber-секции, когда
/// физическая (материальная) нелинейность отключена — геометрическая нелинейность элемента при этом
/// не затрагивается.</summary>
public readonly record struct OpenSeesElasticSectionSpec(
    double E,
    double A,
    double Iz,
    double Iy,
    double GJ);
