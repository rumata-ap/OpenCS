using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace OpenCS.Utilites
{
   public static class Pars
   {
      public static bool ParseAny(string text, out double result)
      {
         return double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out result) ||
                double.TryParse(text.Replace('.', ','), NumberStyles.Float, new CultureInfo("ru-RU"), out result);
      }
   }

   public class AnyDoubleConverter : IValueConverter
   {
      public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
      {
         if (value is double d) return d.ToString("G", culture);
         return value?.ToString() ?? "";
      }

      public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
      {
          if (value is string str && Pars.ParseAny(str, out double res) && double.IsFinite(res))
             return res;
         return Binding.DoNothing;
      }
   }

   public class Round2Convert : IValueConverter
   {
      public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
      {
         //return 1000 * (double)value;
         return $"{(double)value:F2}";
      }

      public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
      {
         Pars.ParseAny((string)parameter, out double res0);
         if (Pars.ParseAny((string)value, out double res)) return res;
         else return res0;
      }
   }
   public class MmToMConvert : IValueConverter
   {
      public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
      {
         return 1000 * (double)value;
         //return $"{0.001 * (double)value :F3}";
      }

      public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
      {
         return 0.001 * double.Parse((string)value, CultureInfo.InvariantCulture);
      }
   }
   public class MPaConvert : IValueConverter
   {
      public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
      {
         return 0.001 * (double)value;
         //return $"{0.001 * (double)value :F3}";
      }

      public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
      {
         return 1000 * double.Parse((string)value, CultureInfo.InvariantCulture);
      }
   }
   public class GPaConvert : IValueConverter
   {
      public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
      {
         return 0.000001 * (double)value;
         //return $"{0.000001 * (double)value:F3}";
      }

      public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
      {
         return 1000000 * double.Parse((string)value, CultureInfo.InvariantCulture);
      }
   }

   public class MPaConvertInv : IValueConverter
   {
      public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
      {
         return -0.001 * (double)value;
         //return $"{-0.001 * (double)value:F3}";
      }

      public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
      {
         return -1000 * double.Parse((string)value, CultureInfo.InvariantCulture);
      }
   }

   public class ConvertInv : IValueConverter
   {
      public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
      {
         return -(double)value;
      }

      public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
      {
         return -double.Parse((string)value, CultureInfo.InvariantCulture);
      }
   }

   /// <summary>Н → кН (и Н·м → кН·м — коэффициент тот же, различается только подпись единицы в заголовке).</summary>
   public class NToKNConvert : IValueConverter
   {
      public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
      {
         return 0.001 * (double)value;
      }

      public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
      {
         return 1000 * double.Parse((string)value, CultureInfo.InvariantCulture);
      }
   }

   /// <summary>Подпись компоненты усилия для селектора 3D-эпюры — символ + единица измерения (кН/кН·м).</summary>
   public class FemForceComponentLabelConverter : IValueConverter
   {
      public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
      {
         if (value is not OpenCS.ViewModels.FemForceComponent c) return value?.ToString() ?? "";
         bool isForce = c is OpenCS.ViewModels.FemForceComponent.N
            or OpenCS.ViewModels.FemForceComponent.Qy or OpenCS.ViewModels.FemForceComponent.Qz;
         string unit = isForce ? Loc.S("UnitKN") : Loc.S("UnitKNm");
         return $"{c}, {unit}";
      }

      public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
         Binding.DoNothing;
   }
}
