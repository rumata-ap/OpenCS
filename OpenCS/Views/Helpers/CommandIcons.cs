using System.Windows;
using System.Windows.Controls;

namespace OpenCS.Views.Helpers;

/// <summary>
/// Иконки команд МКЭ-схемы по ключу строки пункта меню — одно место соответствия «действие → иконка»
/// для разметки и для меню, собираемых в коде. В XAML: <c>helpers:CommandIcons.Header="FemSchemaDelete"</c>
/// вместо <c>Header="{DynamicResource FemSchemaDelete}"</c> — ставит подпись (с переключением языка) и иконку.
/// В коде: <see cref="Apply"/>.
/// </summary>
public static class CommandIcons
{
    /// <summary>Ключ строки пункта → ключ иконки в <c>FemCommandIcons.xaml</c>.</summary>
    public static IReadOnlyDictionary<string, string> ByHeaderKey { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Схемы
        ["FemSchemaNew"] = "CmdIcon_SchemaNew",
        ["FemSchemaDuplicate"] = "CmdIcon_Duplicate",
        ["FemSchemaRename"] = "CmdIcon_Rename",
        ["FemSchemaDelete"] = "CmdIcon_Delete",
        ["FemSchemaMeshSteps"] = "CmdIcon_MeshStep",
        ["StoreyFragmentMenu"] = "CmdIcon_Split",
        ["StoreyFragmentMasksMenu"] = "CmdIcon_Import",
        ["StoreyFragmentMasksDelete"] = "CmdIcon_Delete",
        ["FemMemberNew"] = "CmdIcon_MemberNew",
        ["FemNodeTreeOpenEditor"] = "CmdIcon_OpenEditor",

        // ЛИРА-САПР
        ["FemSchemaMenuLira"] = "CmdIcon_Lira",
        ["ImportSubMenuLiraForces"] = "CmdIcon_Lira",
        ["ImportLiraForcesFromApi"] = "CmdIcon_Import",
        ["ImportLiraRsnFromApi"] = "CmdIcon_Import",
        ["ImportLiraRsuFromApi"] = "CmdIcon_Import",
        ["FemSchemaLoadLiraRbt"] = "CmdIcon_RebarImport",
        ["FemSchemaLoadLiraAsp"] = "CmdIcon_RebarImport",
        ["FemSchemaRefreshLiraTza"] = "CmdIcon_Refresh",
        ["FemSchemaRefreshLiraAxes"] = "CmdIcon_Refresh",
        ["FemSchemaRefreshLiraStiffness"] = "CmdIcon_Refresh",
        ["FemSchemaCreateLiraPlateSections"] = "CmdIcon_SectionNew",
        ["FemSchemaCreateLiraBarSections"] = "CmdIcon_SectionNew",
        ["FemSchemaConvertLiraBlocks"] = "CmdIcon_MembersFromMesh",
        ["LiraLoadsTransfer"] = "CmdIcon_LoadsImport",
        ["LiraBoundaryRefresh"] = "CmdIcon_SupportImport",

        // SCAD
        ["FemSchemaMenuScad"] = "CmdIcon_Scad",
        ["ImportSubMenuScadForces"] = "CmdIcon_Scad",
        ["ScadForcesLoadCasesFromApi"] = "CmdIcon_Import",
        ["ScadForcesCombinationsFromApi"] = "CmdIcon_Import",
        ["ScadForcesRsuFromApi"] = "CmdIcon_Import",
        ["ScadMaskSnapshotTake"] = "CmdIcon_Import",
        ["ScadRebarLoad"] = "CmdIcon_RebarImport",
        ["ScadRebarRefreshFromSpr"] = "CmdIcon_Refresh",
        ["ScadLoadsTransfer"] = "CmdIcon_LoadsImport",
        ["ScadBoundaryRefresh"] = "CmdIcon_SupportImport",
        ["ScadPluginInstall"] = "CmdIcon_Plugin",
        ["FemSchemaCreateScadPlateSections"] = "CmdIcon_SectionNew",
        ["FemSchemaCreateScadBarSections"] = "CmdIcon_SectionNew",
        ["ScadForcesSetProject"] = "CmdIcon_FileLink",

        // Конструктивные элементы и узлы
        ["FemCreatePlateMode"] = "CmdIcon_CreatePlate",
        ["FemCreateWallMode"] = "CmdIcon_CreateWall",
        ["FemCreateSpatialPlateMode"] = "CmdIcon_CreateSpatialPlate",
        ["FemMemberSectionCtx"] = "CmdIcon_Section",
        ["FemMemberPropertiesCtx"] = "CmdIcon_Properties",
        ["FemMemberTreeProperties"] = "CmdIcon_Properties",
        ["FemMemberRotationCtx"] = "CmdIcon_Rotate",
        ["FemMemberRigidJunctionCtx"] = "CmdIcon_Properties",
        ["FemMemberMeshStepCtx"] = "CmdIcon_MeshStep",
        ["FemMemberLoadCtx"] = "CmdIcon_MemberLoad",
        ["FemMemberSplitCtx"] = "CmdIcon_Split",
        ["FemMemberDeleteCtx"] = "CmdIcon_Delete",
        ["FemShellDelete"] = "CmdIcon_Delete",
        ["FemMemberForcesCtx"] = "CmdIcon_Diagram",
        ["FemMemberMoveCtx"] = "CmdIcon_Move",
        ["FemMemberCopyCtx"] = "CmdIcon_Copy",
        ["FemNodeMoveCtx"] = "CmdIcon_Move",
        ["FemNodeCopyCtx"] = "CmdIcon_Copy",
        ["FemNodeLoadCtx"] = "CmdIcon_NodeLoad",
        ["FemNodeBoundaryCtx"] = "CmdIcon_Support",
        ["FemKinematicLoadCtx"] = "CmdIcon_KinematicLoad",
        ["FemNodePropertiesCtx"] = "CmdIcon_Properties",
        ["FemNodeTreeProperties"] = "CmdIcon_Properties",
        ["FemNodeDeleteCtx"] = "CmdIcon_Delete",
        ["FemNodeTreeMembers"] = "CmdIcon_Adjacent",
        ["FemNodeTreeCopyCoords"] = "CmdIcon_Clipboard",
        ["FemBoundarySelect"] = "CmdIcon_Select",
        ["FemBoundaryEdit"] = "CmdIcon_Edit",
        ["FemBoundaryRigidJunctionParams"] = "CmdIcon_Properties",
        ["FemBoundaryObjects"] = "CmdIcon_Table",
        ["FemBoundaryAssignSelected"] = "CmdIcon_GroupAdd",
        ["FemBoundaryRemoveSelected"] = "CmdIcon_GroupRemove",
        ["FemBoundaryShowOnSchema"] = "CmdIcon_ShowResult",
        ["FemFieldAssign"] = "CmdIcon_Import",
        ["FemFieldEdit"] = "CmdIcon_Edit",
        ["FemFieldRepeat"] = "CmdIcon_Import",
        ["FemFieldShow"] = "CmdIcon_ShowResult",
        ["FemFieldEnable"] = "CmdIcon_ShowAll",
        ["FemFieldDisable"] = "CmdIcon_Hide",
        ["FemFieldDelete"] = "CmdIcon_Delete",
        ["FemMeshNodeBoundaryEdit"] = "CmdIcon_Support",
        ["FemLineSupportMenu"] = "CmdIcon_LineSupport",
        ["FemBoundaryLineDelete"] = "CmdIcon_Delete",
        ["FemBoundaryNodalDelete"] = "CmdIcon_Delete",
        ["FemLoadCaseActivate"] = "CmdIcon_Select",
        ["FemCombinationShow"] = "CmdIcon_ShowResult",
        ["FemLoadCaseCreate"] = "CmdIcon_Add",
        ["FemLoadCaseEdit"] = "CmdIcon_Edit",
        ["FemLoadCaseDelete"] = "CmdIcon_Delete",
        ["FemCombinationCreate"] = "CmdIcon_Add",
        ["FemCombinationEdit"] = "CmdIcon_Edit",
        ["FemCombinationDelete"] = "CmdIcon_Delete",
        ["FemCombinationGroupDelete"] = "CmdIcon_Delete",
        ["FemCombinationsDeleteAll"] = "CmdIcon_Delete",
        ["FemCombinationsSp20"] = "CmdIcon_GroupAuto",
        ["FemLineSupportRemove"] = "CmdIcon_Delete",
        ["FemLineSupportPickExit"] = "CmdIcon_ClearSelection",
        ["FemLoadEditCtx"] = "CmdIcon_Edit",
        ["FemLoadDeleteCtx"] = "CmdIcon_Delete",
        ["PlanarRegionEditCtx"] = "CmdIcon_Edit",
        ["PlanarRegionDeleteCtx"] = "CmdIcon_Delete",
        ["Fem3DMeshMembersFromSelection"] = "CmdIcon_MembersFromMesh",
        ["FemMeshMembersFromGroup"] = "CmdIcon_MembersFromMesh",
        ["Fem3DClearSelection"] = "CmdIcon_ClearSelection",

        // Группы
        ["FemMembersGroupNew"] = "CmdIcon_GroupNew",
        ["FemMeshGroupNew"] = "CmdIcon_GroupNew",
        ["FemMemberTreeGroupFromMember"] = "CmdIcon_GroupNew",
        ["FemNodeTreeGroupFromMembers"] = "CmdIcon_GroupNew",
        ["FemMemberTreeGroupAll"] = "CmdIcon_GroupNew",
        ["FemMemberTreeGroupAllBars"] = "CmdIcon_GroupNew",
        ["FemMemberTreeGroupAllShells"] = "CmdIcon_GroupNew",
        ["Fem3DMeshGroupFromSelection"] = "CmdIcon_GroupNew",
        ["Fem3DMembersGroupFromSelection"] = "CmdIcon_GroupNew",
        ["FemGroupAuto"] = "CmdIcon_GroupAuto",
        ["FemMemberTreeAddToGroup"] = "CmdIcon_GroupAdd",
        ["Fem3DGroupAddTo"] = "CmdIcon_GroupAdd",
        ["FemMemberTreeRemoveFromGroup"] = "CmdIcon_GroupRemove",
        ["Fem3DGroupRemoveFrom"] = "CmdIcon_GroupRemove",
        ["FemGroupRename"] = "CmdIcon_Rename",
        ["FemMemberDelete"] = "CmdIcon_Delete",
        ["FemGroupShowOnlyIn3D"] = "CmdIcon_ShowOnly",
        ["FemGroupHideIn3D"] = "CmdIcon_Hide",
        ["Fem3DFragmentGroups"] = "CmdIcon_Group",
        ["Fem3DFragmentMeshGroupItem"] = "CmdIcon_Group",
        ["Fem3DFragmentMembersGroupItem"] = "CmdIcon_Group",
        ["Fem3DFragmentShowOnlyCtx"] = "CmdIcon_ShowOnly",
        ["Fem3DStoreyFragmentCtx"] = "CmdIcon_Split",
        ["Fem3DFragmentHideCtx"] = "CmdIcon_Hide",
        ["Fem3DFragmentShowCtx"] = "CmdIcon_ShowAll",

        // Расчёты и результаты
        ["FemAnalysisCreate"] = "CmdIcon_AnalysisNew",
        ["FemAnalysisEdit"] = "CmdIcon_Edit",
        ["FemAnalysisRun"] = "CmdIcon_Run",
        ["FemAnalysisViewResult"] = "CmdIcon_ShowResult",
        ["FemAnalysisResultWindow"] = "CmdIcon_Table",
        ["FemAnalysisDelete"] = "CmdIcon_Delete",
        ["FemBarDiagramMenu"] = "CmdIcon_Diagram",
        ["FemResultMember2D"] = "CmdIcon_Diagram",
        ["FemResultMenuMember2D"] = "CmdIcon_Diagram",
        ["FemResultMemberSection"] = "CmdIcon_Section",
        ["FemResultMenuMemberSection"] = "CmdIcon_Section",
        ["FemMemberForceSectionState"] = "CmdIcon_SectionState",
        ["FemResultNodeValues"] = "CmdIcon_NodeValues",
        ["FemResultMenuMemberForceSet"] = "CmdIcon_ForceSetNew",
        ["FemResultMemberCreateForceSet"] = "CmdIcon_ForceSetNew",
        ["FemCsfeaCreateForceSets"] = "CmdIcon_ForceSetNew",
        ["FemCsfeaCreateForceSetsSelected"] = "CmdIcon_ForceSetNew",
        ["FemCsfeaBatchForceSets"] = "CmdIcon_ForceSetNew",
        ["FemCsfeaBatchShowVariant"] = "CmdIcon_ShowResult",
        ["FemCsfeaBatchNewFromProblems"] = "CmdIcon_AnalysisNew",
        ["FemCsfeaLinearOnStiffness"] = "CmdIcon_AnalysisNew",

        // Наборы усилий
        ["DuplicateForceSet"] = "CmdIcon_Duplicate",
        ["DeleteForceSetItem"] = "CmdIcon_Delete",
        ["DeleteSelectedForceSetsMenu"] = "CmdIcon_DeleteAll",
        ["DeleteFemForceSetsMenu"] = "CmdIcon_DeleteAll",

        // Проверки
        ["FemCheckAdd"] = "CmdIcon_CheckAdd",
        ["FemCheckAddUls"] = "CmdIcon_CheckUls",
        ["FemCheckAddSls"] = "CmdIcon_CheckSls",
        ["FemCheckEdit"] = "CmdIcon_Edit",
        ["RunFemCheck"] = "CmdIcon_Run",
        ["FemCheckDelete"] = "CmdIcon_Delete",
        ["FemCheckDeleteAll"] = "CmdIcon_DeleteAll",
    };

    /// <summary>Ставит иконку пункту по ключу его строки; ключ без иконки пропускается.</summary>
    public static MenuItem Apply(MenuItem item, string headerKey)
    {
        if (ByHeaderKey.TryGetValue(headerKey, out var iconKey) && MenuIcon.Create(iconKey, item) is { } icon)
            item.Icon = icon;
        return item;
    }

    /// <summary>Пункт меню с подписью по ключу строки (как <c>Loc.S</c>) и иконкой.</summary>
    public static MenuItem Item(string headerKey) => Apply(new MenuItem { Header = OpenCS.Utilites.Loc.S(headerKey) }, headerKey);

    public static readonly DependencyProperty HeaderProperty = DependencyProperty.RegisterAttached(
        "Header", typeof(string), typeof(CommandIcons), new PropertyMetadata(null, OnHeaderChanged));

    public static string? GetHeader(DependencyObject d) => (string?)d.GetValue(HeaderProperty);
    public static void SetHeader(DependencyObject d, string? value) => d.SetValue(HeaderProperty, value);

    static void OnHeaderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not MenuItem item || e.NewValue is not string key) return;
        item.SetResourceReference(HeaderedItemsControl.HeaderProperty, key);
        Apply(item, key);
    }
}
