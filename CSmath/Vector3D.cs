using static System.Math;

namespace CSmath.Geometry
{
   /// <summary>
   /// Трёхмерный вектор, наследуемый от базового класса <see cref="Vector"/>.
   /// Предоставляет операции сложения, вычитания, поэлементного умножения и деления,
   /// а также вычисление косинуса угла между векторами и векторного произведения.
   /// </summary>
   [Serializable]
   public class Vector3D : Vector
   {
      /// <summary>
      /// Первая компонента вектора (абсцисса).
      /// </summary>
      public double X { get => this[0]; set => this[0] = value; }

      /// <summary>
      /// Вторая компонента вектора (ордината).
      /// </summary>
      public double Y { get => this[1]; set => this[1] = value; }

      /// <summary>
      /// Третья компонента вектора (аппликата).
      /// </summary>
      public double Z { get => this[2]; set => this[2] = value; }

      /// <summary>
      /// Длина (модуль) вектора: sqrt(X^2 + Y^2 + Z^2).
      /// </summary>
      public double Length => Sqrt(X * X + Y * Y + Z * Z);

      /// <summary>
      /// Единичный вектор (нормализованный), направленный так же, как данный вектор.
      /// </summary>
      public Vector3D Unit => new(X / Length, Y / Length, Z / Length);

      /// <summary>
      /// Создаёт трёхмерный вектор с нулевыми компонентами.
      /// </summary>
      public Vector3D() : base(3) { }

      /// <summary>
      /// Создаёт трёхмерный вектор с заданными компонентами.
      /// </summary>
      /// <param name="x">Значение компоненты X.</param>
      /// <param name="y">Значение компоненты Y.</param>
      /// <param name="z">Значение компоненты Z.</param>
      public Vector3D(double x, double y, double z) : base(3)
      {
         X = x; Y = y; Z = z;
      }

      /// <summary>
      /// Создаёт копию заданного трёхмерного вектора.
      /// </summary>
      /// <param name="source">Исходный вектор для копирования.</param>
      public Vector3D(Vector3D source) : base(source) { }

      /// <summary>
      /// Создаёт трёхмерный вектор из n-мерного вектора, используя первые три компоненты.
      /// Если размерность исходного вектора меньше 3, компоненты принимают значения по умолчанию.
      /// </summary>
      /// <param name="source">Исходный n-мерный вектор.</param>
      public Vector3D(Vector source) : base(3)
      {
         if (source.N >= 3)
         {
            this[0] = source[0]; this[1] = source[1]; this[2] = source[2];
         }
      }

      /// <summary>
      /// Создаёт трёхмерный вектор из двумерного, полагая Z = 0.
      /// </summary>
      /// <param name="source">Исходный двумерный вектор.</param>
      public Vector3D(Vector2D source) : base(3)
      {
         this[0] = source.X; this[1] = source.Y; this[2] = 0;
      }

      /// <summary>
      /// Создаёт трёхмерный вектор из массива, используя первые три элемента.
      /// Если длина массива меньше 3, компоненты принимают значения по умолчанию.
      /// </summary>
      /// <param name="source">Массив значений компонент.</param>
      public Vector3D(double[] source) : base(3)
      {
         if (source.Length >= 3)
         {
            this[0] = source[0]; this[1] = source[1]; this[2] = source[2];
         }
      }

      /// <summary>
      /// Вычисляет косинус угла между двумя трёхмерными векторами.
      /// </summary>
      /// <param name="v1">Первый вектор.</param>
      /// <param name="v2">Второй вектор.</param>
      /// <returns>Косинус угла между векторами v1 и v2.</returns>
      public static double CosAngleBetVectors(Vector3D v1, Vector3D v2)
      {
         return (v1.X * v2.X + v1.Y * v2.Y + v1.Z * v2.Z) / (v1.Length * v2.Length);
      }

      /// <summary>
      /// Преобразует данный трёхмерный вектор в двумерный, отбрасывая Z-компоненту.
      /// </summary>
      /// <returns>Новый двумерный вектор с компонентами X и Y.</returns>
      public Vector2D ToVector2d() => new() { X = X, Y = Y };

      /// <summary>
      /// Преобразует данный вектор в объект базового класса <see cref="Vector"/>.
      /// </summary>
      /// <returns>Новый вектор с компонентами X, Y и Z.</returns>
      public Vector ToVector() => new(new double[] { X, Y, Z });

      /// <summary>
      /// Вычисляет векторное произведение двух трёхмерных векторов.
      /// </summary>
      /// <param name="v1">Первый вектор.</param>
      /// <param name="v2">Второй вектор.</param>
      /// <returns>Трёхмерный вектор, перпендикулярный обоим исходным векторам.</returns>
      public static Vector3D Cross(Vector3D v1, Vector3D v2) => v1 ^ v2;

      /// <summary>
      /// Оператор векторного произведения двух трёхмерных векторов.
      /// </summary>
      /// <param name="v1">Первый вектор.</param>
      /// <param name="v2">Второй вектор.</param>
      /// <returns>Трёхмерный вектор — результат векторного произведения.</returns>
      public static Vector3D operator ^(Vector3D v1, Vector3D v2)
      {
         return new Vector3D
         {
            X = v1.Y * v2.Z - v1.Z * v2.Y,
            Y = v1.Z * v2.X - v1.X * v2.Z,
            Z = v1.X * v2.Y - v1.Y * v2.X
         };
      }

      /// <summary>
      /// Оператор поэлементного умножения двух трёхмерных векторов.
      /// </summary>
      /// <param name="v1">Первый вектор.</param>
      /// <param name="v2">Второй вектор.</param>
      /// <returns>Вектор, компоненты которого равны произведению соответствующих компонент v1 и v2.</returns>
      public static Vector3D operator *(Vector3D v1, Vector3D v2)
      {
         return new Vector3D { X = v1.X * v2.X, Y = v1.Y * v2.Y, Z = v1.Z * v2.Z };
      }

      /// <summary>
      /// Оператор поэлементного деления двух трёхмерных векторов.
      /// </summary>
      /// <param name="v1">Вектор-делимое.</param>
      /// <param name="v2">Вектор-делитель.</param>
      /// <returns>Вектор, компоненты которого равны частному соответствующих компонент v1 и v2.</returns>
      public static Vector3D operator /(Vector3D v1, Vector3D v2)
      {
         return new Vector3D { X = v1.X / v2.X, Y = v1.Y / v2.Y, Z = v1.Z / v2.Z };
      }

      /// <summary>
      /// Оператор поэлементного сложения двух трёхмерных векторов.
      /// </summary>
      /// <param name="v1">Первый вектор.</param>
      /// <param name="v2">Второй вектор.</param>
      /// <returns>Вектор, компоненты которого равны сумме соответствующих компонент v1 и v2.</returns>
      public static Vector3D operator +(Vector3D v1, Vector3D v2)
      {
         return new Vector3D { X = v1.X + v2.X, Y = v1.Y + v2.Y, Z = v1.Z + v2.Z };
      }

      /// <summary>
      /// Оператор поэлементного вычитания двух трёхмерных векторов.
      /// </summary>
      /// <param name="v1">Вектор-уменьшаемое.</param>
      /// <param name="v2">Вектор-вычитаемое.</param>
      /// <returns>Вектор, компоненты которого равны разности соответствующих компонент v1 и v2.</returns>
      public static Vector3D operator -(Vector3D v1, Vector3D v2)
      {
         return new Vector3D { X = v1.X - v2.X, Y = v1.Y - v2.Y, Z = v1.Z - v2.Z };
      }
   }
}