using CScore;

using System.Windows.Media;

namespace OpenCS.ViewModels
{
   /// <summary>Вид примитива DXF: контур или окружность.</summary>
   public enum DxfPrimitiveKind { Contour, Circle }

   /// <summary>Роль примитива при назначении в MaterialArea.</summary>
   public enum DxfRole { None, Hull, Hole, RebarGroup, SingleBar }

   /// <summary>Информация о слое DXF: имя и цвет для легенды.</summary>
   public record LayerInfo(string Name, string HexColor)
   {
      /// <summary>Кисть для отображения цветного маркера в легенде слоёв.</summary>
      public Brush LayerBrush { get; } =
         new SolidColorBrush((Color)ColorConverter.ConvertFromString(HexColor));
   }

   /// <summary>
   /// Обёртка DXF-примитива: связывает геометрию для рендера с доменным объектом
   /// (<see cref="Contour"/> или <see cref="CircleP"/>) и назначенной ролью.
   /// </summary>
   public class DxfPrimitive
   {
      public DxfPrimitiveKind Kind      { get; init; }
      public string           LayerName { get; init; } = string.Empty;
      public DxfRole          Role      { get; set; } = DxfRole.None;

      // Заполнено когда Kind == Contour
      public double[]? Xs      { get; init; }
      public double[]? Ys      { get; init; }
      public Contour?  Contour { get; init; }

      // Заполнено когда Kind == Circle
      public double   CenterX { get; init; }
      public double   CenterY { get; init; }
      public double   Radius  { get; init; }
      public CircleP? Circle  { get; init; }
   }
}
