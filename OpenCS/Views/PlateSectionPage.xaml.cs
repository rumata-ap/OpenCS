using CScore;
using OpenCS.Utilites;
using OpenCS.ViewModels;

using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OpenCS.Views
{
   public partial class PlateSectionPage : UserControl
   {
      public PlateSectionPage(AppViewModel app)
      {
         InitializeComponent();
         DataContext = new PlateSectionVM(new PlateSection(), app);
      }

      public PlateSectionPage(PlateSection ps, AppViewModel app)
      {
         InitializeComponent();
         DataContext = new PlateSectionVM(ps, app);
      }
   }
}

namespace OpenCS.ViewModels
{
   /// <summary>ViewModel строки арматурного слоя в PlateSectionPage.</summary>
   public class PlateRebarLayerVM : ViewModelBase
   {
      readonly PlateRebarLayer _model;
      readonly System.Action _onChanged;

      public PlateRebarLayerVM(PlateRebarLayer model, System.Action onChanged)
      {
         _model = model;
         _onChanged = onChanged;
      }

      public PlateRebarLayer Model => _model;

      public string Name
      {
         get => _model.Name;
         set { _model.Name = value; OnPropertyChanged(); }
      }

      public string InputMode
      {
         get => _model.InputMode;
         set { _model.InputMode = value; OnPropertyChanged(); _onChanged(); OnPropertyChanged(nameof(AsxCm2)); OnPropertyChanged(nameof(AsyCm2)); }
      }

      // Ввод в мм/см²
      public double DiameterXMm
      {
         get => _model.DiameterX * 1000.0;
         set { _model.DiameterX = value / 1000.0; OnPropertyChanged(); _model.RecalcArea(); _onChanged(); OnPropertyChanged(nameof(AsxCm2)); }
      }

      public double DiameterYMm
      {
         get => _model.DiameterY * 1000.0;
         set { _model.DiameterY = value / 1000.0; OnPropertyChanged(); _model.RecalcArea(); _onChanged(); OnPropertyChanged(nameof(AsyCm2)); }
      }

      public double SpacingXMm
      {
         get => _model.SpacingX * 1000.0;
         set { _model.SpacingX = value / 1000.0; OnPropertyChanged(); _model.RecalcArea(); _onChanged(); OnPropertyChanged(nameof(AsxCm2)); }
      }

      public double SpacingYMm
      {
         get => _model.SpacingY * 1000.0;
         set { _model.SpacingY = value / 1000.0; OnPropertyChanged(); _model.RecalcArea(); _onChanged(); OnPropertyChanged(nameof(AsyCm2)); }
      }

      public double ZsxMm
      {
         get => _model.Zsx * 1000.0;
         set { _model.Zsx = value / 1000.0; OnPropertyChanged(); }
      }

      public double ZsyMm
      {
         get => _model.Zsy * 1000.0;
         set { _model.Zsy = value / 1000.0; OnPropertyChanged(); }
      }

      public double CountPerMeterX
      {
         get => _model.CountPerMeterX;
         set { _model.CountPerMeterX = value; OnPropertyChanged(); _model.RecalcArea(); _onChanged(); OnPropertyChanged(nameof(AsxCm2)); }
      }

      public double CountPerMeterY
      {
         get => _model.CountPerMeterY;
         set { _model.CountPerMeterY = value; OnPropertyChanged(); _model.RecalcArea(); _onChanged(); OnPropertyChanged(nameof(AsyCm2)); }
      }

      // Отображаемые/вводимые площади (см²/м). В режиме "direct" пишутся напрямую.
      public double AsxCm2
      {
         get => _model.Asx * 1e4;
         set { _model.Asx = value / 1e4; OnPropertyChanged(); _onChanged(); }
      }
      public double AsyCm2
      {
         get => _model.Asy * 1e4;
         set { _model.Asy = value / 1e4; OnPropertyChanged(); _onChanged(); }
      }
   }

   /// <summary>Опция режима задания арматуры слоя: англ. ключ + локализованная подпись.</summary>
   public sealed record InputModeOption(string Key, string Display);

   /// <summary>ViewModel для PlateSectionPage.</summary>
   public class PlateSectionVM : ViewModelBase
   {
      readonly PlateSection _model;

      public PlateSectionVM(PlateSection model, AppViewModel app)
      {
         _model = model;
         App    = app;

         RebarLayers = new ObservableCollection<PlateRebarLayerVM>(
            model.RebarLayers.ConvertAll(l => new PlateRebarLayerVM(l, () => { })));

         ConcreteMaterial = app.Concretes.FirstOrDefault(m => m.Id == model.ConcreteMaterialId);
         RebarMaterial    = app.Armatures.FirstOrDefault(m => m.Id == model.RebarMaterialId);

         SofteningOptions = [Loc.S("PlateSofteningNone"), Loc.S("PlateSofteningVC")];
         SofteningIndex   = model.SofteningModel == "vecchio_collins" ? 1 : 0;

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

         ConcreteDiagramTypeOptions =
         [
            new InputModeOption("L2",   Loc.S("DiagramTypeL2")),
            new InputModeOption("L3",   Loc.S("DiagramTypeL3")),
            new InputModeOption("SP63", Loc.S("DiagramTypeSP63")),
            new InputModeOption("EKB",  Loc.S("DiagramTypeEKB")),
            new InputModeOption("SP35", Loc.S("DiagramTypeSP35")),
         ];

         SaveCommand            = new RelayCommand(_ => Save());
         DeleteCommand          = new RelayCommand(_ => Delete());
         AddRebarLayerCommand   = new RelayCommand(_ => AddRebarLayer());
         DeleteRebarLayerCommand = new RelayCommand(_ => DeleteRebarLayer());
      }

      public AppViewModel App { get; }

      public string Tag
      {
         get => _model.Tag;
         set { _model.Tag = value; OnPropertyChanged(); }
      }

      /// <summary>Толщина в мм (UI-единицы).</summary>
      public double HMm
      {
         get => _model.H * 1000.0;
         set { _model.H = value / 1000.0; OnPropertyChanged(); }
      }

      public int NLayers
      {
         get => _model.NLayers;
         set { _model.NLayers = value; OnPropertyChanged(); }
      }

      Material? _concreteMaterial;
      public Material? ConcreteMaterial
      {
         get => _concreteMaterial;
         set
         {
            _concreteMaterial = value;
            _model.ConcreteMaterialId = value?.Id ?? 0;
            OnPropertyChanged();
         }
      }

      Material? _rebarMaterial;
      public Material? RebarMaterial
      {
         get => _rebarMaterial;
         set
         {
            _rebarMaterial = value;
            _model.RebarMaterialId = value?.Id ?? 0;
            OnPropertyChanged();
         }
      }

      public bool TensionConcrete
      {
         get => _model.TensionConcrete;
         set { _model.TensionConcrete = value; OnPropertyChanged(); }
      }

      public string[] SofteningOptions { get; }
      public InputModeOption[] InputModeOptions { get; }
      public InputModeOption[] PlateModelOptions { get; }
      public InputModeOption[] ConcreteDiagramTypeOptions { get; }

      /// <summary>Нелинейная модель пластины (layered | char1d_principal | char1d_axial).</summary>
      public string PlateModel
      {
         get => string.IsNullOrEmpty(_model.PlateModel) ? "layered" : _model.PlateModel;
         set
         {
            _model.PlateModel = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsLayered));
         }
      }

      /// <summary>NLayers применим только к слоистой модели.</summary>
      public bool IsLayered => PlateModel == "layered";

      /// <summary>Тип диаграммы бетона (строковый ключ enum DiagrammType).</summary>
      public string ConcreteDiagramType
      {
         get => _model.ConcreteDiagramType.ToString();
         set
         {
            if (Enum.TryParse<DiagrammType>(value, out var dt))
               _model.ConcreteDiagramType = dt;
            OnPropertyChanged();
         }
      }

      int _softeningIndex;
      public int SofteningIndex
      {
         get => _softeningIndex;
         set
         {
            _softeningIndex = value;
            _model.SofteningModel = value == 1 ? "vecchio_collins" : "";
            OnPropertyChanged();
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

      void AddRebarLayer()
      {
         var layer = new PlateRebarLayer
         {
            Name      = $"Слой {RebarLayers.Count + 1}",
            InputMode = "diameter_spacing",
            DiameterX = 0.012, DiameterY = 0.012,
            SpacingX  = 0.2,   SpacingY  = 0.2,
            Zsx = -(_model.H / 2.0 - 0.03),
            Zsy = -(_model.H / 2.0 - 0.04),
         };
         layer.RecalcArea();
         _model.RebarLayers.Add(layer);
         var vm = new PlateRebarLayerVM(layer, () => { });
         RebarLayers.Add(vm);
         SelectedRebarLayer = vm;
      }

      void DeleteRebarLayer()
      {
         if (_selectedRebarLayer == null) return;
         _model.RebarLayers.Remove(_selectedRebarLayer.Model);
         RebarLayers.Remove(_selectedRebarLayer);
         SelectedRebarLayer = null;
      }

      void Save()
      {
         if (_model.Num == 0)
            _model.Num = App.PlateSections.Count > 0
               ? App.PlateSections.Max(s => s.Num) + 1 : 1;
         App.db.SavePlateSection(_model);
         if (!App.PlateSections.Contains(_model))
            App.PlateSections.Add(_model);
      }

      void Delete()
      {
         var res = System.Windows.MessageBox.Show(
            Loc.S("ConfirmDeleteRegion"), Loc.S("Warning"),
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
         if (res != System.Windows.MessageBoxResult.Yes) return;
         App.db.DeletePlateSection(_model);
         App.CurrentPage = null!;
      }
   }
}
