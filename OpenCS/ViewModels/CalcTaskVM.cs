using CScore;
using OpenCS.Services;
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

      bool isExpanded;

      /// <summary>Узел задачи раскрыт в дереве; ставится, когда у задачи появился новый результат.</summary>
      public bool IsExpanded
      {
         get => isExpanded;
         set { if (isExpanded == value) return; isExpanded = value; OnPropertyChanged(); }
      }

      public CalcTaskVM(CalcTask model) => Model = model;

      /// <summary>
      /// Подписи сечения, набора и строки по виду задачи: сечение пластины, огневое (его стержневое сечение) или
      /// стержневое; набор — среди всех наборов; метка строки — запросом к базе, без загрузки строк набора
      /// (наборы РСУ бывают в миллионы строк). Не найдено — «?», нет ссылки — пусто.
      /// </summary>
      public static CalcTaskVM Build(CalcTask ct, DatabaseService db)
      {
         var vm = new CalcTaskVM(ct);
         var spec = CalcTaskKinds.Get(ct.Kind);
         if (ct.SectionId != 0)
         {
            string? tag = spec?.Section == CalcTaskSectionKind.Plate
               ? db.PlateSections.FirstOrDefault(s => s.Id == ct.SectionId)?.Tag
               : db.CrossSections.FirstOrDefault(s => s.Id == ct.SectionId)?.Tag;
            vm.sectionTag = tag ?? "?";
         }
         if (ct.ForceSetId != 0)
         {
            var fs = db.ForceSets.FirstOrDefault(f => f.Id == ct.ForceSetId);
            vm.forceSetTag = fs?.Tag ?? "?";
            if (ct.ForceItemId != 0)
            {
               var row = fs == null ? null : db.ForceItemRef(ct.ForceItemId, fs.Kind == "shell");
               vm.forceItemLabel = row is { } r ? (string.IsNullOrWhiteSpace(r.Label) ? r.Num.ToString() : r.Label) : "?";
            }
         }
         return vm;
      }
   }
}
