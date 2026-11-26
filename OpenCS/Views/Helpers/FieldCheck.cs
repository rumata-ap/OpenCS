using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenCS.Utilites;

namespace OpenCS.Views.Helpers;

/// <summary>
/// Проверка числовых полей простых окон: запятая или точка (<see cref="NumberInput"/>), нераспознанное или вне правила —
/// красная рамка, сообщение «Подпись поля: причина», фокус на поле; окно не закрывается. Молча подставлять 0 или
/// прежнее значение нельзя — расчёт пошёл бы не с тем, что видно в поле.
/// </summary>
public static class FieldCheck
{
    /// <summary>Правило: положительное число.</summary>
    public static string? Positive(double v) => v > 0 ? null : Loc.S("FieldErrPositive");

    /// <summary>Правило: неотрицательное число.</summary>
    public static string? NonNegative(double v) => v >= 0 ? null : Loc.S("FieldErrNonNegative");

    /// <summary>Правило: не ноль.</summary>
    public static string? NonZero(double v) => v != 0 ? null : Loc.S("FieldErrNonZero");

    /// <summary>Снимает красные рамки с полей перед новой проверкой.</summary>
    public static void Clear(params TextBox[] boxes)
    {
        foreach (var b in boxes) b.ClearValue(Control.BorderBrushProperty);
    }

    /// <summary>Читает число поля; <paramref name="rule"/> — дополнительная проверка (текст ошибки или <c>null</c>).</summary>
    public static bool Read(Window owner, TextBox box, string labelKey, out double value, Func<double, string?>? rule = null)
    {
        if (!NumberInput.TryParse(box.Text, out value))
        {
            Fail(owner, box, labelKey, Loc.S("FieldErrNumber"));
            return false;
        }
        if (rule?.Invoke(value) is { } error)
        {
            Fail(owner, box, labelKey, error);
            return false;
        }
        return true;
    }

    /// <summary>Читает целое число поля; дробное или нераспознанное — ошибка у поля.</summary>
    public static bool ReadInt(Window owner, TextBox box, string labelKey, out int value, Func<int, string?>? rule = null)
    {
        value = 0;
        if (!NumberInput.TryParse(box.Text, out double v) || v != Math.Floor(v) || Math.Abs(v) > int.MaxValue)
        {
            Fail(owner, box, labelKey, Loc.S("FieldErrInteger"));
            return false;
        }
        value = (int)v;
        if (rule?.Invoke(value) is { } error)
        {
            Fail(owner, box, labelKey, error);
            return false;
        }
        return true;
    }

    /// <summary>Правило: число в (<paramref name="min"/>; <paramref name="max"/>].</summary>
    public static Func<double, string?> InRange(double min, double max) =>
        v => v > min && v <= max ? null : string.Format(Loc.S("FieldErrRangeOC"), Num(min), Num(max));

    /// <summary>Правило: число в [<paramref name="min"/>; <paramref name="max"/>).</summary>
    public static Func<double, string?> InRangeCO(double min, double max) =>
        v => v >= min && v < max ? null : string.Format(Loc.S("FieldErrRangeCO"), Num(min), Num(max));

    /// <summary>Правило: число в [<paramref name="min"/>; <paramref name="max"/>].</summary>
    public static Func<double, string?> InRangeCC(double min, double max) =>
        v => v >= min && v <= max ? null : string.Format(Loc.S("FieldErrRangeCC"), Num(min), Num(max));

    /// <summary>Правило для целых: не меньше <paramref name="min"/>.</summary>
    public static Func<int, string?> IntMin(int min) =>
        v => v >= min ? null : string.Format(Loc.S("FieldErrIntMin"), min);

    /// <summary>Правило для целых: от <paramref name="min"/> до <paramref name="max"/>.</summary>
    public static Func<int, string?> IntRange(int min, int max) =>
        v => v >= min && v <= max ? null : string.Format(Loc.S("FieldErrIntRange"), min, max);

    static string Num(double v) => v.ToString("G", System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>Пустое поле — <c>null</c> (например, «общий шаг»), иначе число по правилу.</summary>
    public static bool ReadOptional(Window owner, TextBox box, string labelKey, out double? value, Func<double, string?>? rule = null)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(box.Text)) return true;
        if (!Read(owner, box, labelKey, out double v, rule)) return false;
        value = v;
        return true;
    }

    /// <summary>Красная рамка, сообщение с подписью поля, фокус.</summary>
    public static void Fail(Window owner, TextBox box, string labelKey, string message)
    {
        box.BorderBrush = Brushes.Red;
        string label = Loc.S(labelKey).Trim().TrimEnd(':');
        MessageBox.Show(owner, $"{label}: {message}", Loc.S("Warning"), MessageBoxButton.OK, MessageBoxImage.Warning);
        box.Focus();
        box.SelectAll();
    }
}
