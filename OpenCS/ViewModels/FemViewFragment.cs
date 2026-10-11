using System.Globalization;
using CScore.Fem;

namespace OpenCS.ViewModels;

/// <summary>
/// Фрагмент 3D-вида схемы: какие КонЭ и КЭ сетки скрыты. Единица скрытия — атом: КЭ сетки (<c>e:номер</c>) или
/// КонЭ без КЭ сетки (<c>m:тег</c>); атомы КонЭ — его КЭ сетки (по <see cref="FemElement.SourceMemberTag"/>), если он
/// дискретизирован. КонЭ видим, если видим хотя бы один его атом. Хранятся скрытые атомы — элементы, появившиеся после
/// фрагментации, видимы.
/// </summary>
public sealed class FemViewFragment
{
    readonly HashSet<string> _hidden = new(StringComparer.Ordinal);
    IReadOnlyList<string> _members = [];
    Func<IEnumerable<FemElement>>? _meshSource;
    Dictionary<string, List<string>>? _meshByMember;
    HashSet<string>? _meshAtoms;

    /// <summary>Фрагмент изменился — вид нужно перестроить.</summary>
    public event EventHandler? Changed;

    /// <summary>Что-то скрыто.</summary>
    public bool IsActive => _hidden.Count > 0;

    /// <summary>КонЭ вида (теги).</summary>
    public void SetMembers(IEnumerable<string> memberTags) => _members = memberTags.Select(t => t.Trim()).ToList();

    /// <summary>Источник КЭ сетки схемы (читается при первой операции); вызов сбрасывает прочитанное.</summary>
    public void SetMeshSource(Func<IEnumerable<FemElement>> source)
    {
        _meshSource = source;
        _meshByMember = null;
        _meshAtoms = null;
    }

    public static string MemberAtom(string tag) => "m:" + tag.Trim();

    public static string MeshAtom(string tag) => "e:" + CanonicalTag(tag);

    /// <summary>Номер КЭ без пробелов и ведущих нулей; нечисловой тег — как есть.</summary>
    public static string CanonicalTag(string tag)
    {
        var t = tag.Trim();
        return long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n)
            ? n.ToString(CultureInfo.InvariantCulture) : t;
    }

    void EnsureMesh()
    {
        if (_meshByMember != null) return;
        _meshByMember = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        _meshAtoms = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in _meshSource?.Invoke() ?? [])
        {
            string atom = MeshAtom(e.ElemTag);
            _meshAtoms.Add(atom);
            if (e.SourceMemberTag is not { } owner || e.Origin == FemMember.MeshSourceImported) continue;
            if (!_meshByMember.TryGetValue(owner.Trim(), out var list)) _meshByMember[owner.Trim()] = list = [];
            list.Add(atom);
        }
    }

    /// <summary>Атомы КонЭ: его КЭ сетки или он сам.</summary>
    public IReadOnlyList<string> AtomsOfMember(string tag)
    {
        EnsureMesh();
        return _meshByMember!.TryGetValue(tag.Trim(), out var atoms) ? atoms : [MemberAtom(tag)];
    }

    public IEnumerable<string> AtomsOfMembers(IEnumerable<string> tags) => tags.SelectMany(AtomsOfMember);

    public static IEnumerable<string> AtomsOfMesh(IEnumerable<string> tags) => tags.Select(MeshAtom);

    /// <summary>Атомы группы: у группы КЭ — номера КЭ, у группы КонЭ — атомы её КонЭ.</summary>
    public IEnumerable<string> AtomsOfGroup(FemMemberGroup group) =>
        group.IsMeshGroup ? AtomsOfMesh(group.Tags) : AtomsOfMembers(group.Tags);

    /// <summary>Все атомы вида: КЭ сетки схемы и КонЭ вида.</summary>
    HashSet<string> Universe()
    {
        EnsureMesh();
        var all = new HashSet<string>(_meshAtoms!, StringComparer.Ordinal);
        foreach (var m in _members) all.UnionWith(AtomsOfMember(m));
        return all;
    }

    public bool IsMemberVisible(string tag) => _hidden.Count == 0 || AtomsOfMember(tag).Any(a => !_hidden.Contains(a));

    public bool IsMeshVisible(string tag) => _hidden.Count == 0 || !_hidden.Contains(MeshAtom(tag));

    /// <summary>Скрыть всё, кроме атомов; false — среди них нет атомов вида.</summary>
    public bool ShowOnly(IEnumerable<string> atoms)
    {
        var universe = Universe();
        var keep = atoms.Where(universe.Contains).ToHashSet(StringComparer.Ordinal);
        if (keep.Count == 0) return false;
        universe.ExceptWith(keep);
        return Replace(universe);
    }

    /// <summary>Скрыть атомы; false — нечего скрывать или скрылось бы всё.</summary>
    public bool Hide(IEnumerable<string> atoms)
    {
        var universe = Universe();
        var hidden = new HashSet<string>(_hidden, StringComparer.Ordinal);
        hidden.UnionWith(atoms.Where(universe.Contains));
        if (universe.IsSubsetOf(hidden)) return false;
        return Replace(hidden);
    }

    /// <summary>Показать атомы вдобавок к видимым.</summary>
    public bool Show(IEnumerable<string> atoms)
    {
        var hidden = new HashSet<string>(_hidden, StringComparer.Ordinal);
        hidden.ExceptWith(atoms);
        return Replace(hidden);
    }

    /// <summary>Видимое скрыть, скрытое показать; false — ничего не скрыто.</summary>
    public bool Invert()
    {
        if (_hidden.Count == 0) return false;
        var universe = Universe();
        universe.ExceptWith(_hidden);
        if (universe.Count == 0) return false;
        return Replace(universe);
    }

    public bool ShowAll() => Replace([]);

    bool Replace(HashSet<string> hidden)
    {
        if (hidden.SetEquals(_hidden)) return false;
        _hidden.Clear();
        _hidden.UnionWith(hidden);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }
}
