using CScore;

using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace OpenCS.ViewModels
{
   public class XYVM : INotifyPropertyChanged
   {
      int parent;
      double x;
      double y;
      public int Id { get; set; }
      public int Parent
      {
         get => parent;
         set
         {
            parent = value;
            OnPropertyChanged("Parent");
         }
      }
      public double X
      {
         get => x;
         set
         {
            x = value;
            OnPropertyChanged("X");
         }
      }

      public double Y
      {
         get => y;
         set
         {
            y = value;
            OnPropertyChanged("Y");
         }
      }

      public XY ToXY()
      {
         return new XY(x, y);
      }

      public event PropertyChangedEventHandler? PropertyChanged;
      public void OnPropertyChanged([CallerMemberName] string prop = "")
      {
         if (PropertyChanged != null)
            PropertyChanged(this, new PropertyChangedEventArgs(prop));
      }
   }
}
