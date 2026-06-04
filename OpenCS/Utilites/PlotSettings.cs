using System.Text.Json.Serialization;

namespace OpenCS.Utilites
{
   /// <summary>
   /// Глобальные настройки отображения графиков. Сериализуются в JSON.
   /// </summary>
   public class PlotSettings
   {
      [JsonPropertyName("bg")]
      public string Background { get; set; } = "#FFFFFF";

      [JsonPropertyName("grid")]
      public string Grid { get; set; } = "#D3D3D3";

      [JsonPropertyName("curve")]
      public string Curve { get; set; } = "#003A6C";

      [JsonPropertyName("fill")]
      public string Fill { get; set; } = "#F0EACD50";

      [JsonPropertyName("marker")]
      public string MarkerFill { get; set; } = "#003A6C";

      [JsonPropertyName("text")]
      public string Text { get; set; } = "#333333";

      [JsonPropertyName("highlight")]
      public string Highlight { get; set; } = "#FF0000";

      [JsonPropertyName("curveThk")]
      public double CurveThickness { get; set; } = 1.5;

      [JsonPropertyName("markerSize")]
      public double MarkerSize { get; set; } = 4;

      [JsonPropertyName("fontSize")]
      public double FontSize { get; set; } = 9;

      [JsonPropertyName("showGrid")]
      public bool ShowGrid { get; set; } = true;

      [JsonPropertyName("showLabels")]
      public bool ShowPointLabels { get; set; }

      [JsonPropertyName("showTooltips")]
      public bool ShowTooltips { get; set; } = true;

      [JsonPropertyName("showAxesVals")]
      public bool ShowAxesValues { get; set; } = true;

      [JsonPropertyName("axesOrigin")]
      public bool AxesAtOrigin { get; set; }

      [JsonPropertyName("axesColor")]
      public string AxesColor { get; set; } = "#000000";

      [JsonPropertyName("axesFontSize")]
      public double AxesFontSize { get; set; } = 10;

      [JsonPropertyName("gridThk")]
      public double GridThickness { get; set; } = 0.3;

      [JsonPropertyName("tickCount")]
      public int TickCount { get; set; } = 6;

      [JsonPropertyName("scaleY")]
      public double ScaleY { get; set; } = 1.0;

      [JsonPropertyName("scaleX")]
      public double ScaleX { get; set; } = 1.0;

      [JsonPropertyName("dxfCanvasBg")]
      public string DxfCanvasBackground { get; set; } = "#F5F5F5";

      public static PlotSettings Default => new();

      public PlotSettings Clone() => new()
      {
         Background = Background, Grid = Grid, Curve = Curve,
         Fill = Fill, MarkerFill = MarkerFill, Text = Text,
         Highlight = Highlight,
         CurveThickness = CurveThickness, MarkerSize = MarkerSize,
         FontSize = FontSize,
         ShowGrid = ShowGrid, ShowPointLabels = ShowPointLabels,
         ShowTooltips = ShowTooltips,
         ShowAxesValues = ShowAxesValues, AxesAtOrigin = AxesAtOrigin,
         AxesColor = AxesColor, AxesFontSize = AxesFontSize,
         GridThickness = GridThickness, TickCount = TickCount,
         ScaleX = ScaleX, ScaleY = ScaleY,
         DxfCanvasBackground = DxfCanvasBackground
      };
   }
}
