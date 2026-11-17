using OpenCS.Views;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Поиск вида задачи в окне свойств задачи.</summary>
public sealed class CalcTaskKindFilterTests
{
    [Fact]
    public void KindFilter_WordsInAnyOrder_InLabelOrGroup()
    {
        var k = new CalcTaskKindItem { Id = "punching_batch", Label = "Продавливание плиты по набору усилий (СП 63)", Group = "1-я ГПС" };
        Assert.True(CalcTaskPropsDlgVM.KindMatches(k, ""));
        Assert.True(CalcTaskPropsDlgVM.KindMatches(k, "продав"));
        Assert.True(CalcTaskPropsDlgVM.KindMatches(k, "набор ПРОДАВ"));
        Assert.True(CalcTaskPropsDlgVM.KindMatches(k, "гпс плиты"));
        Assert.False(CalcTaskPropsDlgVM.KindMatches(k, "продав трещ"));
    }
}
