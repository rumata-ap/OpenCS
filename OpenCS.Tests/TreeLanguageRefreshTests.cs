using System.Collections.ObjectModel;
using CScore.Fem;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Подписи групп деревьев берутся из строк при показе и перечитываются при смене языка (вид → язык).</summary>
[Collection("Loc language")]
public sealed class TreeLanguageRefreshTests
{
    [Fact]
    public void GroupLabels_FollowLanguage_AndNotify()
    {
        try
        {
            Loc.SetLanguage(Loc.Russian);
            var checks = new FemChecksRootNode(new ObservableCollection<FemCheck>());
            var tasks = new CalcTasksSubGroupNode("uls", "CalcTaskGroupUls");
            Assert.Equal("1-я ГПС (прочность)", checks.Groups[0].Label);
            Assert.Equal(Loc.S("CalcTaskGroupUls"), tasks.Label);

            var changed = new List<string?>();
            checks.Groups[0].PropertyChanged += (_, e) => changed.Add(e.PropertyName);
            tasks.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            Loc.SetLanguage(Loc.English);
            checks.RefreshLanguage();
            tasks.RefreshLabel();
            Assert.Equal(["Label", "Label"], changed);
            Assert.Equal("1st LS Group (Strength)", checks.Groups[0].Label);
            Assert.Equal("1st LS Group (Strength)", tasks.Label);
            Assert.Equal(Loc.S("FemGroupOther"), checks.Groups[3].Label);
        }
        finally { Loc.SetLanguage(null); }
    }
}
