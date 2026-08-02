using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CScore.Planar;

/// <summary>Строит детерминированный отпечаток входных данных сетки.</summary>
public static class PlanarMeshFingerprint
{
    public static string Compute(
        PlanarRegion region,
        PlanarMeshSettings settings,
        PlanarMeshProvenance provenance,
        string? constraintSourceFingerprint = null)
    {
        ArgumentNullException.ThrowIfNull(region);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(provenance);
        settings.Validate();

        var parts = new List<string>
        {
            "planar-mesh-v2",
            "msh41",
            region.GeometryFingerprint,
            Format(settings.MaxElementSizeM),
            settings.Algorithm.ToString(CultureInfo.InvariantCulture),
            settings.ElementMode.ToString(),
            provenance.GmshVersion,
            provenance.GeneratorVersion
        };
        if (!string.IsNullOrEmpty(constraintSourceFingerprint))
            parts.Add($"constraint-source:{constraintSourceFingerprint}");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", parts)))).ToLowerInvariant();
    }

    static string Format(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
}
