using System;

namespace CScore.Combinations
{
   /// <summary>
   /// Одно загружение: метаданные + матрица усилий (n_sections × n_components).
   /// Для одного поперечного сечения n_sections = 1.
   /// </summary>
   public class Loading
   {
      /// <summary>Название загружения (уникальное в задаче).</summary>
      public string Name { get; }

      /// <summary>Вид нагрузки.</summary>
      public NormLoadType LoadType { get; }

      /// <summary>
      /// Матрица усилий [n_sections, n_components].
      /// Forces[s, k] — компонента k в сечении s.
      /// </summary>
      public double[,] Forces { get; }

      /// <summary>Имена компонент усилий, например ("N","Mx","My","Vx","Vy","T").</summary>
      public string[] ComponentNames { get; }

      /// <summary>Коэффициент надёжности по нагрузке (неблагоприятное действие).</summary>
      public double GammaFUnfav { get; }

      /// <summary>Коэффициент надёжности по нагрузке (благоприятное действие).</summary>
      public double GammaFFav { get; }

      /// <summary>ψ₁ — в роли ведущей переменной нагрузки.</summary>
      public double Psi1 { get; }

      /// <summary>ψ₂ — в роли сопровождающей переменной нагрузки.</summary>
      public double Psi2 { get; }

      /// <summary>Группа взаимоисключения (null если нет). Из группы попадает не более одной нагрузки.</summary>
      public string? Group { get; }

      public int NSections   => Forces.GetLength(0);
      public int NComponents => Forces.GetLength(1);

      Loading(string name, NormLoadType type, double[,] forces, string[] componentNames,
              double gammaFUnfav, double gammaFFav, double psi1, double psi2, string? group)
      {
         if (string.IsNullOrEmpty(name))
            throw new ArgumentException("Имя загружения не может быть пустым.");
         if (forces.GetLength(1) != componentNames.Length)
            throw new ArgumentException(
               $"Загружение '{name}': forces.GetLength(1)={forces.GetLength(1)} " +
               $"не совпадает с componentNames.Length={componentNames.Length}.");

         Name = name; LoadType = type; Forces = forces; ComponentNames = componentNames;
         GammaFUnfav = gammaFUnfav; GammaFFav = gammaFFav; Psi1 = psi1; Psi2 = psi2; Group = group;
      }

      // ---------------------------------------------------------------
      // Фабричные методы
      // ---------------------------------------------------------------

      /// <summary>Постоянная нагрузка (γf_unfav=1.1, γf_fav=0.9).</summary>
      public static Loading Permanent(string name, double[,] forces,
         string[]? componentNames = null,
         double gammaFUnfav = 1.1, double gammaFFav = 0.9,
         string? group = null)
         => new(name, NormLoadType.Permanent, forces,
                componentNames ?? InferNames(forces),
                gammaFUnfav, gammaFFav, 1.0, 1.0, group);

      /// <summary>Длительная переменная нагрузка (ψ₂=0.95).</summary>
      public static Loading LongTerm(string name, double[,] forces,
         string[]? componentNames = null,
         double gammaFUnfav = 1.2, double gammaFFav = 1.0,
         double psi1 = 1.0, double psi2 = 0.95,
         string? group = null)
         => new(name, NormLoadType.LongTerm, forces,
                componentNames ?? InferNames(forces),
                gammaFUnfav, gammaFFav, psi1, psi2, group);

      /// <summary>Кратковременная переменная нагрузка (ψ₂=0.9).</summary>
      public static Loading ShortTerm(string name, double[,] forces,
         string[]? componentNames = null,
         double gammaFUnfav = 1.4, double gammaFFav = 1.0,
         double psi1 = 1.0, double psi2 = 0.9,
         string? group = null)
         => new(name, NormLoadType.ShortTerm, forces,
                componentNames ?? InferNames(forces),
                gammaFUnfav, gammaFFav, psi1, psi2, group);

      /// <summary>Особая нагрузка (γf=1.0 по умолчанию, включается с коэф. 1.0).</summary>
      public static Loading Accidental(string name, double[,] forces,
         string[]? componentNames = null,
         double gammaFUnfav = 1.0, double gammaFFav = 1.0,
         string? group = null)
         => new(name, NormLoadType.Accidental, forces,
                componentNames ?? InferNames(forces),
                gammaFUnfav, gammaFFav, 1.0, 1.0, group);

      // ---------------------------------------------------------------
      // Вспомогательные
      // ---------------------------------------------------------------

      static string[] InferNames(double[,] forces)
      {
         int n = forces.GetLength(1);
         return n switch
         {
            6 => ["N", "Mx", "My", "Vx", "Vy", "T"],
            8 => ["Nx", "Ny", "Nxy", "Mx", "My", "Mxy", "Qx", "Qy"],
            _ => System.Linq.Enumerable.Range(0, n).Select(i => $"F{i}").ToArray()
         };
      }

      public override string ToString() =>
         $"Loading('{Name}', {LoadType}, n_sect={NSections}, " +
         $"γf=[{GammaFFav}..{GammaFUnfav}], ψ=[{Psi2}..{Psi1}]" +
         (Group != null ? $", group='{Group}'" : "") + ")";
   }
}
