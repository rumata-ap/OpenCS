using CSmath.Geometry;

namespace CSmath
{
   /// <summary>
   /// Вектор произвольной размерности с поэлементными арифметическими операциями.
   /// Поддерживает операции сложения, вычитания, умножения, деления, а также
   /// сравнения с другими векторами и скалярами. Является базовым классом для <see cref="Vector2D"/> и <see cref="Vector3D"/>.
   /// </summary>
   [Serializable]
   public class Vector : IVector
   {
      double[] arr;
      int n;

      /// <summary>
      /// Размерность вектора (количество элементов).
      /// </summary>
      public int N { get => n; }

      /// <summary>
      /// Доступ к элементу вектора по индексу.
      /// </summary>
      /// <param name="i">Индекс элемента (от 0 до N-1).</param>
      public double this[int i] { get => arr[i]; set => arr[i] = value; }

      /// <summary>
      /// Создаёт вектор размерности 3, все элементы равны нулю.
      /// </summary>
      public Vector()
      {
         n = 3;
         arr = new double[3];
      }

      /// <summary>
      /// Создаёт вектор заданной размерности, все элементы равны нулю.
      /// </summary>
      /// <param name="N">Размерность вектора.</param>
      public Vector(int N)
      {
         n = N;
         arr = new double[N];
      }

      /// <summary>
      /// Создаёт вектор заданной размерности, все элементы равны указанному значению.
      /// </summary>
      /// <param name="val">Значение для заполнения всех элементов.</param>
      /// <param name="N">Размерность вектора.</param>
      public Vector(double val, int N)
      {
         n = N;
         arr = new double[N];
         for (int i = 0; i < arr.Length; i++) arr[i] = val;
      }

      /// <summary>
      /// Создаёт копию указанного вектора.
      /// </summary>
      /// <param name="source">Исходный вектор.</param>
      public Vector(Vector source)
      {
         n = source.N;
         arr = new double[source.N];
         arr = (double[])source.arr.Clone();
      }

      /// <summary>
      /// Создаёт трёхмерный вектор из компонент <see cref="Vector3D"/>.
      /// </summary>
      /// <param name="source">Трёхмерный вектор-источник.</param>
      public Vector(Vector3D source)
      {
         n = 3;
         arr = new double[n];
         arr[0] = source.X;
         arr[1] = source.Y;
         arr[2] = source.Z;
      }

      /// <summary>
      /// Создаёт вектор из массива значений.
      /// </summary>
      /// <param name="source">Массив значений. Размерность вектора определяется длиной массива.</param>
      public Vector(double[] source)
      {
         n = source.Length;
         arr = new double[source.Length];
         arr = (double[])source.Clone();
      }

      /// <summary>
      /// Создаёт вектор из списка значений.
      /// </summary>
      /// <param name="source">Список значений.</param>
      public Vector(List<double> source)
      {
         n = source.Count;
         arr = source.ToArray();
      }

      /// <summary>
      /// Возвращает копию внутренних данных вектора в виде массива.
      /// </summary>
      /// <returns>Копия массива значений вектора.</returns>
      public double[] ToArray()
      {
         return arr;
      }

      /// <summary>
      /// Евклидова норма (длина) вектора.
      /// </summary>
      public double Norma
      {
         get
         {
            double sum = 0;
            for (int i = 0; i < arr.Length; i++) sum += arr[i] * arr[i];
            return Math.Sqrt(sum);
         }
      }

      /// <summary>
      /// Сумма всех элементов вектора.
      /// </summary>
      /// <returns>Сумма элементов.</returns>
      public double Sum()
      {
         double sum = 0;
         for (int i = 0; i < arr.Length; i++) sum += arr[i];
         return sum;
      }

      /// <summary>
      /// Преобразует вектор в список значений.
      /// </summary>
      /// <returns>Список значений вектора.</returns>
      public List<double> ToList()
      {
         return new List<double>(arr);
      }

      /// <summary>
      /// Преобразует вектор в матрицу заданной размерности.
      /// </summary>
      /// <param name="r">Количество строк матрицы.</param>
      /// <param name="c">Количество столбцов матрицы.</param>
      /// <returns>Матрица, заполненная элементами вектора по строкам.</returns>
      /// <exception cref="ArgumentException">Размерность вектора не совпадает с размерностью матрицы (r * c).</exception>
      public Matrix ToMatrix(int r, int c)
      {
         if (r * c != n) { throw new System.ArgumentException("Размерность вектора не соответствует размерности матрицы."); }
         Matrix res = new Matrix(r, c);
         int k = 0;
         for (int i = 0; i < r; i++)
         {
            for (int j = 0; j < c; j++)
            {
               res[i, j] = arr[k];
               k++;
            }
         }

         return res;
      }

      /// <summary>
      /// Преобразует вектор в трёхмерный вектор <see cref="Vector3D"/>.
      /// </summary>
      /// <returns>Трёхмерный вектор с компонентами X, Y, Z из первых трёх элементов.</returns>
      public Vector3D ToVector3d()
      {
         return new Vector3D { X = arr[0], Y = arr[1], Z = arr[2] };
      }

      /// <summary>
      /// Создаёт вектор с равномерно распределёнными значениями от start до end.
      /// </summary>
      /// <param name="start">Начальное значение.</param>
      /// <param name="end">Конечное значение.</param>
      /// <param name="count">Количество интервалов (количество точек = count + 1).</param>
      /// <returns>Вектор с равномерно распределёнными значениями.</returns>
      public static Vector Arange(double start, double end, int count)
      {
         double s = start;
         double l = end - start;
         double dl = l / count;
         List<double> list = new List<double>(count);
         for (int i = 0; i <= count; i++)
         {
            list.Add(s + dl * i);
         }

         return new Vector(list);
      }

      /// <summary>
      /// Объединяет два вектора в один.
      /// </summary>
      /// <param name="v1">Первый вектор.</param>
      /// <param name="v2">Второй вектор.</param>
      /// <returns>Вектор, содержащий элементы v1, за которыми следуют элементы v2.</returns>
      public static Vector Stack(Vector v1, Vector v2)
      {
         List<double> res = v1.ToList();
         res.AddRange(v2.ToList());
         return new Vector(res);
      }

      /// <summary>
      /// Транспонирует вектор, преобразуя его в матрицу-строку (1 × N).
      /// </summary>
      /// <returns>Матрица размерности 1 × N.</returns>
      public Matrix Transpose()
      {
         Matrix res = new Matrix(1, n);
         for (int i = 0; i < n; i++)
         {
            res[0, i] = arr[i];
         }
         return res;
      }


      /// <summary>
      /// Выполняет указанное действие над каждым элементом вектора по индексу.
      /// </summary>
      /// <param name="func">Действие, принимающее индекс элемента.</param>
      public void ProcessFunction(Action<int> func)
      {
         for (var i = 0; i < N; i++)
         {
            func(i);
         }
      }

      /// <summary>
      /// Применяет функцию к каждому элементу вектора и возвращает новый вектор с результатами.
      /// </summary>
      /// <param name="func">Функция преобразования элемента.</param>
      /// <returns>Новый вектор с результатами применения функции.</returns>
      public Vector ProcessFunction(Func<double, double> func)
      {
         double[] res = new double[N];
         for (var i = 0; i < N; i++)
         {
            res[i] = func(this[i]);
         }
         return new Vector(res);
      }

      /// <summary>
      /// Применяет функцию с дополнительным булевым параметром к каждому элементу вектора.
      /// </summary>
      /// <param name="func">Функция преобразования элемента с булевым параметром.</param>
      /// <param name="ten">Булевый параметр, передаваемый в функцию.</param>
      /// <returns>Новый вектор с результатами применения функции.</returns>
      public Vector ProcessFunction(Func<double, bool, double> func, bool ten)
      {
         double[] res = new double[N];
         for (var i = 0; i < N; i++)
         {
            res[i] = func(this[i], ten);
         }
         return new Vector(res);
      }

      /// <summary>
      /// Преобразует радианы в градусы.
      /// </summary>
      public static Func<double, double> RadToDeg = radians => radians * (180.0 / Math.PI);

      /// <summary>
      /// Преобразует градусы в радианы.
      /// </summary>
      public static Func<double, double> DegToRad = degrees => degrees * (Math.PI / 180.0);

      /// <summary>
      /// Умножает вектор на матрицу-строку (внешнее произведение).
      /// </summary>
      /// <param name="v1">Вектор-столбец.</param>
      /// <param name="v2T">Матрица-строка.</param>
      /// <returns>Матрица размерности v1.N × v2T.M.</returns>
      /// <exception cref="ArgumentException">Размерности не совпадают.</exception>
      public static Matrix operator *(Vector v1, Matrix v2T)
      {
         if (v1.n != v2T.M) { throw new ArgumentException("Не совпадают размерности векторов."); }
         double[,] res = new double[v1.n, v2T.M];
         for (int i = 0; i < v1.n; ++i)
         {
            for (int j = 0; j < v2T.M; ++j)
            {
               res[i, j] = v1[i] * v2T[0, j];
            }
         }

         return new Matrix(res);
      }

      /// <summary>
      /// Поэлементное умножение двух векторов (адамарово произведение).
      /// </summary>
      /// <exception cref="ArgumentException">Размерности векторов не совпадают.</exception>
      public static Vector operator *(Vector v1, Vector v2)
      {
         if (v1.n != v2.n) { throw new ArgumentException("Не совпадают размерности векторов."); }
         double[] res = new double[v1.n];
         for (int i = 0; i < v1.n; i++)
         {
            res[i] = v1[i] * v2[i];
         }

         return new Vector(res);
      }

      /// <summary>
      /// Умножает вектор на скаляр.
      /// </summary>
      public static Vector operator *(Vector v1, double prime)
      {
         double[] res = new double[v1.n];
         for (int i = 0; i < v1.n; i++)
         {
            res[i] = v1[i] * prime;
         }

         return new Vector(res);
      }

      /// <summary>
      /// Умножает скаляр на вектор.
      /// </summary>
      public static Vector operator *(double prime, Vector v1)
      {
         double[] res = new double[v1.n];
         for (int i = 0; i < v1.n; i++)
         {
            res[i] = v1[i] * prime;
         }

         return new Vector(res);
      }

      /// <summary>
      /// Поэлементное деление двух векторов.
      /// </summary>
      /// <exception cref="ArgumentException">Размерности векторов не совпадают.</exception>
      public static Vector operator /(Vector v1, Vector v2)
      {
         if (v1.n != v2.n) { throw new System.ArgumentException("Не совпадают размерности векторов."); }
         double[] res = new double[v1.n];
         for (int i = 0; i < v1.n; i++)
         {
            res[i] = v1[i] / v2[i];
         }

         return new Vector(res);
      }

      /// <summary>
      /// Делит вектор на скаляр.
      /// </summary>
      public static Vector operator /(Vector v1, double prime)
      {
         double[] res = new double[v1.n];
         for (int i = 0; i < v1.n; i++)
         {
            res[i] = v1[i] / prime;
         }

         return new Vector(res);
      }

      /// <summary>
      /// Делит скаляр на вектор (поэлементно: prime / v1[i]).
      /// </summary>
      public static Vector operator /(double prime, Vector v1)
      {
         double[] res = new double[v1.n];
         for (int i = 0; i < v1.n; i++)
         {
            res[i] = prime / v1[i];
         }

         return new Vector(res);
      }

      /// <summary>
      /// Поэлементное сложение двух векторов.
      /// </summary>
      /// <exception cref="ArgumentException">Размерности векторов не совпадают.</exception>
      public static Vector operator +(Vector v1, Vector v2)
      {
         if (v1.n != v2.n) { throw new System.ArgumentException("Не совпадают размерности векторов."); }
         double[] res = new double[v1.n];
         for (int i = 0; i < v1.n; i++)
         {
            res[i] = v1[i] + v2[i];
         }

         return new Vector(res);
      }


      /// <summary>
      /// Прибавляет скаляр к каждому элементу вектора.
      /// </summary>
      public static Vector operator +(Vector v1, double prime)
      {
         double[] res = new double[v1.n];
         for (int i = 0; i < v1.n; i++)
         {
            res[i] = v1[i] + prime;
         }

         return new Vector(res);
      }

      /// <summary>
      /// Прибавляет вектор к скаляру (эквивалентно v1 + prime).
      /// </summary>
      public static Vector operator +(double prime, Vector v1)
      {
         double[] res = new double[v1.n];
         for (int i = 0; i < v1.n; i++)
         {
            res[i] = v1[i] + prime;
         }

         return new Vector(res);
      }

      /// <summary>
      /// Поэлементное вычитание двух векторов.
      /// </summary>
      /// <exception cref="ArgumentException">Размерности векторов не совпадают.</exception>
      public static Vector operator -(Vector v1, Vector v2)
      {
         if (v1.n != v2.n) { throw new System.ArgumentException("Не совпадают размерности векторов."); }
         double[] res = new double[v1.n];
         for (int i = 0; i < v1.n; i++)
         {
            res[i] = v1[i] - v2[i];
         }

         return new Vector(res);
      }


      /// <summary>
      /// Вычитает скаляр из каждого элемента вектора.
      /// </summary>
      public static Vector operator -(Vector v1, double prime)
      {
         double[] res = new double[v1.n];
         for (int i = 0; i < v1.n; i++)
         {
            res[i] = v1[i] - prime;
         }

         return new Vector(res);
      }

      /// <summary>
      /// Вычитает вектор из скаляра (поэлементно: prime - v1[i]).
      /// </summary>
      public static Vector operator -(double prime, Vector v1)
      {
         double[] res = new double[v1.n];
         for (int i = 0; i < v1.n; i++)
         {
            res[i] = prime - v1[i];
         }

         return new Vector(res);
      }

      /// <summary>
      /// Поэлементное сравнение «меньше» двух векторов. Возвращает вектор из 0 и 1.
      /// </summary>
      /// <exception cref="ArgumentException">Размерности векторов не совпадают.</exception>
      public static Vector operator <(Vector v1, Vector v2)
      {
         if (v1.n != v2.n) { throw new ArgumentException("Не совпадают размерности векторов."); }
         double[] res = new double[v1.n];
         for (int i = 0; i < v1.n; i++)
         {
            res[i] = v1[i] < v2[i] ? 1 : 0;
         }

         return new Vector(res);
      }

      /// <summary>
      /// Поэлементное сравнение «меньше или равно» двух векторов. Возвращает вектор из 0 и 1.
      /// </summary>
      /// <exception cref="ArgumentException">Размерности векторов не совпадают.</exception>
      public static Vector operator <=(Vector v1, Vector v2)
      {
         if (v1.n != v2.n) { throw new ArgumentException("Не совпадают размерности векторов."); }
         double[] res = new double[v1.n];
         for (int i = 0; i < v1.n; i++)
         {
            res[i] = v1[i] <= v2[i] ? 1 : 0;
         }

         return new Vector(res);
      }

      /// <summary>
      /// Поэлементное сравнение «больше» двух векторов. Возвращает вектор из 0 и 1.
      /// </summary>
      /// <exception cref="ArgumentException">Размерности векторов не совпадают.</exception>
      public static Vector operator >(Vector v1, Vector v2)
      {
         if (v1.n != v2.n) { throw new ArgumentException("Не совпадают размерности векторов."); }
         double[] res = new double[v1.n];
         for (int i = 0; i < v1.n; i++)
         {
            res[i] = v1[i] > v2[i] ? 1 : 0;
         }

         return new Vector(res);
      }

      /// <summary>
      /// Поэлементное сравнение «больше или равно» двух векторов. Возвращает вектор из 0 и 1.
      /// </summary>
      /// <exception cref="ArgumentException">Размерности векторов не совпадают.</exception>
      public static Vector operator >=(Vector v1, Vector v2)
      {
         if (v1.n != v2.n) { throw new ArgumentException("Не совпадают размерности векторов."); }
         double[] res = new double[v1.n];
         for (int i = 0; i < v1.n; i++)
         {
            res[i] = v1[i] >= v2[i] ? 1 : 0;
         }

         return new Vector(res);
      }

      /// <summary>
      /// Поэлементное сравнение «меньше» вектора и скаляра. Возвращает вектор из 0 и 1.
      /// </summary>
      public static Vector operator <(Vector v1, double v2)
      {
         double[] res = new double[v1.n];
         for (int i = 0; i < v1.n; i++)
         {
            res[i] = v1[i] < v2 ? 1 : 0;
         }

         return new Vector(res);
      }

      /// <summary>
      /// Поэлементное сравнение «меньше или равно» вектора и скаляра. Возвращает вектор из 0 и 1.
      /// </summary>
      public static Vector operator <=(Vector v1, double v2)
      {
         double[] res = new double[v1.n];
         for (int i = 0; i < v1.n; i++)
         {
            res[i] = v1[i] <= v2 ? 1 : 0;
         }

         return new Vector(res);
      }

      /// <summary>
      /// Поэлементное сравнение «больше» вектора и скаляра. Возвращает вектор из 0 и 1.
      /// </summary>
      public static Vector operator >(Vector v1, double v2)
      {
         double[] res = new double[v1.n];
         for (int i = 0; i < v1.n; i++)
         {
            res[i] = v1[i] > v2 ? 1 : 0;
         }

         return new Vector(res);
      }

      /// <summary>
      /// Поэлементное сравнение «больше или равно» вектора и скаляра. Возвращает вектор из 0 и 1.
      /// </summary>
      public static Vector operator >=(Vector v1, double v2)
      {
         double[] res = new double[v1.n];
         for (int i = 0; i < v1.n; i++)
         {
            res[i] = v1[i] >= v2 ? 1 : 0;
         }

         return new Vector(res);
      }

   }
}