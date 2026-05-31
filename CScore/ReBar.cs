using CSmath.Geometry;

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CScore
{
   /// <summary>
   /// Арматурный стержень — дискретный элемент армирования в поперечном сечении.
   /// Характеризуется координатами центра, диаметром, площадью и расчётными
   /// параметрами (напряжение, деформация, усилия). Является базовым классом
   /// для <see cref="ReBarLayer"/>.
   /// </summary>
   [Serializable]
   public class ReBar
   {
      string str;
      double nu1 = 1;
      double nu2 = 1;

      /// <summary>
      /// Секущий модуль упругости арматурного стержня.
      /// </summary>
      public double E { get; set; }

      /// <summary>
      /// Касательный модуль упругости арматурного стержня.
      /// </summary>
      public double E2 { get; set; }

      /// <summary>
      /// Напряжение в арматурном стержне [МПа].
      /// </summary>
      public double Sig { get; set; }

      /// <summary>
      /// Деформация арматурного стержня (от внешней нагрузки).
      /// </summary>
      public double Eps { get; set; }

      /// <summary>
      /// Предварительная деформация арматурного стержня (от преднапряжения).
      /// </summary>
      public double Eps_p { get; set; }

      /// <summary>
      /// Коэффициент упругости ν₁. По умолчанию 1.
      /// </summary>
      public double Nu1 { get => nu1; set => nu1 = value; }

      /// <summary>
      /// Коэффициент упругости ν₂. По умолчанию 1.
      /// </summary>
      public double Nu2 { get => nu2; set => nu2 = value; }

      /// <summary>
      /// Метка (тег) арматурного стержня.
      /// </summary>
      public string? Tag { get; set; }

      /// <summary>
      /// Трёхмерная точка (X, Y, Eps) — координаты стержня и его деформация.
      /// </summary>
      internal Vector3D Point { get => new(X, Y, Eps); }

      /// <summary>
      /// Продольное усилие в арматурном стержне: N = σ · A.
      /// </summary>
      public double N { get; set; }

      /// <summary>
      /// Площадь поперечного сечения арматурного стержня [м²].
      /// </summary>
      public double Area { get; set; }

      /// <summary>
      /// Изгибающий момент относительно оси X (M_y = σ · A · y).
      /// </summary>
      public double My { get; set; }

      /// <summary>
      /// Изгибающий момент относительно оси Y (M_z = σ · A · x).
      /// </summary>
      public double Mz { get; set; }

      /// <summary>
      /// Координата X центра арматурного стержня [м].
      /// </summary>
      public double X { get; set; }

      /// <summary>
      /// Координата Y центра арматурного стержня [м].
      /// </summary>
      public double Y { get; set; }

      /// <summary>
      /// Первичный ключ для EF Core.
      /// </summary>
      [JsonIgnore] public int Id { get; set; }

      /// <summary>
      /// Порядковый номер арматурного стержня.
      /// </summary>
      public int Num { get; set; }

      /// <summary>
      /// Текстовое описание стержня (возвращает ToString).
      /// </summary>
      public string Description { get => ToString(); set => str = value; }

      /// <summary>
      /// Площадь сечения в см² (Area × 10000), отформатированная с 3 знаками.
      /// </summary>
      public string Astr { get => $"{10000 * Area:F3}"; set => str = value; }

      /// <summary>
      /// Ссылка на родительскую группу арматуры. Не сериализуется.
      /// </summary>
      [JsonIgnore] public ReBarGroup? Group { get; set; }

      /// <summary>
      /// Внешний ключ для связи с ReBarGroup. Не сериализуется.
      /// </summary>
      [JsonIgnore] public int GroupId { get; set; }

      /// <summary>
      /// Диаметр арматурного стержня [м].
      /// </summary>
      public double Diameter { get; set; }

      /// <summary>
      /// Диаметр в мм (Diameter × 1000), отформатированный без дробной части.
      /// </summary>
      public string Dstr { get => $"{1000*Diameter:F0}"; set => str = value; }

      /// <summary>
      /// Конструктор по умолчанию.
      /// </summary>
      public ReBar() { }

      /// <summary>
      /// Создаёт арматурный стержень из кругового объекта <see cref="CircleP"/>.
      /// Копирует координаты, площадь, диаметр и тег.
      /// </summary>
      /// <param name="c">Круговой объект с координатами, диаметром и площадью.</param>
      public ReBar(CircleP c)
      {
         X = c.X;
         Y = c.Y;
         Nu1 = 1;
         Nu2 = 1;
         Area = c.Area;
         Diameter = c.Diameter;
         Tag = c.Tag;
      }

      /// <summary>
      /// Создаёт арматурный стержень заданного диаметра с координатами центра
      /// и коэффициентами упругости. Площадь вычисляется как π·d²/4.
      /// </summary>
      /// <param name="d">Диаметр стержня [м].</param>
      /// <param name="x">Координата X центра [м].</param>
      /// <param name="y">Координата Y центра [м].</param>
      /// <param name="nu1">Коэффициент упругости ν₁ (по умолчанию 1).</param>
      /// <param name="nu2">Коэффициент упругости ν₂ (по умолчанию 1).</param>
      public ReBar(double d, double x = 0, double y = 0, double nu1 = 1, double nu2 = 1)
      {
         X = x;
         Y = y;
         Nu1 = nu1;
         Nu2 = nu2;
         Area = 0.25 * Math.PI * d * d;
         Diameter = d;
      }

      /// <inheritdoc/>
      public override string ToString()
      {
         if (Group == null)
            return $"{Num:D3}#rebar : {Tag} | <No Group>";
         else return $"{Num:D3}#rebar : {Tag} | <{Group.Tag}>";
      }

      /// <summary>
      /// Создаёт копию арматурного стержня с сохранением координат, диаметра,
      /// площади и преддеформации.
      /// </summary>
      /// <returns>Новый объект ReBar с теми же значениями полей.</returns>
      public virtual ReBar Clone()
      {
         var res = new ReBar(Diameter, X, Y)
         {
            Area = Area,
            Eps_p = Eps_p
         };
         return res;
      }
   }
}