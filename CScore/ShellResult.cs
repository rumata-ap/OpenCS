namespace CScore
{
   /// <summary>
   /// Результирующие усилия оболочечного сечения на 1 м ширины.
   /// Единицы: N* — кН/м, M* — кН·м/м, EA* — кН/м, EI* — кН·м.
   /// </summary>
   public class ShellResult
   {
      // ── Суммарные ────────────────────────────────────────────────────────────
      /// <summary>Мембранная сила Nx, кН/м.</summary>
      public double Nx  { get; set; }
      /// <summary>Мембранная сила Ny, кН/м.</summary>
      public double Ny  { get; set; }
      /// <summary>Мембранная сдвиговая сила Nxy, кН/м.</summary>
      public double Nxy { get; set; }
      /// <summary>Изгибающий момент Mx, кН·м/м.</summary>
      public double Mx  { get; set; }
      /// <summary>Изгибающий момент My, кН·м/м.</summary>
      public double My  { get; set; }
      /// <summary>Крутящий момент Mxy, кН·м/м.</summary>
      public double Mxy { get; set; }

      // ── Детализация: бетон ────────────────────────────────────────────────────
      public double NxConcrete  { get; set; }
      public double NyConcrete  { get; set; }
      public double NxyConcrete { get; set; }
      public double MxConcrete  { get; set; }
      public double MyConcrete  { get; set; }
      public double MxyConcrete { get; set; }

      // ── Детализация: арматура ─────────────────────────────────────────────────
      public double NxRebar { get; set; }
      public double NyRebar { get; set; }
      public double MxRebar { get; set; }
      public double MyRebar { get; set; }

      // ── Жёсткость ─────────────────────────────────────────────────────────────
      /// <summary>z-координата упругого центра тяжести, м.</summary>
      public double Zc  { get; set; }
      /// <summary>Касательная мембранная жёсткость ∂Nx/∂ε₀x, кН/м.</summary>
      public double EAx { get; set; }
      /// <summary>Касательная мембранная жёсткость ∂Ny/∂ε₀y, кН/м.</summary>
      public double EAy { get; set; }
      /// <summary>Касательная изгибная жёсткость ∂Mx/∂κx, кН·м.</summary>
      public double EIx { get; set; }
      /// <summary>Касательная изгибная жёсткость ∂My/∂κy, кН·м.</summary>
      public double EIy { get; set; }

      public static ShellResult Zero => new();

      public double[] ToArray()
         => new[] { Nx, Ny, Nxy, Mx, My, Mxy };
   }
}
