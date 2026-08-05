using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CScore.Planar;

namespace CScore.PlateStrip;

/// <summary>Детерминированный SHA-256 отпечаток входов PlateStripGeometryBuilder.Build —
/// идиома идентична PlanarGeometryFingerprint.Compute (тот же Fmt = "G17" invariant, не
/// "R"). Не зависит от построенной геометрии — только от входов, поэтому вычислим
/// независимо от успеха построения.</summary>
public static class PlateStripFingerprint
{
    public static string Compute(PlanarRegion region, SupportLocus start, SupportLocus end, double explicitWidthM)
    {
        var parts = new List<string>
        {
            region.GeometryFingerprint,
            LocusFingerprint(start),
            LocusFingerprint(end),
            Fmt(explicitWidthM)
        };
        return Hash(string.Join("|", parts));
    }

    static string LocusFingerprint(SupportLocus locus) =>
        $"{locus.StructuralMode}:{Fmt(locus.Frame.Origin.X)},{Fmt(locus.Frame.Origin.Y)},{Fmt(locus.Frame.Origin.Z)}";

    static string Fmt(double v) => v.ToString("G17", CultureInfo.InvariantCulture);

    static string Hash(string input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
}
