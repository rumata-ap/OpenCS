using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CScore.Fem;

/// <summary>
/// Переводимый текст диагностики: ключ шаблона из <see cref="DiagCatalog"/> и аргументы (строки, числа, вложенный
/// <see cref="DiagText"/>). CScore не знает языка интерфейса: <see cref="ToString"/> даёт русский текст по шаблону
/// каталога (журнал, тесты, CLI без языка), а OpenCS.App подставляет те же аргументы в шаблон <c>Diag_&lt;ключ&gt;</c>
/// текущего языка. В отчётах БД хранится ключ и аргументы — старая запись показывается на языке интерфейса.
/// </summary>
[JsonConverter(typeof(DiagTextJsonConverter))]
public sealed class DiagText
{
    public string Key { get; }
    /// <summary>Русский шаблон каталога (string.Format: {0}, {0:0.###}).</summary>
    public string Template { get; }
    public IReadOnlyList<object?> Args { get; }

    /// <exception cref="KeyNotFoundException">Ключа нет в каталоге.</exception>
    public DiagText(string key, params object?[] args)
    {
        Key = key;
        Template = DiagCatalog.Template(key);
        Args = args;
    }

    DiagText(string key, string template, IReadOnlyList<object?> args)
    {
        Key = key;
        Template = template;
        Args = args;
    }

    /// <summary>Текст по шаблону <paramref name="template"/> (null — русский каталога); вложенные — через <paramref name="nested"/>.</summary>
    public string Format(IFormatProvider? provider, string? template = null, Func<DiagText, string>? nested = null) =>
        string.Format(provider, template ?? Template,
            Args.Select(a => a is DiagText t ? nested?.Invoke(t) ?? t.Format(provider) : a).ToArray());

    /// <summary>Русский текст (текущая культура для чисел).</summary>
    public override string ToString() => Format(CultureInfo.CurrentCulture);

    /// <summary>Из ключа и аргументов, прочитанных из БД; ключа больше нет в каталоге — null (показ — по Message).</summary>
    internal static DiagText? TryRestore(string key, IReadOnlyList<object?> args)
    {
        if (key == JoinKey)
            return args.Count > 0 && args[0] is string sep ? new DiagText(key, JoinTemplate(sep, args.Count - 1), args) : null;
        return DiagCatalog.TryTemplate(key, out var template) ? new DiagText(key, template, args) : null;
    }

    /// <summary>Ключ склейки: первый аргумент — разделитель, остальные — части (перевод частей — вложенными).</summary>
    public const string JoinKey = "diag_join";

    static string JoinTemplate(string separator, int count) =>
        string.Join(separator.Replace("{", "{{").Replace("}", "}}"), Enumerable.Range(1, count).Select(i => $"{{{i}}}"));

    /// <summary>Склейка частей через <paramref name="separator"/>; строки и числа — как есть, <see cref="DiagText"/> — переводятся.</summary>
    public static DiagText Join(string separator, IEnumerable<object?> items)
    {
        var args = new List<object?> { separator };
        args.AddRange(items);
        return new DiagText(JoinKey, JoinTemplate(separator, args.Count - 1), args);
    }

    /// <summary>
    /// Список не длиннее <paramref name="max"/>: «a, b, c» либо «a, b и ещё N» (хвост — <paramref name="moreKey"/>:
    /// <c>diag_list_more</c> «и ещё N», <c>diag_list_more_dots</c> «… ещё N», <c>diag_list_dots</c> « …»,
    /// <c>diag_list_semicolon_dots</c> «; …»). Части-строки склеиваются сразу, с <see cref="DiagText"/> — через <see cref="Join"/>.
    /// </summary>
    /// <summary>Не больше <paramref name="max"/> меток: «a, b, c» либо «a, b … (всего N)».</summary>
    public static object Total(IEnumerable<string> items, int max)
    {
        var list = items as IReadOnlyList<string> ?? items.ToList();
        return list.Count <= max ? string.Join(", ", list) : new DiagText("diag_list_total", string.Join(", ", list.Take(max)), list.Count);
    }

    public static object List<T>(IEnumerable<T> items, int max = 10, string separator = ", ", string moreKey = "diag_list_more")
    {
        var list = items.Cast<object?>().ToList();
        var taken = list.Take(max).ToList();
        object head = taken.Any(i => i is DiagText) ? Join(separator, taken) : string.Join(separator, taken);
        return list.Count <= max ? head : new DiagText(moreKey, head, list.Count - max);
    }
}

/// <summary>JSON <see cref="DiagText"/>: {"k": ключ, "a": [строка | число | bool | null | вложенный]}.</summary>
public sealed class DiagTextJsonConverter : JsonConverter<DiagText>
{
    public override DiagText? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        return FromElement(doc.RootElement);
    }

    static DiagText? FromElement(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("k", out var k) || k.ValueKind != JsonValueKind.String)
            return null;
        var args = new List<object?>();
        if (e.TryGetProperty("a", out var a) && a.ValueKind == JsonValueKind.Array)
            foreach (var x in a.EnumerateArray())
            {
                object? value = x.ValueKind switch
                {
                    JsonValueKind.String => x.GetString(),
                    JsonValueKind.Number => x.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Object => FromElement(x),
                    _ => null,
                };
                if (x.ValueKind == JsonValueKind.Object && value == null) return null;
                args.Add(value);
            }
        return DiagText.TryRestore(k.GetString()!, args);
    }

    public override void Write(Utf8JsonWriter writer, DiagText value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("k", value.Key);
        writer.WriteStartArray("a");
        foreach (var arg in value.Args)
            switch (arg)
            {
                case null: writer.WriteNullValue(); break;
                case DiagText t: Write(writer, t, options); break;
                case bool b: writer.WriteBooleanValue(b); break;
                case string s: writer.WriteStringValue(s); break;
                case IConvertible c when arg is double or float or int or long or short or byte or decimal or uint or ulong:
                    writer.WriteNumberValue(c.ToDouble(CultureInfo.InvariantCulture)); break;
                default: writer.WriteStringValue(Convert.ToString(arg, CultureInfo.CurrentCulture)); break;
            }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
