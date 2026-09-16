using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

public sealed class ShellLayeredSlsBatchResultVMTests
{
   const string SampleJson = """
      {"all_ok":false,"ok_count":1,"total":2,"rows":[
        {"num":1,"label":"т.1","status":"ok","acrc_mm":0.15,"crack_angle_deg":90.0,"direction":"x","face":"низ"},
        {"num":2,"label":"т.2","status":"not_passed","acrc_mm":0.45,"crack_angle_deg":135.0,"direction":"y","face":"верх"}
      ]}
      """;

   [Fact]
   public void Rows_ParsedFromDataJson()
   {
      var vm = new ShellLayeredSlsBatchResultVM(SampleJson);

      Assert.Equal(2, vm.Rows.Count);
      Assert.Equal("т.1", vm.Rows[0].Label);
      Assert.Contains("0.15", vm.Rows[0].AcrcText);
      Assert.Contains("90", vm.Rows[0].AngleText);
      Assert.Equal(Loc.S("ShellLayeredSlsBatchStatusNotPassed"), vm.Rows[1].StatusText);
   }
}
