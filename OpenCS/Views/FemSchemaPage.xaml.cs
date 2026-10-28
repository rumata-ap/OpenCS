using System.Windows;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using CScore.Fem;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using System.Windows.Controls;
using CScore.Fem.Editing;
using OpenCS.Services;
using OpenCS.Tasks;

namespace OpenCS.Views;

public partial class FemSchemaPage : UserControl, ISubmodelUiHost
{
    void OnAnalyzeStraightChain(object sender, RoutedEventArgs e) => _editorVm.AnalyzeStraightChain();

    CancellationTokenSource ISubmodelUiHost.BeginBusy(string message) => _app.BeginBusyWithCancellation(message);

    void ISubmodelUiHost.EndBusy(string? message) => _app.EndBusy(message);

    bool ISubmodelUiHost.ConfirmDiscardUnsavedEdits() =>
        !_editorVm.Session.IsDirty
        || MessageBox.Show(Loc.S("SubmodelConfirmDiscard"), Loc.S("Confirmation"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    void ISubmodelUiHost.ReloadSchemaPage() => _app.ReloadFemSchemaPage(openSubmodelTab: true);

    FemAnalysisParams? ISubmodelUiHost.EditNonlinearParameters(FemAnalysis? existing, FemAnalysisParams? current)
    {
        var dialog = FemAnalysisDialog.ForSubmodelNonlinear(_schema, _app.db.GetFemNodes(_schema.Id), existing, current);
        dialog.Owner = Window.GetWindow(this);
        return dialog.ShowDialog() == true ? dialog.ResultParams : null;
    }

    /// <summary>Выбирает узел и открывает его свойства (закрепления, нагрузки) — из 3D-вида и из дерева схемы.</summary>
    public void OpenNodeProperties(string tag)
    {
        var node = _editorVm.Session.Nodes.FirstOrDefault(n => n.NodeTag == tag);
        if (node == null) return;
        _editorVm.Selection.ToggleNode(tag, additive: false);
        var dlg = new FemNodePropertiesDialog(node, _editorVm) { Owner = Window.GetWindow(this) };
        dlg.MemberSelected += elemTag => _editorVm.Selection.ToggleElement(elemTag, additive: false);
        dlg.Show();
    }

    void ISubmodelUiHost.Log(FemValidationDiagnostic diagnostic)
    {
        if (diagnostic.IsError) _app.LogService.Error(diagnostic.Message);
        else _app.LogService.Warning(diagnostic.Message);
    }
    readonly FemSchemaEditorVM _editorVm;
    readonly Fem3DVM _fem3d;
    readonly AppViewModel _app;
    readonly FemSchema _schema;

    /// <summary>Показать в 3D-виде схемы результат постановки (источник мозаики «Расчёт»).</summary>
    public void ShowAnalysisResult(int analysisId, int? stepIndex = null, ViewModels.FemCsfeaCaseKey? caseKey = null)
    {
        tabs.SelectedIndex = 0;
        ShowResultWithoutGlyphs(analysisId, stepIndex, caseKey);
    }

    /// <summary>Результат постановки — без глифов схемы (как «Скрыть всё» в меню глифов): опоры, нагрузки и сечения
    /// заслоняют мозаику и эпюры; вернуть — тем же меню.</summary>
    void ShowResultWithoutGlyphs(int analysisId, int? stepIndex = null, ViewModels.FemCsfeaCaseKey? caseKey = null)
    {
        _fem3d.SetAllGlyphs(false);
        _fem3d.Mosaic.ShowAnalysis(analysisId, stepIndex, caseKey);
    }

    /// <summary>Фрагмент 3D-вида схемы по группе (из дерева проекта).</summary>
    public void ApplyGroupFragment(FemMemberGroup group, FemFragmentAction action)
    {
        tabs.SelectedIndex = 0;
        _fem3d.ApplyGroupFragment(group, action);
    }

    /// <summary>Фрагмент 3D-вида по КонЭ (из дерева схемы).</summary>
    public void ApplyMembersFragment(IReadOnlyCollection<string> memberTags, FemFragmentAction action)
    {
        tabs.SelectedIndex = 0;
        _fem3d.ApplyMembersFragment(memberTags, action);
    }

    /// <summary>«Выделить в схеме» у узлов и КонЭ дерева: выбор в 3D заменяется ими.</summary>
    public void SelectInSchema(IReadOnlyCollection<string> nodeTags, IReadOnlyCollection<string> memberTags)
    {
        tabs.SelectedIndex = 0;
        _editorVm.Selection.Set(nodeTags, memberTags, [], []);
    }

    /// <summary>«Удалить» у КонЭ дерева — командой редактора (отмена, сохранение схемы).</summary>
    public void DeleteMember(string elemTag) => _editorVm.DeleteMemberByTag(elemTag);

    /// <summary>«Удалить узел» из дерева — с тем же подтверждением, что в 3D (сколько КонЭ уйдёт вместе с узлом).</summary>
    public void DeleteNodes(IReadOnlyList<string> nodeTags) => ConfirmAndDeleteNodes(nodeTags);

    /// <summary>Снимает узловые связи (закрепления и пружины) с узлов КонЭ одной отменой.</summary>
    public void ClearNodeBoundary(IReadOnlyCollection<string> nodeTags) =>
        SetNodeBoundary(nodeTags, 0, new double[6]);

    public FemSchemaPage(FemSchema schema, AppViewModel app)
    {
        _app = app;
        _schema = schema;
        InitializeComponent();
        _editorVm = new FemSchemaEditorVM(schema, app);
        app.RegisterFemSchemaEditor(_editorVm);
        DataContext = _editorVm;

        // Вид строится по сессии при показе (LoadAsync): сетка-подложка читается в фоне, без замирания.
        _fem3d = new Fem3DVM(schema, app.db) { Selection = _editorVm.Selection, EditMode = true, SessionSource = () => _editorVm.Session };
        RestoreActiveLoadCase();
        loadCasesPanel.LoadCaseRenamed += lc => _app.OnFemActiveLoadCaseChanged(_schema, lc.Tag);
        view3D.Editor = _editorVm;
        view3D.DataContext = _fem3d;
        _editorVm.MeshDiscretized += async (_, _) =>
        {
            try
            {
                await _fem3d.LoadMeshOverlayAsync();
                app.ReloadFemMeshSnapshotTree(schema.Id);
                view3D.ShowMeshOverlay();
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception);
            }
        };
        _editorVm.OpenSchemaRequested += child => app.OpenFemSchema(child, openSubmodelTab: true);
        if (_editorVm.IsSubmodel)
        {
            submodelPanel.DataContext = new SubmodelPanelVM(schema.Id, app.db, new FemAnalysisExecutorSubmodelRunner(app),
                () => app.CalcSettings, this);
            if (app.OpenSubmodelTabRequested) tabs.SelectedItem = submodelTab;
        }
        if (app.ShowAnalysisResultRequested is (int analysisId, var stepIndex, var caseKey))
            ShowResultWithoutGlyphs(analysisId, stepIndex, caseKey);

        view3D.NodeCreateRequested += p => _editorVm.CreateNodeAt(p.X, p.Y, p.Z);
        view3D.BarCreateRequested  += (a, b) => _editorVm.CreateBarBetween(a, b, view3D.PendingBarSectionTag);
        view3D.CreateNodeModeCloseRequested += () => _editorVm.CreateNodeMode = false;
        view3D.CreateBarModeCloseRequested  += () => _editorVm.CreateBarMode  = false;
        view3D.PlateFrameRequested += tag =>
        {
            var frame = _editorVm.BuildPlateFrame(tag);
            if (frame != null) OpenPlanarRegionMemberDialog(frame);
        };
        view3D.WallFrameRequested += (a, b) =>
        {
            var frame = _editorVm.BuildWallFrame(a, b);
            if (frame != null) OpenPlanarRegionMemberDialog(frame);
        };
        view3D.SpatialPlateFrameRequested += (a, b, c) =>
        {
            var frame = _editorVm.BuildSpatialPlateFrame(a, b, c);
            if (frame != null) OpenPlanarRegionMemberDialog(frame);
        };
        view3D.PlanarRegionEditRequested += OpenPlanarRegionEdit;
        view3D.PlanarRegionDeleteRequested += DeletePlanarRegionMember;
        view3D.SetBarSectionItemsSource(_editorVm.CrossSections);
        view3D.MemberDeleteRequested += tag => _editorVm.DeleteMemberByTag(tag);
        view3D.MemberSplitRequested  += tag => _editorVm.SplitMemberByTag(tag);
        view3D.MemberPropertiesRequested  += OpenMemberProperties;
        view3D.MemberSectionEditRequested += OpenMemberProperties;
        view3D.MemberRotationRequested    += OpenMemberRotation;
        view3D.MemberMeshStepRequested    += OpenMemberMeshStep;
        view3D.MemberRigidJunctionRequested += OpenMemberRigidJunction;
        view3D.MemberForcesRequested      += tag => app.ShowMemberForceDiagram(schema, tag);
        view3D.FragmentTransformRequested += TransformFragment;
        view3D.NodeDeleteRequested += ConfirmAndDeleteNodes;
        view3D.NodePropertiesRequested += OpenNodeProperties;
        _editorVm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(FemSchemaEditorVM.CreateNodeMode))
                view3D.SetCreateNodeMode(_editorVm.CreateNodeMode);
            else if (args.PropertyName == nameof(FemSchemaEditorVM.CreateBarMode))
                view3D.SetCreateBarMode(_editorVm.CreateBarMode);
            else if (args.PropertyName == nameof(FemSchemaEditorVM.CreatePlateMode))
                view3D.SetCreatePlateMode(_editorVm.CreatePlateMode);
            else if (args.PropertyName == nameof(FemSchemaEditorVM.CreateWallMode))
                view3D.SetCreateWallMode(_editorVm.CreateWallMode);
            else if (args.PropertyName == nameof(FemSchemaEditorVM.CreateSpatialPlateMode))
                view3D.SetCreateSpatialPlateMode(_editorVm.CreateSpatialPlateMode);
            else if (args.PropertyName == nameof(FemSchemaEditorVM.Session) && !_fem3d.IsLoading)
                _fem3d.LoadFromSession(_editorVm.Session);
        };
        _editorVm.GeometryLocked += message => MessageBox.Show(Window.GetWindow(this), message,
            Loc.S("FemMemberMeshLockedTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
        _editorVm.SaveBlocked += errors => MessageBox.Show(
            string.Join("\n", errors.Select(d => d.Message)),
            Loc.S("FemSaveBlockedTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);

        PreviewKeyDown += OnPreviewKeyDown;
        LoadFragmentBanner();
    }

    /// <summary>Шапка фрагмента: сверка родителя в фоне; «изменён/удалён» — ещё и строкой журнала.</summary>
    async void LoadFragmentBanner()
    {
        var banner = new StoreyFragmentBannerVM(_app.db, _schema);
        fragmentBanner.DataContext = banner;
        if (!banner.IsVisible) return;
        await banner.LoadAsync();
        if (banner.IsWarning) _app.LogService.Warning($"{_schema.Tag}: {banner.StatusText}");
        if (banner.IsMaskWarning) _app.LogService.Warning($"{_schema.Tag}: {banner.MaskText}");
    }

    void FragmentMaskReport_Click(object sender, RoutedEventArgs e)
    {
        if (fragmentBanner.DataContext is not StoreyFragmentBannerVM banner) return;
        new StoreyFragmentReportDialog(banner.SchemaTag, Loc.S("StoreyFragmentMasksReportTaken"), banner.MaskReportRows,
            "StoreyFragmentMasksReportTitle") { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    /// <summary>Маски сняты или удалены — шапка (и ГУ разрезов) перечитываются.</summary>
    public void ReloadFragmentState() => LoadFragmentBanner();

    void FragmentMasks_Click(object sender, RoutedEventArgs e) => _app.TakeStoreyFragmentMasks(_schema);

    void FragmentReport_Click(object sender, RoutedEventArgs e)
    {
        if (fragmentBanner.DataContext is not StoreyFragmentBannerVM banner) return;
        new StoreyFragmentReportDialog(banner.SchemaTag, Loc.S("StoreyFragmentReportBuilt"), banner.ReportRows)
        {
            Owner = Window.GetWindow(this),
        }.ShowDialog();
    }

    /// <summary>Выполняет шаг команды «Переместить» (copies = 0) или «Копировать» из 3D-вида; одна отмена на шаг.</summary>
    void TransformFragment(IReadOnlyCollection<string> nodeTags, IReadOnlyCollection<string> memberTags,
        System.Windows.Media.Media3D.Vector3D offset, int copies)
    {
        bool copy = copies > 0;
        string? message = copy
            ? _editorVm.CopyFragment(nodeTags, memberTags, offset.X, offset.Y, offset.Z, copies)
            : _editorVm.MoveFragment(nodeTags, memberTags, offset.X, offset.Y, offset.Z);
        if (message != null)
            MessageBox.Show(Window.GetWindow(this), message, Loc.S(copy ? "FemFragmentCopyTitle" : "FemFragmentMoveTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>
    /// Активное загружение (как в RFEM) — запомненное для схемы или первое: загружение по умолчанию редактора нагрузок и
    /// источник глифов 3D. Смена в 3D или на вкладке «Загружения» синхронизирует второе и отмечается в дереве.
    /// </summary>
    void RestoreActiveLoadCase()
    {
        string? tag = FemLoadTree.ActiveTag(_editorVm.Session.LoadCases, _app.db.GetFemActiveLoadCaseTag(_schema.Id));
        _fem3d.PreferredLoadCaseTag = tag;
        _editorVm.SelectedLoadCase = _editorVm.Session.LoadCases.FirstOrDefault(lc => lc.Tag == tag);
        _editorVm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(FemSchemaEditorVM.SelectedLoadCase) || _editorVm.SelectedLoadCase is not { } lc) return;
            if (_fem3d.SelectedDiagramLoadSource?.LoadCase != lc) _fem3d.SelectDiagramLoadCase(lc);
            _app.OnFemActiveLoadCaseChanged(_schema, lc.Tag);
        };
        _fem3d.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(Fem3DVM.SelectedDiagramLoadSource)) return;
            if (_fem3d.SelectedDiagramLoadSource?.LoadCase is { } lc && _editorVm.SelectedLoadCase != lc)
                _editorVm.SelectedLoadCase = lc;
        };
    }

    /// <summary>Активное загружение по имени (щелчок в дереве).</summary>
    public void SetActiveLoadCase(string tag)
    {
        _fem3d.PreferredLoadCaseTag = tag;
        if (_editorVm.Session.LoadCases.FirstOrDefault(lc => lc.Tag == tag) is { } lc) _editorVm.SelectedLoadCase = lc;
    }

    /// <summary>«Создать загружение…» из дерева: параметры в диалоге, добавление — одной отменой.</summary>
    public void CreateLoadCase()
    {
        var session = _editorVm.Session;
        string prefix = Loc.S("FemLoadCaseDefaultTag"), tag = prefix;
        for (int n = 2; session.LoadCases.Any(lc => lc.Tag == tag); n++) tag = $"{prefix} {n}";
        var template = new FemLoadCase { SchemaId = session.Schema.Id, Tag = tag, Sp20Type = "short_term" };
        var dialog = new FemLoadCaseDialog(template, null, _editorVm.Sp20GammaDefaults)
        {
            Owner = Window.GetWindow(this),
            Validate = v => session.LoadCases.Any(lc => lc.Tag == v.Tag) ? Loc.S("FemLoadCaseTagTaken") : null,
        };
        if (dialog.ShowDialog() != true) return;
        var values = dialog.Result;
        values.SchemaId = session.Schema.Id;
        session.Execute(new AddLoadCaseCommand(values));
        _editorVm.RefreshCollections();
    }

    /// <summary>«Изменить…» у загружения в дереве: параметры одной отменой; активное загружение следует за именем.</summary>
    public void EditLoadCase(string tag, string? originText)
    {
        if (_editorVm.Session.LoadCases.FirstOrDefault(lc => lc.Tag == tag) is not { } loadCase) return;
        var dialog = new FemLoadCaseDialog(loadCase, originText, _editorVm.Sp20GammaDefaults)
        {
            Owner = Window.GetWindow(this),
            Validate = v => _editorVm.Session.LoadCases.Any(lc => !ReferenceEquals(lc, loadCase) && lc.Tag == v.Tag)
                ? Loc.S("FemLoadCaseTagTaken") : null,
        };
        if (dialog.ShowDialog() != true) return;
        bool wasActive = _app.db.GetFemActiveLoadCaseTag(_schema.Id) == tag;
        if (_editorVm.EditLoadCase(loadCase, dialog.Result) is { } error) { _app.LogService.Warning(error); return; }
        if (wasActive && loadCase.Tag != tag)
        {
            _app.OnFemActiveLoadCaseChanged(_schema, loadCase.Tag);
            SetActiveLoadCase(loadCase.Tag);
        }
    }

    /// <summary>«Удалить» у загружения в дереве: с его нагрузками и слагаемыми комбинаций — одной отменой.</summary>
    public void DeleteLoadCase(string tag)
    {
        if (_editorVm.Session.LoadCases.FirstOrDefault(lc => lc.Tag == tag) is { } loadCase) _editorVm.DeleteLoadCase(loadCase);
    }

    /// <summary>«Создать комбинацию…» из дерева: состав в диалоге, добавление — одной отменой.</summary>
    public void CreateLoadDefinition()
    {
        var session = _editorVm.Session;
        string prefix = Loc.S("FemLoadDefinitionDefaultTag"), tag = prefix;
        for (int n = 2; session.LoadDefinitions.Any(d => d.Tag == tag); n++) tag = $"{prefix} {n}";
        var definition = new FemLoadDefinition { SchemaId = session.Schema.Id, Tag = tag, SourceKind = "manual" };
        definition.SetExpression(new FemLoadExpression { Mode = FemLoadExpressionMode.Sum });
        var dialog = new FemCombinationDialog(definition, session.LoadCases)
        {
            Owner = Window.GetWindow(this),
            Validate = t => session.LoadDefinitions.Any(d => d.Tag == t) ? Loc.S("FemCombinationTagTaken") : null,
        };
        if (dialog.ShowDialog() != true) return;
        definition.Tag = dialog.DefinitionTag;
        definition.CombinationType = dialog.CombinationType;
        definition.SetExpression(new FemLoadExpression
        {
            Mode = dialog.Terms.Count == 1 ? FemLoadExpressionMode.Single : FemLoadExpressionMode.Sum,
            Terms = dialog.Terms.ToList(),
            CombinationType = dialog.CombinationType,
        });
        session.Execute(new AddLoadDefinitionCommand(definition));
        _editorVm.RefreshCollections();
    }

    /// <summary>«Изменить…» у комбинации в дереве: имя, тип и коэффициенты одной отменой.</summary>
    public void EditLoadDefinition(string tag)
    {
        var session = _editorVm.Session;
        if (session.LoadDefinitions.FirstOrDefault(d => d.Tag == tag) is not { } definition) return;
        var dialog = new FemCombinationDialog(definition, session.LoadCases)
        {
            Owner = Window.GetWindow(this),
            Validate = t => session.LoadDefinitions.Any(d => !ReferenceEquals(d, definition) && d.Tag == t)
                ? Loc.S("FemCombinationTagTaken") : null,
        };
        if (dialog.ShowDialog() != true) return;
        if (_editorVm.EditLoadDefinition(definition, dialog.DefinitionTag, dialog.CombinationType, dialog.Terms) is { } error)
            _app.LogService.Warning(error);
    }

    /// <summary>«Удалить» у комбинации в дереве — одной отменой.</summary>
    public void DeleteLoadDefinition(string tag)
    {
        if (_editorVm.Session.LoadDefinitions.FirstOrDefault(d => d.Tag == tag) is { } definition)
            _editorVm.DeleteLoadDefinition(definition);
    }

    /// <summary>
    /// «Удалить» у группы типа или у всей ветки «Комбинации» в дереве — с подтверждением, одной отменой.
    /// </summary>
    public void DeleteLoadDefinitions(IReadOnlyCollection<string> tags)
    {
        var set = tags.ToHashSet(StringComparer.Ordinal);
        var definitions = _editorVm.Session.LoadDefinitions.Where(d => set.Contains(d.Tag)).ToList();
        if (definitions.Count == 0) return;
        if (definitions.Count > 1 && MessageBox.Show(Window.GetWindow(this),
                string.Format(Loc.S("FemCombinationsDeleteConfirm"), definitions.Count), Loc.S("Confirmation"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _editorVm.DeleteLoadDefinitions(definitions);
    }

    /// <summary>«Сочетания по СП 20» из дерева — по параметрам загружений.</summary>
    public void GenerateSp20LoadDefinitions() => _editorVm.GenerateSp20LoadDefinitions("fundamental");

    /// <summary>Нагрузки комбинации в 3D (щелчок по комбинации в дереве).</summary>
    public void ShowLoadDefinition(string tag)
    {
        tabs.SelectedIndex = 0;
        _fem3d.SelectDiagramDefinition(tag);
    }

    /// <summary>Выделяет объекты ГУ из дерева схемы; вкладка не переключается — выбор виден в 3D при переходе на неё.</summary>
    public void SelectBoundaryTargets(FemBoundaryTargets targets) =>
        _editorVm.Selection.Set(targets.NodeTags, [], targets.MeshElemTags, targets.MeshNodeTags, targets.PlateEdges);

    /// <summary>
    /// Линейная связь граням (правка типа из дерева) одной отменой — по текущим граням сессии (ключ связи мог устареть
    /// после перенумерации контура); грани выделяются, схема сохраняется пользователем.
    /// </summary>
    public void SetLineSupports(IReadOnlyCollection<CScore.Fem.FemPlateEdgeKey> keys, CScore.Fem.FemLineSupport template)
    {
        // Грани — по сессии и областям из БД: 3D-вид может быть ещё не загружен (страница только открыта из дерева).
        var edges = CScore.Fem.FemLineSupportGeometry.EdgesByMember(_editorVm.Session.Members, _app.db.GetPlanarRegions(_editorVm.Session.Schema.Id))
            .Values.SelectMany(x => x).Where(e => keys.Contains(e.Key)).ToList();
        if (edges.Count == 0) return;
        _editorVm.SetLineSupports(edges, template);
        _editorVm.Selection.Set([], [], [], [], keys);
    }

    /// <summary>Снимает линейные связи граней (в т. ч. потерявших грань) одной отменой.</summary>
    public void RemoveLineSupports(IReadOnlyCollection<CScore.Fem.FemPlateEdgeKey> keys) => _editorVm.RemoveLineSupports(keys);

    /// <summary>Поля из таблиц изменились: глифы ГУ и мозаика 3D перечитываются.</summary>
    public void ReloadBoundary() => _ = _fem3d.ReloadBoundaryAsync();

    /// <summary>Мозаика ГУ из дерева схемы (C1 упругого основания, переменные пружины) — на вкладке 3D.</summary>
    public void ShowMosaic(PlateRebarMosaicSourceKind kind)
    {
        tabs.SelectedIndex = 0;
        _fem3d.Mosaic.ShowSource(kind);
    }

    /// <summary>Одна узловая связь (закрепления и пружины) узлам КонЭ — правка типа узловой связи из дерева — одной
    /// отменой; узлы выделяются, схема сохраняется пользователем.</summary>
    public void SetNodeBoundary(IReadOnlyCollection<string> nodeTags, int mask, IReadOnlyList<double> stiffnesses)
    {
        var tags = nodeTags.ToHashSet(StringComparer.Ordinal);
        var nodes = _editorVm.Session.Nodes.Where(n => tags.Contains(n.NodeTag)).ToList();
        if (nodes.Count == 0) return;
        _editorVm.Session.Execute(new SetNodeBoundaryCommand(nodes, mask, stiffnesses));
        _editorVm.RefreshCollections();
        _editorVm.Selection.Set(tags, [], [], []);
    }

    /// <summary>Сессия редактора — объекты для проверки списков типа ГУ из дерева.</summary>
    public CScore.Fem.Editing.FemSchemaEditSession EditorSession => _editorVm.Session;

    /// <summary>Выбор в редакторе — «Назначить выделенным» у типа ГУ в дереве.</summary>
    public FemSchemaSelectionVM EditorSelection => _editorVm.Selection;

    /// <summary>
    /// Состав типа узловой связи (из дерева): узлам <paramref name="add"/> — закрепления и пружины типа, у
    /// <paramref name="remove"/> связь снимается — одной отменой; затронутые узлы выделяются.
    /// </summary>
    public void AssignNodeBoundary(IReadOnlyCollection<string> add, IReadOnlyCollection<string> remove, int mask,
        IReadOnlyList<double> stiffnesses)
    {
        var addSet = add.ToHashSet(StringComparer.Ordinal);
        var removeSet = remove.Where(t => !addSet.Contains(t)).ToHashSet(StringComparer.Ordinal);
        var addNodes = _editorVm.Session.Nodes.Where(n => addSet.Contains(n.NodeTag)).ToList();
        var removeNodes = _editorVm.Session.Nodes.Where(n => removeSet.Contains(n.NodeTag)).ToList();
        var commands = new List<IFemEditCommand>();
        if (removeNodes.Count > 0) commands.Add(new SetNodeBoundaryCommand(removeNodes, 0, new double[6]));
        if (addNodes.Count > 0) commands.Add(new SetNodeBoundaryCommand(addNodes, mask, stiffnesses));
        if (commands.Count == 0) return;
        _editorVm.Session.Execute(commands.Count == 1 ? commands[0] : new CompositeFemCommand(commands));
        _editorVm.RefreshCollections();
        _editorVm.Selection.Set(addSet.Concat(removeSet), [], [], []);
    }

    /// <summary>
    /// Состав типа линейной связи (из дерева): граням <paramref name="add"/> — параметры типа, с <paramref name="remove"/>
    /// связь снимается — одной отменой; грани ищутся по сессии, затронутые выделяются.
    /// </summary>
    public void AssignLineSupports(IReadOnlyCollection<CScore.Fem.FemPlateEdgeKey> add,
        IReadOnlyCollection<CScore.Fem.FemPlateEdgeKey> remove, CScore.Fem.FemLineSupport template)
    {
        var addSet = add.ToHashSet();
        var removeSet = remove.Where(k => !addSet.Contains(k)).ToHashSet();
        var edges = CScore.Fem.FemLineSupportGeometry.EdgesByMember(_editorVm.Session.Members, _app.db.GetPlanarRegions(_editorVm.Session.Schema.Id))
            .Values.SelectMany(x => x).Where(e => addSet.Contains(e.Key)).ToList();
        var removed = _editorVm.Session.LineSupports.Where(s => removeSet.Contains(s.Key)).ToList();
        var commands = new List<IFemEditCommand>();
        if (removed.Count > 0) commands.Add(new RemoveLineSupportsCommand(removed));
        if (edges.Count > 0) commands.Add(new SetLineSupportsCommand(edges, template));
        if (commands.Count == 0) return;
        _editorVm.Session.Execute(commands.Count == 1 ? commands[0] : new CompositeFemCommand(commands));
        _editorVm.RefreshCollections();
        _editorVm.Selection.Set([], [], [], [], addSet.Concat(removeSet));
    }

    /// <summary>Запуск «Переместить» / «Копировать» из дерева: вкладка 3D, объекты выбираются, команда ждёт базовую точку.</summary>
    public void StartTransform(IReadOnlyCollection<string> nodeTags, IReadOnlyCollection<string> memberTags, bool copy)
    {
        tabs.SelectedIndex = 0;
        var selection = _editorVm.Selection;
        selection.Clear();
        foreach (var tag in nodeTags) selection.ToggleNode(tag, additive: true);
        foreach (var tag in memberTags) selection.ToggleElement(tag, additive: true);
        view3D.StartTransform(copy ? FemTransformKind.Copy : FemTransformKind.Move, nodeTags, memberTags);
    }

    void CreateMember_Click(object sender, RoutedEventArgs e)
        => _editorVm.CreateMemberGroupFromElements(barsGrid.SelectedItems.OfType<FemMember>());

    void AssignMissingGj_Click(object sender, RoutedEventArgs e)
        => ExecuteGjBatch(FemGjBatchMode.MissingOnly, confirmOverwrite: false);

    void RecalculateGj_Click(object sender, RoutedEventArgs e)
        => ExecuteGjBatch(FemGjBatchMode.RecalculateManual, confirmOverwrite: true);

    void ExecuteGjBatch(FemGjBatchMode mode, bool confirmOverwrite)
    {
        var members = barsGrid.SelectedItems.OfType<FemMember>().ToArray();
        if (members.Length == 0) members = _editorVm.Members.ToArray();

        var plan = _editorVm.PreviewGjBatch(members, mode);
        if (confirmOverwrite && plan.Assigned > 0 && MessageBox.Show(
                Window.GetWindow(this),
                string.Format(Loc.S("FemGjRecalculateConfirm"), plan.Assigned),
                Loc.S("FemGjRecalculateTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        _editorVm.ApplyGjBatch(plan);
        MessageBox.Show(
            Window.GetWindow(this),
            string.Format(Loc.S("FemGjBatchSummary"),
                plan.Assigned, plan.Fallback, plan.SkippedSaintVenant, plan.SkippedNoSection),
            Loc.S("FemGjBatchTitle"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    void OpenMemberProperties(string tag)
    {
        var member = _editorVm.Session.Members.FirstOrDefault(m => m.ElemTag == tag);
        if (member == null) return;
        new FemMemberPropertiesDialog(member, _editorVm) { Owner = Window.GetWindow(this) }.Show();
    }

    void ConfirmAndDeleteNodes(IReadOnlyList<string> nodeTags)
    {
        if (CScore.Fem.Editing.FemMeshLock.LockedMembersOf(_editorVm.Session.Members, nodeTags).Count > 0)
        {
            _editorVm.DeleteNodesByTags(nodeTags); // откажет и покажет причину
            return;
        }
        var impact = _editorVm.GetNodeDeletionImpact(nodeTags);
        if (impact.NodeCount == 0) return;

        string resourceKey = impact.NodeCount == 1
            ? "FemNodeDeleteConfirm"
            : "FemNodesDeleteConfirm";
        string message = impact.NodeCount == 1
            ? string.Format(Loc.S(resourceKey), nodeTags[0], impact.MemberCount)
            : string.Format(Loc.S(resourceKey), impact.NodeCount, impact.MemberCount);

        if (MessageBox.Show(
                Window.GetWindow(this), message, Loc.S("FemNodeDeleteConfirmTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        _editorVm.DeleteNodesByTags(nodeTags);
    }

    void OpenPlanarRegionMemberDialog(CScore.Planar.Frame3D frame,
        FemMember? existingMember = null, CScore.Planar.PlanarRegion? existingRegion = null)
    {
        var dlg = new PlanarRegionMemberDialog(_app, _schema, frame, existingMember, existingRegion)
        {
            Owner = Window.GetWindow(this)
        };
        dlg.ShowDialog();

        if (dlg.SavedMember is { } saved)
        {
            _editorVm.Session.Members.RemoveAll(m => m.Id == saved.Id);
            _editorVm.Session.Members.Add(saved);
            _editorVm.RefreshCollections();
            _fem3d.LoadFromSession(_editorVm.Session);
        }
        else if (dlg.DeletedMember is { } deleted)
        {
            _editorVm.Session.Members.RemoveAll(m => m.Id == deleted.Id);
            _editorVm.RefreshCollections();
            _fem3d.LoadFromSession(_editorVm.Session);
        }
    }

    void OpenPlanarRegionEdit(string elemTag)
    {
        var member = _editorVm.Session.Members.FirstOrDefault(m => m.ElemTag == elemTag);
        if (member?.PlanarRegionId is not int regionId) return;
        var region = _app.db.GetPlanarRegions(_schema.Id).FirstOrDefault(r => r.Id == regionId);
        if (region == null) return;
        OpenPlanarRegionMemberDialog(region.Frame, member, region);
    }

    void DeletePlanarRegionMember(string elemTag)
    {
        var member = _editorVm.Session.Members.FirstOrDefault(m => m.ElemTag == elemTag);
        if (member == null) return;
        _app.db.DeleteFemMember(member);
        if (member.PlanarRegionId is int regionId) _app.db.DeletePlanarRegion(regionId);

        _editorVm.Session.Members.RemoveAll(m => m.Id == member.Id);
        _editorVm.RefreshCollections();
        _fem3d.LoadFromSession(_editorVm.Session);
    }

    /// <summary>«Шаг сетки…»: локальный шаг щёлкнутого КонЭ; если он выделен — всех выделенных КонЭ.</summary>
    void OpenMemberMeshStep(string tag)
    {
        var selected = _editorVm.Selection.SelectedElemTags;
        var tags = selected.Contains(tag) ? selected.ToList() : [tag];
        var members = _editorVm.Session.Members.Where(m => tags.Contains(m.ElemTag) && !m.IsMeshLocked).ToList();
        if (members.Count == 0) return;

        int shells = members.Count(m => m.ElemType == "shell"), bars = members.Count - shells;
        string target = members.Count == 1
            ? string.Format(Loc.S(shells == 1 ? "FemMemberMeshStepTargetShell" : "FemMemberMeshStepTargetBar"), members[0].ElemTag)
            : string.Format(Loc.S("FemMemberMeshStepTargetMany"), members.Count, bars, shells);
        static string M(double v) => string.Format(Loc.S("FemMeshStepMeters"), v.ToString("0.###", System.Globalization.CultureInfo.CurrentCulture));
        var (barStep, plateStep) = _app.db.GetFemSchemaMeshSteps(_schema.Id);
        string barCommon = barStep is double b ? M(b) : Loc.S("FemMeshBarStepNone");
        string plateCommon = plateStep is double p ? M(p) : Loc.S("FemMeshPlateStepNone");
        string common = string.Format(Loc.S("FemMemberMeshStepCommon"),
            bars == 0 ? plateCommon : shells == 0 ? barCommon : $"{barCommon} / {plateCommon}");
        var steps = members.Select(m => m.TargetMeshLengthM).Distinct().ToList();

        var dlg = new FemMemberMeshStepDialog(target, common, steps.Count == 1 ? steps[0] : null) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;
        _editorVm.SetMembersMeshStep(members.Select(m => m.ElemTag).ToList(), dlg.StepM);
        _app.LogService.Info(string.Format(Loc.S(dlg.StepM is null ? "FemMemberMeshStepSetCommon" : "FemMemberMeshStepSet"),
            members.Count, dlg.StepM is double s ? M(s) : ""));
    }

    /// <summary>«АЖТ примыкания…»: параметры щёлкнутого стержня; если он выделен — всех выделенных стержней.</summary>
    public void OpenMemberRigidJunction(string tag)
    {
        var selected = _editorVm.Selection.SelectedElemTags;
        var tags = selected.Contains(tag) ? selected.ToList() : [tag];
        var members = _editorVm.Session.Members.Where(m => tags.Contains(m.ElemTag) && m.ElemType == "beam").ToList();
        if (members.Count == 0) return;

        string target = members.Count == 1
            ? string.Format(Loc.S("FemMemberMeshStepTargetBar"), members[0].ElemTag)
            : string.Format(Loc.S("FemMemberRigidJunctionTargetMany"), members.Count);
        var dlg = new FemMemberRigidJunctionDialog(target, members.Count, RigidJunctionParamsSummary.Of(members))
        {
            Owner = Window.GetWindow(this),
        };
        if (dlg.ShowDialog() != true) return;
        _editorVm.SetMembersRigidJunction(members.Select(m => m.ElemTag).ToList(), dlg.Off, dlg.Coupled, dlg.Dofs, dlg.ClearTrace);
    }

    void OpenMemberRotation(string tag)
    {
        var member = _editorVm.Session.Members.FirstOrDefault(m => m.ElemTag == tag);
        if (member == null) return;

        new FemMemberRotationDialog(member.RotationDeg, value =>
        {
            _editorVm.Session.Execute(new SetMemberRotationCommand(member, value));
            _editorVm.RefreshCollections();
            _fem3d.LoadFromSession(_editorVm.Session);
        }) { Owner = Window.GetWindow(this) }.Show();
    }

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        if (!ctrl) return;

        if (e.Key == Key.C)
        {
            _editorVm.CopySelection();
        }
        else if (e.Key == Key.V && _editorVm.HasClipboard)
        {
            var dlg = new FemFragmentOffsetDialog("FemPasteOffsetTitle", null, showCopies: true) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true) _editorVm.PasteClipboard(dlg.Dx, dlg.Dy, dlg.Dz, dlg.Copies);
        }
        else if (e.Key == Key.Z && _editorVm.UndoCommand.CanExecute(null))
        {
            _editorVm.UndoCommand.Execute(null);
        }
        else if (e.Key == Key.Y && _editorVm.RedoCommand.CanExecute(null))
        {
            _editorVm.RedoCommand.Execute(null);
        }
    }
}
