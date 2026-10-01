namespace CScore.Import
{
   internal static class LiraForceMapper
   {
      /// <summary>
      /// Усилия стержня ЛИРЫ → строка набора OpenCS. Оси сечения OpenCS: x — вдоль местной оси Y1
      /// (ширина), y — вдоль Z1 (высота), поэтому <c>Mx ← My</c> ЛИРЫ (изгиб в плоскости X1Z1),
      /// <c>My ← Mz</c>, <c>Vy ← Qz</c>, <c>Vx ← Qy</c>. Положительный My ЛИРЫ растягивает нижнее волокно,
      /// положительный Mx OpenCS — верхнее, отсюда инверсия (<see cref="LiraImportOptions.InvertBarBendingMoments"/>).
      /// Сверено 30.09.2026 с подбором арматуры ЛИРЫ на схеме 1-lin (438 сечений балок).
      /// </summary>
      public static LoadItem MapBar(IReadOnlyDictionary<string, double> src, LiraUnitScales units, LiraImportOptions opt)
      {
         double f = units.Force;
         double m = units.Moment;
         double sign = opt.InvertBarBendingMoments ? -1 : 1;

         return new LoadItem
         {
            N  = Get(src, "N") * f,
            T  = Get(src, "MX") * m,
            Mx = Get(src, "MY") * m * sign,
            My = Get(src, "MZ") * m * sign,
            Vy = Get(src, "QZ") * f,
            Vx = Get(src, "QY") * f,
         };
      }

      public static ShellLoadItem MapShell(IReadOnlyDictionary<string, double> src, LiraUnitScales units, LiraImportOptions opt)
      {
         double sf = units.ShellForce;
         double sm = units.ShellMoment;
         double st = units.Stress;
         double sign = opt.InvertShellBendingMoments ? -1.0 : 1.0;
         return new ShellLoadItem
         {
            SigmaX = Get(src, "NX") * st,
            SigmaY = Get(src, "NY") * st,
            TauXY  = Get(src, "TXY") * st,
            Mx  = Get(src, "MX") * sm * sign,
            My  = Get(src, "MY") * sm * sign,
            Mxy = Get(src, "MXY") * sm * sign,
            Qx  = Get(src, "QX") * sf,
            Qy  = Get(src, "QY") * sf,
         };
      }

      static double Get(IReadOnlyDictionary<string, double> src, string key)
         => src.TryGetValue(key, out var v) ? v : 0.0;
   }
}
