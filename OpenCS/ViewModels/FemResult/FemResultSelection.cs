namespace OpenCS.ViewModels.FemResult;

/// <summary>Режим выбора в показанном результате: конструктивные элементы целиком или отдельные КЭ сетки.</summary>
public enum FemResultPickMode { Members, Elements }

/// <summary>
/// Выбранное в показанном результате расчёта: КЭ сетки результата или узел. В режиме КонЭ щелчок по КЭ выбирает
/// все КЭ его конструктивного элемента (КЭ без КонЭ — сам по себе).
/// </summary>
public sealed class FemResultSelection
{
    readonly FemResultMesh _mesh;
    readonly HashSet<string> _elements = new(StringComparer.Ordinal);

    public FemResultSelection(FemResultMesh mesh) => _mesh = mesh;

    /// <summary>Выбор изменился.</summary>
    public event Action? Changed;

    /// <summary>Выбранные КЭ сетки результата (номера).</summary>
    public IReadOnlyCollection<string> Elements => _elements;

    /// <summary>Выбранный узел; null — выбраны КЭ или ничего.</summary>
    public int? Node { get; private set; }

    /// <summary>Режим, в котором сделан выбор (карточка КонЭ — только в режиме КонЭ).</summary>
    public FemResultPickMode Mode { get; private set; } = FemResultPickMode.Members;

    public bool IsEmpty => _elements.Count == 0 && Node == null;

    public bool Contains(string elemTag) => _elements.Contains(elemTag);

    /// <summary>Конструктивные элементы выбранных КЭ (по порядку тегов).</summary>
    public IReadOnlyList<string> Members => _elements.Select(_mesh.OwnerOf).OfType<string>()
        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

    /// <summary>КЭ, выбираемые щелчком по КЭ в режиме: в режиме КонЭ — все КЭ его КонЭ.</summary>
    public IReadOnlyList<string> Expand(string elemTag, FemResultPickMode mode) =>
        mode == FemResultPickMode.Members && _mesh.OwnerOf(elemTag) is { } owner
            ? _mesh.ElementsOf(owner).Where(t => t == elemTag || IsVisible?.Invoke(t) != false).ToList()
            : [elemTag];

    /// <summary>КЭ видим во фрагменте вида (скрытые КЭ КонЭ не выбираются); null — видимы все.</summary>
    public Func<string, bool>? IsVisible { get; set; }

    /// <summary>Щелчок по КЭ: без Ctrl — выбрать только его (в режиме КонЭ — его КонЭ); с Ctrl — добавить или убрать.</summary>
    public void PickElement(string elemTag, FemResultPickMode mode, bool additive)
    {
        var tags = Expand(elemTag, mode);
        if (Node != null || mode != Mode) { _elements.Clear(); Node = null; }
        Mode = mode;
        if (!additive) _elements.Clear();
        if (additive && tags.All(_elements.Contains)) _elements.ExceptWith(tags);
        else _elements.UnionWith(tags);
        Changed?.Invoke();
    }

    /// <summary>Щелчок по узлу: выбирается только он (повторный щелчок снимает выбор).</summary>
    public void PickNode(int node)
    {
        bool same = Node == node && _elements.Count == 0;
        _elements.Clear();
        Node = same ? null : node;
        Changed?.Invoke();
    }

    public void Clear()
    {
        if (IsEmpty) return;
        _elements.Clear();
        Node = null;
        Changed?.Invoke();
    }
}

/// <summary>Строка карточки значений: подпись и значение.</summary>
public sealed record FemResultCardRow(string Label, string Value);

/// <summary>Карточка значений выбранного в результате: заголовок и строки.</summary>
public sealed record FemResultCard(string Title, IReadOnlyList<FemResultCardRow> Rows);
