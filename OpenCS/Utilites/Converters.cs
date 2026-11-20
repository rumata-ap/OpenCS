using System.Globalization;
using System.Windows;
using System.Windows.Data;
using OpenCS.Services;

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
         if (value is double d)
         {
            if (parameter is string fmt && !string.IsNullOrEmpty(fmt))
               return d.ToString(fmt, culture);
            return d.ToString("G", culture);
         }
         return value?.ToString() ?? "";
      }

      public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
      {
          if (value is string str && Pars.ParseAny(str, out double res) && double.IsFinite(res))
             return res;
         return Binding.DoNothing;
      }
   }

   /// <summary>
   /// Как <see cref="AnyDoubleConverter"/>, но нераспознанный текст (например, «1.05.05») — ошибка проверки
   /// привязки (красная рамка, <c>Validation.HasError</c>), а не молчаливое сохранение прежнего значения.
   /// </summary>
   public class StrictDoubleConverter : IValueConverter
   {
      readonly AnyDoubleConverter _inner = new();

      public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
         => _inner.Convert(value, targetType, parameter, culture);

      public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
         => value is string str && Pars.ParseAny(str.Trim(), out double res) && double.IsFinite(res)
            ? res
            : DependencyProperty.UnsetValue;
   }

   /// <summary>
   /// Код (тип задачи, статус) → локализованная подпись по ключу «префикс + код»
   /// (префикс — <c>ConverterParameter</c>); нет ключа — сам код.
   /// </summary>
   public class LocKeyConverter : IValueConverter
   {
      public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
      {
         string code = value?.ToString() ?? "";
         return code.Length > 0 && Loc.TryGet((parameter as string ?? "") + code, out string text) ? text : code;
      }

      public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
         => Binding.DoNothing;
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
         return 0.001 * NumberInput.Parse((string)value);
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
         return 1000 * NumberInput.Parse((string)value);
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
         return 1000000 * NumberInput.Parse((string)value);
      }
   }

   public class MPaConvertInv : IValueConverter
   {
      public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
      {
         // 0.0 − x, а не −x: ноль показывается «0.00», а не «-0.00».
         return 0.0 - 0.001 * (double)value;
      }

      public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
      {
         return -1000 * NumberInput.Parse((string)value);
      }
   }

   public class ConvertInv : IValueConverter
   {
      public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
      {
         return 0.0 - (double)value;   // не «-0.00000» у нулевых деформаций
      }

      public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
      {
         return -NumberInput.Parse((string)value);
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
         return 1000 * NumberInput.Parse((string)value);
      }
   }

   /// <summary>Подпись компоненты усилия для селектора 3D-эпюры — символ + единица измерения (кН/кН·м).</summary>
   public class FemForceComponentLabelConverter : IValueConverter
   {
      public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
      {
         if (value is not FemForceComponent c) return value?.ToString() ?? "";
         bool isForce = c is FemForceComponent.N
            or FemForceComponent.Qy or FemForceComponent.Qz;
         string unit = isForce ? Loc.S("UnitKN") : Loc.S("UnitKNm");
         return $"{c}, {unit}";
      }

      public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
         Binding.DoNothing;
   }

   /// <summary>Локализованная подпись стороны внутреннего mesh-узла.</summary>
   public class FemForceSourceSideLabelConverter : IValueConverter
   {
      public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
         value is FemForceSourceSide side
            ? Loc.S(side switch
            {
               FemForceSourceSide.Only => "FemForceSetSourceOnly",
               FemForceSourceSide.Left => "FemForceSetSourceLeft",
               FemForceSourceSide.Right => "FemForceSetSourceRight",
               _ => "FemForceSetSourceOnly"
            })
            : "";

      public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
         Binding.DoNothing;
   }

   /// <summary>Подпись группы результата в просмотре 2D-эпюры.</summary>
   public class FemResultGroupLabelConverter : IValueConverter
   {
      public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
      {
         OpenCS.ViewModels.FemResultGroup.Forces => Loc.S("FemResultGroupForces"),
         OpenCS.ViewModels.FemResultGroup.Displacements => Loc.S("FemResultGroupDisplacements"),
         OpenCS.ViewModels.FemResultGroup.Rotations => Loc.S("FemResultGroupRotations"),
         _ => value?.ToString() ?? ""
      };

      public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
         Binding.DoNothing;
   }

   /// <summary>Подпись глобальной узловой компоненты в просмотре 2D-эпюры.</summary>
   public class FemNodalComponentLabelConverter : IValueConverter
   {
      public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
      {
         OpenCS.ViewModels.FemNodalComponent.Ux => Loc.S("FemResultNodalUx"),
         OpenCS.ViewModels.FemNodalComponent.Uy => Loc.S("FemResultNodalUy"),
         OpenCS.ViewModels.FemNodalComponent.Uz => Loc.S("FemResultNodalUz"),
         OpenCS.ViewModels.FemNodalComponent.Rx => Loc.S("FemResultNodalRx"),
         OpenCS.ViewModels.FemNodalComponent.Ry => Loc.S("FemResultNodalRy"),
         OpenCS.ViewModels.FemNodalComponent.Rz => Loc.S("FemResultNodalRz"),
         _ => value?.ToString() ?? ""
      };

      public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
         Binding.DoNothing;
   }

   /// <summary>Подпись единицы линейного результата.</summary>
   public class FemLengthUnitLabelConverter : IValueConverter
   {
      public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
      {
         OpenCS.ViewModels.FemLengthUnit.Millimeters => Loc.S("FemLengthMillimeters"),
         OpenCS.ViewModels.FemLengthUnit.Centimeters => Loc.S("FemLengthCentimeters"),
         OpenCS.ViewModels.FemLengthUnit.Meters => Loc.S("FemLengthMeters"),
         _ => value?.ToString() ?? ""
      };

      public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
         Binding.DoNothing;
   }

   /// <summary>Подпись коэффициента радианного результата.</summary>
   public class FemRotationScaleLabelConverter : IValueConverter
   {
      public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
      {
         OpenCS.ViewModels.FemRotationScale.One => Loc.S("FemRotationScaleOne"),
         OpenCS.ViewModels.FemRotationScale.OneHundred => Loc.S("FemRotationScaleOneHundred"),
         OpenCS.ViewModels.FemRotationScale.OneThousand => Loc.S("FemRotationScaleOneThousand"),
         _ => value?.ToString() ?? ""
      };

      public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
         Binding.DoNothing;
   }

   /// <summary>Подпись фильтра узловой таблицы результатов.</summary>
   public class FemDisplacementDisplayModeLabelConverter : IValueConverter
   {
      public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
      {
         OpenCS.ViewModels.FemDisplacementDisplayMode.AllNodes => Loc.S("FemDisplayAllNodes"),
         OpenCS.ViewModels.FemDisplacementDisplayMode.ExtremesOnly => Loc.S("FemDisplayExtremesOnly"),
         _ => value?.ToString() ?? ""
      };

      public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
         Binding.DoNothing;
   }

   /// <summary>Локализует список компонент, по которым строка выбрана как экстремальная.</summary>
   public class FemExtremeComponentsLabelConverter : IValueConverter
   {
      public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
      {
         if (value is not IEnumerable<OpenCS.ViewModels.FemNodalComponent> components)
            return value?.ToString() ?? "";
         return string.Join(", ", components.Select(component =>
            Loc.S($"FemResultNodal{component}")));
      }

      public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
         Binding.DoNothing;
   }
}
