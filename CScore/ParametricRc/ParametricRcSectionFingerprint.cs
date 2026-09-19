using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CScore.ParametricRc;

/// <summary>Создаёт устойчивый отпечаток смысловой геометрии параметрического сечения.</summary>
public static class ParametricRcSectionFingerprint
{
    /// <summary>Вычисляет SHA-256 без учёта расчётного состояния и параметров сетки.</summary>
    public static string Compute(CrossSection section, int generatorVersion)
    {
        ArgumentNullException.ThrowIfNull(section);
        var tokens = new List<string> { "parametric-rc", generatorVersion.ToString(CultureInfo.InvariantCulture) };
        foreach (var area in section.Areas.OrderBy(AreaKey, StringComparer.Ordinal))
        {
            tokens.Add($"area|{area.Category}|{area.MaterialId}|host:{HostKey(area)}|pool:{PoolKey(area)}|{area.RebarRepresentation}|{area.IdealizedAxis}");
            foreach (var contour in area.Contours)
            {
                tokens.Add($"contour|{contour.Type}|{contour.IsPolyline}");
                for (int i = 0; i < Math.Min(contour.X.Count, contour.Y.Count); i++)
                    tokens.Add($"p|{F(contour.X[i])}|{F(contour.Y[i])}");
            }
            foreach (var fiber in area.Fibers.Where(f => f.TypeFiber == FiberType.point)
                         .OrderBy(f => f.X).ThenBy(f => f.Y).ThenBy(f => f.Area).ThenBy(f => f.Diameter))
                tokens.Add($"bar|{F(fiber.X)}|{F(fiber.Y)}|{F(fiber.Area)}|{F(fiber.Diameter)}");
            foreach (var group in area.Stirrups.OrderBy(g => g.MaterialId).ThenBy(g => g.SpacingM))
            {
                tokens.Add($"stirrup|{group.MaterialId}|{F(group.SpacingM)}|{F(group.OffsetM ?? 0)}");
                foreach (var element in group.Elements.OrderBy(e => e.CenterlineContour.WKT, StringComparer.Ordinal))
                {
                    tokens.Add($"element|{F(element.BarAreaM2)}|{F(element.BarDiameterM)}|{JsonSerializer.Serialize(element.Source)}");
                    var c = element.CenterlineContour;
                    for (int i = 0; i < Math.Min(c.X.Count, c.Y.Count); i++) tokens.Add($"s|{F(c.X[i])}|{F(c.Y[i])}");
                }
            }
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", tokens))));
    }

    static string AreaKey(MaterialArea area)
    {
        string contour = string.Join('|', area.Contours.Select(c =>
            $"{c.Type}:{c.IsPolyline}:{string.Join(';', c.X.Zip(c.Y, (x, y) => F(x) + ',' + F(y)))}"));
        string bars = string.Join(';', area.Fibers.Where(f => f.TypeFiber == FiberType.point)
            .OrderBy(f => f.X).ThenBy(f => f.Y).Select(f => $"{F(f.X)},{F(f.Y)},{F(f.Area)},{F(f.Diameter)}"));
        string stirrups = string.Join('|', area.Stirrups
            .OrderBy(g => g.MaterialId).ThenBy(g => g.SpacingM)
            .Select(g => $"{g.MaterialId}:{F(g.SpacingM)}:{string.Join(';', g.Elements
                .OrderBy(e => e.CenterlineContour.WKT, StringComparer.Ordinal)
                .Select(e => e.CenterlineContour.WKT))}"));
        return $"{area.Category}|{area.MaterialId}|{contour}|{bars}|{stirrups}|{area.RebarRepresentation}|{area.IdealizedAxis}";
    }

    static string HostKey(MaterialArea area) => area.HostArea is null ? "" : AreaKey(area.HostArea);

    static string PoolKey(MaterialArea area)
    {
        var contour = area.PoolContour;
        return contour is null
            ? ""
            : string.Join(';', contour.X.Zip(contour.Y, (x, y) => F(x) + ',' + F(y)));
    }

    static string F(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
}
