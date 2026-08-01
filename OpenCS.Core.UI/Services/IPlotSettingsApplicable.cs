using OpenCS.Utilites;

namespace OpenCS.Services
{
   /// <summary>Контракт страницы, реагирующей на изменение настроек графиков (ApplyPlotSettings).
   /// Реализуется страницами GUI-проектов (WPF: ContourPlot, MaterialAreaPage, CrossSectionPage, RebarGroupEditorPage).</summary>
   public interface IPlotSettingsApplicable
   {
      /// <summary>Применяет текущие настройки графиков к активным IPlotService страницы.</summary>
      void ApplyPlotSettings(PlotSettings settings);
   }
}
