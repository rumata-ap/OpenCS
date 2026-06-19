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
            Vy = Get(src, "QZ") * f,
            Vx = Get(src, "QY") * f,
         };
      }

      public static ShellLoadItem MapShell(IReadOnlyDictionary<string, double> src, LiraUnitScales units)
      {
         double sf = units.ShellForce;
         double sm = units.ShellMoment;
         return new ShellLoadItem
         {
            Nx  = Get(src, "NX") * sf,
            Ny  = Get(src, "NY") * sf,
            Nxy = Get(src, "TXY") * sf,
            Mx  = Get(src, "MX") * sm,
            My  = Get(src, "MY") * sm,
            Mxy = Get(src, "MXY") * sm,
            Qx  = Get(src, "QX") * sf,
            Qy  = Get(src, "QY") * sf,
         };
      }

      static double Get(IReadOnlyDictionary<string, double> src, string key)
         => src.TryGetValue(key, out var v) ? v : 0.0;
   }
}
