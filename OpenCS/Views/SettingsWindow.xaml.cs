using OpenCS.Utilites;
using CalcSettings = OpenCS.Utilites.CalcSettings;
using CsvExportSettings = OpenCS.Utilites.CsvExportSettings;
using LiraImportSettings = OpenCS.Utilites.LiraImportSettings;
using AcadImportSettings = OpenCS.Utilites.AcadImportSettings;
using ArcDiscretization = OpenCS.Utilites.ArcDiscretization;

using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace OpenCS.Views
{
   public partial class SettingsWindow : Window
   {
      readonly AppViewModel _mvm;
      readonly PlotSettings _settings;
      readonly CalcSettings _calcSettings;
      readonly CsvExportSettings _csvSettings;
      readonly LiraImportSettings _liraSettings;
       readonly AcadImportSettings _acadSettings;
       readonly GmshSettings _gmshSettings;
      /// <summary>Черновик настроек проекта: проверка и запись — через ProjectSettingsAuthoring (как в CLI).</summary>
      readonly Services.ProjectSettings _draft;
      /// <summary>Поля с неверным вводом и текст ошибки; при них «OK» и «Применить» не сохраняют.</summary>
      readonly Dictionary<TextBox, string> _errors = [];

      public SettingsWindow(AppViewModel mvm)
      {
         InitializeComponent();
         _mvm = mvm;
         _settings = mvm.PlotSettings.Clone();
         _csvSettings = mvm.CsvSettings.Clone();
          _draft = new Services.ProjectSettings
          {
             Calc = mvm.CalcSettings.Clone(), Lira = mvm.LiraImportSettings.Clone(),
             Acad = mvm.AcadImportSettings.Clone(), Gmsh = mvm.GmshSettings.Clone(),
          };
          _calcSettings = _draft.Calc;
          _liraSettings = _draft.Lira;
          _acadSettings = _draft.Acad;
          _gmshSettings = _draft.Gmsh;
          Owner = Dialogs.DialogOwner.Active();

         LoadToUi();
         HookTextBoxes();
         InitColorDefaults();
         LoadCalcToUi();
         HookCalcBoxes();
         LoadCsvToUi();
         HookCsvControls();
          LoadLiraToUi();
          HookLiraControls();
          LoadAcadToUi();
          HookAcadControls();
          LoadOpenSeesToUi();
           HookOpenSeesControls();
           LoadGmshToUi();
           HookGmshControls();
           BindFields();
       }

      void LoadToUi()
      {
         BgField.Value = _settings.Background;
         GridField.Value = _settings.Grid;
         CurveField.Value = _settings.Curve;
         FillField.Value = _settings.Fill;
         MarkerField.Value = _settings.MarkerFill;
         TextField.Value = _settings.Text;
         HighlightField.Value = _settings.Highlight;
         AxesColorField.Value = _settings.AxesColor;
         AxesFontSzBox.Text = _settings.AxesFontSize.ToString("F0");
         DxfBgField.Value = _settings.DxfCanvasBackground;
         CentroidField.Value = _settings.CentroidColor;
         CentroidSzBox.Text = _settings.CentroidSize.ToString("F0");
         CurveThBox.Text = _settings.CurveThickness.ToString("F1");
         MarkerSzBox.Text = _settings.MarkerSize.ToString("F0");
         FontSzBox.Text = _settings.FontSize.ToString("F0");
         ShowGridCb.IsChecked = _settings.ShowGrid;
         GridThBox.Text = _settings.GridThickness.ToString("F2");
         TickCountBox.Text = _settings.TickCount.ToString();
         ShowLabelsCb.IsChecked = _settings.ShowPointLabels;
         ShowTooltipsCb.IsChecked = _settings.ShowTooltips;
         ShowAxesValsCb.IsChecked = _settings.ShowAxesValues;
         AxesOriginCb.IsChecked = _settings.AxesAtOrigin;
         OriginReferenceAxesCb.IsChecked = _settings.ShowOriginReferenceAxes;
         ForceSetColorizeCb.IsChecked = _settings.ForceSetColorize;
         RefreshPreview();
      }

      /// <summary>Значения по умолчанию задают кнопки возврата в полях цвета.</summary>
      void InitColorDefaults()
      {
         var d = PlotSettings.Default;
         var c = CalcSettings.Default;
         BgField.DefaultValue = d.Background;
         GridField.DefaultValue = d.Grid;
         CurveField.DefaultValue = d.Curve;
         FillField.DefaultValue = d.Fill;
         MarkerField.DefaultValue = d.MarkerFill;
         TextField.DefaultValue = d.Text;
         HighlightField.DefaultValue = d.Highlight;
         AxesColorField.DefaultValue = d.AxesColor;
         DxfBgField.DefaultValue = d.DxfCanvasBackground;
         CentroidField.DefaultValue = d.CentroidColor;
         HullField.DefaultValue = c.HullColor;
         HoleField.DefaultValue = c.HoleColor;
         NeutralAxisField.DefaultValue = c.NeutralAxisColor;
         CentroidNdsField.DefaultValue = c.CentroidNdsColor;
      }

      void HookTextBoxes()
      {
         HookColor(BgField, v => _settings.Background = v);
         HookColor(GridField, v => _settings.Grid = v);
         HookColor(CurveField, v => _settings.Curve = v);
         HookColor(FillField, v => _settings.Fill = v);
         HookColor(MarkerField, v => _settings.MarkerFill = v);
         HookColor(TextField, v => _settings.Text = v);
         HookColor(HighlightField, v => _settings.Highlight = v);
         HookColor(AxesColorField, v => _settings.AxesColor = v);
         HookColor(DxfBgField, v => _settings.DxfCanvasBackground = v);
         HookColor(CentroidField, v => _settings.CentroidColor = v);

         HookNumber(CurveThBox, 0.1, 20, v => _settings.CurveThickness = v);
         HookNumber(GridThBox, 0.05, 5, v => _settings.GridThickness = v);
         HookNumber(MarkerSzBox, 1, 30, v => _settings.MarkerSize = v);
         HookNumber(FontSzBox, 5, 48, v => _settings.FontSize = v);
         HookNumber(AxesFontSzBox, 5, 48, v => _settings.AxesFontSize = v);
         HookNumber(CentroidSzBox, 1, 30, v => _settings.CentroidSize = v);
         HookNumber(TickCountBox, 2, 30, v => _settings.TickCount = (int)Math.Round(v), integer: true);

         HookCheck(ShowGridCb, v => _settings.ShowGrid = v);
         HookCheck(ShowLabelsCb, v => _settings.ShowPointLabels = v);
         HookCheck(ShowTooltipsCb, v => _settings.ShowTooltips = v);
         HookCheck(ShowAxesValsCb, v => _settings.ShowAxesValues = v);
         HookCheck(AxesOriginCb, v => _settings.AxesAtOrigin = v);
         HookCheck(OriginReferenceAxesCb, v => _settings.ShowOriginReferenceAxes = v);
         HookCheck(ForceSetColorizeCb, v => _settings.ForceSetColorize = v);
      }

      void HookColor(Helpers.ColorField f, Action<string> set) =>
         f.ValueChanged += v => { set(v); RefreshPreview(); };

      void HookCheck(CheckBox cb, Action<bool> set)
      {
         cb.Checked += (_, _) => { set(true); RefreshPreview(); };
         cb.Unchecked += (_, _) => { set(false); RefreshPreview(); };
      }

      /// <summary>
      /// Числовое поле с проверкой диапазона: при неверном вводе рамка краснеет и в подсказке
      /// написано, что допустимо; прежнее значение настройки при этом не меняется.
      /// </summary>
      void HookNumber(TextBox tb, double min, double max, Action<double> set, bool integer = false)
      {
         tb.TextChanged += (_, _) =>
         {
            double v = 0;
            bool ok = integer
               ? int.TryParse(tb.Text, out var iv) && (v = iv) >= min && v <= max
               : Pars.ParseAny(tb.Text, out v) && v >= min && v <= max;
            if (ok)
            {
               SetError(tb, null);
               set(v);
               RefreshPreview();
            }
            else
               SetError(tb, string.Format(Loc.S("SettingErrRange"), Services.SettingRange.Between(min, max).Text()));
         };
      }

      // ── Живой предпросмотр ───────────────────────────────────────────────

      Services.WpfPlotService? _preview;

      void RefreshPreview()
      {
         if (PreviewCanvas is null) return;
         _preview ??= new Services.WpfPlotService(PreviewCanvas);
         _preview.Clear();
         _preview.ApplySettings(_settings);

         // Условная диаграмма σ–ε бетона: заливка под кривой, кривая и маркеры.
         const int n = 40;
         var xs = new double[n + 2];
         var ys = new double[n + 2];
         for (int i = 0; i < n; i++)
         {
            double t = i / (double)(n - 1);
            xs[i] = 3.5 * t;
            ys[i] = 30 * (1 - (1 - t) * (1 - t));
         }
         xs[n] = xs[n - 1]; ys[n] = 0;
         xs[n + 1] = 0; ys[n + 1] = 0;
         _preview.AddPolygon(xs, ys, _settings.Fill, _settings.Curve);

         var mx = new[] { xs[0], xs[8], xs[16], xs[24], xs[32], xs[n - 1] };
         var my = new[] { ys[0], ys[8], ys[16], ys[24], ys[32], ys[n - 1] };
         _preview.AddMarkers(mx, my);

         _preview.SetXLabel(Loc.S("SettingsPreviewX"));
         _preview.SetYLabel(Loc.S("SettingsPreviewY"));
         _preview.Refresh();
      }

      void Apply_Click(object sender, RoutedEventArgs e) => TrySaveAndApply();

      void Ok_Click(object sender, RoutedEventArgs e)
      {
         if (TrySaveAndApply()) Close();
      }

      /// <summary>Записывает настройки; при неверных полях ничего не пишет, открывает вкладку с первой ошибкой.</summary>
      internal bool TrySaveAndApply()
      {
         if (_errors.Count > 0)
         {
            var (box, error) = _errors.First();
            if (TabOf(box) is { } tab) Tabs.SelectedItem = tab;
            StatusText.Text = Loc.S("SettingsTabHasErrors") + " " + FieldCaption(box, error);
            box.Focus();
            return false;
         }
         var serviceErrors = Services.ProjectSettingsAuthoring.Save(_mvm.db, _draft);
         if (serviceErrors.Count > 0)
         {
            StatusText.Text = Loc.S("SettingsTabHasErrors") + " " + string.Join("; ", serviceErrors.Select(x => $"{x.Path}: {x.Error}"));
            return false;
         }
         StatusText.Text = "";

         _mvm.PlotSettings = _settings.Clone();
         _mvm.ApplyPlotSettings();
         _mvm.db.SavePlotSettings(_mvm.PlotSettings);

         _mvm.CalcSettings = _calcSettings.Clone();
         _mvm.NotifyCalcSettingsApplied();

         _mvm.CsvSettings = _csvSettings.Clone();
         _mvm.db.SaveCsvSettings(_mvm.CsvSettings);

          _mvm.LiraImportSettings = _liraSettings.Clone();

           _mvm.AcadImportSettings = _acadSettings.Clone();

           _mvm.GmshSettings = _gmshSettings.Clone();
           return true;
       }

      void Cancel_Click(object sender, RoutedEventArgs e)
      {
         Close();
      }

      void LoadCalcToUi()
      {

         HullField.Value           = _calcSettings.HullColor;
         HullThickBox.Text         = _calcSettings.HullThickness.ToString("F1");
         HoleField.Value           = _calcSettings.HoleColor;
         HoleThickBox.Text         = _calcSettings.HoleThickness.ToString("F1");
         NeutralAxisField.Value    = _calcSettings.NeutralAxisColor;
         NeutralAxisThickBox.Text  = _calcSettings.NeutralAxisThickness.ToString("F1");
         CentroidNdsField.Value    = _calcSettings.CentroidNdsColor;
         CentroidNdsSizeBox.Text   = _calcSettings.CentroidNdsSize.ToString("F0");
         LabelFontSizeBox.Text     = _calcSettings.FiberLabelFontSize.ToString("F0");
         NewtonJacobianCombo.SelectedIndex = _calcSettings.NewtonJacobian == "central" ? 1 : 0;
         Sp20LoadValueKindCombo.SelectedIndex = _calcSettings.Sp20LoadValueKind == "design" ? 1 : 0;
         BatchParallelCb.IsChecked   = _calcSettings.BatchParallel;
         ShellWarmStartCb.IsChecked  = _calcSettings.ShellWarmStart;
         RebarDifferentialDiagramCb.IsChecked = _calcSettings.RebarDifferentialDiagram;
         RebarCompressionByRsCb.IsChecked = _calcSettings.RebarCompressionByRs;
         ConsiderConcreteTensionUlsCb.IsChecked = _calcSettings.ConsiderConcreteTensionUls;
         ElasticStiffnessWithoutRebarCb.IsChecked = _calcSettings.ElasticStiffnessWithoutRebar;
         SelectComboByTag(FireCompressionZoneMethodCombo, _calcSettings.FireCompressionZoneMethod);
         PsiSMethodCombo.SelectedIndex = _calcSettings.CrackWidthPsiSMethod == "strain8232" ? 1 : 0;
         StageStrainModeCombo.SelectedIndex = _calcSettings.StageStrainMode == "fibers" ? 1 : 0;
      }

      void HookCalcBoxes()
      {
         NewtonJacobianCombo.SelectionChanged += (_, _) =>
         {
            _calcSettings.NewtonJacobian =
               (NewtonJacobianCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string ?? "forward";
         };

         HookColor(HullField, v => _calcSettings.HullColor = v);
         HookColor(HoleField, v => _calcSettings.HoleColor = v);
         HookColor(NeutralAxisField, v => _calcSettings.NeutralAxisColor = v);
         HookColor(CentroidNdsField, v => _calcSettings.CentroidNdsColor = v);
         HookNumber(HullThickBox, 0.1, 20, v => _calcSettings.HullThickness = v);
         HookNumber(HoleThickBox, 0.1, 20, v => _calcSettings.HoleThickness = v);
         HookNumber(NeutralAxisThickBox, 0.1, 20, v => _calcSettings.NeutralAxisThickness = v);
         HookNumber(CentroidNdsSizeBox, 1, 30, v => _calcSettings.CentroidNdsSize = v);
         HookNumber(LabelFontSizeBox, 5, 48, v => _calcSettings.FiberLabelFontSize = v);
         Sp20LoadValueKindCombo.SelectionChanged += (_, _) =>
            _calcSettings.Sp20LoadValueKind = ComboTag(Sp20LoadValueKindCombo) ?? "normative";
         BatchParallelCb.Checked    += (_, _) => _calcSettings.BatchParallel  = true;
         BatchParallelCb.Unchecked  += (_, _) => _calcSettings.BatchParallel  = false;
         ShellWarmStartCb.Checked   += (_, _) => _calcSettings.ShellWarmStart = true;
         ShellWarmStartCb.Unchecked += (_, _) => _calcSettings.ShellWarmStart = false;
         RebarDifferentialDiagramCb.Checked   += (_, _) => _calcSettings.RebarDifferentialDiagram = true;
         RebarDifferentialDiagramCb.Unchecked += (_, _) => _calcSettings.RebarDifferentialDiagram = false;
         RebarCompressionByRsCb.Checked   += (_, _) => _calcSettings.RebarCompressionByRs = true;
         RebarCompressionByRsCb.Unchecked += (_, _) => _calcSettings.RebarCompressionByRs = false;
         ConsiderConcreteTensionUlsCb.Checked   += (_, _) => _calcSettings.ConsiderConcreteTensionUls = true;
         ConsiderConcreteTensionUlsCb.Unchecked += (_, _) => _calcSettings.ConsiderConcreteTensionUls = false;
         ElasticStiffnessWithoutRebarCb.Checked   += (_, _) => _calcSettings.ElasticStiffnessWithoutRebar = true;
         ElasticStiffnessWithoutRebarCb.Unchecked += (_, _) => _calcSettings.ElasticStiffnessWithoutRebar = false;
         FireCompressionZoneMethodCombo.SelectionChanged += (_, _) =>
         {
            _calcSettings.FireCompressionZoneMethod =
               ComboTag(FireCompressionZoneMethodCombo) ?? "auto";
         };
         StageStrainModeCombo.SelectionChanged += (_, _) =>
            _calcSettings.StageStrainMode = StageStrainModeCombo.SelectedIndex == 1 ? "fibers" : "plane";
         PsiSMethodCombo.SelectionChanged += (_, _) =>
         {
            _calcSettings.CrackWidthPsiSMethod =
               (PsiSMethodCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string ?? "stress8138";
         };
         // Шаги перебора и длина зоны допускают 0 («авто» / «2·h0» по норме), k — строго больше нуля.
      }


      void LoadCsvToUi()
      {
         CsvSemicolon.IsChecked = _csvSettings.Delimiter == ";";
         CsvComma.IsChecked     = _csvSettings.Delimiter == ",";
         CsvUtf8.IsChecked      = _csvSettings.Encoding == "utf-8";
         CsvWin1251.IsChecked   = _csvSettings.Encoding == "windows-1251";
      }

      void HookCsvControls()
      {
         CsvSemicolon.Checked += (_, _) => _csvSettings.Delimiter = ";";
         CsvComma.Checked     += (_, _) => _csvSettings.Delimiter = ",";
         CsvUtf8.Checked      += (_, _) => _csvSettings.Encoding = "utf-8";
         CsvWin1251.Checked   += (_, _) => _csvSettings.Encoding = "windows-1251";
      }

      void LoadLiraToUi()
      {
         const double ten = 10.0;
         LiraTon981.IsChecked = Math.Abs(_liraSettings.TonToKnFactor - ten) > 0.01;
         LiraTon10.IsChecked  = Math.Abs(_liraSettings.TonToKnFactor - ten) <= 0.01;
         LiraInvertBarMxMyCb.IsChecked   = _liraSettings.InvertBarBendingMoments;
         LiraInvertShellMxMyCb.IsChecked = _liraSettings.InvertShellBendingMoments;
         LiraTextDirBox.Text = _liraSettings.TextFileDirectory ?? "";
      }

      void HookLiraControls()
      {
         LiraTon981.Checked += (_, _) => _liraSettings.TonToKnFactor = 9.80665;
         LiraTon10.Checked  += (_, _) => _liraSettings.TonToKnFactor = 10.0;
         LiraInvertBarMxMyCb.Checked   += (_, _) => _liraSettings.InvertBarBendingMoments = true;
         LiraInvertBarMxMyCb.Unchecked += (_, _) => _liraSettings.InvertBarBendingMoments = false;
         LiraInvertShellMxMyCb.Checked   += (_, _) => _liraSettings.InvertShellBendingMoments = true;
         LiraInvertShellMxMyCb.Unchecked += (_, _) => _liraSettings.InvertShellBendingMoments = false;
         LiraTextDirBox.TextChanged += (_, _) =>
            _liraSettings.TextFileDirectory = string.IsNullOrWhiteSpace(LiraTextDirBox.Text) ? null : LiraTextDirBox.Text.Trim();
      }

      void LoadAcadToUi()
      {
         const double mm = 0.001;
         const double cm = 0.01;
         AcadMm.IsChecked = Math.Abs(_acadSettings.ScaleFactor - mm) < 0.0001;
         AcadCm.IsChecked = Math.Abs(_acadSettings.ScaleFactor - cm) < 0.0001;
         AcadM.IsChecked  = Math.Abs(_acadSettings.ScaleFactor - 1.0) < 0.0001;

         AcadArcChordRb.IsChecked = _acadSettings.ArcDiscretizationMode == ArcDiscretization.ChordLength;
         AcadArcFixedRb.IsChecked = _acadSettings.ArcDiscretizationMode == ArcDiscretization.FixedSegments;
      }

      void HookAcadControls()
      {
         AcadMm.Checked += (_, _) => _acadSettings.ScaleFactor = 0.001;
         AcadCm.Checked += (_, _) => _acadSettings.ScaleFactor = 0.01;
         AcadM.Checked  += (_, _) => _acadSettings.ScaleFactor = 1.0;

         AcadArcChordRb.Checked += (_, _) => _acadSettings.ArcDiscretizationMode = ArcDiscretization.ChordLength;
         AcadArcFixedRb.Checked += (_, _) => _acadSettings.ArcDiscretizationMode = ArcDiscretization.FixedSegments;
      }

      void LoadOpenSeesToUi()
      {
         if (!double.IsFinite(_calcSettings.OpenSeesDefaultGjKnm2) || _calcSettings.OpenSeesDefaultGjKnm2 <= 0)
            _calcSettings.OpenSeesDefaultGjKnm2 = CalcSettings.DefaultOpenSeesGjKnm2;
         OpenSeesAutoGjFromSectionCb.IsChecked = _calcSettings.OpenSeesAutoGjFromSection;
         SubmodelLinearPrecheckCb.IsChecked = _calcSettings.SubmodelLinearPrecheck;
         OpenSeesExeBox.Text = _calcSettings.OpenSeesExecutablePath ?? "";
         OpenSeesArtifactsPathBox.Text = _calcSettings.OpenSeesArtifactsPath ?? "";
         SelectComboByTag(OpenSeesGeomTransfCombo, _calcSettings.OpenSeesGeomTransfKind);
         SelectComboByTag(OpenSeesConvergenceTestCombo, _calcSettings.OpenSeesConvergenceTest);
         SelectComboByTag(OpenSeesAlgorithmCombo, _calcSettings.OpenSeesAlgorithm);
         OpenSeesRecordFiberStatesCb.IsChecked = _calcSettings.OpenSeesRecordFiberStates;
         OpenSeesFiberStatesIntegrationPointsBox.IsEnabled = _calcSettings.OpenSeesRecordFiberStates;
         CsfeaBarStiffnessAggregateCombo.SelectedIndex = _calcSettings.CsfeaBarStiffnessMean ? 1 : 0;
      }

      void HookOpenSeesControls()
      {
         OpenSeesAutoGjFromSectionCb.Checked += (_, _) => _calcSettings.OpenSeesAutoGjFromSection = true;
         OpenSeesAutoGjFromSectionCb.Unchecked += (_, _) => _calcSettings.OpenSeesAutoGjFromSection = false;
         SubmodelLinearPrecheckCb.Checked += (_, _) => _calcSettings.SubmodelLinearPrecheck = true;
         SubmodelLinearPrecheckCb.Unchecked += (_, _) => _calcSettings.SubmodelLinearPrecheck = false;
         OpenSeesExeBox.TextChanged += (_, _) =>
            _calcSettings.OpenSeesExecutablePath = string.IsNullOrWhiteSpace(OpenSeesExeBox.Text) ? null : OpenSeesExeBox.Text.Trim();
         OpenSeesArtifactsPathBox.TextChanged += (_, _) =>
            _calcSettings.OpenSeesArtifactsPath = string.IsNullOrWhiteSpace(OpenSeesArtifactsPathBox.Text) ? null : OpenSeesArtifactsPathBox.Text.Trim();
         OpenSeesArtifactsPathBox.LostFocus += (_, _) =>
         {
            StatusText.Text = OpenCS.OpenSees.Runtime.WindowsShortPath.IsAsciiSafe(OpenSeesArtifactsPathBox.Text)
               ? "" : Loc.S("OpenSeesArtifactsPathNonAsciiWarning");
         };
         OpenSeesGeomTransfCombo.SelectionChanged += (_, _) =>
            _calcSettings.OpenSeesGeomTransfKind = ComboTag(OpenSeesGeomTransfCombo) ?? "Linear";
         OpenSeesConvergenceTestCombo.SelectionChanged += (_, _) =>
            _calcSettings.OpenSeesConvergenceTest = ComboTag(OpenSeesConvergenceTestCombo) ?? "EnergyIncr";
         OpenSeesAlgorithmCombo.SelectionChanged += (_, _) =>
            _calcSettings.OpenSeesAlgorithm = ComboTag(OpenSeesAlgorithmCombo) ?? "NewtonLineSearch";
         OpenSeesRecordFiberStatesCb.Checked += (_, _) =>
         {
            _calcSettings.OpenSeesRecordFiberStates = true;
            OpenSeesFiberStatesIntegrationPointsBox.IsEnabled = true;
         };
         OpenSeesRecordFiberStatesCb.Unchecked += (_, _) =>
         {
            _calcSettings.OpenSeesRecordFiberStates = false;
            OpenSeesFiberStatesIntegrationPointsBox.IsEnabled = false;
         };
         CsfeaBarStiffnessAggregateCombo.SelectionChanged += (_, _) =>
            _calcSettings.CsfeaBarStiffnessMean = CsfeaBarStiffnessAggregateCombo.SelectedIndex == 1;
      }

      void LiraTextDirBrowse_Click(object sender, RoutedEventArgs e)
      {
         var initial = string.IsNullOrWhiteSpace(LiraTextDirBox.Text) ? null : LiraTextDirBox.Text.Trim();
         var folder = _mvm.FileDialogService.SelectFolder(Loc.S("LiraTextDirGroup"), initial);
         if (folder is not null) LiraTextDirBox.Text = folder;
      }

      void OpenSeesExeBrowse_Click(object sender, RoutedEventArgs e)
      {
         var path = _mvm.FileDialogService.OpenFile(Loc.S("OpenSeesExeFilter"), Loc.S("FemAnalysisExeLabel"));
         if (path is not null) OpenSeesExeBox.Text = path;
      }

       void OpenSeesArtifactsPathBrowse_Click(object sender, RoutedEventArgs e)
      {
         var initial = string.IsNullOrWhiteSpace(OpenSeesArtifactsPathBox.Text) ? null : OpenSeesArtifactsPathBox.Text.Trim();
         var folder = _mvm.FileDialogService.SelectFolder(Loc.S("OpenSeesArtifactsPathLabel"), initial);
         if (folder is not null) OpenSeesArtifactsPathBox.Text = folder;
       }

       void LoadGmshToUi()
       {
          GmshExeBox.Text = _gmshSettings.ExecutablePath ?? "";
          SelectComboByTag(GmshAlgorithmCombo, _gmshSettings.Algorithm.ToString());
          SelectComboByTag(GmshElementModeCombo, _gmshSettings.ElementMode.ToString());
          GmshKeepArtifactsCb.IsChecked = _gmshSettings.KeepArtifacts;
          GmshArtifactsPathBox.Text = _gmshSettings.ArtifactsPath ?? "";
       }

       /// <summary>Пресет «регулярная сетка»: Frontal-Delaunay for Quads с рекомбинацией — почти прямоугольные Q4,
       /// T3 только у отверстий, колонн и стыков (проба 08.10: медиана отклонения угла Q4 от 90° — 2° против 18° у
       /// Frontal-Delaunay, T3 в 8 раз меньше).</summary>
       void GmshPresetRegular_Click(object sender, RoutedEventArgs e)
       {
          SelectComboByTag(GmshAlgorithmCombo, "8");
          SelectComboByTag(GmshElementModeCombo, nameof(CScore.Planar.PlanarMeshElementMode.Mixed));
       }

       /// <summary>Пресет «свободная сетка» — прежние настройки по умолчанию (Frontal-Delaunay, T3+Q4).</summary>
       void GmshPresetFree_Click(object sender, RoutedEventArgs e)
       {
          SelectComboByTag(GmshAlgorithmCombo, "6");
          SelectComboByTag(GmshElementModeCombo, nameof(CScore.Planar.PlanarMeshElementMode.Mixed));
       }



       void HookGmshControls()
       {
          GmshExeBox.TextChanged += (_, _) =>
             _gmshSettings.ExecutablePath = string.IsNullOrWhiteSpace(GmshExeBox.Text) ? null : GmshExeBox.Text.Trim();
          GmshAlgorithmCombo.SelectionChanged += (_, _) =>
             _gmshSettings.Algorithm = int.TryParse(ComboTag(GmshAlgorithmCombo), out var alg) ? alg : 6;
          GmshElementModeCombo.SelectionChanged += (_, _) =>
             _gmshSettings.ElementMode = Enum.TryParse<CScore.Planar.PlanarMeshElementMode>(ComboTag(GmshElementModeCombo), out var mode)
                ? mode : CScore.Planar.PlanarMeshElementMode.Mixed;
          GmshKeepArtifactsCb.Checked   += (_, _) => _gmshSettings.KeepArtifacts = true;
          GmshKeepArtifactsCb.Unchecked += (_, _) => _gmshSettings.KeepArtifacts = false;
          GmshArtifactsPathBox.TextChanged += (_, _) =>
             _gmshSettings.ArtifactsPath = string.IsNullOrWhiteSpace(GmshArtifactsPathBox.Text) ? null : GmshArtifactsPathBox.Text.Trim();
       }

       void GmshExeBrowse_Click(object sender, RoutedEventArgs e)
       {
          var path = _mvm.FileDialogService.OpenFile(Loc.S("GmshExeFilter"), Loc.S("GmshExecutablePath"));
          if (path is not null) GmshExeBox.Text = path;
       }

       void GmshArtifactsPathBrowse_Click(object sender, RoutedEventArgs e)
       {
          var initial = string.IsNullOrWhiteSpace(GmshArtifactsPathBox.Text) ? null : GmshArtifactsPathBox.Text.Trim();
          var folder = _mvm.FileDialogService.SelectFolder(Loc.S("GmshArtifactsPathLabel"), initial);
          if (folder is not null) GmshArtifactsPathBox.Text = folder;
       }

       async void GmshCheckVersion_Click(object sender, RoutedEventArgs e)
       {
          GmshCheckVersionBtn.IsEnabled = false;
          try
          {
             var explicitPath = string.IsNullOrWhiteSpace(GmshExeBox.Text) ? null : GmshExeBox.Text.Trim();
             var executable = new OpenCS.Gmsh.Runtime.GmshExecutableResolver().Resolve(explicitPath);
             var version = await OpenCS.Gmsh.Runtime.GmshProcessRunner.ReadVersionAsync(
                executable.Path, TimeSpan.FromSeconds(10), CancellationToken.None);
             GmshVersionText.Text = string.Format(Loc.S("GmshVersionResolvedFormat"), executable.Source, version);
          }
          catch (Exception ex) when (ex is FileNotFoundException or IOException or InvalidDataException)
          {
             GmshVersionText.Text = string.Format(Loc.S("GmshVersionNotFoundFormat"), ex.Message);
          }
          finally
          {
             GmshCheckVersionBtn.IsEnabled = true;
          }
       }

      // ── Числовые поля через службу: разбор с запятой и точкой, диапазон, ошибка текстом ────────

      /// <summary>Поля окна, связанные с полями настроек проекта (путь документа CLI).</summary>
      (TextBox Box, string Path)[] BoundFields() =>
      [
            (GridDensityBox, "sections.gridDensity"),
            (NewtonTolBox, "sections.newton.tolerance"),
            (NewtonMaxIterBox, "sections.newton.maxIterations"),
            (NewtonDeltaHBox, "sections.newton.deltaH"),
            (LimitBisectAbsBox, "sections.limitBisection.absTolerance"),
            (LimitBisectRelBox, "sections.limitBisection.relTolerance"),
            (Sp63EtaMinBox, "sections.descendingBranch.sp63EtaMin"),
            (EkbEtaMinBox, "sections.descendingBranch.ekbEtaMin"),
            (ShellTolResBox, "sections.plates.newtonResidual"),
            (Sp20GammaGBox, "loads.gammaF.permanent"),
            (Sp20GammaGFavBox, "loads.gammaF.permanentFavourable"),
            (Sp20GammaLBox, "loads.gammaF.longTerm"),
            (Sp20GammaQBox, "loads.gammaF.shortTerm"),
            (Sp20GammaABox, "loads.gammaF.accidental"),
            (ShearStationStepBox, "sections.shear.stationStepM"),
            (ShearProjectionStepBox, "sections.shear.projectionStepM"),
            (ShearMomentZoneBox, "sections.shear.momentZoneLengthM"),
            (ShearAnchorageFactorBox, "sections.shear.anchorageFactor"),
            (AcadArcChordBox, "import.autocad.arcs.chordMm"),
            (AcadArcSegBox, "import.autocad.arcs.segments"),
            (OpenSeesDefaultGjBox, "openSees.defaultGjKnm2"),
            (OpenSeesTimeoutBox, "gui.openSeesTimeoutSeconds"),
            (OpenSeesRefinementDivisionsBox, "openSees.refinementDivisions"),
            (OpenSeesMaxRefinementDepthBox, "openSees.maxRefinementDepth"),
            (OpenSeesToleranceBox, "openSees.tolerance"),
            (OpenSeesMaxIterationsBox, "openSees.maxIterations"),
            (OpenSeesIntegrationPointsBox, "openSees.integrationPoints"),
            (OpenSeesFiberStatesIntegrationPointsBox, "openSees.fiberStatesIntegrationPoints"),
            (CsfeaThreadsBox, "gui.csfeaThreads"),
            (CsfeaStiffnessFloorBox, "csfea.stiffnessFloor"),
            (GmshTimeoutBox, "gui.gmshTimeoutSeconds"),
            (GmshSeamToleranceBox, "mesh.seamToleranceMm"),
      ];

      void BindFields()
      {
         foreach (var (box, path) in BoundFields())
         {
            var field = Services.ProjectSettingsAuthoring.Field(path);
            box.Text = field.Format(_draft);
            box.TextChanged += (_, _) =>
            {
               string? error = field.Parse(box.Text, out var value);
               if (error == null) field.Set(_draft, value);
               SetError(box, error);
            };
         }
      }

      /// <summary>Неверное поле: светло-красный фон, подсказка, строка внизу окна, вкладка выделена красным.</summary>
      void SetError(TextBox box, string? error)
      {
         if (error == null)
         {
            if (!_errors.Remove(box)) return;
            box.ClearValue(Control.BackgroundProperty);
            box.ClearValue(FrameworkElement.ToolTipProperty);
            if (StatusText != null) StatusText.Text = _errors.Count > 0 ? FieldCaption(_errors.First().Key, _errors.First().Value) : "";
         }
         else
         {
            _errors[box] = error;
            box.Background = new SolidColorBrush(Color.FromRgb(0xFD, 0xE2, 0xE2));
            box.ToolTip = error;
            if (StatusText != null) StatusText.Text = FieldCaption(box, error);
         }
         if (Tabs == null) return;
         foreach (var tab in Tabs.Items.OfType<TabItem>())
         {
            if (tab.Header is not TextBlock header) continue;
            // Только заголовок: Foreground вкладки наследуется её содержимым (красились все подписи).
            if (_errors.Keys.Any(b => TabOf(b) == tab)) header.Foreground = Brushes.Crimson;
            else header.ClearValue(TextBlock.ForegroundProperty);
         }
      }

      /// <summary>Вкладка поля — по логическому дереву (визуального у невыбранной вкладки ещё нет).</summary>
      static TabItem? TabOf(DependencyObject element)
      {
         for (var p = LogicalTreeHelper.GetParent(element); p != null; p = LogicalTreeHelper.GetParent(p))
            if (p is TabItem tab) return tab;
         return null;
      }

      /// <summary>«Подпись поля: ошибка» — подпись из TextBlock той же строки сетки (или стоящего перед полем).</summary>
      static string FieldCaption(TextBox box, string error)
      {
         string? caption = null;
         if (box.Parent is Grid grid)
         {
            int row = Grid.GetRow(box);
            caption = grid.Children.OfType<TextBlock>().FirstOrDefault(t => Grid.GetRow(t) == row && Grid.GetColumn(t) < Grid.GetColumn(box))?.Text;
         }
         else if (box.Parent is Panel panel)
         {
            int i = panel.Children.IndexOf(box);
            caption = panel.Children.OfType<UIElement>().Take(i).OfType<TextBlock>().LastOrDefault()?.Text;
         }
         caption = caption?.Trim().TrimEnd(':');
         return string.IsNullOrEmpty(caption) ? error : $"{caption}: {error}";
      }

      /// <summary>Неверные поля окна (для проверок).</summary>
      internal IReadOnlyDictionary<TextBox, string> FieldErrors => _errors;

      static string? ComboTag(ComboBox combo) => (combo.SelectedItem as ComboBoxItem)?.Tag as string;

      static void SelectComboByTag(ComboBox combo, string tag)
      {
         foreach (var item in combo.Items.OfType<ComboBoxItem>())
            if (item.Tag as string == tag) { combo.SelectedItem = item; return; }
         if (combo.Items.Count > 0) combo.SelectedIndex = 0;
      }

      /// <summary>Сброс только вкладки «Графики»: графики, холст сечения и стили линий НДС.</summary>
      void ResetPlotTab_Click(object sender, RoutedEventArgs e)
      {
         var def = PlotSettings.Default;
         foreach (var pr in typeof(PlotSettings).GetProperties())
            if (pr.CanWrite) pr.SetValue(_settings, pr.GetValue(def));

         var cd = CalcSettings.Default;
         _calcSettings.HullColor = cd.HullColor;
         _calcSettings.HullThickness = cd.HullThickness;
         _calcSettings.HoleColor = cd.HoleColor;
         _calcSettings.HoleThickness = cd.HoleThickness;
         _calcSettings.NeutralAxisColor = cd.NeutralAxisColor;
         _calcSettings.NeutralAxisThickness = cd.NeutralAxisThickness;
         _calcSettings.CentroidNdsColor = cd.CentroidNdsColor;
         _calcSettings.CentroidNdsSize = cd.CentroidNdsSize;
         _calcSettings.FiberLabelFontSize = cd.FiberLabelFontSize;

         LoadToUi();
         LoadCalcToUi();
      }
   }
}
