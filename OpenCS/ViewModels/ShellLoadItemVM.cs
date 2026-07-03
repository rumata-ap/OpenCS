using System;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.ViewModels
{
   /// <summary>ViewModel для одной строки набора усилий пластины.</summary>
   public class ShellLoadItemVM : ViewModelBase
   {
      readonly ShellLoadItem _model;
      readonly Action? _onChanged;

      public ShellLoadItemVM(ShellLoadItem model, Action? onChanged = null)
      {
         _model = model;
         _onChanged = onChanged;
      }

      void Touch() => _onChanged?.Invoke();

      public ShellLoadItem Model => _model;

      public int Num => _model.Num;

      public string Label
      {
         get => _model.Label;
         set { _model.Label = value; Touch(); OnPropertyChanged(); }
      }

      public double Nx
      {
         get => _model.Nx;
         set { _model.Nx = value; Touch(); OnPropertyChanged(); }
      }

      public double Ny
      {
         get => _model.Ny;
         set { _model.Ny = value; Touch(); OnPropertyChanged(); }
      }

      public double Nxy
      {
         get => _model.Nxy;
         set { _model.Nxy = value; Touch(); OnPropertyChanged(); }
      }

      public double Mx
      {
         get => _model.Mx;
         set { _model.Mx = value; Touch(); OnPropertyChanged(); }
      }

      public double My
      {
         get => _model.My;
         set { _model.My = value; Touch(); OnPropertyChanged(); }
      }

      public double Mxy
      {
         get => _model.Mxy;
         set { _model.Mxy = value; Touch(); OnPropertyChanged(); }
      }

      public double Qx
      {
         get => _model.Qx;
         set { _model.Qx = value; Touch(); OnPropertyChanged(); }
      }

      public double Qy
      {
         get => _model.Qy;
         set { _model.Qy = value; Touch(); OnPropertyChanged(); }
      }
   }
}
