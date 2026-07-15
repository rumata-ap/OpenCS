using System.Globalization;

namespace OpenCS.OpenSees.Tcl;

/// <summary>Форматирует числовые значения Tcl независимо от текущей культуры.</summary>
public static class TclNumber
{
    /// <summary>Возвращает конечное число в invariant-культуре с точностью G17.</summary>
    public static string Format(double value)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), value, "Tcl число должно быть конечным.");

        return value.ToString("G17", CultureInfo.InvariantCulture);
    }
}
