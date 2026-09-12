using System.Text.Json;
using CScore.Sp63.Normal;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Проверяет разбор результата нормального сечения и разделение вердикта.</summary>
public sealed class Sp63NormalResultVMTests
{
   [Fact]
   public void CalculatedPassed_ShowsStrengthVerdictAndDetails()
   {
      var vm = Create(new Sp63NormalResult
      {
         Status = Sp63NormalStatus.Calculated,
         StrengthPassed = true,
         Branch = "bending",
         StrengthDetails =
         [new CScore.CheckDetail
         {
            Formula = "(8.4)",
            Description = "Sp63Normal_BendingCheck",
            NormReference = "8.1.8",
            Applied = 10,
            Allowable = 20,
            Variables = new Dictionary<string, double> { ["xi"] = 0.4 }
         }]
      });

      Assert.Equal(Loc.S("Sp63NormalVerdictPassed"), vm.VerdictText);
      Assert.Equal(System.Windows.Visibility.Visible, vm.StrengthVisibility);
      Assert.Empty(vm.ApplicabilityRows);
      Assert.Single(vm.StrengthRows);
      Assert.Equal(0.5.ToString("G6", System.Globalization.CultureInfo.CurrentCulture),
         vm.StrengthRows[0].RatioText);
   }

   [Theory]
   [InlineData(Sp63NormalStatus.NotApplicable, "Sp63NormalVerdictUnavailable")]
   [InlineData(Sp63NormalStatus.InvalidInput, "Sp63NormalVerdictInvalidInput")]
   public void NonCalculatedResult_DoesNotShowStrengthFailure(
      Sp63NormalStatus status, string verdictKey)
   {
      var vm = Create(new Sp63NormalResult
      {
         Status = status,
         StrengthPassed = null,
         ApplicabilityMessages =
         [new Sp63NormalMessage("reason", Sp63NormalMessageKind.Applicability,
            "8.1", "missing_localization_key")]
      });

      Assert.Equal(Loc.S(verdictKey), vm.VerdictText);
      Assert.Equal(System.Windows.Visibility.Collapsed, vm.StrengthVisibility);
      Assert.Equal(System.Windows.Visibility.Visible, vm.ApplicabilityVisibility);
      Assert.Single(vm.ApplicabilityRows);
      Assert.Equal("missing_localization_key", vm.ApplicabilityRows[0].Text);
   }

   static Sp63NormalResultVM Create(Sp63NormalResult model) =>
      new(JsonSerializer.Serialize(model));
}
