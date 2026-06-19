namespace CScore.Import
{
   internal enum LiraElementKind
   {
      Unknown,
      Bar,
      Shell,
   }

   internal readonly struct LiraUnitScales
   {
      public LiraUnitScales(double force, double moment, double shellForce, double shellMoment)
      {
         Force       = force;
         Moment      = moment;
         ShellForce  = shellForce;
         ShellMoment = shellMoment;
      }

      /// <summary>т → кN.</summary>
      public double Force { get; }

      /// <summary>т·м → кN·м.</summary>
      public double Moment { get; }

      /// <summary>т/м → кN/м.</summary>
      public double ShellForce { get; }

      /// <summary>(т·м)/м → кN·м/м.</summary>
      public double ShellMoment { get; }

      public static LiraUnitScales FromPreLines(IReadOnlyList<string> preLines, double tonFactor)
      {
         double force = tonFactor, moment = tonFactor, sForce = tonFactor, sMoment = tonFactor;
         foreach (var line in preLines)
         {
            var l = line.ToLowerInvariant();
            if (l.Contains("усилий:"))
               force = ScaleFromLine(l, tonFactor);
            else if (l.Contains("моментов:") && !l.Contains("расп") && !l.Contains("бимомент"))
               moment = ScaleFromLine(l, tonFactor);
            else if (l.Contains("расп") && l.Contains("момент"))
               sMoment = ScaleFromLine(l, tonFactor);
            else if (l.Contains("расп") && l.Contains("сил"))
               sForce = ScaleFromLine(l, tonFactor);
         }
         return new LiraUnitScales(force, moment, sForce, sMoment);
      }

      static double ScaleFromLine(string lowerLine, double tonFactor)
      {
         if (lowerLine.Contains("кн") || lowerLine.Contains("kn"))
            return 1.0;
         if (lowerLine.Contains(": т") || lowerLine.EndsWith(" т"))
            return tonFactor;
         return tonFactor;
      }
   }
}
