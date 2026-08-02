using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CScore.Planar;

/// <summary>Строит детерминированный fingerprint source contract связи.</summary>
public static class PlanarConnectionFingerprint
{
    public static string Compute(PlanarConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var values = new List<string>
        {
            "planar-connection-v1",
            connection.Id.ToString(CultureInfo.InvariantCulture),
            connection.Tag,
            connection.MeshMode.ToString(),
            Format(connection.MatchingToleranceM),
            Locus(connection.SideA),
            Locus(connection.SideB)
        };
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", values))))
            .ToLowerInvariant();
    }

    static string Locus(ConnectionLocus locus) =>
        string.Join(";", new[]
        {
            locus.RegionId.ToString(CultureInfo.InvariantCulture),
            locus.Tag,
            string.Join(",", locus.Points.Select(point =>
                $"{Format(point.U)},{Format(point.V)}"))
        });

    static string Format(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
}
