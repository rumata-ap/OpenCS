using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CScore
{
   /// <summary>
   /// Материал сечения — хранит базовые свойства и набор характеристик
   /// по видам расчёта (<see cref="CalcType"/>). Поддерживает построение
   /// диаграмм работы материала различных типов (двухлинейная, трёхлинейная,
   /// криволинейная по СП 63.13330).
   /// </summary>
   [Serializable]
   public class Material
   {
      /// <summary>
      /// Словарь характеристик материала по видам расчёта.
      /// Ключ — <see cref="CalcType"/>, значение — <see cref="MaterialChars"/>.
      /// </summary>
      internal Dictionary<CalcType, MaterialChars> chars = [];

      /// <summary>
      /// Первичный ключ для EF Core.
      /// </summary>
      [JsonIgnore]
      public int Id { get; set; }

      /// <summary>
      /// Порядковый номер материала.
      /// </summary>
      public int Num { get; set; }

      /// <summary>
      /// JSON-представление материала для сериализации.
      /// </summary>
      [JsonIgnore]
      public string Json { get; set; } = "";

      /// <summary>
      /// Краткое наименование материала (марка/класс).
      /// </summary>
      public string Tag { get; set; } = "";

      /// <summary>
      /// Полное описание материала.
      /// </summary>
      public string Description { get; set; } = "";

      /// <summary>
      /// Начальный модуль упругости материала [МПа].
      /// </summary>
      public double E { get; set; }

      /// <summary>
      /// Тип материала (бетон, арматура с физическим пределом текучести,
      /// арматура с условным пределом текучести, сталь).
      /// </summary>
      public MatType Type { get; set; } = MatType.None;

      /// <summary>
      /// Список характеристик материала по видам расчёта.
      /// При установке заполняет внутренний словарь <see cref="chars"/>.
      /// </summary>
      [JsonIgnore] public List<MaterialChars> materialChars = [];

      /// <summary>
      /// Список характеристик материала по видам расчёта.
      /// При установке значения с 4 элементами автоматически заполняет
      /// словарь <see cref="chars"/> ключами C, CL, N, NL.
      /// </summary>
      [JsonIgnore] public List<MaterialChars> MaterialChars
      {
         get {  return materialChars; }
         set
         {
            materialChars = value;
            if (value.Count == 4)
            {
               foreach (var item in value)
               {
                  if (item.TypeCalc == CalcType.C) chars[CalcType.C] = item;
                  else if (item.TypeCalc == CalcType.CL) chars[CalcType.CL] = item;
                  else if (item.TypeCalc == CalcType.N) chars[CalcType.N] = item;
                  else chars[CalcType.NL] = item;
               }
            }
         }
      }

      /// <summary>
      /// Характеристики материала для расчёта по первому предельному состоянию
      /// (непродолжительное действие нагрузки).
      /// </summary>
      public MaterialChars C
      {
         get
         {
            foreach (var item in materialChars)
               if (item.TypeCalc == CalcType.C) return item;
            return null; ;
         }
         set
         {
            chars[CalcType.C] = value;
            for (int i = 0; i < materialChars.Count; i++)
               if (materialChars[i].TypeCalc == CalcType.C)
                  materialChars[i] = value;
         }
      }

      /// <summary>
      /// Характеристики материала для расчёта по первому предельному состоянию
      /// (продолжительное действие нагрузки).
      /// </summary>
      public MaterialChars CL
      {
         get
         {
            foreach (var item in materialChars)
               if (item.TypeCalc == CalcType.CL) return item;
            return null;
         }
         set
         {
            chars[CalcType.CL] = value;
            for (int i = 0; i < materialChars.Count; i++)
               if (materialChars[i].TypeCalc == CalcType.CL)
                  materialChars[i] = value;
         }
      }

      /// <summary>
      /// Характеристики материала для расчёта по второй группе предельных состояний
      /// (непродолжительное действие нагрузки).
      /// </summary>
      public MaterialChars N
      {
         get
         {
            foreach (var item in materialChars)
               if (item.TypeCalc == CalcType.N) return item;
            return null; ;
         }
         set
         {
            chars[CalcType.N] = value;
            for (int i = 0; i < materialChars.Count; i++)
               if (materialChars[i].TypeCalc == CalcType.N)
                  materialChars[i] = value;
         }
      }

      /// <summary>
      /// Характеристики материала для расчёта по второй группе предельных состояний
      /// (продолжительное действие нагрузки).
      /// </summary>
      public MaterialChars NL
      {
         get
         {
            foreach (var item in materialChars)
               if (item.TypeCalc == CalcType.NL) return item;
            return null;
         }
         set
         {
            chars[CalcType.NL] = value;
            for (int i = 0; i < materialChars.Count; i++)
               if (materialChars[i].TypeCalc == CalcType.NL)
                  materialChars[i] = value;
         }
      }

      /// <summary>
      /// Конструктор по умолчанию.
      /// </summary>
      public Material()
      {
      }

      /// <summary>
      /// Создаёт материал с пустыми характеристиками для всех четырёх видов расчёта.
      /// </summary>
      /// <param name="n">Не используется (зарезервировано).</param>
      public Material(int n)
      {
         chars[CalcType.C] = new() { TypeCalc = CalcType.C, Material = this };
         chars[CalcType.CL] = new() { TypeCalc = CalcType.CL, Material = this };
         chars[CalcType.N] = new() { TypeCalc = CalcType.N, Material = this };
         chars[CalcType.NL] = new() { TypeCalc = CalcType.NL, Material = this };
         materialChars = [chars[CalcType.C], chars[CalcType.CL], chars[CalcType.N], chars[CalcType.NL]];
      }

      /// <summary>
      /// Создаёт материал с заданными характеристиками по видам расчёта.
      /// </summary>
      /// <param name="id">Идентификатор материала.</param>
      /// <param name="tag">Краткое наименование (марка/класс).</param>
      /// <param name="description">Полное описание материала.</param>
      /// <param name="matType">Тип материала.</param>
      /// <param name="c">Характеристики для расчёта C.</param>
      /// <param name="cl">Характеристики для расчёта CL.</param>
      /// <param name="n">Характеристики для расчёта N.</param>
      /// <param name="nl">Характеристики для расчёта NL.</param>
      public Material(int id, string tag, string description, MatType matType,
         MaterialChars c, MaterialChars cl, MaterialChars n, MaterialChars nl)
      {
         Id = id;
         Tag = tag;
         Description = description;
         C = c;
         CL = cl;
         N = n;
         NL = nl;
         Type = matType;
         E = n.E;
      }

      /// <inheritdoc/>
      public override string ToString()
      {
         return $"{Num}: {Tag} | {Description}";
      }

      /// <summary>
      /// Возвращает словарь диаграмм работы материала для всех видов расчёта
      /// по заданному типу диаграммы.
      /// </summary>
      /// <param name="type">Тип диаграммы (L2 — двухлинейная, L3 — трёхлинейная, SP63 — криволинейная).</param>
      /// <returns>Словарь: ключ — <see cref="CalcType"/>, значение — <see cref="Diagramm"/>.</returns>
      public Dictionary<CalcType, Diagramm> GetDiagramms(DiagrammType type)
      {
         switch (type)
         {
            case DiagrammType.L2:
               return GetD2L();
            case DiagrammType.L3:
               return GetD3L();
            case DiagrammType.SP63:
               return GetDCL();
         }
         return null;
      }

      /// <summary>
      /// Возвращает двухлинейные диаграммы работы материала для всех видов расчёта.
      /// Используется для арматуры с физическим пределом текучести.
      /// </summary>
      /// <returns>Словарь двухлинейных диаграмм по видам расчёта.</returns>
      public Dictionary<CalcType, Diagramm> GetFrFsteelDiagramms()
      {
         return GetD2L();
      }

      /// <summary>
      /// Возвращает трёхлинейные диаграммы работы материала для всех видов расчёта.
      /// Используется для арматуры с условным пределом текучести.
      /// </summary>
      /// <returns>Словарь трёхлинейных диаграмм по видам расчёта.</returns>
      public Dictionary<CalcType, Diagramm> GetUrFsteelDiagramms()
      {
         return GetD3L();
      }

      /// <summary>
      /// Создаёт словарь двухлинейных диаграмм для всех видов расчёта.
      /// </summary>
      Dictionary<CalcType, Diagramm> GetD2L()
      {
         var res = new Dictionary<CalcType, Diagramm>
         {
            { CalcType.C, chars[CalcType.C].D2L() },
            { CalcType.CL, chars[CalcType.CL].D2L() },
            { CalcType.N, chars[CalcType.N].D2L() },
            { CalcType.NL, chars[CalcType.NL].D2L() }
         };

         return res;
      }

      /// <summary>
      /// Создаёт словарь трёхлинейных диаграмм для всех видов расчёта.
      /// </summary>
      Dictionary<CalcType, Diagramm> GetD3L()
      {
         var res = new Dictionary<CalcType, Diagramm>
         {
            { CalcType.C, chars[CalcType.C].D3L() },
            { CalcType.CL, chars[CalcType.CL].D3L() },
            { CalcType.N, chars[CalcType.N].D3L() },
            { CalcType.NL, chars[CalcType.NL].D3L() }
         };

         return res;
      }

      /// <summary>
      /// Создаёт словарь криволинейных диаграмм (по приложению Г СП 63.13330)
      /// для всех видов расчёта.
      /// </summary>
      Dictionary<CalcType, Diagramm> GetDCL()
      {
         var res = new Dictionary<CalcType, Diagramm>
         {
            { CalcType.C, chars[CalcType.C].DCL() },
            { CalcType.CL, chars[CalcType.CL].DCL() },
            { CalcType.N, chars[CalcType.N].DCL() },
            { CalcType.NL, chars[CalcType.NL].DCL() }
         };

         return res;
      }

      /// <summary>
      /// Сериализует объект материала в JSON и сохраняет в свойство <see cref="Json"/>.
      /// </summary>
      public void SetJson()
      {
         Json = JsonConvert.SerializeObject(this, Formatting.Indented);
      }

   }

   /// <summary>
   /// Тип материала: бетон, арматура с физическим пределом текучести,
   /// арматура с условным пределом текучести, конструкционная сталь.
   /// </summary>
   public enum MatType
   {
      /// <summary>Бетон.</summary>
      Concrete = 1,
      /// <summary>Арматура с физическим пределом текучести.</summary>
      ReSteelF = 2,
      /// <summary>Арматура с условным пределом текучести.</summary>
      ReSteelU = 3,
      /// <summary>Конструкционная сталь.</summary>
      Steel = 4,
      /// <summary>Тип не задан.</summary>
      None = 0
   }

   /// <summary>
   /// Вид расчёта: первое предельное состояние (C — непродолжительное, CL — продолжительное),
   /// второе предельное состояние (N — непродолжительное, NL — продолжительное).
   /// </summary>
   public enum CalcType
   {
      /// <summary>Первая группа предельных состояний, непродолжительное действие нагрузки.</summary>
      C = 1,
      /// <summary>Первая группа предельных состояний, продолжительное действие нагрузки.</summary>
      CL = 2,
      /// <summary>Вторая группа предельных состояний, непродолжительное действие нагрузки.</summary>
      N = 3,
      /// <summary>Вторая группа предельных состояний, продолжительное действие нагрузки.</summary>
      NL = 4
   }

   /// <summary>
   /// Влажность окружающей среды: ниже 40%, от 40% до 70%, свыше 70%, любая.
   /// Влияет на коэффициенты условий работы бетона.
   /// </summary>
   public enum Dampness
   {
      /// <summary>Влажность ниже 40%.</summary>
      ниже_40 = 1,
      /// <summary>Влажность от 40% до 70%.</summary>
      от40_до70 = 2,
      /// <summary>Влажность свыше 70%.</summary>
      свыше_70 = 3,
      /// <summary>Любая влажность (не учитывается).</summary>
      any = 0
   }
}