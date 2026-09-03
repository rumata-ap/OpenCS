using System;

namespace CScore
{
   /// <summary>Секущая матрица жёсткости сечения в порядке СП 63: [Mx, My, N] × [ky, kz, e0].</summary>
   public sealed class SecantStiffnessMatrix
   {
      /// <summary>Коэффициент при ky в уравнении для Mx.</summary>
      public double D11 { get; }

      /// <summary>Коэффициент при kz в уравнении для Mx.</summary>
      public double D12 { get; }

      /// <summary>Коэффициент при e0 в уравнении для Mx.</summary>
      public double D13 { get; }

      /// <summary>Коэффициент при kz в уравнении для My.</summary>
      public double D22 { get; }

      /// <summary>Коэффициент при e0 в уравнении для My.</summary>
      public double D23 { get; }

      /// <summary>Коэффициент при e0 в уравнении для N.</summary>
      public double D33 { get; }

      /// <summary>Симметричный коэффициент D21.</summary>
      public double D21 => D12;

      /// <summary>Симметричный коэффициент D31.</summary>
      public double D31 => D13;

      /// <summary>Симметричный коэффициент D32.</summary>
      public double D32 => D23;

      SecantStiffnessMatrix(double d11, double d12, double d13,
                            double d22, double d23, double d33)
      {
         D11 = d11;
         D12 = d12;
         D13 = d13;
         D22 = d22;
         D23 = d23;
         D33 = d33;
      }

      /// <summary>Создаёт вклад одной площадной точки с секущим модулем E.</summary>
      /// <param name="area">Площадь точки.</param>
      /// <param name="x">Координата точки x.</param>
      /// <param name="y">Координата точки y.</param>
      /// <param name="eSec">Секущий модуль материала.</param>
      public static SecantStiffnessMatrix FromContributions(double area, double x, double y, double eSec)
      {
         double weight = area * eSec;
         return new SecantStiffnessMatrix(
            d11: weight * y * y,
            d12: weight * x * y,
            d13: weight * y,
            d22: weight * x * x,
            d23: weight * x,
            d33: weight);
      }

      /// <summary>Нулевая матрица жёсткости.</summary>
      public static SecantStiffnessMatrix Zero => new(0, 0, 0, 0, 0, 0);

      /// <summary>Создаёт матрицу из интегралов E по площади и координатным мономам.</summary>
      public static SecantStiffnessMatrix FromWeightedIntegrals(
         double a0, double ax, double ay, double axx, double axy, double ayy)
      {
         return new SecantStiffnessMatrix(
            d11: ayy,
            d12: axy,
            d13: ay,
            d22: axx,
            d23: ax,
            d33: a0);
      }

      /// <summary>Складывает вклады жёсткости областей.</summary>
      public static SecantStiffnessMatrix operator +(SecantStiffnessMatrix left,
                                                       SecantStiffnessMatrix right)
      {
         return new SecantStiffnessMatrix(
            left.D11 + right.D11,
            left.D12 + right.D12,
            left.D13 + right.D13,
            left.D22 + right.D22,
            left.D23 + right.D23,
            left.D33 + right.D33);
      }

      /// <summary>Умножает матрицу на вектор деформаций [ky, kz, e0].</summary>
      public Load Apply(double ky, double kz, double e0)
      {
         return new Load
         {
            Mx = D11 * ky + D12 * kz + D13 * e0,
            My = D21 * ky + D22 * kz + D23 * e0,
            N = D31 * ky + D32 * kz + D33 * e0
         };
      }

      /// <summary>Возвращает полную симметричную матрицу 3×3 в порядке СП 63.</summary>
      public double[,] ToArray()
      {
         return new[,]
         {
            { D11, D12, D13 },
            { D21, D22, D23 },
            { D31, D32, D33 }
         };
      }
   }

   /// <summary>Результат расчёта секущей матрицы жёсткости с указанием источника интегрирования.</summary>
   public sealed class SectionStateStiffness
   {
      /// <summary>Секущая матрица в порядке [Mx, My, N] × [ky, kz, e0].</summary>
      public SecantStiffnessMatrix Matrix { get; }

      /// <summary>Источник интегрирования: fiber, contour или mixed.</summary>
      public string Source { get; }

      /// <summary>Создаёт результат расчёта жёсткости.</summary>
      public SectionStateStiffness(SecantStiffnessMatrix matrix, string source)
      {
         Matrix = matrix;
         Source = source;
      }
   }

   /// <summary>Численный якобиан solver-а Ньютона в порядке строк [N, Mx, My] и столбцов [e0, ky, kz].</summary>
   public sealed class NewtonJacobian
   {
      /// <summary>Названия строк якобиана.</summary>
      public string[] Rows { get; } = ["N", "Mx", "My"];

      /// <summary>Названия столбцов якобиана.</summary>
      public string[] Columns { get; } = ["e0", "ky", "kz"];

      /// <summary>Значения якобиана по строкам и столбцам.</summary>
      public double[][] Values { get; }

      /// <summary>Шаг конечных разностей.</summary>
      public double Step { get; }

      /// <summary>Признак использования центральных разностей.</summary>
      public bool Central { get; }

      /// <summary>Обозначение схемы конечных разностей.</summary>
      public string Scheme => Central ? "central" : "forward";

      internal NewtonJacobian(double[,] values, double step, bool central)
      {
         Values = new double[3][];
         for (int row = 0; row < 3; row++)
         {
            Values[row] = new double[3];
            for (int column = 0; column < 3; column++)
               Values[row][column] = values[row, column];
         }

         Step = step;
         Central = central;
      }

      /// <summary>Возвращает значение элемента якобиана.</summary>
      public double this[int row, int column] => Values[row][column];

      /// <summary>Возвращает якобиан в формате двумерного массива.</summary>
      public double[,] ToArray()
      {
         var result = new double[3, 3];
         for (int row = 0; row < 3; row++)
            for (int column = 0; column < 3; column++)
               result[row, column] = Values[row][column];
         return result;
      }
   }

   /// <summary>Снимок диагностик одного состояния деформаций, предназначенный для сохранения в отчёте.</summary>
   public sealed class SectionStateDiagnostics
   {
      /// <summary>Секущая матрица жёсткости СП 63.</summary>
      public SecantStiffnessMatrix D { get; init; } = null!;

      /// <summary>Якобиан, использованный решателем Ньютона.</summary>
      public NewtonJacobian Jacobian { get; init; } = null!;

      /// <summary>Источник матрицы: fiber, contour или mixed.</summary>
      public string DSource { get; init; } = "unknown";

      /// <summary>Результирующие усилия в состоянии.</summary>
      public Load Equilibrium { get; init; }

      /// <summary>Минимальная и максимальная деформация бетона.</summary>
      public (double Min, double Max) ConcreteStrainExtrema { get; init; }

      /// <summary>Минимальная и максимальная деформация арматуры.</summary>
      public (double Min, double Max) SteelStrainExtrema { get; init; }
   }
}
