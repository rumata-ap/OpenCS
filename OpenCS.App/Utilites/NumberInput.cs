using System.Globalization;

namespace OpenCS.Utilites;

/// <summary>
/// Разбор числа из поля ввода: десятичный разделитель — запятая или точка (как привык пользователь), разделителей
/// тысяч нет. «14,5» = 14,5, а не 145, как давал double.Parse с InvariantCulture (запятая там — разделитель тысяч).
/// </summary>
public static class NumberInput
{
    public static double Parse(string? text)
    {
        string s = (text ?? "").Trim();
        // «1,000.5» — и запятая, и точка: не угадываем, что из них разделитель тысяч.
        if (s.Contains(',') && s.Contains('.')) throw new FormatException(text);
        s = s.Replace(',', '.');
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value))
            return value;
        throw new FormatException(text);
    }

    public static bool TryParse(string? text, out double value)
    {
        try { value = Parse(text); return true; }
        catch (FormatException) { value = 0; return false; }
    }
}
