using CScore;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace OpenCS.Converters
{
   public class MatTypeToGeometryConverter : IValueConverter
   {
      public object Convert(object value, Type t, object p, CultureInfo c)
      {
         if (value is not MatType mt) return Geometry.Empty;
         string key = mt switch
         {
            MatType.Concrete => "Icon_Concrete",
            MatType.ReSteelF => "Icon_ReSteelF",
            MatType.ReSteelU => "Icon_ReSteelU",
            MatType.Steel    => "Icon_Steel",
            _                => "Icon_Concrete"
         };
         return Application.Current.TryFindResource(key) as Geometry ?? Geometry.Empty;
      }

      public object ConvertBack(object v, Type t, object p, CultureInfo c) =>
         throw new NotImplementedException();
   }
}
