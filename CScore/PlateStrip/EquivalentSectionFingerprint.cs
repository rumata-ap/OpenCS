using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CScore.PlateStrip;

/// <summary>Детерминированные отпечатки входов и результата редукции.</summary>
public static class EquivalentSectionFingerprint
{
    public static string Compute(
        PlateStripBeamAnalogy analogy,
        IPlateSectionResponse source,
        ReductionPolicy policy,
        int widthIntegrationPoints)
    {
        ArgumentNullException.ThrowIfNull(analogy);
        ArgumentNullException.ThrowIfNull(source);
        var tangent = source.Tangent(ShellStrainState.Zero);
        var parts = new List<string>
        {
            $"strip:{analogy.Fingerprint}",
            $"region:{analogy.SourceRegionId}",
            $"width:{analogy.ExplicitWidthM.ToString("G17", CultureInfo.InvariantCulture)}",
            $"policy:{policy}",
            $"points:{widthIntegrationPoints}",
            $"source-kind:{source.SourceKind}",
            $"source:{source.Fingerprint}"
        };
        AddMatrix(parts, tangent.A, "A");
        AddMatrix(parts, tangent.B, "B");
        AddMatrix(parts, tangent.D, "D");
        AddMatrix(parts, tangent.As, "As");
        return Hash(parts);
    }

    public static string ComputeResult(double[,] tangent)
    {
        var parts = new List<string>();
        AddMatrix(parts, tangent, "KBeam");
        return Hash(parts);
    }

    static void AddMatrix(List<string> parts, double[,] matrix, string name)
    {
        for (int i = 0; i < matrix.GetLength(0); i++)
        for (int j = 0; j < matrix.GetLength(1); j++)
            parts.Add($"{name}:{i}:{j}:{matrix[i, j].ToString("G17", CultureInfo.InvariantCulture)}");
    }

    static string Hash(IEnumerable<string> parts) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", parts))));
}
