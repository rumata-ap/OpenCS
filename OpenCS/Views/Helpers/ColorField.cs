using OpenCS.Utilites;

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace OpenCS.Views.Helpers
{
   /// <summary>
   /// Поле выбора цвета: hex-текст (#RRGGBB, #AARRGGBB или имя цвета) + образец, по клику на который
   /// открывается палитра, + кнопка возврата к умолчанию. Неверный ввод подсвечивается красной рамкой
   /// и не передаётся наружу (ValueChanged вызывается только для корректных цветов).
   /// </summary>
   public sealed class ColorField : UserControl
   {
      static readonly string[] Palette =
      [
         "#FFFFFF","#F0F0F0","#D3D3D3","#A0A0A0","#606060","#333333","#000000",
         "#FF0000","#FF8000","#FFC300","#80FF00","#00C800","#00A0FF","#003A6C",
         "#9000FF","#FF00C0","#F0EACD","#FFFACD","#E0FFE0","#E0F0FF","#F0E0FF"
      ];

      readonly TextBox _box = new() { Height = 22, VerticalContentAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center };
      readonly Border _swatchFill = new() { CornerRadius = new CornerRadius(2), Margin = new Thickness(2) };
      readonly Button _swatch = new() { Width = 26, Height = 22, Margin = new Thickness(3, 0, 0, 0), Padding = new Thickness(0), Cursor = Cursors.Hand };
      readonly Button _reset = new() { Width = 22, Height = 22, Margin = new Thickness(3, 0, 0, 0), Padding = new Thickness(0), Content = "↺" };
      readonly Popup _popup = new() { StaysOpen = false, Placement = PlacementMode.Bottom, AllowsTransparency = true };

      string _value = "";
      string _default = "#000000";

      /// <summary>Вызывается при вводе/выборе корректного цвета.</summary>
      public event Action<string>? ValueChanged;

      public ColorField()
      {
         var grid = new Grid();
         grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
         grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
         grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
         Grid.SetColumn(_swatch, 1);
         Grid.SetColumn(_reset, 2);
         grid.Children.Add(_box);
         grid.Children.Add(_swatch);
         grid.Children.Add(_reset);
         Content = grid;

         _swatch.Content = _swatchFill;
         _swatch.ToolTip = Loc.S("SettingsColorPick");
         _popup.PlacementTarget = _swatch;
         _popup.Child = BuildPaletteView();

         _box.TextChanged += (_, _) => OnTextChanged();
         _swatch.Click += (_, _) => _popup.IsOpen = true;
         _reset.Click += (_, _) => Value = _default;
         Value = "";
      }

      /// <summary>Текущее значение (hex или имя цвета).</summary>
      public string Value
      {
         get => _value;
         set
         {
            value ??= "";
            if (_box.Text == value) { OnTextChanged(); return; }
            _box.Text = value;
         }
      }

      /// <summary>Значение по умолчанию — для кнопки возврата.</summary>
      public string DefaultValue
      {
         get => _default;
         set
         {
            _default = value ?? "#000000";
            _reset.ToolTip = string.Format(Loc.S("SettingsColorReset"), _default);
            UpdateResetState();
         }
      }

      static bool TryParse(string? text, out Color color)
      {
         color = default;
         if (string.IsNullOrWhiteSpace(text)) return false;
         try { color = (Color)ColorConverter.ConvertFromString(text.Trim()); return true; }
         catch { return false; }
      }

      void OnTextChanged()
      {
         var text = _box.Text;
         if (TryParse(text, out var c))
         {
            _value = text.Trim();
            _swatchFill.Background = new SolidColorBrush(c);
            _box.ClearValue(Control.BorderBrushProperty);
            _box.ClearValue(FrameworkElement.ToolTipProperty);
            UpdateResetState();
            ValueChanged?.Invoke(_value);
         }
         else
         {
            _swatchFill.Background = Brushes.Transparent;
            _box.BorderBrush = Brushes.Crimson;
            _box.ToolTip = Loc.S("SettingsColorInvalid");
            UpdateResetState();
         }
      }

      void UpdateResetState()
      {
         bool same = TryParse(_value, out var a) && TryParse(_default, out var b) && a == b;
         _reset.IsEnabled = !same;
      }

      Border BuildPaletteView()
      {
         var panel = new WrapPanel { Width = 7 * 28 + 4, Margin = new Thickness(4) };
         foreach (var hex in Palette)
         {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var tile = new Rectangle
            {
               Width = 22, Height = 22, Margin = new Thickness(3),
               RadiusX = 2, RadiusY = 2, Fill = new SolidColorBrush(color),
               Stroke = Brushes.Gray, StrokeThickness = 0.5,
               Cursor = Cursors.Hand, ToolTip = hex
            };
            tile.MouseLeftButtonUp += (_, _) => { Pick(hex); _popup.IsOpen = false; };
            panel.Children.Add(tile);
         }
         return new Border
         {
            Background = SystemColors.WindowBrush,
            BorderBrush = SystemColors.ControlDarkBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Child = panel,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Opacity = 0.35 }
         };
      }

      /// <summary>Выбор из палитры: прозрачность текущего цвета (#AARRGGBB) сохраняется.</summary>
      void Pick(string hex)
      {
         var cur = _value;
         if (cur.Length == 9 && cur[0] == '#' && hex.Length == 7)
            hex = "#" + cur.Substring(1, 2) + hex.Substring(1);
         Value = hex;
      }
   }
}
