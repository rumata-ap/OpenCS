using CScore;
using OpenCS.Services;
using OpenCS.Utilites;

using System.Collections.ObjectModel;
using System.Windows.Input;

namespace OpenCS.ViewModels
{
   /// <summary>ViewModel строки арматурного слоя на странице сечения пластины (правит слой черновика).</summary>
   public class PlateRebarLayerVM : ViewModelBase
   {
      readonly PlateRebarLayer _model;
      readonly Action _onChanged;

      public PlateRebarLayerVM(PlateRebarLayer model, Action onChanged, IReadOnlyList<Material> armatures)
      {
         _model = model;
         _onChanged = onChanged;
         _rebarMaterial = armatures.FirstOrDefault(m => m.Id == model.MaterialId);
      }

      public PlateRebarLayer Model => _model;

      void Changed(bool recalc = false)
      {
         if (recalc)
         {
            _model.RecalcArea();
            OnPropertyChanged(nameof(AsxCm2));
            OnPropertyChanged(nameof(AsyCm2));
         }
         _onChanged();
      }

      Material? _rebarMaterial;
      /// <summary>Материал арматуры слоя. null (MaterialId=0) — общий материал арматуры сечения.</summary>
      public Material? RebarMaterial
      {
         get => _rebarMaterial;
         set { _rebarMaterial = value; _model.MaterialId = value?.Id ?? 0; OnPropertyChanged(); Changed(); }
      }

      public string Name
      {
         get => _model.Name;
         set { _model.Name = value; OnPropertyChanged(); Changed(); }
      }

      public string InputMode
      {
         get => _model.InputMode;
         set { _model.InputMode = value; OnPropertyChanged(); Changed(recalc: true); }
      }

      // Ввод в мм и см²/м; в слое — м и м²/м.
      public double DiameterXMm
      {
         get => _model.DiameterX * 1000.0;
         set { _model.DiameterX = value / 1000.0; OnPropertyChanged(); Changed(recalc: true); }
      }

      public double DiameterYMm
      {
         get => _model.DiameterY * 1000.0;
         set { _model.DiameterY = value / 1000.0; OnPropertyChanged(); Changed(recalc: true); }
      }

      public double SpacingXMm
      {
         get => _model.SpacingX * 1000.0;
         set { _model.SpacingX = value / 1000.0; OnPropertyChanged(); Changed(recalc: true); }
      }

      public double SpacingYMm
      {
         get => _model.SpacingY * 1000.0;
         set { _model.SpacingY = value / 1000.0; OnPropertyChanged(); Changed(recalc: true); }
      }

      public double ZsxMm
      {
         get => _model.Zsx * 1000.0;
         set { _model.Zsx = value / 1000.0; OnPropertyChanged(); Changed(); }
      }

      public double ZsyMm
      {
         get => _model.Zsy * 1000.0;
         set { _model.Zsy = value / 1000.0; OnPropertyChanged(); Changed(); }
      }

      public double CountPerMeterX
      {
         get => _model.CountPerMeterX;
         set { _model.CountPerMeterX = value; OnPropertyChanged(); Changed(recalc: true); }
      }

      public double CountPerMeterY
      {
         get => _model.CountPerMeterY;
         set { _model.CountPerMeterY = value; OnPropertyChanged(); Changed(recalc: true); }
      }

      /// <summary>Площадь по X, см²/м. В режиме «direct» вводится, иначе — из Ø и шага (числа).</summary>
      public double AsxCm2
      {
         get => _model.Asx * 1e4;
         set { _model.Asx = value / 1e4; OnPropertyChanged(); Changed(); }
      }

      /// <summary>Площадь по Y, см²/м.</summary>
      public double AsyCm2
      {
         get => _model.Asy * 1e4;
         set { _model.Asy = value / 1e4; OnPropertyChanged(); Changed(); }
      }
   }

   /// <summary>Опция списка: англ. ключ + локализованная подпись.</summary>
   public sealed record InputModeOption(string Key, string Display);

   /// <summary>
   /// ViewModel страницы сечения пластины. Правится черновик (копия сечения проекта): расчёт видит сечение только
   /// после «Сохранить» — проверка и запись через <see cref="PlateSectionAuthoring"/>; «*» и вопрос при уходе — по
   /// отпечатку записанного состояния.
   /// </summary>
   public class PlateSectionVM : ViewModelBase, IUnsavedChanges
   {
      readonly PlateSection _model;
      PlateSection _draft;
      string _saved;
      string _savedTag;

      public PlateSectionVM(PlateSection model, AppViewModel app)
      {
         _model = model;
         App = app;
         _draft = PlateSectionAuthoring.Draft(model);
         _saved = PlateSectionAuthoring.Fingerprint(_draft);
         _savedTag = model.Tag;

         SofteningOptions = [Loc.S("PlateSofteningNone"), Loc.S("PlateSofteningVC")];
         InputModeOptions =
         [
            new InputModeOption("diameter_spacing", Loc.S("PlateLayerModeSpacing")),
            new InputModeOption("diameter_count",   Loc.S("PlateLayerModeCount")),
            new InputModeOption("direct",           Loc.S("PlateLayerModeDirect")),
         ];
         PlateModelOptions =
         [
            new InputModeOption("layered",          Loc.S("PlateModelLayered")),
            new InputModeOption("char1d_principal", Loc.S("PlateModelChar1dPrincipal")),
            new InputModeOption("char1d_axial",     Loc.S("PlateModelChar1dAxial")),
         ];
         RebarLayers = [];
         LoadDraft();

         SaveCommand             = new RelayCommand(_ => Save());
         DeleteCommand           = new RelayCommand(_ => App.DeletePlateSection(_model));
         AddRebarLayerCommand    = new RelayCommand(_ => AddRebarLayer());
         DeleteRebarLayerCommand = new RelayCommand(_ => DeleteRebarLayer());
      }

      /// <summary>Слои и материалы — из черновика (после создания и «Не сохранять»).</summary>
      void LoadDraft()
      {
         RebarLayers.Clear();
         foreach (var l in _draft.RebarLayers)
            RebarLayers.Add(new PlateRebarLayerVM(l, Touch, App.Armatures));
         _concreteMaterial = App.Concretes.FirstOrDefault(m => m.Id == _draft.ConcreteMaterialId);
         _rebarMaterial = App.Armatures.FirstOrDefault(m => m.Id == _draft.RebarMaterialId);
         _selectedRebarLayer = null;
      }

      public AppViewModel App { get; }

      /// <summary>Сечение проекта, которое показывает страница (для новой — ещё не в пуле).</summary>
      public PlateSection Model => _model;

      /// <summary>Черновик правки (тесты).</summary>
      internal PlateSection Draft => _draft;

      public string Tag
      {
         get => _draft.Tag;
         set { _draft.Tag = value; OnPropertyChanged(); Touch(); }
      }

      /// <summary>Толщина в мм (UI-единицы).</summary>
      public double HMm
      {
         get => _draft.H * 1000.0;
         set { _draft.H = value / 1000.0; OnPropertyChanged(); Touch(); }
      }

      public int NLayers
      {
         get => _draft.NLayers;
         set { _draft.NLayers = value; OnPropertyChanged(); Touch(); }
      }

      Material? _concreteMaterial;
      public Material? ConcreteMaterial
      {
         get => _concreteMaterial;
         set { _concreteMaterial = value; _draft.ConcreteMaterialId = value?.Id ?? 0; OnPropertyChanged(); Touch(); }
      }

      Material? _rebarMaterial;
      public Material? RebarMaterial
      {
         get => _rebarMaterial;
         set { _rebarMaterial = value; _draft.RebarMaterialId = value?.Id ?? 0; OnPropertyChanged(); Touch(); }
      }

      public bool TensionConcrete
      {
         get => _draft.TensionConcrete;
         set { _draft.TensionConcrete = value; OnPropertyChanged(); Touch(); }
      }

      public string[] SofteningOptions { get; }
      public InputModeOption[] InputModeOptions { get; }
      public InputModeOption[] PlateModelOptions { get; }

      /// <summary>Нелинейная модель пластины (layered | char1d_principal | char1d_axial).</summary>
      public string PlateModel
      {
         get => string.IsNullOrEmpty(_draft.PlateModel) ? "layered" : _draft.PlateModel;
         set
         {
            _draft.PlateModel = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsLayered));
            Touch();
         }
      }

      /// <summary>NLayers применим только к слоистой модели.</summary>
      public bool IsLayered => PlateModel == "layered";

      /// <summary>Тип диаграммы бетона (строковый ключ enum DiagrammType).</summary>
      public string ConcreteDiagramType
      {
         get => _draft.ConcreteDiagramType.ToString();
         set
         {
            if (Enum.TryParse<DiagrammType>(value, out var dt))
               _draft.ConcreteDiagramType = dt;
            OnPropertyChanged();
            Touch();
         }
      }

      public int SofteningIndex
      {
         get => _draft.SofteningModel == "vecchio_collins" ? 1 : 0;
         set
         {
            _draft.SofteningModel = value == 1 ? "vecchio_collins" : "";
            OnPropertyChanged();
            Touch();
         }
      }

      public ObservableCollection<PlateRebarLayerVM> RebarLayers { get; }

      PlateRebarLayerVM? _selectedRebarLayer;
      public PlateRebarLayerVM? SelectedRebarLayer
      {
         get => _selectedRebarLayer;
         set { _selectedRebarLayer = value; OnPropertyChanged(); }
      }

      public ICommand SaveCommand { get; }
      public ICommand DeleteCommand { get; }
      public ICommand AddRebarLayerCommand { get; }
      public ICommand DeleteRebarLayerCommand { get; }

      /// <summary>Есть правки, не записанные в проект.</summary>
      public bool IsDirty => PlateSectionAuthoring.Fingerprint(_draft) != _saved;

      /// <summary>Обозначение для вопроса о сохранении.</summary>
      public string DisplayName => !string.IsNullOrWhiteSpace(_draft.Tag) ? _draft.Tag
         : !string.IsNullOrWhiteSpace(_savedTag) ? _savedTag : Loc.S("NewPlateSection");

      void Touch() => OnPropertyChanged(nameof(IsDirty));

      internal void AddRebarLayer()
      {
         var layer = new PlateRebarLayer
         {
            Name      = string.Format(Loc.S("PlateLayerDefaultName"), RebarLayers.Count + 1),
            InputMode = "diameter_spacing",
            DiameterX = 0.012, DiameterY = 0.012,
            SpacingX  = 0.2,   SpacingY  = 0.2,
            Zsx = -(_draft.H / 2.0 - 0.03),
            Zsy = -(_draft.H / 2.0 - 0.04),
         };
         layer.RecalcArea();
         _draft.RebarLayers.Add(layer);
         var vm = new PlateRebarLayerVM(layer, Touch, App.Armatures);
         RebarLayers.Add(vm);
         SelectedRebarLayer = vm;
         Touch();
      }

      void DeleteRebarLayer()
      {
         if (_selectedRebarLayer == null) return;
         _draft.RebarLayers.Remove(_selectedRebarLayer.Model);
         RebarLayers.Remove(_selectedRebarLayer);
         SelectedRebarLayer = null;
         Touch();
      }

      /// <summary>Проверяет черновик и записывает его в сечение проекта; ошибки — в журнал.</summary>
      public bool Save()
      {
         foreach (var l in _draft.RebarLayers) l.RecalcArea();
         var errors = PlateSectionAuthoring.Validate(_draft, App.db.Materials);
         if (errors.Count > 0)
         {
            foreach (var e in errors)
               App.LogService.Error(string.Format(Loc.S("PlateSaveErrorLog"), DisplayName, e.Message));
            return false;
         }
         bool isNew = _model.Id == 0;
         PlateSectionAuthoring.CopyInto(_draft, _model);
         var saveErrors = PlateSectionAuthoring.Save(App.db, _model);
         if (saveErrors.Count > 0)   // не должно случиться: черновик проверен тем же Validate
         {
            App.LogService.Error(string.Format(Loc.S("PlateSaveErrorLog"), DisplayName, saveErrors[0].Message));
            return false;
         }
         _draft.Id = _model.Id;
         _draft.Num = _model.Num;
         _saved = PlateSectionAuthoring.Fingerprint(_model);
         _savedTag = _model.Tag;
         App.RefreshPlateSectionsLive();
         App.MarkDirty(SaveCategory.PlateSections);
         Touch();
         App.LogService.Info(string.Format(Loc.S(isNew ? "PlateSavedNewLog" : "PlateSavedLog"), _model.Tag));
         return true;
      }

      bool IUnsavedChanges.SaveChanges() => Save();

      /// <summary>Черновик — заново из записанного сечения.</summary>
      public void DiscardChanges()
      {
         _draft = PlateSectionAuthoring.Draft(_model);
         _saved = PlateSectionAuthoring.Fingerprint(_draft);
         LoadDraft();
         OnPropertyChanged(string.Empty);
      }
   }
}
