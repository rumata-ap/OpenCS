using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using CScore.Fem;
using OpenCS.Utilites;

namespace OpenCS.Views;

/// <summary>
/// Команды групп в таблицах схемы: «Группа из выделенных…», «Добавить в группу ▾», «Убрать из группы ▾»
/// и колонка «Группы». Таблицы сетки работают с группами КЭ (номера КЭ), таблицы КонЭ — с группами КонЭ.
/// Состав правится только через <see cref="Services.FemGroupService"/>.
/// </summary>
internal sealed class FemGroupTableTools
{
    readonly AppViewModel _app;
    readonly FemSchema _schema;
    readonly DataGrid _grid;
    readonly string _kind;
    readonly Func<object, string> _tagOf;
    readonly GroupNamesConverter _names = new();

    /// <param name="kind">Вид групп таблицы: <see cref="FemMemberGroup.KindMesh"/> или <see cref="FemMemberGroup.KindMembers"/>.</param>
    /// <param name="tagOf">Тег строки таблицы (номер КЭ или тег КонЭ).</param>
    public FemGroupTableTools(AppViewModel app, FemSchema schema, DataGrid grid, string kind, Func<object, string> tagOf)
    {
        _app = app;
        _schema = schema;
        _grid = grid;
        _kind = kind;
        _tagOf = tagOf;
        RebuildIndex();
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = Loc.S("FemGroupsColumn"),
            Binding = new Binding { Converter = _names, ConverterParameter = _tagOf, Mode = BindingMode.OneWay },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            MinWidth = 120,
        });
    }

    bool IsMesh => _kind == FemMemberGroup.KindMesh;

    IEnumerable<FemMemberGroup> Groups => _schema.MemberGroups.Where(g => g.Kind == _kind);

    /// <summary>Теги выделенных строк в порядке таблицы.</summary>
    public List<string> SelectedTags()
    {
        var selected = _grid.SelectedItems.Cast<object>().ToHashSet();
        return _grid.Items.Cast<object>().Where(selected.Contains).Select(_tagOf).ToList();
    }

    /// <summary>«Группа из выделенных…»: диалог имени и типа (у таблиц сетки — и строки номеров).</summary>
    /// <param name="defaultType">Тип, предлагаемый в диалоге (по составу выделенного).</param>
    public void CreateGroup(string? defaultType)
    {
        var tags = SelectedTags();
        if (tags.Count == 0 && !IsMesh) return;

        // Группа из одного КонЭ — по его имени (так было у прежней команды таблиц).
        bool countedName = IsMesh || tags.Count != 1;
        string defaultTag = countedName ? FemGroupComposition.DefaultTag(_kind, tags.Count) : tags[0];
        string initialRange = IsMesh ? LiraElemRangeDialog.FormatRange(tags) : "";
        var dlg = new FemMemberDialog(initialRange, defaultTag, defaultType, showRange: IsMesh,
            title: Loc.S(IsMesh ? "FemMeshGroupDlgTitle" : "FemMembersGroupDlgTitle"));
        if (dlg.ShowDialog() != true) return;

        // Строку номеров не правили — берём выделенные как есть (без разбора, теги бывают нечисловыми).
        if (IsMesh && dlg.Range != initialRange)
            tags = LiraElemRangeDialog.ParseRange(dlg.Range).Select(id => id.ToString(CultureInfo.InvariantCulture)).ToList();
        if (tags.Count == 0) return;
        // Имя по умолчанию не трогали — пусть сервис посчитает его по фактическому составу.
        string? tag = countedName && dlg.MemberTag == defaultTag ? null : dlg.MemberTag;

        if (IsMesh) CreateMeshGroup(tags, tag, dlg.MemberType);
        else if (_app.FemGroups.CreateMembersGroup(_schema, tags, tag, dlg.MemberType) is { } group)
            Done(string.Format(Loc.S("FemGroupCreated"), group.Tag, group.Tags.Count));
    }

    /// <summary>Группа КЭ: КЭ дискретизации в неё не входят — если выбраны только они, предлагается
    /// группа КонЭ из их конструктивных элементов (без молчаливой подмены).</summary>
    void CreateMeshGroup(List<string> tags, string? tag, string? memberType)
    {
        var check = FemGroupComposition.CheckMeshTags(tags, _app.db.GetFemMeshElements(_schema.Id));
        if (check.Accepted.Count == 0 && check.GeneratedOwners.Count > 0)
        {
            string message = string.Format(Loc.S("FemMeshGroupOfferMembers"),
                check.Generated.Count, check.GeneratedOwners.Count);
            if (MessageBox.Show(message, Loc.S("FemGroupCreateTitle"), MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            if (_app.FemGroups.CreateMembersGroup(_schema, check.GeneratedOwners, tag, memberType) is { } membersGroup)
                Done(string.Format(Loc.S("FemGroupCreated"), membersGroup.Tag, membersGroup.Tags.Count));
            return;
        }
        if (_app.FemGroups.CreateMeshGroup(_schema, tags, tag, memberType) is { } group)
            Done(string.Format(Loc.S("FemGroupCreated"), group.Tag, group.Tags.Count));
        else
            MessageBox.Show(Loc.S("FemGroupMeshNoneAccepted"), Loc.S("FemGroupCreateTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>«КонЭ из выделенных…» (таблицы сетки): конструктивные элементы из выделенных КЭ. True — созданы.</summary>
    /// <param name="groupType">Тип группы КонЭ, предлагаемый в диалоге.</param>
    public bool CreateMembers(string? groupType) =>
        _app.CreateFemMembersFromMeshElements(_schema, SelectedTags(), groupType: groupType);

    /// <summary>Выпадающий список «Добавить в группу ▾» под кнопкой.</summary>
    public void ShowAddMenu(Button button)
    {
        var tags = SelectedTags();
        var menu = NewMenu(button);
        FemGroupMenus.FillAdd(menu.Items, Groups, _kind, tags, group =>
        {
            int added = _app.FemGroups.AddTags(_schema, group, tags);
            Done(string.Format(Loc.S("FemGroupTagsAdded"), added, group.Tag));
        });
        menu.IsOpen = true;
    }

    /// <summary>Выпадающий список «Убрать из группы ▾»: только группы, где есть выделенные.</summary>
    public void ShowRemoveMenu(Button button)
    {
        var tags = SelectedTags();
        var menu = NewMenu(button);
        FemGroupMenus.FillRemove(menu.Items, Groups, tags, group =>
        {
            int removed = _app.FemGroups.RemoveTags(group, tags);
            Done(string.Format(Loc.S("FemGroupTagsRemoved"), removed, group.Tag));
        });
        menu.IsOpen = true;
    }

    static ContextMenu NewMenu(Button button) => new()
    {
        PlacementTarget = button,
        Placement = PlacementMode.Bottom,
    };

    /// <summary>Сообщение в журнал и перерисовка колонки «Группы» (строки таблицы — доменные объекты без
    /// уведомлений об изменении).</summary>
    void Done(string message)
    {
        _app.LogService.Info(message);
        Refresh();
    }

    /// <summary>Перечитывает колонку «Группы» — после правок групп в обход таблицы (авто-группировка).</summary>
    public void Refresh()
    {
        RebuildIndex();
        _grid.Items.Refresh();
    }

    /// <summary>Обратный индекс «тег → имена групп» — один раз на загрузку и после каждой правки состава.</summary>
    void RebuildIndex()
    {
        var index = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var group in Groups.OrderBy(g => g.Tag, StringComparer.CurrentCulture))
            foreach (var tag in group.Tags)
            {
                if (!index.TryGetValue(tag, out var names)) index[tag] = names = [];
                names.Add(group.Tag);
            }
        _names.Index = index.ToDictionary(p => p.Key, p => string.Join(", ", p.Value), StringComparer.Ordinal);
    }

    /// <summary>Строка таблицы → имена её групп через запятую.</summary>
    sealed class GroupNamesConverter : IValueConverter
    {
        public Dictionary<string, string> Index { get; set; } = [];

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value != null && parameter is Func<object, string> tagOf && Index.TryGetValue(tagOf(value), out var names)
                ? names : "";

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
