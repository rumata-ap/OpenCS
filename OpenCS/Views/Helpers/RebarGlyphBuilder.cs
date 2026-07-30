using CScore;
using CScore.PlateRebar;

namespace OpenCS.Views.Helpers;

/// <summary>Строит элементы отрисовки (штриховка направлений + текстовая подпись) для
/// одного слоя армирования (зоны или фона) на заданном полигоне. Для зоны — её контур,
/// для фона — bbox корпуса плиты как 4-точечный прямоугольник (см. RefreshRebarPlotElements).</summary>
public static class RebarGlyphBuilder
{
    public static List<PlotElement> Build(
        IReadOnlyList<(double U, double V)> polygon, PlateRebarLayer layout, Material? material)
    {
        var result = new List<PlotElement>();
        if (polygon.Count < 3) return result;

        bool hasX = layout.DiameterX > 0 || layout.SpacingX > 0;
        bool hasY = layout.DiameterY > 0 || layout.SpacingY > 0;
        if (!hasX && !hasY) return result;

        if (hasX)
            foreach (var seg in RebarHatchGeometry.BuildDirectionX(polygon, layout.Angle))
                result.Add(new ScatterElement
                {
                    Xs = [seg.U1, seg.U2], Ys = [seg.V1, seg.V2],
                    Stroke = System.Windows.Media.Brushes.DarkRed, StrokeThickness = 1
                });

        if (hasY)
            foreach (var seg in RebarHatchGeometry.BuildDirectionY(polygon, layout.Angle))
                result.Add(new ScatterElement
                {
                    Xs = [seg.U1, seg.U2], Ys = [seg.V1, seg.V2],
                    Stroke = System.Windows.Media.Brushes.DarkBlue, StrokeThickness = 1
                });

        var (cu, cv) = RebarHatchGeometry.Centroid(polygon);
        var lines = new List<string>();
        if (hasX) lines.Add($"X ⌀{layout.DiameterX * 1000:0.#}×{layout.SpacingX * 1000:0}");
        if (hasY) lines.Add($"Y ⌀{layout.DiameterY * 1000:0.#}×{layout.SpacingY * 1000:0}");
        lines.Add(material?.Tag ?? "гл.");
        result.Add(new TextElement { X = cu, Y = cv, Text = string.Join("\n", lines) });

        return result;
    }
}
