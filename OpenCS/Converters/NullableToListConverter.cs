using System;
using System.Globalization;
using System.Windows.Data;

namespace OpenCS.Converters
{
   /// <summary>Конвертирует nullable-объект в список (для ItemsControl).</summary>
   public class NullableToListConverter : IValueConverter
   {
      public object Convert(object? value, Type t, object p, CultureInfo c)
         => value == null ? Array.Empty<object>() : new[] { value };
      public object ConvertBack(object v, Type t, object p, CultureInfo c)
         => throw new NotSupportedException();
   }
}
