namespace CSmath
{
   /// <summary>
   /// Интерфейс сплайна — интерполирующей функции, заданной на узловых точках
   /// с коэффициентами кусочно-полиномиального представления.
   /// </summary>
   public interface ISpline
   {
      /// <summary>
      /// Массив узлов интерполяции по оси X.
      /// </summary>
      double[] X { get; set; }

      /// <summary>
      /// Массив значений функции в узлах интерполяции.
      /// </summary>
      double[] Y { get; set; }

      /// <summary>
      /// Массив первых производных функции в узлах интерполяции.
      /// </summary>
      double[] DY { get; set; }

      /// <summary>
      /// Массив коэффициентов A кусочно-полиномиального представления сплайна.
      /// </summary>
      double[] A { get; set; }

      /// <summary>
      /// Массив коэффициентов B кусочно-полиномиального представления сплайна.
      /// </summary>
      double[] B { get; set; }

      /// <summary>
      /// Массив коэффициентов C кусочно-полиномиального представления сплайна.
      /// </summary>
      double[] C { get; set; }

      /// <summary>
      /// Массив коэффициентов D кусочно-полиномиального представления сплайна.
      /// </summary>
      double[] D { get; set; }

      /// <summary>
      /// Вычисляет значение сплайна в заданной точке.
      /// </summary>
      /// <param name="xi">Точка, в которой вычисляется значение сплайна.</param>
      /// <returns>Интерполированное значение функции в точке <paramref name="xi"/>.</returns>
      double Interpolate(double xi);

      /// <summary>
      /// Вычисляет производную сплайна в заданной точке.
      /// </summary>
      /// <param name="xi">Точка, в которой вычисляется производная.</param>
      /// <param name="interp">Интерполированное значение функции в точке <paramref name="xi"/> (выходной параметр).</param>
      /// <returns>Значение первой производной сплайна в точке <paramref name="xi"/>.</returns>
      double Derivative(double xi, out double interp);
   }
}