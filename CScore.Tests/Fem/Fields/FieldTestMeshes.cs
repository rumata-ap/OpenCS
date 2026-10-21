using System.Globalization;
using CScore.Fem.Fields;

namespace CScore.Tests.Fem.Fields;

/// <summary>Сетки четырёхугольников для тестов полей: общие узлы по совпадению координат (1 мм).</summary>
sealed class FieldTestMeshes
{
    readonly Dictionary<(long, long, long), string> _tags = [];
    public List<FieldShell> Shells { get; } = [];

    string Tag(FieldVec3 p)
    {
        var key = ((long)Math.Round(p.X * 1000), (long)Math.Round(p.Y * 1000), (long)Math.Round(p.Z * 1000));
        if (!_tags.TryGetValue(key, out var t)) _tags[key] = t = (_tags.Count + 1).ToString(CultureInfo.InvariantCulture);
        return t;
    }

    /// <summary>Прямоугольная сетка nu × nv КЭ от <paramref name="o"/> по векторам шага <paramref name="du"/>, <paramref name="dv"/>.</summary>
    public FieldTestMeshes Grid(string prefix, FieldVec3 o, FieldVec3 du, FieldVec3 dv, int nu, int nv)
    {
        for (int i = 0; i < nu; i++)
            for (int j = 0; j < nv; j++)
            {
                var p = new[] { o + du * i + dv * j, o + du * (i + 1) + dv * j, o + du * (i + 1) + dv * (j + 1), o + du * i + dv * (j + 1) };
                Shells.Add(new FieldShell($"{prefix}{i}_{j}", p.Select(Tag).ToList(), p));
            }
        return this;
    }

    /// <summary>
    /// Подвал: плита z = 0 (x 0…4, y 0…4), стена y = 0 вниз до z = −3, пандус от края x = 4 под 30° вниз, подступенок
    /// y = 4 (z 0…−1) и нижняя плита z = −1 (y 4…6). Ожидается 5 участков.
    /// </summary>
    public static FieldTestMeshes Basement()
    {
        double c = Math.Cos(Math.PI / 6), s = Math.Sin(Math.PI / 6);
        return new FieldTestMeshes()
            .Grid("slab", new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), 4, 4)
            .Grid("wall", new(0, 0, 0), new(1, 0, 0), new(0, 0, -1), 4, 3)
            .Grid("ramp", new(4, 0, 0), new(c, 0, -s), new(0, 1, 0), 3, 4)
            .Grid("riser", new(0, 4, 0), new(1, 0, 0), new(0, 0, -0.5), 4, 2)
            .Grid("low", new(0, 4, -1), new(1, 0, 0), new(0, 1, 0), 4, 2);
    }
}
