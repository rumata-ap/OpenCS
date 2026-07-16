namespace CScore.Import
{
   internal static class LiraForceMapper
   {
      public static LoadItem MapBar(IReadOnlyDictionary<string, double> src, LiraUnitScales units, LiraImportOptions opt)
      {
         double f = units.Force;
         double m = units.Moment;
         double SignMx = opt.InvertBarBendingMoments ? -1 : 1;
         double SignMy = opt.InvertBarBendingMoments ? -1 : 1;

         return new LoadItem
         {
            N  = Get(src, "N") * f,
            T  = Get(src, "MX") * m,
            My = Get(src, "MY") * m * SignMy,
            Mx = Get(src, "MZ") * m * SignMx,
            Vx = Get(src, "QZ") * f,
            Vy = Get(src, "QY") * f,
         };
      }

      public static ShellLoadItem MapShell(IReadOnlyDictionary<string, double> src, LiraUnitScales units, LiraImportOptions opt)
      {
         double sf = units.ShellForce;
         double sm = units.ShellMoment;
         double sign = opt.InvertShellBendingMoments ? -1.0 : 1.0;
         return new ShellLoadItem
         {
            Nx  = Get(src, "NX") * sf,
            Ny  = Get(src, "NY") * sf,
            Nxy = Get(src, "TXY") * sf,
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
