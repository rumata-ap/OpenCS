using System;
using System.Collections.Generic;
using System.Linq;

namespace CScore.Combinations
{
   /// <summary>Одна сгенерированная комбинация (для формирования списка комбинаций).</summary>
   public class GeneratedCase
   {
      public CombType CombType        { get; init; }
      public NormLoadType LeadingType { get; init; }
      public int Section              { get; init; }
      public string Component         { get; init; } = "";
      public int Sign                 { get; init; }

      /// <summary>Суммарный вектор усилий оптимальной комбинации (n_components).</summary>
      public double[] Forces { get; init; } = [];

      /// <summary>Активные загружения: {имя → итоговый коэффициент}.</summary>
      public Dictionary<string, double> Active { get; init; } = [];
   }

   /// <summary>
   /// Огибающая расчётных комбинаций по всем сечениям.
   /// </summary>
   public class Envelope
   {
      public string[] ComponentNames { get; }
      public int NSections           { get; }

      /// <summary>Несвязный максимум [n_comp, n_sect].</summary>
      public double[,] MaxValues { get; }

      /// <summary>Несвязный минимум [n_comp, n_sect].</summary>
      public double[,] MinValues { get; }

      /// <summary>Связный вектор усилий при max компоненты k в сечении s: [n_comp, n_sect, n_comp].</summary>
      public double[,,] MaxForces { get; }

      /// <summary>Связный вектор усилий при min компоненты k в сечении s: [n_comp, n_sect, n_comp].</summary>
      public double[,,] MinForces { get; }

      /// <summary>Активные загружения при max [n_comp, n_sect] → dict или null.</summary>
      public Dictionary<string, double>?[,] MaxActive { get; }

      /// <summary>Активные загружения при min [n_comp, n_sect] → dict или null.</summary>
      public Dictionary<string, double>?[,] MinActive { get; }

      public Envelope(
         string[] componentNames, int nSections,
         double[,] maxValues, double[,] minValues,
         double[,,] maxForces, double[,,] minForces,
         Dictionary<string, double>?[,] maxActive,
         Dictionary<string, double>?[,] minActive)
      {
         ComponentNames = componentNames; NSections = nSections;
         MaxValues = maxValues; MinValues = minValues;
         MaxForces = maxForces; MinForces = minForces;
         MaxActive = maxActive; MinActive = minActive;
      }

      // ---------------------------------------------------------------
      // Доступ к данным
      // ---------------------------------------------------------------

      int Idx(string component)
      {
         int i = Array.IndexOf(ComponentNames, component);
         if (i < 0) throw new KeyNotFoundException(
            $"Компонента '{component}' не найдена. Доступны: {string.Join(", ", ComponentNames)}");
         return i;
      }

      /// <summary>Несвязный максимум компоненты по всем сечениям (длина NSections).</summary>
      public double[] MaxValue(string component)
      {
         int k = Idx(component);
         return Enumerable.Range(0, NSections).Select(s => MaxValues[k, s]).ToArray();
      }

      /// <summary>Несвязный минимум компоненты по всем сечениям (длина NSections).</summary>
      public double[] MinValue(string component)
      {
         int k = Idx(component);
         return Enumerable.Range(0, NSections).Select(s => MinValues[k, s]).ToArray();
      }

      /// <summary>
      /// Связный вектор усилий при максимуме компоненты (размер [NSections, NComponents]).
      /// Строка s — полный набор усилий в сечении s при той комбинации, где component максимальна.
      /// </summary>
      public double[,] Max(string component)
      {
         int k = Idx(component), nc = ComponentNames.Length;
         var result = new double[NSections, nc];
         for (int s = 0; s < NSections; s++)
            for (int j = 0; j < nc; j++)
               result[s, j] = MaxForces[k, s, j];
         return result;
      }

      /// <summary>Связный вектор усилий при минимуме компоненты [NSections, NComponents].</summary>
      public double[,] Min(string component)
      {
         int k = Idx(component), nc = ComponentNames.Length;
         var result = new double[NSections, nc];
         for (int s = 0; s < NSections; s++)
            for (int j = 0; j < nc; j++)
               result[s, j] = MinForces[k, s, j];
         return result;
      }

      /// <summary>Загружения, активные при max компоненты в сечении section.</summary>
      public Dictionary<string, double> ActiveAtMax(string component, int section) =>
         MaxActive[Idx(component), section] ?? [];

      /// <summary>Загружения, активные при min компоненты в сечении section.</summary>
      public Dictionary<string, double> ActiveAtMin(string component, int section) =>
         MinActive[Idx(component), section] ?? [];

      /// <summary>Сводная таблица диапазонов: компонента → (min[], max[]).</summary>
      public Dictionary<string, (double[] Min, double[] Max)> RangeTable()
      {
         var result = new Dictionary<string, (double[], double[])>();
         for (int k = 0; k < ComponentNames.Length; k++)
         {
            var minArr = Enumerable.Range(0, NSections).Select(s => MinValues[k, s]).ToArray();
            var maxArr = Enumerable.Range(0, NSections).Select(s => MaxValues[k, s]).ToArray();
            result[ComponentNames[k]] = (minArr, maxArr);
         }
         return result;
      }

      public override string ToString() =>
         $"Envelope(components=[{string.Join(", ", ComponentNames)}], n_sect={NSections})";
   }
}
