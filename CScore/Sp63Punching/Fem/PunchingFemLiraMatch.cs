using CScore.Import;
using CScore.Planar;

namespace CScore.Sp63Punching.Fem;

/// <summary>
/// Сопоставление узлов продавливания ЛИРЫ из *.asp с узлами проверки по МКЭ: по номеру узла (формат v6; у импортной
/// схемы тег узла сетки — номер узла ЛИРЫ), иначе по точке узла (v5 номера не хранит).
/// </summary>
/// <param name="ByNode">Запись ЛИРЫ по тегу узла проверки.</param>
/// <param name="Unmatched">Записи ЛИРЫ, для которых узел проверки не найден.</param>
public sealed record PunchingFemLiraMatch(
    IReadOnlyDictionary<string, LiraAspPunching> ByNode, IReadOnlyList<LiraAspPunching> Unmatched)
{
    /// <summary>Допуск совпадения точки узла, м.</summary>
    public const double PointTolerance = 0.01;

    /// <summary>Пустое сопоставление (файла нет).</summary>
    public static PunchingFemLiraMatch Empty { get; } = new(new Dictionary<string, LiraAspPunching>(), []);

    /// <summary>Сопоставить записи ЛИРЫ с узлами (тег и точка узла в общих осях, м).</summary>
    public static PunchingFemLiraMatch Build(IEnumerable<LiraAspPunching> records, IReadOnlyList<(string Tag, PlanarVector3 Point)> joints)
    {
        var tags = joints.Select(j => j.Tag).ToHashSet(StringComparer.Ordinal);
        var byNode = new Dictionary<string, LiraAspPunching>(StringComparer.Ordinal);
        var unmatched = new List<LiraAspPunching>();
        var pending = new List<LiraAspPunching>();
        foreach (var r in records)
        {
            string? tag = r.Node?.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (tag != null && tags.Contains(tag) && !byNode.ContainsKey(tag)) byNode[tag] = r;
            else pending.Add(r);
        }
        foreach (var r in pending)
        {
            if (r.Contour.Count == 0) { unmatched.Add(r); continue; }
            var p = new PlanarVector3(r.Point.X, r.Point.Y, r.Point.Z);
            string? best = null;
            double bestD = PointTolerance;
            foreach (var (tag, point) in joints)
            {
                if (byNode.ContainsKey(tag)) continue;
                double d = (point - p).Length;
                if (d <= bestD) { bestD = d; best = tag; }
            }
            if (best != null) byNode[best] = r;
            else unmatched.Add(r);
        }
        return new PunchingFemLiraMatch(byNode, unmatched);
    }
}
