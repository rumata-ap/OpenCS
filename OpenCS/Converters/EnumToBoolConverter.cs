using System;
using System.Globalization;
using System.Windows.Data;

namespace OpenCS.Converters
{
   public class EnumToBoolConverter : IValueConverter
   {
      public object Convert(object value, Type t, object param, CultureInfo c)
         => value?.Equals(param) == true;
      public object ConvertBack(object value, Type t, object param, CultureInfo c)
         => (bool)value ? param : Binding.DoNothing;
   }
}
