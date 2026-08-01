namespace OpenCS.Utilites
{
   /// <summary>Цвет в формате ARGB. GUI-агностический аналог System.Windows.Media.Color,
   /// используется в ViewModels и вычислительных помощниках; GUI-проекты конвертируют его
   /// в собственные типы кистей/цветов.</summary>
   public readonly record struct Argb(byte A, byte R, byte G, byte B)
   {
      public static Argb FromRgb(byte r, byte g, byte b) => new(255, r, g, b);
      public static Argb FromArgb(byte a, byte r, byte g, byte b) => new(a, r, g, b);

      public Argb WithAlpha(byte a) => new(a, R, G, B);

      /// <summary>Строковое представление "#AARRGGBB" (совместимо с WPF BrushConverter и Avalonia).</summary>
      public string ToHex() => $"#{A:X2}{R:X2}{G:X2}{B:X2}";

      public override string ToString() => ToHex();
   }
}
