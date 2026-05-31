using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;
using Newtonsoft.Json;
using NetTopologySuite.Geometries;
using System.Net.NetworkInformation;

namespace CScore
{
   /// <summary>
   /// Группа арматурных стержней — объединяет стержни с общим материалом
   /// и диаграммой работы. Используется в <see cref="RCFiberRegion"/>
   /// для моделирования армирования железобетонного сечения.
   /// </summary>
   [Serializable]
   public class ReBarGroup
   {
      string str;

      /// <summary>
      /// Первичный ключ для EF Core.
      /// </summary>
      [JsonIgnore] public int Id { get; set; }

      /// <summary>
      /// Порядковый номер группы. Не сохраняется в БД.
      /// </summary>
      public int Num { get; set; }

      /// <summary>
      /// Наименование (тег) группы арматуры.
      /// </summary>
      public string Tag { get; set; } = "";

      /// <summary>
      /// Материал арматуры. Не сериализуется в JSON.
      /// </summary>
      [JsonIgnore] public Material? Material { get; set; }

      /// <summary>
      /// Список арматурных стержней в группе.
      /// </summary>
      public List<ReBar> ReBars { get; set; } = [];

      /// <summary>
      /// Словарь диаграмм работы материала по видам расчёта.
      /// Не сохраняется в БД.
      /// </summary>
      public Dictionary<CalcType, Diagramm>? Diagramms { get; set; }

      /// <summary>
      /// Ссылка на родительскую железобетонную область. Не сериализуется.
      /// </summary>
      [JsonIgnore] public RCFiberRegion? RCFiberRegion { get; set; }

      /// <summary>
      /// Внешний ключ для связи с RCFiberRegion. Не сериализуется.
      /// </summary>
      [JsonIgnore] public int RCFiberRegionId { get; set; }

      /// <summary>
      /// Тип области (по умолчанию <see cref="RegionType.Rebar"/>).
      /// </summary>
      public RegionType Type { get; set; } = RegionType.Rebar;

      /// <summary>
      /// Текстовое описание группы.
      /// </summary>
      public string? Description { get; set; }

      /// <summary>
      /// Конструктор по умолчанию.
      /// </summary>
      public ReBarGroup() { }

      /// <summary>
      /// Создаёт группу арматуры с заданным тегом, материалом и коллекцией стержней.
      /// </summary>
      /// <param name="tag">Наименование (тег) группы.</param>
      /// <param name="material">Материал арматуры.</param>
      /// <param name="reBars">Коллекция арматурных стержней.</param>
      public ReBarGroup(string tag, Material material, IEnumerable<ReBar> reBars)
      {
         Tag = tag;
         Material = material;
         ReBars = [];
         foreach (var item in reBars)
            ReBars.Add(item);
         Type = RegionType.Rebar;
      }

      /// <inheritdoc/>
      public override string ToString()
      {
         if (Material == null)
            return $"{Num:D3}#ReBarGroup : {Tag} | <No Material>";
         else return $"{Num:D3}#ReBarGroup : {Tag} | <{Material.Tag}>";
      }

      /// <summary>
      /// Создаёт глубокую копию группы арматуры, включая клонирование всех стержней.
      /// </summary>
      /// <returns>Новый объект ReBarGroup с копиями стержней и диаграмм.</returns>
      public ReBarGroup Clone()
      {
         List<ReBar> reBars = new List<ReBar>(ReBars.Count);
         for (int i = 0; i < ReBars.Count; i++) reBars.Add((ReBar)ReBars[i].Clone());
         ReBarGroup res = new ReBarGroup(Tag, Material, reBars) { Diagramms = Diagramms };
         Type = RegionType.Rebar;
         return res;
      }

      /// <summary>
      /// Сдвигает все арматурные стержни группы к центральным координатам.
      /// </summary>
      /// <param name="centr">Координаты нового центра (вычитаются из текущих).</param>
      public void ToCentr(XY centr)
      {
         for (int i = 0; i < ReBars.Count; i++)
         {
            ReBars[i].X -= centr.X;
            ReBars[i].Y -= centr.Y;
         }
      }

      /// <summary>
      /// Возвращает все арматурные стержни группы из центральных координат к исходным.
      /// </summary>
      /// <param name="centr">Координаты центра (прибавляются к текущим).</param>
      public void ToStart(XY centr)
      {
         for (int i = 0; i < ReBars.Count; i++)
         {
            ReBars[i].X += centr.X;
            ReBars[i].Y += centr.Y;
         }
      }

      /// <summary>
      /// Вычисляет деформации и напряжения во всех арматурных стержнях группы
      /// по заданной кривизне плоскости деформаций.
      /// </summary>
      /// <param name="kykze0">Кривизна плоскости деформаций (e₀, k_y, k_z).</param>
      /// <param name="calc">Тип расчёта (C, CL, N, NL).</param>
      /// <param name="ca">Учитывать работу на сжатие.</param>
      public void SetEps(Kurvature kykze0, CalcType calc, bool ca)
      {
         for (int i = 0; i < ReBars.Count; i++)
         {
            ReBars[i].Eps = kykze0.e0 + kykze0.ky * ReBars[i].Y + kykze0.kz * ReBars[i].X;
         }


         Diagramm dgr = Diagramms[calc];
         dgr.Sig(this, ca);
      }

      /// <summary>
      /// Вычисляет внутренние усилия от предварительного напряжения в арматуре
      /// для заданного типа расчёта.
      /// </summary>
      /// <param name="calc">Тип расчёта.</param>
      /// <param name="tb">Учитывать работу на растяжение.</param>
      /// <param name="ca">Учитывать работу на сжатие.</param>
      /// <returns>Нагрузка <see cref="Load"/> с усилиями от преднапряжения в арматуре.</returns>
      public Load GetPreLoad(CalcType calc, bool tb, bool ca)
      {
         Diagramm dia = Diagramms[calc];

         Load res = new Load() { Calc = calc };
         for (int i = 0; i < ReBars.Count; i++)
         {
            double s = dia.Sig(ReBars[i].Eps_p, out double E2, tb, ca);
            res.N_ps += ReBars[i].Area * s;
            res.My_ps += ReBars[i].Area * s * ReBars[i].Y;
            res.Mz_ps += ReBars[i].Area * s * ReBars[i].X;
         }

         return res;
      }
   }
}