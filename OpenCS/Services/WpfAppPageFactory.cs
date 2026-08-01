using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using CScore;
using CScore.Fem;
using CScore.Fire.Entities;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using OpenCS.Views;
using OpenCS.Views.Dialogs;

namespace OpenCS.Services
{
   /// <summary>
   /// WPF-реализация <see cref="IAppPageFactory"/>: единственное место, где VM-слой
   /// создаёт страницы и диалоги GUI. Регистрируется в UiServices.Pages при старте.
   /// </summary>
   public sealed class WpfAppPageFactory : IAppPageFactory
   {
      /// <inheritdoc/>
      public string GetPageTitle(IContentPage page) => page switch
      {
         MaterialPage _ => Loc.S("Material"),
         MaterialCharsPage _ => Loc.S("MaterialParams"),
         ContoursView _ => Loc.S("Contours"),
         CirclesView _ => Loc.S("Circles"),
         FromDxfPage _ => Loc.S("FromDxf"),
         DxfInteractiveView _ => Loc.S("FromDxf"),
         DiagramPage _ => Loc.S("DiagramTag"),
         CrossSectionPage _ => Loc.S("CrossSections"),
         TwoStageSectionEditorPage _ => Loc.S("TwoStageSections"),
         PlateSectionPage _ => Loc.S("PlateSections"),
         MaterialAreaPage _ => Loc.S("MaterialAreaLabel"),
         RebarGroupEditorPage _ => Loc.S("RebarGroups"),
         BarForceSetPage _ => Loc.S("BarForceSets"),
         ShellForceSetPage _ => Loc.S("ShellForceSets"),
         FireSectionView _ => Loc.S("FireSection_Node"),
         CalcTasksPage _ => Loc.S("CalcTasks"),
         FemSchemaPage _ => Loc.S("FemSchemaPageTitle"),
         FemMemberEditorPage _ => Loc.S("FemMemberDlgTitle"),
         FemCheckResultView _ => Loc.S("FemCheckDlgTitle"),
         FemAnalysisResultView _ => Loc.S("FemAnalysisResult"),
         FemMemberForceView _ => Loc.S("FemMemberForceViewTitle"),
         CalcResultView _ => Loc.S("CalcResult"),
         _ => "",
      };

      /// <inheritdoc/>
      public IContentPage CreateTwoStageSectionEditorPage() => new TwoStageSectionEditorPage(CurrentApp);

      /// <inheritdoc/>
      public IContentPage CreateTwoStageSectionEditorPage(TwoStageSection section)
         => new TwoStageSectionEditorPage(section, CurrentApp);

      /// <inheritdoc/>
      public IContentPage CreateCrossSectionPage() => new CrossSectionPage(CurrentApp);

      /// <inheritdoc/>
      public IContentPage CreateCrossSectionPage(CrossSection section) => new CrossSectionPage(section, CurrentApp);

      /// <inheritdoc/>
      public IContentPage CreatePlateSectionPage() => new PlateSectionPage(CurrentApp);

      /// <inheritdoc/>
      public IContentPage CreatePlateSectionPage(PlateSection section) => new PlateSectionPage(section, CurrentApp);

      /// <inheritdoc/>
      public IContentPage CreateFireSectionPage(FireSectionDef section) => new FireSectionView(section, CurrentApp);

      /// <inheritdoc/>
      public IContentPage CreateFemSchemaPage(FemSchema schema)
         => new Views.FemSchemaPage(schema, CurrentApp);

      /// <inheritdoc/>
      public IContentPage CreateFemMemberEditorPage(FemMemberGroup group) => new FemMemberEditorPage(group, CurrentApp);

      /// <inheritdoc/>
      public IContentPage CreateBarForceSetPage() => new BarForceSetPage(CurrentApp);

      /// <inheritdoc/>
      public IContentPage CreateBarForceSetPage(ForceSet set) => new BarForceSetPage(set, CurrentApp);

      /// <inheritdoc/>
      public IContentPage CreateShellForceSetPage() => new ShellForceSetPage(CurrentApp);

      /// <inheritdoc/>
      public IContentPage CreateShellForceSetPage(ForceSet set) => new ShellForceSetPage(set, CurrentApp);

      /// <inheritdoc/>
      public IContentPage CreateMaterialAreaPage(MaterialArea area) => new MaterialAreaPage(area, CurrentApp);

      /// <inheritdoc/>
      public IContentPage CreateRebarGroupEditorPage(MaterialArea area) => new RebarGroupEditorPage(area, CurrentApp);

      /// <inheritdoc/>
      public IContentPage CreateContourPlotPage(bool isSaved)
      {
         var page = new ContourPlot(CurrentApp);
         page.DataContext = CurrentApp.CurrentContour;
         return page;
      }

      /// <inheritdoc/>
      public IContentPage CreateDiagramPage(Diagramm diagram, bool isNew)
         => new DiagramPage(diagram, CurrentApp, isNew);

      /// <inheritdoc/>
      public IContentPage CreateMaterialPage(Material material) => new MaterialPage(material, CurrentApp);

      /// <inheritdoc/>
      public IContentPage CreateMaterialFromSourcePage(int tabIndex)
      {
         var material = new Material(0);
         var vm = new MaterialVM { Material = material, mvm = CurrentApp };
         var page = new MaterialPage(material, CurrentApp, vm);
         var window = new FromDataSourceWindow(vm, tabIndex);
         window.ShowDialog();
         return page;
      }

      /// <inheritdoc/>
      public IContentPage CreateFromDxfPage(string fileName) => new FromDxfPage(CurrentApp, fileName);

      /// <inheritdoc/>
      public IContentPage CreateCalcTasksPage() => new CalcTasksPage(CurrentApp);

      /// <inheritdoc/>
      public IContentPage CreateCalcResultPage(CalcResult result) => new CalcResultView(result, CurrentApp);

      /// <inheritdoc/>
      public IContentPage CreateFemCheckResultPage(CalcResult result) => new FemCheckResultView(result);

      /// <inheritdoc/>
      public FemAnalysisResultPage CreateFemAnalysisResultPage(CalcResult result,
          AppViewModel app, FemSchema schema,
          Action<string> showMemberForce,
          Action<string> goToSection,
          Action<string> showNodeValues)
      {
         var vm = new FemAnalysisResultVM(result, app.db, schema);
         vm.ShowMemberForceRequested += showMemberForce;
         vm.GoToSectionRequested += goToSection;
         vm.ShowNodeValuesRequested += showNodeValues;
         var page = new FemAnalysisResultView(vm);
         return new FemAnalysisResultPage(page, new FemAnalysisResultHandle(vm));
      }

      /// <inheritdoc/>
      public IContentPage CreateFemMemberForcePage(DatabaseService db, FemSchema schema, string memberTag, CalcResult result)
         => new FemMemberForceView(db, schema, memberTag, result);

      /// <inheritdoc/>
      public void ShowSettingsWindow(AppViewModel app) => new SettingsWindow(app).ShowDialog();

      /// <inheritdoc/>
      public TemplateRectResult? ShowTemplateRectDialog()
      {
         var dlg = new TemplateRectDialog();
         if (dlg.ShowDialog() != true) return null;
         return new TemplateRectResult(dlg.WidthMm, dlg.HeightMm, dlg.ContourName);
      }

      /// <inheritdoc/>
      public TemplateTeeResult? ShowTemplateTeeDialog()
      {
         var dlg = new TemplateTeeDialog();
         if (dlg.ShowDialog() != true) return null;
         return new TemplateTeeResult(dlg.WidthMm, dlg.HeightMm, dlg.TwMm, dlg.TfMm, dlg.ContourName);
      }

      /// <inheritdoc/>
      public TemplateIBeamResult? ShowTemplateIBeamDialog()
      {
         var dlg = new TemplateIBeamDialog();
         if (dlg.ShowDialog() != true) return null;
         return new TemplateIBeamResult(dlg.HeightMm, dlg.WidthMm, dlg.TwMm, dlg.TfMm, dlg.ContourName);
      }

      /// <inheritdoc/>
      public TemplateAngleResult? ShowTemplateAngleDialog()
      {
         var dlg = new TemplateAngleDialog();
         if (dlg.ShowDialog() != true) return null;
         return new TemplateAngleResult(dlg.WidthMm, dlg.HeightMm, dlg.TwMm, dlg.TfMm, dlg.ContourName);
      }

      /// <inheritdoc/>
      public TemplateCircleResult? ShowTemplateCircleDialog()
      {
         var dlg = new TemplateCircleDialog();
         if (dlg.ShowDialog() != true) return null;
         return new TemplateCircleResult(dlg.DiameterMm, dlg.Segments, dlg.ContourName);
      }

      /// <inheritdoc/>
      public ProfilePolyResult? ShowProfilePolyDialog()
      {
         var dlg = new ProfilePolyDialog();
         if (dlg.ShowDialog() != true) return null;
         return new ProfilePolyResult(dlg.ShapeType, dlg.ProfileId, dlg.ContourName,
             dlg.IsHollow, dlg.NArc, dlg.Slope ?? 0.0);
      }

      /// <inheritdoc/>
      public (double X, double Y, double Radius)? ShowCircleDialog()
      {
         var dlg = new CircleDialog();
         if (dlg.ShowDialog() != true) return null;
         return (dlg.X, dlg.Y, dlg.Radius);
      }

      /// <inheritdoc/>
      public string? ShowTextInputDialog(string titleKey, string labelKey, string initialValue)
      {
         var dlg = new TextInputDialog(Loc.S(titleKey), Loc.S(labelKey), initialValue);
         if (dlg.ShowDialog() != true) return null;
         return dlg.Value;
      }

      /// <inheritdoc/>
      public void ShowSp20Dialog(System.Collections.IEnumerable sets, AppViewModel app)
      {
         var dlg = new SP20Dialog(sets.Cast<ForceSet>(), app);
         dlg.ShowDialog();
      }

      /// <inheritdoc/>
      public FireSectionDef? ShowFireSectionDialog(AppViewModel app, FireSectionDef? existing = null)
      {
         var dlg = new FireSectionDialog(app, existing);
         if (dlg.ShowDialog() != true) return null;
         return dlg.Result;
      }

      /// <inheritdoc/>
      public CalcTask? ShowCalcTaskDialog(AppViewModel app, CalcTask? existing = null, string? groupKey = null)
      {
         var dlg = new CalcTaskPropsDialog(app, existing, groupKey);
         if (dlg.ShowDialog() != true) return null;
         return dlg.Result;
      }

      /// <inheritdoc/>
      public FemMemberInput? ShowFemMemberDialog()
      {
         var dlg = new FemMemberDialog();
         if (dlg.ShowDialog() != true) return null;
         return new FemMemberInput(dlg.Range, dlg.MemberTag, string.IsNullOrWhiteSpace(dlg.MemberType) ? null : dlg.MemberType);
      }

      /// <inheritdoc/>
      public ScadForceImportOptions? ShowScadForceImportDialog(string? seed)
      {
         var dlg = new ScadForceImportDialog(seed);
         if (dlg.ShowDialog() != true) return null;
         return new ScadForceImportOptions(dlg.ImportAllElements, dlg.ElementText, dlg.ThicknessMm);
      }

      /// <inheritdoc/>
      public FemCheck? ShowFemCheckCreateDialog(AppViewModel app, bool sls)
      {
         var dlg = sls
            ? (Window)new FemSlsCheckDialog(app)
            : new FemCheckDialog(app);
         if (dlg.ShowDialog() != true) return null;
         return dlg switch
         {
            FemSlsCheckDialog slsDlg => slsDlg.ResultCheck,
            FemCheckDialog checkDlg => checkDlg.ResultCheck,
            _ => null,
         };
      }

      /// <inheritdoc/>
      public void ShowFemCheckEditDialog(AppViewModel app, FemCheck check, bool sls)
      {
         if (sls)
            new FemSlsCheckDialog(app, check).ShowDialog();
         else
            new FemCheckDialog(app, check).ShowDialog();
      }

      /// <inheritdoc/>
      public FemAnalysis? ShowFemAnalysisDialog(FemSchema schema, FemAnalysis? existing = null)
      {
         var dlg = new FemAnalysisDialog(schema, existing);
         if (dlg.ShowDialog() != true) return null;
         return dlg.Result;
      }

      /// <inheritdoc/>
      public IReadOnlyList<CScore.ForceSet>? ShowDeleteForceSetsDialog(IReadOnlyList<CScore.ForceSet> sets)
      {
         var dlg = new DeleteForceSetsDialog(sets);
         if (dlg.ShowDialog() != true) return null;
         return dlg.SelectedSets;
      }

      /// <inheritdoc/>
      public void ShowContourPropsWindow(Contour contour, string tag)
      {
         new ContourPropsWindow(contour, tag).ShowDialog();
      }

      /// <inheritdoc/>
      public (double X, double Y)? ShowDoubleInputDialog(string title, string labelX, string labelY,
          double initialX, double initialY)
      {
         var dlg = new DoubleInputDialog(title, labelX, labelY, initialX, initialY);
         if (dlg.ShowDialog() != true) return null;
         return (dlg.Value1, dlg.Value2);
      }

      /// <inheritdoc/>
      public void MarkMaterialPageSaved(IContentPage page)
      {
         if (page is MaterialPage mp && mp.DataContext is MaterialVM vm) vm.IsSaved = true;
      }

      /// <inheritdoc/>
      public void InitializeSectionTree(AppViewModel app)
      {
         var composite = new System.Windows.Data.CompositeCollection
         {
            new System.Windows.Data.CollectionContainer { Collection = app.FiberSectionsLive },
            new OpenCS.ViewModels.SectionTreeGroup(app.TwoStageSectionsLive),
            new OpenCS.ViewModels.PlateSectionTreeGroup(app.PlateSectionsLive),
         };
         app.SectionTreeItems = composite;
      }

      /// <inheritdoc/>
      public void ApplyLanguage(int lang)
      {
         var resources = System.Windows.Application.Current.Resources;
         var dicts = resources.MergedDictionaries
             .Where(d => d.Source != null &&
                        (d.Source.OriginalString.Contains("Strings.en-US") ||
                         d.Source.OriginalString.Contains("Strings.ru-RU")))
             .ToList();

         foreach (var d in dicts)
            resources.MergedDictionaries.Remove(d);

         System.Windows.ResourceDictionary dict = new();
         switch (lang)
         {
            case 0:
               dict.Source = new Uri("Resources/Strings.ru-RU.xaml", UriKind.Relative);
               break;
            case 1:
               dict.Source = new Uri("Resources/Strings.en-US.xaml", UriKind.Relative);
               break;
         }
         resources.MergedDictionaries.Add(dict);
      }

      /// <inheritdoc/>
      public void ShutdownApplication()
      {
         var current = System.Windows.Application.Current;
         if (current.MainWindow != null)
            current.MainWindow.Close();
         else
            current.Shutdown();
      }

      static AppViewModel CurrentApp
      {
         get
         {
            var app = System.Windows.Application.Current?.MainWindow?.DataContext as AppViewModel;
            if (app == null)
               throw new InvalidOperationException("AppViewModel не найден в DataContext главного окна");
            return app;
         }
      }

      /// <summary>Платформо-независимая обёртка над FemAnalysisResultVM для показа значений узлов.</summary>
      sealed class FemAnalysisResultHandle : IFemAnalysisResultHandle
      {
         readonly FemAnalysisResultVM _vm;

         public FemAnalysisResultHandle(FemAnalysisResultVM vm) => _vm = vm;

         public bool TryGetNodeResult(string tag, out (double X, double Y, double Z) point,
             out OpenCS.OpenSees.Structural.FemNodeDisplacement? displacement,
             out OpenCS.OpenSees.Structural.FemNodeReaction? reaction)
         {
            if (!_vm.TryGetNodeResult(tag, out var p, out displacement, out reaction))
            {
               point = default;
               return false;
            }
            point = (p.X, p.Y, p.Z);
            return true;
         }

         public IReadOnlyList<string> Diagnostics => _vm.Diagnostics;

         public bool HasArtifacts => _vm.HasArtifacts;

         public string ArtifactDirectory => _vm.ArtifactDirectory ?? "";
      }
   }
}
