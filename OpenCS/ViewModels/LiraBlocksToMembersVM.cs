using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using CScore.Fem;
using CScore.Import;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Строка кБ ЛИРЫ в диалоге выбора: состав (стержни/пластины) и признак «уже преобразован».</summary>
public sealed class LiraBlockRowVM : ViewModelBase
{
    bool _isSelected;

    public LiraBlockRowVM(LiraBlockInfo block, int barCount, int plateCount, bool isConverted)
    {
        Block = block;
        BarCount = barCount;
        PlateCount = plateCount;
        IsConverted = isConverted;
    }

    public LiraBlockInfo Block { get; }
    public int Id => Block.Id;
    public string Type => Block.Type;
    public string Floor => Block.Floor;
    public string Mark => Block.Mark;
    public int BarCount { get; }
    public int PlateCount { get; }
    public bool IsConverted { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected == value) return; _isSelected = value; OnPropertyChanged(); }
    }
}

/// <summary>
/// Диалог «Конструктивные элементы из кБ ЛИРЫ»: список кБ схемы с фильтрами по типу и этажу и выбором галочками.
/// Сам ничего не записывает — отдаёт выбранные кБ вызывающей стороне.
/// </summary>
public sealed class LiraBlocksToMembersVM : ViewModelBase
{
    string _typeFilter = "";
    string _floorFilter = "";

    public LiraBlocksToMembersVM(IReadOnlyList<LiraBlockInfo> blocks, IReadOnlyList<FemElement> meshElements,
        IReadOnlyList<FemMember> members)
    {
        var typeByTag = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var e in meshElements) typeByTag.TryAdd(e.ElemTag, e.ElemType);
        var memberTags = members.Select(m => m.ElemTag).ToHashSet(StringComparer.Ordinal);

        Rows = blocks.Select(b =>
        {
            int bars = b.ElementTags.Count(t => typeByTag.GetValueOrDefault(t) == "beam");
            int plates = b.ElementTags.Count(t => typeByTag.GetValueOrDefault(t) == "shell");
            bool converted = memberTags.Contains(b.Tag) || memberTags.Any(t => t.StartsWith(b.Tag + " · ", StringComparison.Ordinal));
            var row = new LiraBlockRowVM(b, bars, plates, converted);
            row.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(LiraBlockRowVM.IsSelected)) OnPropertyChanged(nameof(SelectedCount)); };
            return row;
        }).ToList();

        string all = Loc.S("LiraBlocksFilterAll");
        Types = [all, .. Rows.Select(r => r.Type).Distinct().Order()];
        Floors = [all, .. Rows.Select(r => r.Floor).Where(f => f.Length > 0).Distinct().Order()];
        _typeFilter = all;
        _floorFilter = all;

        View = CollectionViewSource.GetDefaultView(Rows);
        View.Filter = o => o is LiraBlockRowVM r
            && (_typeFilter == Types[0] || r.Type == _typeFilter)
            && (_floorFilter == Floors[0] || r.Floor == _floorFilter);

        SelectVisibleCommand = new RelayCommand(_ => SetVisible(true));
        ClearVisibleCommand = new RelayCommand(_ => SetVisible(false));
    }

    public List<LiraBlockRowVM> Rows { get; }
    public ICollectionView View { get; }
    public List<string> Types { get; }
    public List<string> Floors { get; }

    public string TypeFilter
    {
        get => _typeFilter;
        set { _typeFilter = value ?? Types[0]; OnPropertyChanged(); View.Refresh(); }
    }

    public string FloorFilter
    {
        get => _floorFilter;
        set { _floorFilter = value ?? Floors[0]; OnPropertyChanged(); View.Refresh(); }
    }

    public int SelectedCount => Rows.Count(r => r.IsSelected);

    public ICommand SelectVisibleCommand { get; }
    public ICommand ClearVisibleCommand { get; }

    public IReadOnlyList<LiraBlockInfo> SelectedBlocks => Rows.Where(r => r.IsSelected).Select(r => r.Block).ToList();

    void SetVisible(bool value)
    {
        foreach (var row in View.Cast<LiraBlockRowVM>()) row.IsSelected = value;
    }
}
