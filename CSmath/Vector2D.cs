using System;
using static System.Math;

namespace CSmath.Geometry
{
   /// <summary>
   /// Двумерный вектор, наследуемый от базового класса <see cref="Vector"/>.
   /// Предоставляет операции сложения, вычитания, поэлементного умножения и деления,
   /// а также вычисление косинуса угла между векторами и векторного произведения.
   /// </summary>
   [Serializable]
   public class Vector2D : Vector
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
      /// Длина (модуль) вектора: sqrt(X^2 + Y^2).
      /// </summary>
      public double Length => Sqrt(X * X + Y * Y);

      /// <summary>
      /// Единичный вектор (нормализованный), направленный так же, как данный вектор.
      /// </summary>
      public Vector2D Unit => new(X / Length, Y / Length);

      /// <summary>
      /// Создаёт двумерный вектор с нулевыми компонентами.
      /// </summary>
      public Vector2D() : base(2) { }

      /// <summary>
      /// Создаёт двумерный вектор с заданными компонентами.
      /// </summary>
      /// <param name="v1">Значение компоненты X.</param>
      /// <param name="v2">Значение компоненты Y.</param>
      public Vector2D(double v1, double v2) : base(2)
      {
         this[0] = v1; this[1] = v2;
      }

      /// <summary>
      /// Создаёт копию заданного двумерного вектора.
      /// </summary>
      /// <param name="source">Исходный вектор для копирования.</param>
      public Vector2D(Vector2D source) : base(source) { }

      /// <summary>
      /// Создаёт двумерный вектор из n-мерного вектора, используя первые две компоненты.
      /// Если размерность исходного вектора меньше 2, компоненты принимают значения по умолчанию.
      /// </summary>
      /// <param name="source">Исходный n-мерный вектор.</param>
      public Vector2D(Vector source) : base(2)
      {
         if (source.N >= 2)
         {
            this[0] = source[0]; this[1] = source[1];
         }
      }

      /// <summary>
      /// Создаёт двумерный вектор из массива, используя первые два элемента.
      /// Если длина массива меньше 2, компоненты принимают значения по умолчанию.
      /// </summary>
      /// <param name="source">Массив значений компонент.</param>
      public Vector2D(double[] source) : base(2)
      {
         if (source.Length >= 2)
         {
            this[0] = source[0]; this[1] = source[1];
         }
      }

      /// <summary>
      /// Вычисляет косинус угла между двумя векторами.
      /// </summary>
      /// <param name="v1">Первый вектор.</param>
      /// <param name="v2">Второй вектор.</param>
      /// <returns>Косинус угла между векторами v1 и v2.</returns>
      public static double CosAngleBetVectors(Vector2D v1, Vector2D v2)
      {
         return (v1.X * v2.X + v1.Y * v2.Y) / (v1.Length * v2.Length);
      }

      /// <summary>
      /// Преобразует данный вектор в объект базового класса <see cref="Vector"/>.
      /// </summary>
      /// <returns>Новый вектор с компонентами X и Y.</returns>
      public Vector ToVector() => new(new double[] { X, Y });

      /// <summary>
      /// Вычисляет векторное произведение двух двумерных векторов.
      /// Результат — трёхмерный вектор, перпендикулярный плоскости исходных векторов.
      /// </summary>
      /// <param name="v1">Первый вектор.</param>
      /// <param name="v2">Второй вектор.</param>
      /// <returns>Трёхмерный вектор — результат векторного произведения.</returns>
      public static Vector3D Cross(Vector2D v1, Vector2D v2) => v1 ^ v2;

      /// <summary>
      /// Оператор векторного произведения двух двумерных векторов.
      /// Возвращает трёхмерный вектор, Z-компонента которого равна v1.X * v2.Y - v1.Y * v2.X.
      /// </summary>
      /// <param name="v1">Первый вектор.</param>
      /// <param name="v2">Второй вектор.</param>
      /// <returns>Трёхмерный вектор — результат векторного произведения.</returns>
      public static Vector3D operator ^(Vector2D v1, Vector2D v2)
      {
         return new Vector3D
         {
            X = v1.Y * 0 - 0 * v2.Y,
            Y = 0 * v2.X - v1.X * 0,
            Z = v1.X * v2.Y - v1.Y * v2.X
         };
      }

      /// <summary>
      /// Оператор поэлементного умножения двух двумерных векторов.
      /// </summary>
      /// <param name="v1">Первый вектор.</param>
      /// <param name="v2">Второй вектор.</param>
      /// <returns>Вектор, компоненты которого равны произведению соответствующих компонент v1 и v2.</returns>
      public static Vector2D operator *(Vector2D v1, Vector2D v2)
      {
         return new Vector2D { X = v1.X * v2.X, Y = v1.Y * v2.Y };
      }

      /// <summary>
      /// Оператор поэлементного деления двух двумерных векторов.
      /// </summary>
      /// <param name="v1">Вектор-делимое.</param>
      /// <param name="v2">Вектор-делитель.</param>
      /// <returns>Вектор, компоненты которого равны частному соответствующих компонент v1 и v2.</returns>
      public static Vector2D operator /(Vector2D v1, Vector2D v2)
      {
         return new Vector2D { X = v1.X / v2.X, Y = v1.Y / v2.Y };
      }

      /// <summary>
      /// Оператор поэлементного сложения двух двумерных векторов.
      /// </summary>
      /// <param name="v1">Первый вектор.</param>
      /// <param name="v2">Второй вектор.</param>
      /// <returns>Вектор, компоненты которого равны сумме соответствующих компонент v1 и v2.</returns>
      public static Vector2D operator +(Vector2D v1, Vector2D v2)
      {
         return new Vector2D { X = v1.X + v2.X, Y = v1.Y + v2.Y };
      }

      /// <summary>
      /// Оператор поэлементного вычитания двух двумерных векторов.
      /// </summary>
      /// <param name="v1">Вектор-уменьшаемое.</param>
      /// <param name="v2">Вектор-вычитаемое.</param>
      /// <returns>Вектор, компоненты которого равны разности соответствующих компонент v1 и v2.</returns>
      public static Vector2D operator -(Vector2D v1, Vector2D v2)
      {
         return new Vector2D { X = v1.X - v2.X, Y = v1.Y - v2.Y };
      }
   }
}