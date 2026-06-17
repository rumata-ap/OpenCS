using CScore;
using CScore.Fire;
using CScore.Fire.Entities;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using System.Globalization;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Input;

namespace OpenCS.Views
{
   /// <summary>
   /// Базовое представление огневого сечения с запуском теплового расчёта.
   /// </summary>
   public partial class FireSectionView : UserControl
   {
      public FireSectionView(FireSectionDef model, AppViewModel app)
      {
         InitializeComponent();
         DataContext = new FireSectionViewModel(model, app);
      }
   }

   /// <summary>
   /// ViewModel представления огневого сечения.
   /// </summary>
   public class FireSectionViewModel : ViewModelBase
   {
      readonly FireSectionDef _model;
      readonly AppViewModel _app;

      string tag;
      string fireDurationMinText;
      string fireCurve;
      string meshStepMText;
      string timeStepSText;
      string bcPreset;
      string lastRunInfo = "";

      public ICommand SaveCommand { get; }
      public ICommand RunThermalCommand { get; }

      public string Tag
      {
         get => tag;
         set { tag = value; OnPropertyChanged(); }
      }

      public string LinkedCrossSectionName
      {
         get
         {
            var sec = _app.CrossSections.FirstOrDefault(x => x.Id == _model.SectionId);
            return sec?.Tag ?? Loc.S("FireSection_SectionNotFound");
         }
      }

      public string FireDurationMinText
      {
         get => fireDurationMinText;
         set { fireDurationMinText = value; OnPropertyChanged(); }
      }

      public string FireCurve
      {
         get => fireCurve;
         set { fireCurve = value; OnPropertyChanged(); }
      }

      public string MeshStepMText
      {
         get => meshStepMText;
         set { meshStepMText = value; OnPropertyChanged(); }
      }

      public string TimeStepSText
      {
         get => timeStepSText;
         set { timeStepSText = value; OnPropertyChanged(); }
      }

      public string BcPreset
      {
         get => bcPreset;
         set { bcPreset = value; OnPropertyChanged(); }
      }

      public string LastRunInfo
      {
         get => lastRunInfo;
         set { lastRunInfo = value; OnPropertyChanged(); }
      }

      public FireThermalResultVM ThermalResult { get; private set; }

      public FireSectionViewModel(FireSectionDef model, AppViewModel app)
      {
         _model = model;
         _app = app;
         tag = model.Tag;
         fireDurationMinText = model.FireDurationMin.ToString("G", CultureInfo.InvariantCulture);
         fireCurve = model.FireCurve;
         meshStepMText = model.MeshStepM.ToString("G", CultureInfo.InvariantCulture);
         timeStepSText = model.TimeStepS.ToString("G", CultureInfo.InvariantCulture);
         bcPreset = model.BcPreset;
         ThermalResult = BuildThermalVm();
         SaveCommand = new RelayCommand(_ => Save());
         RunThermalCommand = new RelayCommand(_ => RunThermal());
      }

      FireThermalResultVM BuildThermalVm()
      {
         if (_model.Id <= 0)
            return new FireThermalResultVM(_model, null, null);

         int? rid = _app.db.GetLatestFireThermalResultId(_model.Id);
         if (rid is null)
            return new FireThermalResultVM(_model, null, null);

         try
         {
            var thermal = _app.db.LoadFireThermalResult(rid.Value);
            return new FireThermalResultVM(_model, thermal, rid);
         }
         catch
         {
            return new FireThermalResultVM(_model, null, null);
         }
      }

      void RefreshThermalResult()
      {
         ThermalResult = BuildThermalVm();
         OnPropertyChanged(nameof(ThermalResult));
      }

      void Save()
      {
         ApplyFormToModel();
         _app.db.SaveFireSection(_model);
         _app.IsDirty = true;
         LastRunInfo = Loc.S("FireSection_Saved");
         _app.LogService.Info(string.Format(Loc.S("FireSection_SavedLog"), _model.Tag));
      }

      void RunThermal()
      {
         ApplyFormToModel();
         var section = _app.CrossSections.FirstOrDefault(s => s.Id == _model.SectionId);
         if (section == null)
         {
            LastRunInfo = Loc.S("FireSection_SectionNotFound");
            _app.LogService.Error(Loc.S("FireSection_SectionNotFound"));
            return;
         }

         try
         {
            if (_model.Id == 0)
               _app.db.SaveFireSection(_model);

            var result = FireThermalService.Run(_model, section, ResolveAggregateType(section));
            int resultId = _app.db.SaveFireThermalResult(_model.Id, result);
            _app.IsDirty = true;
            RefreshThermalResult();
            LastRunInfo = string.Format(Loc.S("FireSection_RunOk"), resultId);
            _app.LogService.Info(string.Format(Loc.S("FireSection_RunOkLog"), _model.Tag, resultId));
         }
         catch (Exception ex)
         {
            LastRunInfo = string.Format(Loc.S("FireSection_RunError"), ex.Message);
            _app.LogService.Error(string.Format(Loc.S("FireSection_RunError"), ex.Message));
         }
      }

      void ApplyFormToModel()
      {
         _model.Tag = string.IsNullOrWhiteSpace(Tag)
            ? string.Format(Loc.S("FireSection_DefaultTag"), _app.FireSections.Count + 1)
            : Tag.Trim();
         _model.FireDurationMin = ParsePositiveOrDefault(FireDurationMinText, _model.FireDurationMin, 60.0);
         _model.FireCurve = string.IsNullOrWhiteSpace(FireCurve) ? "iso834" : FireCurve;
         _model.MeshStepM = ParsePositiveOrDefault(MeshStepMText, _model.MeshStepM, 0.01);
         _model.TimeStepS = ParsePositiveOrDefault(TimeStepSText, _model.TimeStepS, 5.0);
         _model.BcPreset = string.IsNullOrWhiteSpace(BcPreset) ? "manual" : BcPreset;
      }

      static double ParsePositiveOrDefault(string? text, double current, double fallback)
      {
         if (string.IsNullOrWhiteSpace(text)) return current > 0.0 ? current : fallback;
         string normalized = text.Trim().Replace(',', '.');
         if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return current > 0.0 ? current : fallback;
         return value > 0.0 ? value : (current > 0.0 ? current : fallback);
      }

      string ResolveAggregateType(CrossSection section)
      {
         foreach (var area in section.Areas)
         {
            var mat = area.Material ?? _app.Materials.FirstOrDefault(m => m.Id == area.MaterialId);
            if (mat?.Type == MatType.Concrete)
               return string.IsNullOrWhiteSpace(mat.AggregateType) ? "silicate" : mat.AggregateType;
         }
         return "silicate";
      }
   }
}
