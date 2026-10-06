using System.Collections.ObjectModel;

namespace OpenCS.ViewModels;

/// <summary>Общий выбор узлов/элементов схемы, синхронизируемый между 3D-видом и гридами.
/// КЭ импортированной сетки выбираются отдельно от конструктивных элементов (у них свои номера).</summary>
public sealed class FemSchemaSelectionVM
{
    public ObservableCollection<string> SelectedNodeTags { get; } = [];
    public ObservableCollection<string> SelectedElemTags { get; } = [];
    public ObservableCollection<string> SelectedMeshElemTags { get; } = [];

    public event EventHandler? Changed;

    public void ToggleNode(string tag, bool additive)
    {
        if (!additive) { SelectedElemTags.Clear(); SelectedMeshElemTags.Clear(); if (!SelectedNodeTags.Contains(tag)) SelectedNodeTags.Clear(); }
        if (!SelectedNodeTags.Remove(tag)) SelectedNodeTags.Add(tag);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleElement(string tag, bool additive)
    {
        if (!additive) { SelectedNodeTags.Clear(); SelectedMeshElemTags.Clear(); if (!SelectedElemTags.Contains(tag)) SelectedElemTags.Clear(); }
        if (!SelectedElemTags.Remove(tag)) SelectedElemTags.Add(tag);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>КЭ сетки: щелчок — только он, Ctrl+щелчок — добавить/убрать.</summary>
    public void ToggleMeshElement(string tag, bool additive)
    {
        if (!additive) { SelectedNodeTags.Clear(); SelectedElemTags.Clear(); if (!SelectedMeshElemTags.Contains(tag)) SelectedMeshElemTags.Clear(); }
        if (!SelectedMeshElemTags.Remove(tag)) SelectedMeshElemTags.Add(tag);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        SelectedNodeTags.Clear();
        SelectedElemTags.Clear();
        SelectedMeshElemTags.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
