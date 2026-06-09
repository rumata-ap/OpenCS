using CScore;
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace OpenCS.Converters
{
   public class MatTypeToBrushConverter : IValueConverter
   {
      public object Convert(object value, Type t, object p, CultureInfo c)
      {
         if (value is not MatType mt) return Brushes.Gray;
         return mt switch
         {
            MatType.Concrete => new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)),
            MatType.ReSteelF => new SolidColorBrush(Color.FromRgb(0xF9, 0x73, 0x16)),
            MatType.ReSteelU => new SolidColorBrush(Color.FromRgb(0xEA, 0xB3, 0x08)),
            MatType.Steel    => new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)),
            _                => Brushes.Gray
         };
      }

      public object ConvertBack(object v, Type t, object p, CultureInfo c) =>
         throw new NotImplementedException();
   }
}
