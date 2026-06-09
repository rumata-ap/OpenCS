using CScore;
using OpenCS.Utilites;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

namespace OpenCS.ViewModels
{
   /// <summary>ViewModel для CrossSection.</summary>
   public class CrossSectionVM : ViewModelBase
   {
      readonly CrossSection _model;

      public CrossSectionVM(CrossSection model, AppViewModel app)
      {
         _model = model;
         App = app;
         Areas = new ObservableCollection<MaterialAreaVM>(
            model.Areas.Select(a => new MaterialAreaVM(a, app)));

         AddConcreteAreaCommand = new RelayCommand(_ => AddArea(MatType.Concrete));
         AddRebarAreaCommand    = new RelayCommand(_ => AddArea(MatType.ReSteelF));
         AddSteelAreaCommand    = new RelayCommand(_ => AddArea(MatType.Steel));
      }

      public AppViewModel App { get; }
      public CrossSection Model => _model;

      public string Tag
      {
         get => _model.Tag;
         set { _model.Tag = value; OnPropertyChanged(); }
      }

      public ObservableCollection<MaterialAreaVM> Areas { get; }

      public ICommand AddConcreteAreaCommand { get; }
      public ICommand AddRebarAreaCommand { get; }
      public ICommand AddSteelAreaCommand { get; }

      void AddArea(MatType type)
      {
         var area = new MaterialArea { Tag = $"Область {Areas.Count + 1}" };
         _model.Areas.Add(area);
         Areas.Add(new MaterialAreaVM(area, App));
         App.IsDirty = true;
      }
   }
}
