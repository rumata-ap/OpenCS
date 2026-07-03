namespace CScore
{
   /// <summary>
   /// Секущие и упругие жёсткости оболочечного сечения по толщине.
   /// Вычисляются в <see cref="PlateSection.ComputeSecant"/> по сходившемуся НДС.
   /// Единицы: EA — кН/м, EI — кН·м, Zc — мм.
   /// </summary>
   public class ShellSecantStiffness
   {
      // ── Секущие ─────────────────────────────────────────────────────────────
      /// <summary>Секущая мембранная жёсткость ∑E_sec·dz, кН/м.</summary>
      public double EAx { get; set; }
      /// <summary>Секущая мембранная жёсткость ∑E_sec·dz, кН/м.</summary>
      public double EAy { get; set; }
      /// <summary>z-координата секущего ц.т. от срединной плоскости, мм.</summary>
      public double ZcxMm { get; set; }
      /// <summary>z-координата секущего ц.т. от срединной плоскости, мм.</summary>
      public double ZcyMm { get; set; }
      /// <summary>Секущая изгибная жёсткость от ц.т., кН·м.</summary>
      public double EIxc { get; set; }
      /// <summary>Секущая изгибная жёсткость от ц.т., кН·м.</summary>
      public double EIyc { get; set; }

      // ── Упругие ──────────────────────────────────────────────────────────────
      /// <summary>Упругая (начальная) мембранная жёсткость, кН/м.</summary>
      public double EAxEl { get; set; }
      /// <summary>Упругая мембранная жёсткость, кН/м.</summary>
      public double EAyEl { get; set; }
      /// <summary>z-координата упругого ц.т., мм.</summary>
      public double ZcxElMm { get; set; }
      /// <summary>z-координата упругого ц.т., мм.</summary>
      public double ZcyElMm { get; set; }
      /// <summary>Упругая изгибная жёсткость от ц.т., кН·м.</summary>
      public double EIxcEl { get; set; }
      /// <summary>Упругая изгибная жёсткость от ц.т., кН·м.</summary>
      public double EIycEl { get; set; }

      // ── Коэффициенты снижения (секущ./упруг.) ────────────────────────────────
      /// <summary>Коэффициент снижения мембранной жёсткости x. −1 если не вычислен.</summary>
      public double PhiEAx { get; set; }
      /// <summary>Коэффициент снижения мембранной жёсткости y. −1 если не вычислен.</summary>
      public double PhiEAy { get; set; }
      /// <summary>Коэффициент снижения изгибной жёсткости x. −1 если не вычислен.</summary>
      public double PhiEIxc { get; set; }
      /// <summary>Коэффициент снижения изгибной жёсткости y. −1 если не вычислен.</summary>
      public double PhiEIyc { get; set; }
   }
}
