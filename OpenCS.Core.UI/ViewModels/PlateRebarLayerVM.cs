using CScore;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>ViewModel строки арматурного слоя в PlateSectionPage.</summary>
public class PlateRebarLayerVM : ViewModelBase
{
   readonly PlateRebarLayer _model;
   readonly System.Action _onChanged;

   readonly IReadOnlyList<Material> _armatures;

   public PlateRebarLayerVM(PlateRebarLayer model, System.Action onChanged, IReadOnlyList<Material> armatures)
   {
      _model = model;
      _onChanged = onChanged;
      _armatures = armatures;
      _rebarMaterial = armatures.FirstOrDefault(m => m.Id == model.MaterialId);
   }

   public PlateRebarLayer Model => _model;

   Material? _rebarMaterial;
   /// <summary>Материал арматуры слоя. null (MaterialId=0) — использовать глобальный материал арматуры сечения.</summary>
   public Material? RebarMaterial
   {
      get => _rebarMaterial;
      set { _rebarMaterial = value; _model.MaterialId = value?.Id ?? 0; OnPropertyChanged(); _onChanged(); }
   }

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
