using System.Collections.ObjectModel;

namespace OpenCS.ViewModels;

/// <summary>Общий выбор узлов/элементов схемы, синхронизируемый между 3D-видом и гридами.
/// КЭ импортированной сетки выбираются отдельно от конструктивных элементов (у них свои номера).</summary>
public sealed class FemSchemaSelectionVM
{
    public ObservableCollection<string> SelectedNodeTags { get; } = [];
    public ObservableCollection<string> SelectedElemTags { get; } = [];
    public ObservableCollection<string> SelectedMeshElemTags { get; } = [];
    /// <summary>Узлы сетки — выбираются только из дерева (ГУ сеточного уровня), в 3D щелчком не выбираются.</summary>
    public ObservableCollection<string> SelectedMeshNodeTags { get; } = [];

    public event EventHandler? Changed;

    public void ToggleNode(string tag, bool additive)
    {
        if (!additive) { SelectedElemTags.Clear(); SelectedMeshElemTags.Clear(); SelectedMeshNodeTags.Clear(); if (!SelectedNodeTags.Contains(tag)) SelectedNodeTags.Clear(); }
        if (!SelectedNodeTags.Remove(tag)) SelectedNodeTags.Add(tag);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleElement(string tag, bool additive)
    {
        if (!additive) { SelectedNodeTags.Clear(); SelectedMeshElemTags.Clear(); SelectedMeshNodeTags.Clear(); if (!SelectedElemTags.Contains(tag)) SelectedElemTags.Clear(); }
        if (!SelectedElemTags.Remove(tag)) SelectedElemTags.Add(tag);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>КЭ сетки: щелчок — только он, Ctrl+щелчок — добавить/убрать.</summary>
    public void ToggleMeshElement(string tag, bool additive)
    {
        if (!additive) { SelectedNodeTags.Clear(); SelectedElemTags.Clear(); SelectedMeshNodeTags.Clear(); if (!SelectedMeshElemTags.Contains(tag)) SelectedMeshElemTags.Clear(); }
        if (!SelectedMeshElemTags.Remove(tag)) SelectedMeshElemTags.Add(tag);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        SelectedNodeTags.Clear();
        SelectedElemTags.Clear();
        SelectedMeshElemTags.Clear();
        SelectedMeshNodeTags.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Заменяет выбор заданными объектами (из дерева схемы) одним уведомлением.</summary>
    public void Set(IEnumerable<string> nodeTags, IEnumerable<string> elemTags, IEnumerable<string> meshElemTags,
        IEnumerable<string> meshNodeTags)
    {
        Reset(SelectedNodeTags, nodeTags);
        Reset(SelectedElemTags, elemTags);
        Reset(SelectedMeshElemTags, meshElemTags);
        Reset(SelectedMeshNodeTags, meshNodeTags);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    static void Reset(ObservableCollection<string> target, IEnumerable<string> tags)
    {
        target.Clear();
        foreach (var tag in tags.Distinct(StringComparer.Ordinal)) target.Add(tag);
    }
}
