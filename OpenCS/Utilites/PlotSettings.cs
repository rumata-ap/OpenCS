using Newtonsoft.Json;

namespace OpenCS.Utilites
{
   /// <summary>
   /// Глобальные настройки отображения графиков. Сериализуются в JSON.
   /// </summary>
   public class PlotSettings
   {
      [JsonProperty("bg")]
      public string Background { get; set; } = "#FFFFFF";

      [JsonProperty("grid")]
      public string Grid { get; set; } = "#D3D3D3";

      [JsonProperty("curve")]
      public string Curve { get; set; } = "#003A6C";

      [JsonProperty("fill")]
      public string Fill { get; set; } = "#F0EACD50";

      [JsonProperty("marker")]
      public string MarkerFill { get; set; } = "#003A6C";

      [JsonProperty("text")]
      public string Text { get; set; } = "#333333";

      [JsonProperty("highlight")]
      public string Highlight { get; set; } = "#FF0000";

      [JsonProperty("curveThk")]
      public double CurveThickness { get; set; } = 1.5;

      [JsonProperty("markerSize")]
      public double MarkerSize { get; set; } = 4;

      [JsonProperty("fontSize")]
      public double FontSize { get; set; } = 9;

      [JsonProperty("showGrid")]
      public bool ShowGrid { get; set; } = true;

      [JsonProperty("showLabels")]
      public bool ShowPointLabels { get; set; }

      [JsonProperty("showTooltips")]
      public bool ShowTooltips { get; set; } = true;

      [JsonProperty("showAxesVals")]
      public bool ShowAxesValues { get; set; } = true;

      [JsonProperty("axesOrigin")]
      public bool AxesAtOrigin { get; set; }

      [JsonProperty("axesColor")]
      public string AxesColor { get; set; } = "#000000";

      [JsonProperty("axesFontSize")]
      public double AxesFontSize { get; set; } = 10;

      [JsonProperty("gridThk")]
      public double GridThickness { get; set; } = 0.3;

      [JsonProperty("tickCount")]
      public int TickCount { get; set; } = 6;

      [JsonProperty("scaleY")]
      public double ScaleY { get; set; } = 1.0;

      [JsonProperty("scaleX")]
      public double ScaleX { get; set; } = 1.0;

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
         ScaleX = ScaleX, ScaleY = ScaleY
      };
   }
}
