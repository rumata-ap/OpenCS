using CScore.Fem;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Фрагмент 3D-вида схемы: атомы КонЭ и КЭ сетки, операции скрытия и изоляции.</summary>
public class FemViewFragmentTests
{
    static FemElement E(string tag, string? member, string origin = FemMember.MeshSourceGenerated) =>
        new() { ElemTag = tag, SourceMemberTag = member, Origin = origin };

    /// <summary>Балка Б1 — КЭ 1, 2; плита П1 — КЭ 3, 4; колонна К1 не дискретизирована.</summary>
    FemViewFragment Fragment()
    {
        var fragment = new FemViewFragment();
        fragment.SetMembers(["Б1", "П1", "К1"]);
        fragment.SetMeshSource(() => [E("1", "Б1"), E("2", "Б1"), E("3", "П1"), E("004", "П1")]);
        fragment.Changed += (_, _) => _changed++;
        return fragment;
    }

    int _changed;

    [Fact]
    public void AtomsOfMember_MeshElementsOrMemberItself()
    {
        var fragment = Fragment();
        Assert.Equal(["e:1", "e:2"], fragment.AtomsOfMember("Б1"));
        Assert.Equal(["e:3", "e:4"], fragment.AtomsOfMember("П1"));   // номер КЭ без ведущих нулей
        Assert.Equal(["m:К1"], fragment.AtomsOfMember("К1"));
        Assert.False(fragment.IsActive);
        Assert.True(fragment.IsMemberVisible("Б1"));
    }

    [Fact]
    public void ShowOnlyMember_HidesOtherMembersAndTheirMeshElements()
    {
        var fragment = Fragment();
        Assert.True(fragment.ShowOnly(fragment.AtomsOfMember("Б1")));

        Assert.True(fragment.IsActive);
        Assert.Equal(1, _changed);
        Assert.True(fragment.IsMemberVisible("Б1"));
        Assert.False(fragment.IsMemberVisible("П1"));
        Assert.False(fragment.IsMemberVisible("К1"));
        Assert.True(fragment.IsMeshVisible("2"));
        Assert.False(fragment.IsMeshVisible("04"));
        // Элемент, появившийся после фрагментации, виден.
        Assert.True(fragment.IsMemberVisible("Новый"));
        Assert.True(fragment.IsMeshVisible("99"));
    }

    [Fact]
    public void HideMeshElements_HidesMemberOnlyWhenAllItsElementsHidden()
    {
        var fragment = Fragment();
        var group = new FemMemberGroup { Kind = FemMemberGroup.KindMesh };
        group.SetTags(["3"]);

        Assert.True(fragment.Hide(fragment.AtomsOfGroup(group)));
        Assert.True(fragment.IsMemberVisible("П1"));
        Assert.False(fragment.IsMeshVisible("3"));

        Assert.True(fragment.Hide(FemViewFragment.AtomsOfMesh(["4"])));
        Assert.False(fragment.IsMemberVisible("П1"));
        Assert.True(fragment.IsMemberVisible("Б1"));
    }

    [Fact]
    public void MembersGroup_InvertShowAndShowAll()
    {
        var fragment = Fragment();
        var group = new FemMemberGroup { Kind = FemMemberGroup.KindMembers };
        group.SetTags(["Б1", "К1"]);

        Assert.True(fragment.Hide(fragment.AtomsOfGroup(group)));
        Assert.False(fragment.IsMemberVisible("К1"));
        Assert.True(fragment.IsMemberVisible("П1"));

        Assert.True(fragment.Invert());
        Assert.True(fragment.IsMemberVisible("К1"));
        Assert.True(fragment.IsMemberVisible("Б1"));
        Assert.False(fragment.IsMemberVisible("П1"));

        Assert.True(fragment.Show(fragment.AtomsOfMember("П1")));
        Assert.True(fragment.IsMemberVisible("П1"));
        Assert.False(fragment.IsActive);   // всё показано

        Assert.True(fragment.Hide(fragment.AtomsOfMember("П1")));
        Assert.True(fragment.ShowAll());
        Assert.False(fragment.IsActive);
        Assert.False(fragment.ShowAll());   // уже всё показано — без события
        Assert.Equal(5, _changed);
    }

    [Fact]
    public void Operations_DoNotHideEverythingOrApplyEmptySets()
    {
        var fragment = Fragment();
        Assert.False(fragment.Hide(fragment.AtomsOfMembers(["Б1", "П1", "К1"])));
        Assert.False(fragment.ShowOnly(["e:999", "m:нет"]));
        Assert.False(fragment.Invert());   // ничего не скрыто
        Assert.False(fragment.IsActive);
        Assert.Equal(0, _changed);
    }

    [Fact]
    public void ImportedElements_AreNotAtomsOfMember()
    {
        // КЭ импортированной сетки под элементом из кБ ЛИРЫ принадлежат сетке, а не КонЭ.
        var fragment = new FemViewFragment();
        fragment.SetMembers(["кБ"]);
        fragment.SetMeshSource(() => [E("10", "кБ", FemMember.MeshSourceImported), E("11", null, FemMember.MeshSourceImported)]);

        Assert.Equal(["m:кБ"], fragment.AtomsOfMember("кБ"));
        Assert.True(fragment.ShowOnly(FemViewFragment.AtomsOfMesh(["10"])));
        Assert.False(fragment.IsMemberVisible("кБ"));
        Assert.False(fragment.IsMeshVisible("11"));
        Assert.True(fragment.IsMeshVisible("10"));
    }
}
