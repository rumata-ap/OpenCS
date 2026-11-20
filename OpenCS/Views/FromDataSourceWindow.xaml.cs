using OpenCS.ViewModels;

using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace OpenCS.Views
{
   /// <summary>
   /// Справочник материалов. Выбор строки сразу применяется к материалу (видно на странице), окончательно — по OK
   /// или двойному щелчку; «Отмена», Esc и крестик возвращают материал в состояние до открытия окна.
   /// </summary>
   public partial class FromDataSourceWindow : Window
   {
      readonly Action _restore;

      public FromDataSourceWindow(MaterialVM material, int tabIndex = 0)
      {
         InitializeComponent();
         _restore = material.CaptureState();
         DataSourceVM vm = new() { Material = material };
         DataContext = vm;
         switch (tabIndex)
         {
            case 1: vm.RfsteelTabIsSelected = true; break;
            case 2: vm.SteelTabIsSelected = true; break;
         }
         vm.Select();
      }

      void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

      void List_MouseDoubleClick(object sender, MouseButtonEventArgs e)
      {
         if (DataContext is DataSourceVM { SelectedMaterial: not null }) DialogResult = true;
      }

      protected override void OnClosing(CancelEventArgs e)
      {
         if (DialogResult != true) _restore();
         base.OnClosing(e);
      }
   }
}
