using CScore;
using CScore.Fem;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

public sealed class ShellLayeredSlsResultVMTests
{
   static string MakeDataJson(bool passed)
   {
      var data = new OpenCS.Tasks.ShellLayeredSlsResultData
      {
         Strips =
         [
            new ShellCrackStripResult
            {
               LayerIndex = 0, LayerName = "верх", Direction = "x", Z = 0.0875,
               IsTop = true, MDes = 50, NDes = 0, Mcrc = 8.5, Cracked = true,
               SigmaS = 236_400, SigmaSCrc = 40_000, PsiS = 0.86, Phi1 = 1.0, Phi2 = 0.5, Phi3 = 1.0,
               LsM = 0.15, AcrcMm = passed ? 0.15 : 0.45, CrackAngleDeg = 90.0,
            },
            new ShellCrackStripResult
            {
               LayerIndex = 0, LayerName = "верх", Direction = "y", Z = 0.0875,
               IsTop = true, MDes = 10, NDes = 0, Mcrc = 8.5, Cracked = false,
               AcrcMm = 0, CrackAngleDeg = 0.0,
            },
         ],
         GoverningIndex = 0, AcrcMaxMm = passed ? 0.15 : 0.45, AcrcLimMm = 0.3,
         Utilization = (passed ? 0.15 : 0.45) / 0.3, Passed = passed, Converged = true,
      };
      return System.Text.Json.JsonSerializer.Serialize(data);
   }

   [Fact]
   public void Rows_IncludeBothStrips_EvenUncracked()
   {
      var vm = new ShellLayeredSlsResultVM(MakeDataJson(true), "Задача 1", "П-1", "2026-09-16");

      Assert.Equal(2, vm.Rows.Count);
      Assert.Contains(vm.Rows, r => r.DirectionText == "x" && r.AngleText.Contains("90")
         && r.SigmaSCrcText.Contains("40"));
      Assert.Contains(vm.Rows, r => r.DirectionText == "y" && !r.Cracked);
   }

   [Fact]
   public void VerdictText_ReflectsPassed()
   {
      var vmPassed = new ShellLayeredSlsResultVM(MakeDataJson(true), "Задача 1", "П-1", "2026-09-16");
      var vmFailed = new ShellLayeredSlsResultVM(MakeDataJson(false), "Задача 1", "П-1", "2026-09-16");

      Assert.Contains(Loc.S("ShellLayeredSlsFaceTop"), vmPassed.GoverningText, StringComparison.OrdinalIgnoreCase);
      Assert.Contains("x", vmPassed.GoverningText, StringComparison.OrdinalIgnoreCase);
      Assert.Equal("0.50", vmPassed.UtilizationText);
      Assert.NotEqual(vmPassed.VerdictText, vmFailed.VerdictText);
   }
}
