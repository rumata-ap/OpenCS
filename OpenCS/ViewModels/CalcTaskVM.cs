using CScore;
using OpenCS.Utilites;
using System.Collections.ObjectModel;

namespace OpenCS.ViewModels
{
   /// <summary>
   /// ViewModel расчётной задачи. Хранит ссылки на отображаемые имена сечения и набора усилий.
   /// </summary>
   public class CalcTaskVM : ViewModelBase
   {
      public CalcTask Model { get; }

      string sectionTag   = "";
      string forceSetTag  = "";
      string forceItemLabel = "";

      /// <summary>Отображаемое имя связанного сечения.</summary>
      public string SectionTag
      {
         get => sectionTag;
         set { sectionTag = value; OnPropertyChanged(); OnPropertyChanged(nameof(SummaryLine)); }
      }

      /// <summary>Отображаемое имя набора усилий.</summary>
      public string ForceSetTag
      {
         get => forceSetTag;
         set { forceSetTag = value; OnPropertyChanged(); OnPropertyChanged(nameof(SummaryLine)); }
      }

      /// <summary>Метка строки усилий.</summary>
      public string ForceItemLabel
      {
         get => forceItemLabel;
         set { forceItemLabel = value; OnPropertyChanged(); OnPropertyChanged(nameof(SummaryLine)); }
      }

      /// <summary>Краткая строка для отображения в TreeView.</summary>
      public string SummaryLine =>
         $"{Model.Num:D3}  {Model.Tag}  [{Model.Kind} / {Model.CalcType}]";

      /// <summary>Результаты выполнения этой задачи (отфильтрованные из общей коллекции).</summary>
      public ObservableCollection<CalcResult> Results { get; } = [];

      public CalcTaskVM(CalcTask model) => Model = model;
   }
}
