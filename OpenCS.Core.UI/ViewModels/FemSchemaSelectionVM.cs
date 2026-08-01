using System.Collections.ObjectModel;

namespace OpenCS.ViewModels;

/// <summary>Общий выбор узлов/элементов схемы, синхронизируемый между 3D-видом и гридами.</summary>
public sealed class FemSchemaSelectionVM
{
    public ObservableCollection<string> SelectedNodeTags { get; } = [];
    public ObservableCollection<string> SelectedElemTags { get; } = [];

    public event EventHandler? Changed;

    public void ToggleNode(string tag, bool additive)
    {
        if (!additive) { SelectedElemTags.Clear(); if (!SelectedNodeTags.Contains(tag)) SelectedNodeTags.Clear(); }
        if (!SelectedNodeTags.Remove(tag)) SelectedNodeTags.Add(tag);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleElement(string tag, bool additive)
    {
        if (!additive) { SelectedNodeTags.Clear(); if (!SelectedElemTags.Contains(tag)) SelectedElemTags.Clear(); }
        if (!SelectedElemTags.Remove(tag)) SelectedElemTags.Add(tag);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        SelectedNodeTags.Clear();
        SelectedElemTags.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
