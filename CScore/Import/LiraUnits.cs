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
      public LiraUnitScales(double force, double moment, double shellForce, double shellMoment, double stress)
      {
         Force       = force;
         Moment      = moment;
         ShellForce  = shellForce;
         ShellMoment = shellMoment;
         Stress      = stress;
      }

      /// <summary>т → кN.</summary>
      public double Force { get; }

      /// <summary>т·м → кN·м.</summary>
      public double Moment { get; }

      /// <summary>т/м → кN/м.</summary>
      public double ShellForce { get; }

      /// <summary>(т·м)/м → кN·м/м.</summary>
      public double ShellMoment { get; }

      /// <summary>т/м² → кПа (кN/м²), масштаб для сырых импортированных напряжений σx/σy/τxy.</summary>
      public double Stress { get; }

      public static LiraUnitScales FromPreLines(IReadOnlyList<string> preLines, double tonFactor)
      {
         double force = tonFactor, moment = tonFactor, sForce = tonFactor, sMoment = tonFactor, stress = tonFactor;
         foreach (var raw in preLines)
         {
            var l = NormalizeHomoglyphs(raw.ToLowerInvariant());
            string? val = ValueAfterColon(l);
            if (val == null) continue;

            if (l.Contains("усилий:"))
               force = UnitTokens.ParseCompoundToKnBase(val, tonFactor) ?? force;
            else if (l.Contains("напряжений:"))
               stress = UnitTokens.ParseCompoundToKnBase(val, tonFactor) ?? stress;
            else if (l.Contains("моментов:") && !l.Contains("расп") && !l.Contains("бимомент"))
               moment = UnitTokens.ParseCompoundToKnBase(val, tonFactor) ?? moment;
            else if (l.Contains("расп") && l.Contains("момент"))
               sMoment = UnitTokens.ParseCompoundToKnBase(val, tonFactor) ?? sMoment;
            else if (l.Contains("расп") && l.Contains("сил"))
               sForce = UnitTokens.ParseCompoundToKnBase(val, tonFactor) ?? sForce;
         }
         return new LiraUnitScales(force, moment, sForce, sMoment, stress);
      }

      /// <summary>ЛИРА-экспорт кодирует часть кириллических «р» латинской «p» (подтверждено
      /// побайтово на реальных файлах) — нормализуем перед сравнением ключевых слов.</summary>
      static string NormalizeHomoglyphs(string s) => s.Replace('p', 'р');

      static string? ValueAfterColon(string line)
      {
         int idx = line.IndexOf(':');
         return idx >= 0 && idx + 1 < line.Length ? line[(idx + 1)..].Trim() : null;
      }
   }
}
