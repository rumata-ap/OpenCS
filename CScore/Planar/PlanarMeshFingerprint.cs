using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CScore.Planar;

/// <summary>Строит детерминированный отпечаток входных данных сетки.</summary>
public static class PlanarMeshFingerprint
{
    public static string Compute(PlanarRegion region, PlanarMeshSettings settings, PlanarMeshProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(region);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(provenance);
        settings.Validate();

        var source = string.Join("|",
            "planar-mesh-v2",
            "msh41",
            region.GeometryFingerprint,
            Format(settings.MaxElementSizeM),
            settings.Algorithm.ToString(CultureInfo.InvariantCulture),
            settings.ElementMode.ToString(),
            provenance.GmshVersion,
            provenance.GeneratorVersion);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
    }

    static string Format(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
}
