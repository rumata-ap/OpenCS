using CScore;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

public class ShellLayeredSlsInfrastructureTests
{
   [Theory]
   [InlineData("shell_layered_sls")]
   [InlineData("shell_layered_sls_batch")]
   public void NewTaskKindsAreRegisteredAndDoNotFailAsUnknown(string kind)
   {
      var task = new CalcTask { Kind = kind, Tag = "test" };

      Assert.True(CalcTaskForceHelper.UsesDummyForceItem(task));
      Assert.Contains(kind, TaskRunner.KindList);

      var result = TaskRunner.Run(task, null!, new LoadItem(), CalcSettings.Default);

      Assert.Equal("error", result.Status);
      Assert.DoesNotContain("Unknown task kind", result.DataJson, StringComparison.Ordinal);
   }
}
