using System.Globalization;
using System.Windows.Data;
using CScore;
using CScore.Fem;

namespace OpenCS.Converters;

/// <summary>
/// Подпись сечения строки таблицы стержней и пластин схемы: у родных КонЭ и КЭ — имя назначенного сечения
/// (<see cref="FemMember.CrossSectionId"/>, <see cref="FemMember.PlateSectionId"/>, <see cref="FemElement.CrossSectionId"/>),
/// у импортных — тег жёсткости источника (<c>SectionTag</c>). Привязка — ко всей строке (<c>Binding</c> без пути).
/// </summary>
public sealed class FemSectionLabelConverter(
    IEnumerable<CrossSection> crossSections, IEnumerable<PlateSection> plateSections) : IValueConverter
{
    /// <summary>Подпись сечения КонЭ: назначенное сечение, иначе тег источника; пусто — сечения нет.</summary>
    public static string Label(FemMember m, IEnumerable<CrossSection> cross, IEnumerable<PlateSection> plates)
    {
        string? assigned = m.ElemType == "shell"
            ? plates.FirstOrDefault(s => s.Id == m.PlateSectionId)?.Tag
            : cross.FirstOrDefault(s => s.Id == m.CrossSectionId)?.Tag;
        return !string.IsNullOrEmpty(assigned) ? assigned : m.SectionTag ?? "";
    }

    /// <summary>Подпись сечения КЭ сетки: назначенное сечение стержня, иначе тег источника.</summary>
    public static string Label(FemElement e, IEnumerable<CrossSection> cross)
    {
        string? assigned = e.CrossSectionId is int id ? cross.FirstOrDefault(s => s.Id == id)?.Tag : null;
        return !string.IsNullOrEmpty(assigned) ? assigned : e.SectionTag ?? "";
    }

    /// <inheritdoc/>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        FemMember m => Label(m, crossSections, plateSections),
        FemElement e => Label(e, crossSections),
        _ => ""
    };

    /// <inheritdoc/>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
