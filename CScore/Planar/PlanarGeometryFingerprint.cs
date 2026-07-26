using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CScore.Planar;

/// <summary>Детерминированный SHA-256 отпечаток геометрии PlanarRegion (контуры + Frame3D +
/// разметка границ) — для инвалидации производных данных (будущий MeshSnapshot). Идиома
/// идентична PlateSectionOpenSeesMapper.Fingerprint (OpenCS.OpenSees.CScore).</summary>
public static class PlanarGeometryFingerprint
{
    public static string Compute(IReadOnlyList<Contour> contours, Frame3D frame, IReadOnlyList<BoundarySegment> boundarySegments)
    {
        var parts = new List<string>();

        foreach (var c in contours)
        {
            parts.Add(c.Type.ToString());
            for (int i = 0; i < c.X.Count; i++)
                parts.Add($"{Fmt(c.X[i])},{Fmt(c.Y[i])}");
        }

        parts.Add(FrameFingerprint(frame));

        foreach (var seg in boundarySegments)
            parts.Add($"{seg.Loop}:{seg.HoleIndex}:{seg.StartVertex}:{seg.EndVertex}:{seg.Role}");

        return Hash(string.Join("|", parts));
    }

    static string FrameFingerprint(Frame3D f) => string.Join(",",
        Fmt(f.Origin.X), Fmt(f.Origin.Y), Fmt(f.Origin.Z),
        Fmt(f.LocalX.X), Fmt(f.LocalX.Y), Fmt(f.LocalX.Z),
        Fmt(f.LocalY.X), Fmt(f.LocalY.Y), Fmt(f.LocalY.Z),
        Fmt(f.LocalZ.X), Fmt(f.LocalZ.Y), Fmt(f.LocalZ.Z));

    static string Fmt(double v) => v.ToString("G17", CultureInfo.InvariantCulture);

    static string Hash(string input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
}
