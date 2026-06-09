using CScore;
using OpenCS.Utilites;
using System.Windows.Input;

namespace OpenCS.ViewModels
{
   /// <summary>ViewModel для MaterialArea.</summary>
   public class MaterialAreaVM : ViewModelBase
   {
      readonly MaterialArea _model;

      public MaterialAreaVM(MaterialArea model, AppViewModel app)
      {
         _model = model;
         App = app;
         RemoveAreaCommand = new RelayCommand(_ => App.RemoveMaterialArea(this));
      }

      public AppViewModel App { get; }
      public MaterialArea Model => _model;

      public string Tag
      {
         get => _model.Tag;
         set { _model.Tag = value; OnPropertyChanged(); }
      }

      public Material? Material
      {
         get => _model.Material;
         set
         {
            _model.Material = value;
            _model.MaterialId = value?.Id ?? 0;
            _model.ResolveAndBuildDiagramms();
            OnPropertyChanged();
            OnPropertyChanged(nameof(MaterialType));
         }
      }

      public MatType MaterialType => _model.Material?.Type ?? MatType.None;

      public DiagrammType DiagrammType
      {
         get => _model.DiagrammType;
         set
         {
            _model.DiagrammType = value;
            _model.ResolveAndBuildDiagramms();
            OnPropertyChanged();
         }
      }

      public ICommand RemoveAreaCommand { get; }
   }
}
