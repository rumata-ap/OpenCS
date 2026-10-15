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
    /// <summary>Грани пластинчатых КонЭ — выбираются в режиме «Грани пластин» 3D-вида и из дерева (линейные связи).</summary>
    public ObservableCollection<CScore.Fem.FemPlateEdgeKey> SelectedPlateEdges { get; } = [];

    public event EventHandler? Changed;

    public void ToggleNode(string tag, bool additive)
    {
        if (!additive) { SelectedElemTags.Clear(); SelectedMeshElemTags.Clear(); SelectedMeshNodeTags.Clear(); SelectedPlateEdges.Clear(); if (!SelectedNodeTags.Contains(tag)) SelectedNodeTags.Clear(); }
        if (!SelectedNodeTags.Remove(tag)) SelectedNodeTags.Add(tag);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleElement(string tag, bool additive)
    {
        if (!additive) { SelectedNodeTags.Clear(); SelectedMeshElemTags.Clear(); SelectedMeshNodeTags.Clear(); SelectedPlateEdges.Clear(); if (!SelectedElemTags.Contains(tag)) SelectedElemTags.Clear(); }
        if (!SelectedElemTags.Remove(tag)) SelectedElemTags.Add(tag);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>КЭ сетки: щелчок — только он, Ctrl+щелчок — добавить/убрать.</summary>
    public void ToggleMeshElement(string tag, bool additive)
    {
        if (!additive) { SelectedNodeTags.Clear(); SelectedElemTags.Clear(); SelectedMeshNodeTags.Clear(); SelectedPlateEdges.Clear(); if (!SelectedMeshElemTags.Contains(tag)) SelectedMeshElemTags.Clear(); }
        if (!SelectedMeshElemTags.Remove(tag)) SelectedMeshElemTags.Add(tag);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        SelectedNodeTags.Clear();
        SelectedElemTags.Clear();
        SelectedMeshElemTags.Clear();
        SelectedMeshNodeTags.Clear();
        SelectedPlateEdges.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Грань пластины: добавить или снять (выбор граней всегда накопительный); остальной выбор снимается.</summary>
    public void TogglePlateEdge(CScore.Fem.FemPlateEdgeKey key)
    {
        SelectedNodeTags.Clear(); SelectedElemTags.Clear(); SelectedMeshElemTags.Clear(); SelectedMeshNodeTags.Clear();
        if (!SelectedPlateEdges.Remove(key)) SelectedPlateEdges.Add(key);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Добавляет грани (все грани пластины по Shift+щелчку) или, если все они уже выбраны, снимает их.</summary>
    public void TogglePlateEdges(IReadOnlyCollection<CScore.Fem.FemPlateEdgeKey> keys)
    {
        SelectedNodeTags.Clear(); SelectedElemTags.Clear(); SelectedMeshElemTags.Clear(); SelectedMeshNodeTags.Clear();
        if (keys.All(SelectedPlateEdges.Contains))
            foreach (var key in keys) SelectedPlateEdges.Remove(key);
        else
            foreach (var key in keys.Where(k => !SelectedPlateEdges.Contains(k))) SelectedPlateEdges.Add(key);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Заменяет выбор заданными объектами (из дерева схемы) одним уведомлением.</summary>
    public void Set(IEnumerable<string> nodeTags, IEnumerable<string> elemTags, IEnumerable<string> meshElemTags,
        IEnumerable<string> meshNodeTags, IEnumerable<CScore.Fem.FemPlateEdgeKey>? plateEdges = null)
    {
        Reset(SelectedNodeTags, nodeTags);
        Reset(SelectedElemTags, elemTags);
        Reset(SelectedMeshElemTags, meshElemTags);
        Reset(SelectedMeshNodeTags, meshNodeTags);
        SelectedPlateEdges.Clear();
        foreach (var key in (plateEdges ?? []).Distinct()) SelectedPlateEdges.Add(key);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    static void Reset(ObservableCollection<string> target, IEnumerable<string> tags)
    {
        target.Clear();
        foreach (var tag in tags.Distinct(StringComparer.Ordinal)) target.Add(tag);
    }
}
