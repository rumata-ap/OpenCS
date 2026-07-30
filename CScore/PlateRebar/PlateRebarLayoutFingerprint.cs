using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CScore.PlateRebar;

/// <summary>Канонический SHA-256 отпечаток содержимого списка слоёв армирования — ключ
/// дедупликации секций/откликов сеточных движков (OpenSees, CSfea) по уникальным
/// сочетаниям армирования на элемент. Идиома идентична PlanarGeometryFingerprint
/// (CScore.Planar) и PlateSectionOpenSeesMapper.Fingerprint (OpenCS.OpenSees.CScore).</summary>
public static class PlateRebarLayoutFingerprint
{
    public static string Compute(IReadOnlyList<PlateRebarLayer> layers)
    {
        var parts = layers.Select(layer => string.Join(",",
            layer.Face.ToString(),
            layer.Asx.ToString("G17", CultureInfo.InvariantCulture),
            layer.Asy.ToString("G17", CultureInfo.InvariantCulture),
            layer.Zsx.ToString("G17", CultureInfo.InvariantCulture),
            layer.Zsy.ToString("G17", CultureInfo.InvariantCulture),
            layer.Angle.ToString("G17", CultureInfo.InvariantCulture),
            layer.MaterialId.ToString(CultureInfo.InvariantCulture)));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", parts))))
            .ToLowerInvariant();
    }
}
