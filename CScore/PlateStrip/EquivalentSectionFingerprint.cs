using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CScore.PlateStrip;

/// <summary>Детерминированные отпечатки входов и результата редукции.</summary>
public static class EquivalentSectionFingerprint
{
    public static string Compute(
        PlateStripBeamAnalogy analogy,
        int sourceSchemaId,
        string sourceRegionFingerprint,
        double spanStationFraction,
        IPlateSectionResponse centerlineSource,
        IReadOnlyList<IPlateSectionResponse> widthSources,
        ReductionPolicy policy,
        int widthIntegrationPoints)
    {
        ArgumentNullException.ThrowIfNull(analogy);
        ArgumentNullException.ThrowIfNull(centerlineSource);
        ArgumentNullException.ThrowIfNull(widthSources);

        var parts = new List<string>
        {
            $"strip:{analogy.Fingerprint}",
            $"region:{analogy.SourceRegionId}",
            $"width:{analogy.ExplicitWidthM.ToString("G17", CultureInfo.InvariantCulture)}",
            $"schema:{sourceSchemaId}",
            $"region-fp:{sourceRegionFingerprint}",
            $"station:{spanStationFraction.ToString("G17", CultureInfo.InvariantCulture)}",
            $"policy:{policy}",
            $"points:{widthIntegrationPoints}"
        };
        AddSource(parts, "centerline", centerlineSource);
        for (int i = 0; i < widthSources.Count; i++)
            AddSource(parts, $"width{i}", widthSources[i]);
        return Hash(parts);
    }

    public static string ComputeResult(double[,] tangent)
    {
        var parts = new List<string>();
        AddMatrix(parts, tangent, "KBeam");
        return Hash(parts);
    }

    static void AddSource(List<string> parts, string label, IPlateSectionResponse source)
    {
        parts.Add($"{label}-kind:{source.SourceKind}");
        parts.Add($"{label}-fp:{source.Fingerprint}");
        var tangent = source.Tangent(ShellStrainState.Zero);
        AddMatrix(parts, tangent.A, $"{label}-A");
        AddMatrix(parts, tangent.B, $"{label}-B");
        AddMatrix(parts, tangent.D, $"{label}-D");
        AddMatrix(parts, tangent.As, $"{label}-As");
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
