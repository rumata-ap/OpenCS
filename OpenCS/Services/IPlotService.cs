namespace OpenCS.Services
{
   /// <summary>
   /// Сервис отрисовки графиков. Абстрагирует WPF-рендеринг от ViewModel.
   /// </summary>
   public interface IPlotService
   {
      void Clear();
      void AddScatter(double[] xs, double[] ys, double lineWidth = 1, string color = null, string label = null);
      void AddLine(double[] xs, double[] ys, string label = null);
      void AddPolygon(double[] xs, double[] ys, string fillColor = null, string lineColor = null);
      void AddCircle(double x, double y, double radius, string fillColor = null, string lineColor = null, float lineWidth = 1);
      void AddMarkers(double[] xs, double[] ys, float markerSize = 4, string color = null, string label = null);
      void ShowLegend(bool show = true);
      void EnableSquareAxes();
      void AutoScale();
      void SetAxisLimits(double xMin, double xMax, double yMin, double yMax);
      void SetTitle(string title);
      void SetXLabel(string label);
      void SetYLabel(string label);
      void ApplySettings(Utilites.PlotSettings settings);
      void Refresh();
   }
}