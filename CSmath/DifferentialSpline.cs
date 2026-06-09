namespace CSmath
{
   /// <summary>
   /// Разностный сплайн: f(x) = a.Interpolate(x) − b.Interpolate(x).
   /// Используется для дифференциальных диаграмм арматурной области (σ_сталь − σ_бетон).
   /// </summary>
   public class DifferentialSpline : ISpline
   {
      readonly ISpline _a;
      readonly ISpline _b;

      public DifferentialSpline(ISpline a, ISpline b) { _a = a; _b = b; }

      public double[] X  { get => _a.X;  set { } }
      public double[] Y  { get => _a.Y;  set { } }
      public double[] DY { get => _a.DY; set { } }
      public double[] A  { get => _a.A;  set { } }
      public double[] B  { get => _a.B;  set { } }
      public double[] C  { get => _a.C;  set { } }
      public double[] D  { get => _a.D;  set { } }

      public double Interpolate(double xi) =>
         _a.Interpolate(xi) - _b.Interpolate(xi);

      public double Derivative(double xi, out double interp)
      {
         double da = _a.Derivative(xi, out double va);
         double db = _b.Derivative(xi, out double vb);
         interp = va - vb;
         return da - db;
      }
   }
}
