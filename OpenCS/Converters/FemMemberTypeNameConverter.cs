using System.Globalization;
using System.Windows.Data;
using CScore.Fem;
using OpenCS.Utilites;

namespace OpenCS.Converters;

/// <summary>Тип группы/КонЭ для списка выбора: код (<see cref="FemMemberTypes"/>) и локализованное имя.</summary>
public sealed record FemMemberTypeOption(string? Code, string Name)
{
    /// <summary>«Не задан» и все коды в порядке <see cref="FemMemberTypes.All"/>.</summary>
    public static IReadOnlyList<FemMemberTypeOption> All() =>
        [new(null, Loc.S("FemMemberTypeNone")), .. FemMemberTypes.All.Select(c => new FemMemberTypeOption(c, NameOf(c)))];

    /// <summary>Локализованное имя кода; неизвестный код (свободный текст старых групп) — как есть.</summary>
    public static string NameOf(string? code)
    {
        if (string.IsNullOrEmpty(code)) return "";
        string key = "FemMemberType_" + code;
        string name = Loc.S(key);
        return name == key ? code : name;
    }
}

/// <summary>Код типа → локализованное имя (дерево, таблицы). Параметр — формат непустого имени («  [{0}]»);
/// тип не задан — пустая строка.</summary>
public sealed class FemMemberTypeNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string name = FemMemberTypeOption.NameOf(value as string);
        return name.Length > 0 && parameter is string format ? string.Format(culture, format, name) : name;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
