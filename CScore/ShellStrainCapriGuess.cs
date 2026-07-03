using System;
using System.Linq;

namespace CScore;

/// <summary>
/// Грубая оценка 6-мерного НДС из результата Capri ULS для тёплого старта Ньютона.
/// </summary>
public static class ShellStrainCapriGuess
{
   /// <summary>
   /// Строит [ε₀x, ε₀y, γ₀xy, κx, κy, κxy] по критическому направлению Capri (max η).
   /// </summary>
   public static double[]? Build(
      double[] target,
      PlateSection section,
      Material concrete,
      Material rebar,
      CalcType calc,
      double stepDeg = 10.0)
   {
      if (target.Length != 6)
         return null;

      var p = new ShellSimplSolver.SolveParams(
         target[0], target[1], target[2],
         target[3], target[4], target[5],
         "shell_simpl_capri_uls", stepDeg);

      var capri = ShellSimplSolver.Solve(p, section, concrete, rebar, calc);
      var crit = capri.CapriDirs?
         .Where(d => !d.Strip.NoRebar && double.IsFinite(d.Strip.Eta))
         .OrderByDescending(d => d.Strip.Eta)
         .FirstOrDefault();
      if (crit is null)
         return null;

      if (!concrete.chars.TryGetValue(calc, out var conc) || conc.E <= 0)
         return null;

      double h = Math.Max(section.H, 1e-6);
      double Eb = conc.E;
      double EA = Eb * h;
      double EI = Eb * h * h * h / 12.0;
      double G = Eb / (2.0 * (1.0 + 0.2));

      double alpha = crit.Alpha_deg * Math.PI / 180.0;
      double c = Math.Cos(alpha), s = Math.Sin(alpha);

      double eps0_n = crit.N_n / EA;
      double kappa_n = crit.M_n / EI;

      // Масштаб к уровню спроса по η критического направления (Capri уже «видит» нелинейность).
      double eta = crit.Strip.Eta;
      if (eta > 0.05 && eta < 0.99)
      {
         double sc = 1.0 / eta;
         eps0_n *= sc;
         kappa_n *= sc;
      }

      double eps0x = eps0_n * c * c;
      double eps0y = eps0_n * s * s;
      double gamma0xy = eps0_n * 2.0 * c * s + target[2] / (G * h);

      double kx = kappa_n * c * c;
      double ky = kappa_n * s * s;
      double kxy = kappa_n * 2.0 * c * s;

      return new[] { eps0x, eps0y, gamma0xy, kx, ky, kxy };
   }
}
