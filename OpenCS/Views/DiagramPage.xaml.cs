using CScore;
using Microsoft.Win32;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.ViewModels;

using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace OpenCS.Views
{
    /// <summary>Страница диаграммы пула σ(ε): черновик в <see cref="DiagramEditVM"/>, запись — по «Сохранить».</summary>
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
        }

        void Save_Click(object sender, RoutedEventArgs e)
        {
            // Незавершённая правка ячейки — в черновик до проверки.
            dataGrid.CommitEdit(DataGridEditingUnit.Row, true);
            try { _vm.Save(); }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Loc.S("ErrorSave"), ex.Message),
                                Loc.S("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
                int skipped = _vm.ImportCsv(dlg.FileName);
                if (skipped > 0) _mvm.LogService.Warning(string.Format(Loc.S("DiagramCsvRowsSkipped"), skipped));
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
                FileName = $"{(string.IsNullOrWhiteSpace(_vm.Tag) ? "diagram" : _vm.Tag)}.csv"
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

            try { File.WriteAllText(dlg.FileName, _vm.ExportCsv(delim), enc); }
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
            var d = _vm.Diagram;
            var usedBy = DiagramAuthoring.UsedBy(_mvm.Materials, d.Id);
            if (usedBy.Count > 0)
            {
                MessageBox.Show(string.Format(Loc.S("DiagramUsedByMaterials"), string.Join(", ", usedBy)),
                    Loc.S("Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var res = MessageBox.Show(Loc.S("ConfirmDeleteDiagram"), Loc.S("Confirmation"),
                          MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res != MessageBoxResult.Yes) return;

            if (!_vm.IsNew && DiagramAuthoring.TryDelete(_mvm.db, d, out _))
                _mvm.DiagramsLive.Remove(d);
            ((IUnsavedChanges)_vm).DiscardChanges();   // уйти без вопроса о сохранении удалённой
            _mvm.CurrentPage = null!;
            _mvm.LogService.Info(string.Format(Loc.S("DiagramDeletedCode"), d.Tag));
        }

        void Close_Click(object sender, RoutedEventArgs e)
            => _mvm.CurrentPage = null!;
    }
}
