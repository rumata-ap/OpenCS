using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CScore.Planar;

/// <summary>Детерминированный fingerprint normalized boundary action.</summary>
public static class PlanarBoundaryActionFingerprint
{
    /// <summary>Вычисляет SHA-256 по геометрии, samples, modes и provenance.</summary>
    public static string Compute(PlanarBoundaryActionProviderResult result, PlanarCutInterface cut)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(cut);
        var parts = new List<string>
        {
            result.SourceMode.ToString(),
            cut.Id,
            cut.Kind.ToString(),
            F(cut.NormalFromFragmentToOmittedSide.X),
            F(cut.NormalFromFragmentToOmittedSide.Y),
            F(cut.NormalFromFragmentToOmittedSide.Z),
            cut.ModeByDof.ToString()
        };
        foreach (var action in result.ForceActions.OrderBy(action => action.InterfaceId, StringComparer.Ordinal))
        {
            parts.Add($"force:{action.InterfaceId}:{action.DofMask}:{action.UnitSystem}:{action.Interpolation}");
            parts.AddRange(action.Samples.Select(sample =>
                $"{F(sample.S)}:{F(sample.ForcePerLength.X)}:{F(sample.ForcePerLength.Y)}:{F(sample.ForcePerLength.Z)}:" +
                $"{F(sample.MomentPerLength.X)}:{F(sample.MomentPerLength.Y)}:{F(sample.MomentPerLength.Z)}"));
        }
        foreach (var action in result.KinematicActions.OrderBy(action => action.InterfaceId, StringComparer.Ordinal))
        {
            parts.Add($"kinematic:{action.InterfaceId}:{action.DofMask}:{action.UnitSystem}:{action.Interpolation}");
            parts.AddRange(action.Samples.Select(sample =>
                $"{F(sample.S)}:{F(sample.Displacement.X)}:{F(sample.Displacement.Y)}:{F(sample.Displacement.Z)}:" +
                $"{F(sample.Rotation.X)}:{F(sample.Rotation.Y)}:{F(sample.Rotation.Z)}"));
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", parts)));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    static string F(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
}
