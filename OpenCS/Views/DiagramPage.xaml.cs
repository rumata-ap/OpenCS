using CScore;
using Microsoft.Win32;
using OpenCS.Utilites;
using OpenCS.ViewModels;

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace OpenCS.Views
{
    public partial class DiagramPage : UserControl
    {
        readonly AppViewModel _mvm;
        readonly DiagramEditVM _vm;

        public DiagramPage(Diagramm diagram, AppViewModel mvm, bool isNew = false)
        {
            InitializeComponent();
            _mvm = mvm;
            _vm  = new DiagramEditVM(diagram, mvm, isNew);
            DataContext = _vm;

            titleText.Text = isNew ? Loc.S("NewDiagram") : diagram.Tag;

            calcTypeCombo.ItemsSource = Enum.GetValues(typeof(CalcType));
            matTypeCombo.ItemsSource  = Enum.GetValues(typeof(MatType));
        }

        void Save_Click(object sender, RoutedEventArgs e)
        {
            _vm.Save();
            titleText.Text = _vm.Tag;
        }

        void AddRow_Click(object sender, RoutedEventArgs e)
            => _vm.AddPoint();

        void DeleteRow_Click(object sender, RoutedEventArgs e)
        {
            if (dataGrid.SelectedItem is DiagramPoint p)
                _vm.RemovePoint(p);
        }

        void ImportCsv_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title  = Loc.S("ImportFromCsv"),
                Filter = Loc.S("CsvFilter")
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                _vm.ImportCsv(dlg.FileName);
                diagCanvas.FitToView();
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Loc.S("ErrorSave"), ex.Message),
                                Loc.S("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title    = Loc.S("ExportDiagramCsv"),
                Filter   = Loc.S("CsvFilter"),
                FileName = $"{_vm.Tag ?? "diagram"}.csv"
            };
            if (dlg.ShowDialog() != true) return;

            var settings = _mvm.CsvSettings;
            var delim    = settings.Delimiter == "," ? "," : ";";
            Encoding enc;
            if (settings.Encoding == "utf-8")
                enc = new UTF8Encoding(false);
            else
            {
                try { enc = Encoding.GetEncoding(1251); }
                catch { enc = Encoding.UTF8; }
            }

            var sb = new StringBuilder();
            sb.AppendLine(string.Join(delim, "ε", "σ, МПа"));
            foreach (var p in _vm.Points.OrderBy(p => p.Eps))
                sb.AppendLine($"{p.Eps.ToString(CultureInfo.InvariantCulture)}{delim}{p.Sig.ToString(CultureInfo.InvariantCulture)}");

            try { File.WriteAllText(dlg.FileName, sb.ToString(), enc); }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Loc.S("ErrorSave"), ex.Message),
                                Loc.S("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        void Fit_Click(object sender, RoutedEventArgs e)
            => diagCanvas.FitToView();

        void Delete_Click(object sender, RoutedEventArgs e)
        {
            var diagramId = _vm.Diagram.Id;

            // Защита: не удалять если используется Custom-материалом
            if (diagramId > 0)
            {
                var usedBy = _mvm.Materials
                    .Where(m => m.Type == MatType.Custom
                             && m.CustomDiagramIds.Values.Contains(diagramId))
                    .Select(m => m.Tag)
                    .ToList();

                if (usedBy.Count > 0)
                {
                    MessageBox.Show(
                        string.Format(Loc.S("DiagramUsedByMaterials"),
                                      string.Join(", ", usedBy)),
                        Loc.S("Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            var res = MessageBox.Show(Loc.S("ConfirmDeleteDiagram"), Loc.S("Confirmation"),
                          MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res != MessageBoxResult.Yes) return;

            if (diagramId > 0)
            {
                _mvm.db.DeleteDiagram(_vm.Diagram);
                _mvm.db.Diagrams.Remove(_vm.Diagram);
                _mvm.DiagramsLive.Remove(_vm.Diagram);
            }
            _mvm.CurrentPage = null!;
            _mvm.LogService.Info(string.Format(Loc.S("DiagramDeletedCode"), _vm.Tag));
        }

        void Close_Click(object sender, RoutedEventArgs e)
            => _mvm.CurrentPage = null!;
    }
}
