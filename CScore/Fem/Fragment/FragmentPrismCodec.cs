using System.Text.Json;

namespace CScore.Fem.Fragment;

/// <summary>
/// Призма фрагмента в схеме (<see cref="FemSchema.FragmentPrismJson"/>, спека призмы 3.3): прямоугольник в плане, отступ
/// d от элементов и теги выбранных элементов (null/пусто — неизвестны, например призма задана вручную).
/// </summary>
public sealed record FragmentPrismInfo(FragmentPrism Prism, double? OffsetM = null, IReadOnlyList<string>? MemberTags = null);

/// <summary>JSON призмы фрагмента: <c>{"cx","cy","ax","ay","a","b","d","members"}</c>; неизвестные поля пропускаются.</summary>
public static class FragmentPrismCodec
{
    sealed class Dto
    {
        public double cx { get; set; }
        public double cy { get; set; }
        public double ax { get; set; }
        public double ay { get; set; }
        public double a { get; set; }
        public double b { get; set; }
        public double? d { get; set; }
        public List<string>? members { get; set; }
    }

    public static string Encode(FragmentPrismInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        var p = info.Prism;
        return JsonSerializer.Serialize(new Dto
        {
            cx = p.CenterX, cy = p.CenterY, ax = p.AxisX, ay = p.AxisY, a = p.HalfLength, b = p.HalfWidth,
            d = info.OffsetM, members = info.MemberTags?.ToList(),
        });
    }

    /// <summary>Призма из JSON; null или пустая строка — призмы нет; битый JSON — <see cref="InvalidDataException"/>.</summary>
    public static FragmentPrismInfo? Decode(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        Dto? d;
        try { d = JsonSerializer.Deserialize<Dto>(json); }
        catch (JsonException ex) { throw new InvalidDataException($"Призма фрагмента не читается: {ex.Message}", ex); }
        if (d == null || d.a <= 0 || d.b <= 0) throw new InvalidDataException("Призма фрагмента без размеров.");
        return new FragmentPrismInfo(FragmentPrism.Create(d.cx, d.cy, d.ax, d.ay, d.a, d.b), d.d, d.members);
    }
}
